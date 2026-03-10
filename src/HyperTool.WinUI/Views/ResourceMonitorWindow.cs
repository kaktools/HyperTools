using HyperTool.Models;
using HyperTool.WinUI.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Windows.Graphics;
using Windows.UI;

namespace HyperTool.WinUI.Views;

public sealed class ResourceMonitorWindow : Window
{
    private readonly Func<ResourceMonitorSnapshot> _snapshotProvider;
    private readonly bool _isDarkMode;
    private readonly DispatcherQueueTimer _refreshTimer;
    private readonly StackPanel _hostPanel = new() { Spacing = 10 };
    private readonly Grid _vmGrid = new() { ColumnSpacing = 12, RowSpacing = 12 };
    private readonly TextBlock _summaryText = new() { Opacity = 0.78 };
    private readonly double _vmCardWidth = 520;

    public ResourceMonitorWindow(Func<ResourceMonitorSnapshot> snapshotProvider, string uiTheme)
    {
        _snapshotProvider = snapshotProvider;
        _isDarkMode = string.Equals(uiTheme, "Dark", StringComparison.OrdinalIgnoreCase);

        Title = "HyperTool Resource Monitor";
        DwmWindowHelper.ApplyRoundedCorners(this);
        AppWindow.Resize(new SizeInt32(1125, 910));
        TryApplyWindowIcon();

        Content = BuildLayout();
        ApplyRequestedTheme();
        UpdateTitleBarAppearance();

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(1000);
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        Closed += (_, _) => _refreshTimer.Stop();
        Refresh();
    }

