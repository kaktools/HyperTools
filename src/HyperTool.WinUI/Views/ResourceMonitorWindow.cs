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
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.UI;

namespace HyperTool.WinUI.Views;

public sealed class ResourceMonitorWindow : Window
{
    private readonly Func<ResourceMonitorSnapshot> _snapshotProvider;
    private readonly Func<Task>? _performanceOptimizationAction;
    private readonly bool _isDarkMode;
    private CancellationTokenSource? _refreshLoopCts;
    private Task? _refreshLoopTask;
    private int _refreshIntervalMs = 1000;
    private readonly StackPanel _hostPanel = new() { Spacing = 8 };
    private readonly Grid _vmGrid = new() { ColumnSpacing = 10, RowSpacing = 10 };
    private readonly TextBlock _summaryText = new() { Opacity = 0.78 };
    private readonly Button _hostDiscoButton = new()
    {
        Content = "Leistungsoptimierung",
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Padding = new Thickness(10, 4, 10, 4),
        Opacity = 1,
        Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x1F, 0x88, 0xE5)),
        Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x16, 0x6F, 0xC1)),
        BorderThickness = new Thickness(1),
        MinWidth = 156
    };
    private const double VmCardMinWidth = 380;
    private const double VmCardMaxWidth = 520;
    private ResourceMonitorSnapshot? _lastGoodSnapshot;

    public ResourceMonitorWindow(Func<ResourceMonitorSnapshot> snapshotProvider, string uiTheme, Func<Task>? performanceOptimizationAction = null)
    {
        _snapshotProvider = snapshotProvider;
        _performanceOptimizationAction = performanceOptimizationAction;
        _isDarkMode = string.Equals(uiTheme, "Dark", StringComparison.OrdinalIgnoreCase);

        Title = "HyperTool Ressourcenmonitor";
        DwmWindowHelper.ApplyRoundedCorners(this);
        DwmWindowHelper.ResizeForCurrentDpi(this, 1005, 835);
        TryApplyWindowIcon();

        Content = BuildLayout();
        ApplyRequestedTheme();
        UpdateTitleBarAppearance();

        StartRefreshLoop();

        _hostDiscoButton.Click += async (_, _) => await TriggerPerformanceOptimizationAsync();

        Closed += (_, _) => StopRefreshLoop();
        Refresh();
    }

    private void StartRefreshLoop()
    {
        StopRefreshLoop();

        var dispatcherQueue = DispatcherQueue;
        if (dispatcherQueue is null)
        {
            return;
        }

        _refreshLoopCts = new CancellationTokenSource();
        var token = _refreshLoopCts.Token;
        _refreshLoopTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(_refreshIntervalMs, 500, 5000)), token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    if (!dispatcherQueue.TryEnqueue(() => Refresh()))
                    {
                        break;
                    }
                }
                catch
                {
                    // Keep loop alive unless cancellation requested.
                }
            }
        }, token);
    }

    private void StopRefreshLoop()
    {
        try
        {
            _refreshLoopCts?.Cancel();
        }
        catch
        {
        }

        _refreshLoopCts?.Dispose();
        _refreshLoopCts = null;
        _refreshLoopTask = null;
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
        headCard.MinHeight = 92;
        var headStack = new StackPanel { Spacing = 4 };
        headStack.Children.Add(new TextBlock
        {
            Text = "Ressourcenmonitor",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        headStack.Children.Add(new TextBlock
        {
            Text = "Host- und Guest-Ressourcen in Echtzeit (Prozessor, Arbeitsspeicher, Verlauf).",
            Opacity = 0.82,
            TextWrapping = TextWrapping.Wrap
        });
        headStack.Children.Add(_summaryText);
        headCard.Child = headStack;
        root.Children.Add(headCard);

        var hostCard = CreateCard();
        hostCard.MinHeight = 258;
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
        vmCard.MinHeight = 320;
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
        try
        {
            ResourceMonitorSnapshot snapshot;
            try
            {
                snapshot = _snapshotProvider() ?? _lastGoodSnapshot ?? new ResourceMonitorSnapshot();
                _lastGoodSnapshot = snapshot;
            }
            catch
            {
                if (_lastGoodSnapshot is null)
                {
                    return;
                }

                snapshot = _lastGoodSnapshot;
            }

            var vmSnapshots = (snapshot.VmSnapshots ?? Array.Empty<VmResourceMonitorSnapshot>())
                .Where(item => item is not null)
                .ToList();
            var hostCpuHistory = (snapshot.HostCpuHistory ?? Array.Empty<double>()).ToArray();
            var hostRamHistory = (snapshot.HostRamPressureHistory ?? Array.Empty<double>()).ToArray();

            var hostCpuPercent = Math.Clamp(snapshot.HostCpuPercent, 0d, 100d);
            var hostRamTotalGb = Math.Max(0d, snapshot.HostRamTotalGb);
            var hostRamUsedGb = Math.Clamp(snapshot.HostRamUsedGb, 0d, hostRamTotalGb <= 0 ? double.MaxValue : hostRamTotalGb);
            var hostRamPercent = hostRamTotalGb <= 0d ? 0d : Math.Clamp((hostRamUsedGb / hostRamTotalGb) * 100d, 0d, 100d);

            _refreshIntervalMs = Math.Clamp(snapshot.IntervalMs, 500, 5000);

            var vmConnected = vmSnapshots.Count(item => string.Equals(item.State, "Connected", StringComparison.OrdinalIgnoreCase));
            var summaryText = $"Intervall: {snapshot.IntervalMs} ms   |   Verbundene Guests: {vmConnected}/{vmSnapshots.Count}";

            var hostElements = new List<UIElement>();
            var hostHeaderRow = new Grid();
            hostHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hostHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hostTitle = new TextBlock
            {
                Text = "Host Ressourcen",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            hostHeaderRow.Children.Add(hostTitle);

            // The button instance is reused across ticks, so detach it from the previous header row first.
            if (_hostDiscoButton.Parent is Panel oldParent)
            {
                oldParent.Children.Remove(_hostDiscoButton);
            }

            Grid.SetColumn(_hostDiscoButton, 1);
            hostHeaderRow.Children.Add(_hostDiscoButton);
            hostElements.Add(hostHeaderRow);

            var hostKpiGrid = new Grid { ColumnSpacing = 12 };
            hostKpiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hostKpiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hostKpiGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            hostKpiGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var cpuHeaderRow = new Grid
            {
                ColumnSpacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MinHeight = 60
            };
            cpuHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cpuHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            cpuHeaderRow.Children.Add(new TextBlock
            {
                Text = "Prozessor",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Opacity = 0.9,
                VerticalAlignment = VerticalAlignment.Center
            });

            var cpuPercentText = new TextBlock
            {
                Text = $"{hostCpuPercent:0.#}%",
                FontSize = 30,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                LineHeight = 34,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(cpuPercentText, 1);
            cpuHeaderRow.Children.Add(cpuPercentText);
            hostKpiGrid.Children.Add(cpuHeaderRow);

            var cpuChart = CreateChartCard("Prozessor-Verlauf", hostCpuHistory, 420, 96, Color.FromArgb(0xFF, 0x36, 0xC4, 0xFF));
            Grid.SetRow(cpuChart, 1);
            hostKpiGrid.Children.Add(cpuChart);

            var ramHeaderPanel = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MinHeight = 60
            };
            ramHeaderPanel.Children.Add(new TextBlock
            {
                Text = "Arbeitsspeicher-Auslastung",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Opacity = 0.9,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });

            var hostRamUsageRow = new Grid
            {
                ColumnSpacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            hostRamUsageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hostRamUsageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hostRamBar = (FrameworkElement)CreatePressureBar(hostRamPercent, width: 230);
            Grid.SetColumn(hostRamBar, 0);
            hostRamUsageRow.Children.Add(hostRamBar);

            var hostRamText = new TextBlock
            {
                Text = $"{hostRamUsedGb:0.0}/{hostRamTotalGb:0.0} GB ({hostRamPercent:0.#}%)",
                Opacity = 0.9,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hostRamText, 1);
            hostRamUsageRow.Children.Add(hostRamText);

            ramHeaderPanel.Children.Add(hostRamUsageRow);
            Grid.SetColumn(ramHeaderPanel, 1);
            hostKpiGrid.Children.Add(ramHeaderPanel);

            var ramChart = CreateChartCard("RAM-Auslastung", hostRamHistory, 420, 96, Color.FromArgb(0xFF, 0xFF, 0xB3, 0x3C));
            Grid.SetRow(ramChart, 1);
            Grid.SetColumn(ramChart, 1);
            hostKpiGrid.Children.Add(ramChart);
            hostElements.Add(hostKpiGrid);

            var vmTotalCpu = vmSnapshots.Where(item => string.Equals(item.State, "Connected", StringComparison.OrdinalIgnoreCase)).Sum(item => item.CpuPercent);
            var vmTotalRam = vmSnapshots.Where(item => string.Equals(item.State, "Connected", StringComparison.OrdinalIgnoreCase)).Sum(item => item.RamUsedGb);
            hostElements.Add(new TextBlock
            {
                Text = $"Host vs Guest gesamt   Prozessor: Host {hostCpuPercent:0.#}% / Guest {vmTotalCpu:0.#}%   Arbeitsspeicher: Host {hostRamUsedGb:0.0} GB / Guest {vmTotalRam:0.0} GB",
                Opacity = 0.85
            });

            var vms = vmSnapshots
                .OrderByDescending(item => string.Equals(item.State, "Connected", StringComparison.OrdinalIgnoreCase))
                .ThenBy(item => item.VmName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var vmCardWidth = ResolveVmCardWidthForTwoVisibleCards();
            var vmCards = new List<FrameworkElement>(vms.Count);

            for (var index = 0; index < vms.Count; index++)
            {
                var vmCard = CreateVmCard(vms[index]);
                vmCard.Width = vmCardWidth;
                vmCards.Add(vmCard);
            }

            _summaryText.Text = summaryText;

            _hostPanel.Children.Clear();
            foreach (var hostElement in hostElements)
            {
                _hostPanel.Children.Add(hostElement);
            }

            _vmGrid.Children.Clear();
            _vmGrid.ColumnDefinitions.Clear();
            _vmGrid.RowDefinitions.Clear();
            _vmGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (var index = 0; index < vmCards.Count; index++)
            {
                _vmGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(vmCardWidth) });
                Grid.SetRow(vmCards[index], 0);
                Grid.SetColumn(vmCards[index], index);
                _vmGrid.Children.Add(vmCards[index]);
            }
        }
        catch
        {
            // Keep the previous visual state if a single refresh tick fails.
        }
    }

    private static FrameworkElement CreateVmCard(VmResourceMonitorSnapshot vm)
    {
        var card = CreateCard(10);
        card.MinWidth = 320;
        card.HorizontalAlignment = HorizontalAlignment.Stretch;

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = vm.VmName,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 15
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
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        stack.Children.Add(stateBadge);

        if (string.Equals(vm.State, "Connected", StringComparison.OrdinalIgnoreCase))
        {
            stack.Children.Add(new TextBlock
            {
                Text = string.Equals(vm.ActiveSource, "host", StringComparison.OrdinalIgnoreCase)
                    ? "Host-Fallback aktiv"
                    : "Guest-Agent aktiv",
                Opacity = 0.85
            });
        }

        if (string.Equals(vm.State, "Connected", StringComparison.OrdinalIgnoreCase))
        {
            if (vm.GuestCpuPercent.HasValue && vm.GuestRamUsedGb.HasValue && vm.GuestRamTotalGb.HasValue)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"Guest: CPU {vm.GuestCpuPercent.Value:0.#}%   RAM {vm.GuestRamUsedGb.Value:0.0}/{vm.GuestRamTotalGb.Value:0.0} GB",
                    Opacity = string.Equals(vm.ActiveSource, "guest", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.75
                });
            }

            if (vm.HostCpuPercent.HasValue && vm.HostRamUsedGb.HasValue && vm.HostRamTotalGb.HasValue)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"Host: CPU {vm.HostCpuPercent.Value:0.#}%   RAM {vm.HostRamUsedGb.Value:0.0}/{vm.HostRamTotalGb.Value:0.0} GB",
                    Opacity = string.Equals(vm.ActiveSource, "host", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.75
                });
            }

            stack.Children.Add(CreatePressureBar(vm.RamPressurePercent, width: 240));
            stack.Children.Add(CreateChartCard("Prozessor-Verlauf", vm.CpuHistory ?? Array.Empty<double>(), 420, 66, Color.FromArgb(0xFF, 0x36, 0xC4, 0xFF)));
            stack.Children.Add(CreateChartCard("RAM-Auslastung", vm.RamPressureHistory ?? Array.Empty<double>(), 420, 66, Color.FromArgb(0xFF, 0xFF, 0xB3, 0x3C)));
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

    private double ResolveVmCardWidthForTwoVisibleCards()
    {
        var windowWidth = (Content as FrameworkElement)?.ActualWidth;
        if (!windowWidth.HasValue || windowWidth.Value <= 0)
        {
            return 460;
        }

        const double outerPadding = 68;
        var usableWidth = Math.Max(320d, windowWidth.Value - outerPadding);
        var widthForTwoCards = (usableWidth - _vmGrid.ColumnSpacing) / 2d;
        return Math.Clamp(widthForTwoCards, VmCardMinWidth, VmCardMaxWidth);
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

    private async Task TriggerPerformanceOptimizationAsync()
    {
        if (!_hostDiscoButton.IsEnabled)
        {
            return;
        }

        _hostDiscoButton.IsEnabled = false;
        _hostDiscoButton.Opacity = 0.6;
        try
        {
            Close();
            if (_performanceOptimizationAction is not null)
            {
                await _performanceOptimizationAction();
            }
        }
        finally
        {
            _hostDiscoButton.IsEnabled = true;
            _hostDiscoButton.Opacity = 1;
        }
    }
}