    private UIElement BuildLayout()
    {
        var host = new Grid
        {
            Background = Application.Current.Resources["PageBackgroundBrush"] as Brush
        };

        var root = new Grid
        {
            Background = Application.Current.Resources["PageBackgroundBrush"] as Brush,
            RowSpacing = 12,
            Margin = new Thickness(16)
        };

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var headCard = CreateCard(14);
        var headStack = new StackPanel { Spacing = 4 };
        headStack.Children.Add(new TextBlock
        {
            Text = "Resource Monitor",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        headStack.Children.Add(new TextBlock
        {
            Text = "Host- und Guest-Ressourcen in Echtzeit (CPU, RAM, Trend).",
            Opacity = 0.82,
            TextWrapping = TextWrapping.Wrap
        });
        headStack.Children.Add(_summaryText);
        headCard.Child = headStack;
        root.Children.Add(headCard);

        var hostCard = CreateCard();
        hostCard.Child = _hostPanel;
        Grid.SetRow(hostCard, 1);
        root.Children.Add(hostCard);

        var vmScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _vmGrid
        };

        var vmCard = CreateCard();
        vmCard.Child = vmScroll;
        Grid.SetRow(vmCard, 2);
        root.Children.Add(vmCard);

        host.Children.Add(root);
        return host;
    }

    private static Border CreateCard(double padding = 12)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["PanelBorderBrush"] as Brush,
            Background = Application.Current.Resources["PanelBackgroundBrush"] as Brush,
            Padding = new Thickness(padding)
        };
    }

    private void Refresh()
    {
        var snapshot = _snapshotProvider();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(snapshot.IntervalMs, 500, 5000));

        var vmConnected = snapshot.VmSnapshots.Count(item => string.Equals(item.State, "Connected", StringComparison.OrdinalIgnoreCase));
        _summaryText.Text = $"Intervall: {snapshot.IntervalMs} ms   |   Verbundene Guests: {vmConnected}/{snapshot.VmSnapshots.Count}";

        _hostPanel.Children.Clear();
        _hostPanel.Children.Add(new TextBlock
        {
            Text = "Host Ressourcen",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        _hostPanel.Children.Add(new TextBlock
        {
            Text = $"CPU {snapshot.HostCpuPercent:0.#}%   RAM {snapshot.HostRamUsedGb:0.0}/{snapshot.HostRamTotalGb:0.0} GB",
            Opacity = 0.9
        });
        _hostPanel.Children.Add(new TextBlock
        {
            Text = "RAM-Auslastung",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = 0.9
        });
        _hostPanel.Children.Add(CreatePressureBar(snapshot.HostRamPressurePercent, width: 300));
        _hostPanel.Children.Add(new TextBlock
        {
            Text = $"{Math.Clamp(snapshot.HostRamPressurePercent, 0d, 100d):0.#}%",
            Opacity = 0.84,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var hostCharts = new Grid { ColumnSpacing = 10 };
        hostCharts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hostCharts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hostCharts.Children.Add(CreateChartCard("CPU Trend", snapshot.HostCpuHistory, 460, 96, Color.FromArgb(0xFF, 0x36, 0xC4, 0xFF)));
        var ramChart = CreateChartCard("RAM Pressure", snapshot.HostRamPressureHistory, 460, 96, Color.FromArgb(0xFF, 0xFF, 0xB3, 0x3C));
        Grid.SetColumn(ramChart, 1);
        hostCharts.Children.Add(ramChart);
        _hostPanel.Children.Add(hostCharts);

        var vmTotalCpu = snapshot.VmSnapshots.Where(item => item.State == "Connected").Sum(item => item.CpuPercent);
        var vmTotalRam = snapshot.VmSnapshots.Where(item => item.State == "Connected").Sum(item => item.RamUsedGb);
        _hostPanel.Children.Add(new TextBlock
        {
            Text = $"Host vs Guest gesamt   CPU: Host {snapshot.HostCpuPercent:0.#}% / Guest {vmTotalCpu:0.#}%   RAM: Host {snapshot.HostRamUsedGb:0.0} GB / Guest {vmTotalRam:0.0} GB",
            Opacity = 0.85
        });

        _vmGrid.Children.Clear();
        _vmGrid.ColumnDefinitions.Clear();
        _vmGrid.RowDefinitions.Clear();
        _vmGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var vms = snapshot.VmSnapshots.OrderBy(item => item.VmName, StringComparer.OrdinalIgnoreCase).ToList();
        for (var index = 0; index < vms.Count; index++)
        {
            _vmGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_vmCardWidth) });

            var vmCard = CreateVmCard(vms[index]);
            vmCard.Width = _vmCardWidth;
            Grid.SetRow(vmCard, 0);
            Grid.SetColumn(vmCard, index);
            _vmGrid.Children.Add(vmCard);
        }
    }

    private static FrameworkElement CreateVmCard(VmResourceMonitorSnapshot vm)
    {
        var card = CreateCard();
        card.MinWidth = 320;
        card.HorizontalAlignment = HorizontalAlignment.Stretch;

        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text = vm.VmName,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 16
        });

        var stateBadge = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(1)
        };
        var (stateBg, stateBorder, stateText) = ResolveStateBrushes(vm.State);
        stateBadge.Background = stateBg;
        stateBadge.BorderBrush = stateBorder;
        stateBadge.Child = new TextBlock
        {
            Text = stateText,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        stack.Children.Add(stateBadge);

        if (string.Equals(vm.State, "Connected", StringComparison.OrdinalIgnoreCase))
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Agent verbunden",
                Opacity = 0.85
            });
        }

        if (string.Equals(vm.State, "Connected", StringComparison.OrdinalIgnoreCase))
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"CPU {vm.CpuPercent:0.#}%   RAM {vm.RamUsedGb:0.0}/{vm.RamTotalGb:0.0} GB"
            });
            stack.Children.Add(CreatePressureBar(vm.RamPressurePercent, width: 240));
            stack.Children.Add(CreateChartCard("CPU Trend", vm.CpuHistory, 420, 72, Color.FromArgb(0xFF, 0x36, 0xC4, 0xFF)));
            stack.Children.Add(CreateChartCard("RAM Pressure", vm.RamPressureHistory, 420, 72, Color.FromArgb(0xFF, 0xFF, 0xB3, 0x3C)));
        }

        card.Child = stack;
        return card;
    }

    private static (Brush background, Brush border, string text) ResolveStateBrushes(string state)
    {
        if (string.Equals(state, "Connected", StringComparison.OrdinalIgnoreCase))
        {
            return (
                new SolidColorBrush(Color.FromArgb(0x2A, 0x46, 0xD6, 0x89)),
                new SolidColorBrush(Color.FromArgb(0x90, 0x38, 0xBE, 0x78)),
                "Connected");
        }

        return (
            new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xC1, 0x55)),
            new SolidColorBrush(Color.FromArgb(0x95, 0xE2, 0xA3, 0x2A)),
            "Guest nicht erreichbar");
    }

    private static UIElement CreatePressureBar(double pressurePercent, double width)
    {
        var clamped = Math.Clamp(pressurePercent, 0d, 100d);
        var color = clamped switch
        {
            < 60d => Color.FromArgb(0xFF, 0x39, 0xB5, 0x4A),
            < 80d => Color.FromArgb(0xFF, 0xE3, 0xA4, 0x1A),
            _ => Color.FromArgb(0xFF, 0xD6, 0x44, 0x44)
        };

        var track = new Grid
        {
            Height = 10,
            Width = width,
            Background = new SolidColorBrush(Color.FromArgb(0x35, 0x80, 0x80, 0x80))
        };

        var fill = new Rectangle
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Fill = new SolidColorBrush(color),
            Width = width * clamped / 100d,
            RadiusX = 4,
            RadiusY = 4
        };

        track.Children.Add(fill);
        return track;
    }

    private static FrameworkElement CreateChartCard(string title, IReadOnlyList<double> values, double width, double height, Color lineColor)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            Opacity = 0.82,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        stack.Children.Add(CreateSparkline(values, width, height, lineColor));
        return stack;
    }

    private static UIElement CreateSparkline(IReadOnlyList<double> values, double width, double height, Color lineColor)
    {
        var grid = new Grid
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Color.FromArgb(0x16, 0x80, 0x80, 0x80))
        };

        for (var i = 1; i <= 3; i++)
        {
            grid.Children.Add(new Rectangle
            {
                Height = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, (height / 4d) * i, 0, 0),
                Fill = new SolidColorBrush(Color.FromArgb(0x20, 0xA0, 0xA0, 0xA0))
            });
        }

        if (values.Count < 2)
        {
            grid.Children.Add(new TextBlock
            {
                Text = "Noch keine Daten",
                Opacity = 0.55,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11
            });
            return grid;
        }

        var max = Math.Max(100d, values.Max());
        var stepX = width / Math.Max(values.Count - 1, 1);

        var area = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x2E, lineColor.R, lineColor.G, lineColor.B))
        };

        var line = new Polyline
        {
            Stroke = new SolidColorBrush(lineColor),
            StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        area.Points.Add(new Windows.Foundation.Point(0, height));

        for (var i = 0; i < values.Count; i++)
        {
            var normalized = Math.Clamp(values[i] / max, 0d, 1d);
            var x = i * stepX;
            var y = height - (normalized * height);
            var point = new Windows.Foundation.Point(x, y);
            line.Points.Add(point);
            area.Points.Add(point);
        }

        area.Points.Add(new Windows.Foundation.Point(width, height));
        grid.Children.Add(area);
        grid.Children.Add(line);
        return grid;
    }

    private void ApplyRequestedTheme()
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = _isDarkMode ? ElementTheme.Dark : ElementTheme.Light;
        }
    }

    private void UpdateTitleBarAppearance()
    {
        try
        {
            if (AppWindow?.TitleBar is not AppWindowTitleBar titleBar)
            {
                return;
            }

            if (_isDarkMode)
            {
                titleBar.BackgroundColor = Color.FromArgb(0xFF, 0x17, 0x1F, 0x3A);
                titleBar.ForegroundColor = Color.FromArgb(0xFF, 0xE8, 0xF0, 0xFF);
                titleBar.InactiveBackgroundColor = Color.FromArgb(0xFF, 0x14, 0x1A, 0x31);
                titleBar.InactiveForegroundColor = Color.FromArgb(0xFF, 0x98, 0xAE, 0xD3);
                titleBar.ButtonBackgroundColor = Color.FromArgb(0xFF, 0x17, 0x1F, 0x3A);
                titleBar.ButtonForegroundColor = Color.FromArgb(0xFF, 0xE8, 0xF0, 0xFF);
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0xFF, 0x22, 0x2D, 0x51);
                titleBar.ButtonHoverForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0xFF, 0x2A, 0x36, 0x61);
                titleBar.ButtonPressedForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0xFF, 0x14, 0x1A, 0x31);
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0x98, 0xAE, 0xD3);
            }
            else
            {
                titleBar.BackgroundColor = Color.FromArgb(0xFF, 0xD8, 0xE9, 0xFF);
                titleBar.ForegroundColor = Color.FromArgb(0xFF, 0x0F, 0x24, 0x3C);
                titleBar.InactiveBackgroundColor = Color.FromArgb(0xFF, 0xE6, 0xF2, 0xFF);
                titleBar.InactiveForegroundColor = Color.FromArgb(0xFF, 0x4E, 0x66, 0x83);
                titleBar.ButtonBackgroundColor = Color.FromArgb(0xFF, 0xD8, 0xE9, 0xFF);
                titleBar.ButtonForegroundColor = Color.FromArgb(0xFF, 0x0F, 0x24, 0x3C);
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0xFF, 0xC7, 0xDE, 0xFC);
                titleBar.ButtonHoverForegroundColor = Color.FromArgb(0xFF, 0x0A, 0x1B, 0x30);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0xFF, 0xBA, 0xD3, 0xF7);
                titleBar.ButtonPressedForegroundColor = Color.FromArgb(0xFF, 0x08, 0x19, 0x2C);
                titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0xFF, 0xE6, 0xF2, 0xFF);
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0x4E, 0x66, 0x83);
            }
        }
        catch
        {
        }
    }

    private void TryApplyWindowIcon()
    {
        try
        {
            if (AppWindow is null)
            {
                return;
            }

            var iconPath = new[]
            {
                System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "HyperTool.ico"),
                System.IO.Path.Combine(AppContext.BaseDirectory, "HyperTool.ico")
            }.FirstOrDefault(File.Exists);

            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch
        {
        }
    }
}
