using HyperTool.Models;
using HyperTool.Services;
using HyperTool.Guest.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Win32;
using Windows.Graphics;
using Windows.UI;

namespace HyperTool.Guest;

public sealed partial class App : Application
{
    private const int GuestUsbAutoRefreshFastSeconds = 20;
    private const int GuestUsbAutoRefreshSlowSeconds = 30;
    private const int GuestUsbAutoRefreshNoAutoConnectSeconds = 10;
    private const int GuestUsbAttachedBackgroundRefreshSeconds = 30;
    private const int GuestHostIdentityBackgroundRefreshSeconds = 30;
    private const int GuestHostIdentityUsbRefreshSeconds = 20;
    private static readonly TimeSpan HostIdentityRefreshAfterUsbRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HyperVSocketMonitorHeartbeatInterval = TimeSpan.FromMinutes(1);
    private const int GuestUsbAutoConnectFailureBackoffSeconds = 20;
    private const int UsbServiceOperationTimeoutSeconds = 12;
    private const int GuestUsbHeartbeatIntervalSeconds = 45;
    private const int GuestStaleExportDisconnectSignalThreshold = 2;
    private const int GuestStaleExportDisconnectSignalMinAgeSeconds = 12;
    private const int GuestLocalDetachSignalThreshold = 3;
    private const int GuestLocalDetachSignalMinAgeSeconds = 25;
    private const int GuestAutoDisconnectSuppressionSecondsAfterAttach = 180;
    private const int GuestLocalDetachHintSuppressionSeconds = 15;
    private const int GuestLocalDetachConfirmationAttempts = 2;
    private const int GuestLocalDetachConfirmationDelayMs = 700;
    private const int HyperVProbeGraceAfterDataPathSuccessSeconds = 180;
    private static readonly TimeSpan UsbRefreshSuccessLogHeartbeatInterval = TimeSpan.FromSeconds(45);
    private const int UsbHyperVProbeFailureThresholdBeforeRestart = 12;
    private static readonly TimeSpan UsbHyperVProxyRestartBackoffInterval = TimeSpan.FromSeconds(90);
    private const int SharedFolderCatalogFetchMaxAttempts = 3;
    private static readonly TimeSpan SharedFolderCatalogReadyLogHeartbeatInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RecurringWarnRateLimitInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan UsbStaleExportHintMinInterval = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan UsbPushRefreshMinInterval = TimeSpan.FromSeconds(2);
    private const string GuestWindowTitle = "HyperTool Guest";
    private const string GuestHeadline = "HyperTool Guest";
    private const string GuestIconUri = "ms-appx:///Assets/HyperTool.Guest.Icon.Transparent.png";

    private const string SingleInstanceMutexName = @"Local\HyperTool.Guest.SingleInstance";
    private const string SingleInstancePipeName = "HyperTool.Guest.SingleInstance.Activate";
    private const int SwRestore = 9;
    private const int SwShow = 5;

    private readonly IUsbIpService _usbService = new UsbIpdCliService();

    private GuestMainWindow? _mainWindow;
    private ITrayService? _trayService;
    private GuestTrayControlCenterWindow? _trayControlCenterWindow;
    private GuestConfig? _config;
    private string _configPath = GuestConfigService.DefaultConfigPath;

    private bool _isExitRequested;
    private bool _isExitAnimationRunning;
    private bool _isThemeWindowReopenInProgress;
    private bool _isTrayFunctional;
    private bool _minimizeToTray = true;
    private readonly object _sessionEndingHookSync = new();
    private bool _sessionEndingHookRegistered;
    private int _sessionEndingDisconnectStarted;

    private Mutex? _singleInstanceMutex;
    private CancellationTokenSource? _singleInstanceServerCts;
    private Task? _singleInstanceServerTask;
    private bool _singleInstanceOwned;
    private bool _pendingSingleInstanceShow;
    private CancellationTokenSource? _rainbowPrincessModeCts;
    private CancellationTokenSource? _trollModeCts;
    private readonly Random _trollRandom = new();
    private Grid? _trollOverlayHost;
    private Border? _trollOverlayDimmer;
    private Border? _trollOverlayCrater;
    private Canvas? _trollOverlayCanvas;
    private TextBlock? _trollOverlayBoss;
    private TextBlock? _trollOverlayStatus;
    private UIElement? _trollSceneTarget;
    private TranslateTransform? _trollSceneTranslate;
    private RotateTransform? _trollSceneRotate;
    private readonly List<TrollActorState> _trollOverlayActors = [];

    private static readonly string[] TrollSprites = ["🧌", "🪓", "💥", "🔥", "⚒", "🕳"];

    private sealed class TrollActorState
    {
        public required TextBlock Sprite { get; init; }
        public required string Glyph { get; init; }
        public double X { get; set; }
        public double Y { get; set; }
        public double VX { get; set; }
        public double VY { get; set; }
        public double Angle { get; set; }
        public double Spin { get; set; }
    }

    private static readonly string[] RainbowPaletteKeys =
    [
        "PageBackgroundBrush",
        "PanelBackgroundBrush",
        "PanelBorderBrush",
        "TextPrimaryBrush",
        "TextMutedBrush",
        "AccentBrush",
        "AccentTextBrush",
        "AccentSoftBrush",
        "AccentStrongBrush",
        "SurfaceTopBrush",
        "SurfaceBottomBrush",
        "SurfaceSoftBrush"
    ];

    private readonly List<UsbIpDeviceInfo> _usbDevices = [];
    private readonly ConcurrentDictionary<string, UsbDeviceMetadataEntry> _hostUsbMetadataByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _hostUsbDescriptionsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, UsbDeviceAttachmentEntry> _hostUsbAttachmentsByBusId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _usbAutoConnectBackoffUntilUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _usbHeartbeatLastSentByBusId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _usbLastAttachSuccessUtcByBusId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usbDetachHintSuppressionUntilUtcByBusId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usbManualDisconnectPendingByBusId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc, int Count, DateTimeOffset LastSignalUtc)> _usbAlreadyExportedNotAttachedStateByBusId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc, int Count, DateTimeOffset LastSignalUtc)> _usbPotentialLocalDetachStateByBusId = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectedUsbBusId;
    private bool _isUsbClientAvailable;
    private bool _usbClientMissingLogged;
    private bool _currentUsbTransportUseHyperVSocket = true;
    private HyperVSocketUsbGuestProxy? _usbHyperVSocketProxy;
    private bool _usbHyperVSocketTransportActive;
    private bool _usbIpFallbackActive;
    private bool _usbHyperVSocketServiceReachable;
    private int _usbHyperVSocketProbeFailureCount;
    private DateTimeOffset _usbHyperVSocketProxyLastRestartAttemptUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastHyperVDataPathSuccessUtc = DateTimeOffset.MinValue;
    private int _hyperVDataPathConsecutiveFailures;
    private CancellationTokenSource? _usbDiagnosticsCts;
    private Task? _usbDiagnosticsTask;
    private CancellationTokenSource? _usbAutoRefreshCts;
    private Task? _usbAutoRefreshTask;
    private CancellationTokenSource? _usbChangeNotificationSubscriberCts;
    private Task? _usbChangeNotificationSubscriberTask;
    private CancellationTokenSource? _usbPushFollowUpRefreshCts;
    private CancellationTokenSource? _hyperVSocketMonitorCts;
    private Task? _hyperVSocketMonitorTask;
    private readonly GuestWinFspMountService _winFspMountService = GuestWinFspMountRegistry.Instance;
    private readonly SemaphoreSlim _sharedFolderReconnectGate = new(1, 1);
    private readonly SemaphoreSlim _sharedFolderReconnectTriggerGate = new(1, 1);
    private CancellationTokenSource? _sharedFolderAutoMountCts;
    private Task? _sharedFolderAutoMountTask;
    private readonly SemaphoreSlim _usbRefreshGate = new(1, 1);
    private int _usbRefreshReplayRequested;
    private readonly HyperVEventChannel _hyperVEventChannel = new();
    private DateTimeOffset _lastUsbRefreshCompletedUtc = DateTimeOffset.MinValue;
    private readonly GuestResourceMonitorAgent _resourceMonitorAgent = new();
    private readonly SystemResourceSampler _diagnosticsResourceSampler = new();
    private readonly ConcurrentDictionary<string, RateLimitedWarnState> _rateLimitedWarnStates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usbStaleExportHintLastSentUtc = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _sharedFolderReconnectLastRunUtc;
    private string _sharedFolderReconnectLastSummary = "Noch kein Lauf";
    private DateTimeOffset? _sharedFolderCatalogReadyLastLoggedUtc;
    private string _sharedFolderCatalogReadyLastStateKey = string.Empty;
    private readonly ConcurrentQueue<DateTimeOffset> _configSaveTimestampsUtc = new();
    private DateTimeOffset? _usbRefreshSuccessLastLoggedUtc;
    private string _usbRefreshSuccessLastStateKey = string.Empty;
    private DateTimeOffset _suppressAutoUsbDisconnectSignalsUntilUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _suppressUsbListClearingUntilUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastUsbBackgroundRefreshAttemptUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastHostIdentityBackgroundAttemptUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastUsbPushRefreshTriggeredUtc = DateTimeOffset.MinValue;
    private readonly object _hyperVSocketMetricsSync = new();
    private long _metricHostIdentityFetchAttempts;
    private long _metricHostIdentityFetchSuccesses;
    private long _metricHostIdentityFetchFailures;
    private long _metricSharedFolderCatalogFetchAttempts;
    private long _metricSharedFolderCatalogFetchSuccesses;
    private long _metricSharedFolderCatalogFetchFailures;
    private long _metricTransportSelfTestRuns;
    private long _metricTransportSelfTestFailures;
    private long _metricDiagnosticsAckSent;
    private long _metricDiagnosticsAckFailed;
    private long _metricUsbEventAckSent;
    private long _metricUsbEventAckFailed;
    private long _metricHostResolutionCalls;
    private long _metricHostResolutionHyperV;
    private long _metricHostResolutionOther;
    private long _metricHostResolutionFailures;
    private DateTimeOffset? _lastHostIdentitySuccessUtc;
    private DateTimeOffset? _lastSharedFolderCatalogSuccessUtc;
    private DateTimeOffset? _lastDiagnosticsAckSuccessUtc;
    private DateTimeOffset? _lastUsbEventAckSuccessUtc;
    private DateTimeOffset? _lastTransportSelfTestUtc;
    private string _lastHostIdentityFailure = string.Empty;
    private string _lastSharedFolderCatalogFailure = string.Empty;
    private string _lastDiagnosticsAckFailure = string.Empty;
    private string _lastUsbEventAckFailure = string.Empty;
    private string _lastTransportSelfTestFailure = string.Empty;

    private sealed class RateLimitedWarnState
    {
        public DateTimeOffset LastLoggedAtUtc { get; set; }
        public int SuppressedCount { get; set; }
    }

    private sealed class SharedFolderReconnectCycleResult
    {
        public int Attempted { get; init; }
        public int NewlyMounted { get; init; }
        public int Failed { get; init; }
    }

    public App()
    {
        Microsoft.UI.Xaml.Application.LoadComponent(this, new Uri("ms-appx:///App.xaml"));

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            GuestLogger.Error("runtime.unhandled", ex?.Message ?? "Unbekannter Laufzeitfehler", new
            {
                isTerminating = e.IsTerminating,
                exceptionType = ex?.GetType().FullName,
                stackTrace = ex?.StackTrace,
                exception = ex?.ToString()
            });
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            var ex = e.Exception;
            GuestLogger.Error("runtime.task_unobserved", ex?.Message ?? "Unbeobachtete Task-Exception", new
            {
                exceptionType = ex?.GetType().FullName,
                stackTrace = ex?.StackTrace,
                exception = ex?.ToString()
            });
            e.SetObserved();
        };

        UnhandledException += (_, e) =>
        {
            var ex = e.Exception;
            GuestLogger.Error("ui.unhandled", ex?.Message ?? "Unbekannter UI-Fehler", new
            {
                exceptionType = ex?.GetType().FullName,
                stackTrace = ex?.StackTrace,
                exception = ex?.ToString()
            });
            e.Handled = true;
        };
    }

    private static void EnsureControlResources()
    {
        if (Current is not Application app)
        {
            return;
        }

        var resources = app.Resources;

        EnsureBrushResource(resources, "TabViewScrollButtonBackground", Color.FromArgb(0x00, 0x00, 0x00, 0x00));
        EnsureBrushResource(resources, "TabViewScrollButtonBackgroundPointerOver", Color.FromArgb(0x1F, 0x80, 0x80, 0x80));
        EnsureBrushResource(resources, "TabViewScrollButtonBackgroundPressed", Color.FromArgb(0x33, 0x80, 0x80, 0x80));
        EnsureBrushResource(resources, "TabViewScrollButtonForeground", Colors.WhiteSmoke);
        EnsureBrushResource(resources, "TabViewScrollButtonForegroundPointerOver", Colors.White);
        EnsureBrushResource(resources, "TabViewScrollButtonForegroundPressed", Colors.White);
        EnsureBrushResource(resources, "TabViewScrollButtonForegroundDisabled", Color.FromArgb(0x8F, 0xB0, 0xB0, 0xB0));

        EnsureBrushResource(resources, "TabViewButtonBackground", Color.FromArgb(0x00, 0x00, 0x00, 0x00));
        EnsureBrushResource(resources, "TabViewButtonBackgroundPointerOver", Color.FromArgb(0x1F, 0x90, 0x90, 0x90));
        EnsureBrushResource(resources, "TabViewButtonBackgroundPressed", Color.FromArgb(0x33, 0x90, 0x90, 0x90));
        EnsureBrushResource(resources, "TabViewButtonForeground", Colors.WhiteSmoke);
        EnsureBrushResource(resources, "TabViewButtonForegroundPointerOver", Colors.White);
        EnsureBrushResource(resources, "TabViewButtonForegroundPressed", Colors.White);
        EnsureBrushResource(resources, "TabViewButtonForegroundDisabled", Color.FromArgb(0x7F, 0xA5, 0xA5, 0xA5));
        EnsureBrushResource(resources, "TabViewButtonBorderBrush", Color.FromArgb(0x00, 0x00, 0x00, 0x00));
        EnsureBrushResource(resources, "TabViewButtonBorderBrushPointerOver", Color.FromArgb(0x3F, 0x90, 0x90, 0x90));
        EnsureBrushResource(resources, "TabViewButtonBorderBrushPressed", Color.FromArgb(0x55, 0x90, 0x90, 0x90));

        try
        {
            if (!resources.MergedDictionaries.OfType<XamlControlsResources>().Any())
            {
                resources.MergedDictionaries.Add(new XamlControlsResources());
            }
        }
        catch
        {
        }
    }

    private static void EnsureBrushResource(ResourceDictionary resources, string key, Color color)
    {
        if (resources.ContainsKey(key))
        {
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    public async Task TriggerRainbowPrincessModeAsync()
    {
        _rainbowPrincessModeCts?.Cancel();

        var cts = new CancellationTokenSource();
        _rainbowPrincessModeCts = cts;
        var startedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            while (DateTimeOffset.UtcNow - startedAtUtc < TimeSpan.FromSeconds(30))
            {
                cts.Token.ThrowIfCancellationRequested();

                var seconds = (DateTimeOffset.UtcNow - startedAtUtc).TotalSeconds;
                ApplyRainbowPrincessPalette(seconds);

                await Task.Delay(95, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_rainbowPrincessModeCts, cts))
            {
                _rainbowPrincessModeCts = null;
                RestoreConfiguredThemeAfterRainbowMode();
            }
        }
    }

    private void ApplyRainbowPrincessPalette(double timeSeconds)
    {
        if (Current?.Resources is not ResourceDictionary resources)
        {
            return;
        }

        var baseHue = (timeSeconds * 160d) % 360d;

        foreach (var key in RainbowPaletteKeys)
        {
            var offset = (Math.Abs(key.GetHashCode()) % 360 + baseHue) % 360;
            var saturation = key.Contains("Text", StringComparison.Ordinal) ? 0.24 : 0.76;
            var value = key.Contains("Background", StringComparison.Ordinal) ? 0.33 : 0.92;
            var alpha = key == "AccentSoftBrush" ? (byte)0x8A : (byte)0xFF;
            var color = HsvToColor(offset, saturation, value, alpha);
            SetBrushColorValue(resources, key, color);
        }

        if (_mainWindow?.AppWindow?.TitleBar is AppWindowTitleBar titleBar)
        {
            var titleColor = HsvToColor((baseHue + 35d) % 360d, 0.82, 0.66, 0xFF);
            var textColor = HsvToColor((baseHue + 195d) % 360d, 0.16, 0.99, 0xFF);

            titleBar.BackgroundColor = titleColor;
            titleBar.InactiveBackgroundColor = titleColor;
            titleBar.ButtonBackgroundColor = titleColor;
            titleBar.ButtonHoverBackgroundColor = HsvToColor((baseHue + 58d) % 360d, 0.90, 0.74, 0xFF);
            titleBar.ButtonPressedBackgroundColor = HsvToColor((baseHue + 72d) % 360d, 0.95, 0.58, 0xFF);
            titleBar.ForegroundColor = textColor;
            titleBar.InactiveForegroundColor = textColor;
            titleBar.ButtonForegroundColor = textColor;
            titleBar.ButtonHoverForegroundColor = Colors.White;
            titleBar.ButtonPressedForegroundColor = Colors.White;
        }
    }

    private void RestoreConfiguredThemeAfterRainbowMode()
    {
        if (_mainWindow is null)
        {
            return;
        }

        var configuredTheme = _config?.Ui.Theme ?? "dark";
        _mainWindow.ApplyTheme(configuredTheme);
    }

    public async Task TriggerTrollModeAsync()
    {
        _rainbowPrincessModeCts?.Cancel();
        _trollModeCts?.Cancel();
        ExtendAutoUsbDisconnectSignalSuppression(TimeSpan.FromSeconds(90), "troll-mode");
        ExtendUsbListClearingSuppression(TimeSpan.FromSeconds(120), "troll-mode");

        var cts = new CancellationTokenSource();
        _trollModeCts = cts;
        var startedAtUtc = DateTimeOffset.UtcNow;
        PointInt32? basePosition = null;

        try
        {
            if (_mainWindow?.AppWindow is AppWindow appWindow)
            {
                basePosition = appWindow.Position;
            }

            EnsureTrollOverlayLayer();
            ResetTrollOverlayScene();
        }
        catch
        {
        }

        try
        {
            while (DateTimeOffset.UtcNow - startedAtUtc < TimeSpan.FromSeconds(30))
            {
                cts.Token.ThrowIfCancellationRequested();

                var seconds = (DateTimeOffset.UtcNow - startedAtUtc).TotalSeconds;
                ApplyTrollPalette(seconds);
                UpdateTrollOverlayScene(seconds);
                ApplyTrollSceneWarp(seconds);

                await Task.Delay(80, cts.Token);
            }

            await PlayTrollRecoveryMomentAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_trollModeCts, cts))
            {
                _trollModeCts = null;

                try
                {
                    await FadeOutTrollOverlayForRestartAsync();
                }
                catch
                {
                }

                HideTrollOverlay();
                RestoreMainWindowPosition(basePosition);

                var reloaded = false;
                try
                {
                    var configuredTheme = _config?.Ui.Theme ?? "dark";
                    await RestartForThemeChangeAsync(configuredTheme);
                    reloaded = true;
                }
                catch
                {
                }

                if (!reloaded)
                {
                    RestoreConfiguredThemeAfterRainbowMode();
                }

                SafeFireAndForget.Run(
                    SchedulePostTrollUsbRefreshAsync(),
                    operation: "usb-refresh-post-troll-mode");
            }
        }
    }

    private async Task SchedulePostTrollUsbRefreshAsync()
    {
        if (_isExitRequested)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(900));
            await RefreshUsbDevicesAsync(emitLogs: false, bypassRateLimit: true, forceHostIdentityRefresh: true);

            await Task.Delay(TimeSpan.FromMilliseconds(1300));
            await RefreshUsbDevicesAsync(emitLogs: false, bypassRateLimit: true, forceHostIdentityRefresh: true);
        }
        catch (Exception ex)
        {
            GuestLogger.Debug("usb.refresh.post_troll.failed", ex.Message, new
            {
                exceptionType = ex.GetType().FullName
            });
        }
    }

    private void ApplyTrollPalette(double seconds)
    {
        if (Current?.Resources is not ResourceDictionary resources)
        {
            return;
        }

        Color pageColor;
        Color panelColor;
        Color borderColor;
        Color accentColor;
        Color accentStrongColor;
        Color textPrimaryColor;
        Color textMutedColor;

        if (seconds < 8d)
        {
            var dim = Math.Clamp(seconds / 8d, 0d, 1d);
            pageColor = MixColor(Color.FromArgb(0xFF, 0x16, 0x20, 0x34), Color.FromArgb(0xFF, 0x10, 0x10, 0x12), dim);
            panelColor = MixColor(Color.FromArgb(0xFF, 0x24, 0x2E, 0x45), Color.FromArgb(0xFF, 0x1D, 0x1D, 0x21), dim);
            borderColor = MixColor(Color.FromArgb(0xFF, 0x38, 0x46, 0x5E), Color.FromArgb(0xFF, 0x44, 0x44, 0x49), dim);
            accentColor = MixColor(Color.FromArgb(0xFF, 0x72, 0xC6, 0xFF), Color.FromArgb(0xFF, 0x7C, 0x89, 0x72), dim);
            accentStrongColor = MixColor(accentColor, Color.FromArgb(0xFF, 0x96, 0x9F, 0x89), 0.4d);
            textPrimaryColor = MixColor(Color.FromArgb(0xFF, 0xE8, 0xF0, 0xFF), Color.FromArgb(0xFF, 0xD2, 0xD2, 0xD4), dim);
            textMutedColor = MixColor(Color.FromArgb(0xFF, 0xA4, 0xB2, 0xC8), Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9F), dim);
        }
        else if (seconds < 20d)
        {
            var pulse = (Math.Sin(seconds * 6d) + 1d) / 2d;
            var impactFlash = ((int)(seconds * 5d) % 9 == 0) ? 1d : 0d;
            pageColor = MixColor(Color.FromArgb(0xFF, 0x0F, 0x0F, 0x11), Color.FromArgb(0xFF, 0x1B, 0x14, 0x14), impactFlash * 0.55d);
            panelColor = MixColor(Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1C), Color.FromArgb(0xFF, 0x26, 0x1B, 0x1A), impactFlash * 0.55d);
            borderColor = MixColor(Color.FromArgb(0xFF, 0x45, 0x45, 0x49), Color.FromArgb(0xFF, 0x8A, 0x53, 0x3B), impactFlash * 0.70d);
            accentColor = MixColor(Color.FromArgb(0xFF, 0x7B, 0xB1, 0x56), Color.FromArgb(0xFF, 0xC7, 0x6A, 0x28), pulse * 0.5d + impactFlash * 0.35d);
            accentStrongColor = MixColor(accentColor, Color.FromArgb(0xFF, 0xE5, 0x9A, 0x44), pulse * 0.35d + impactFlash * 0.5d);
            textPrimaryColor = Color.FromArgb(0xFF, 0xD7, 0xD7, 0xD9);
            textMutedColor = Color.FromArgb(0xFF, 0xAA, 0xAA, 0xAF);
        }
        else if (seconds < 25d)
        {
            var blast = (Math.Sin(seconds * 18d) + 1d) / 2d;
            pageColor = MixColor(Color.FromArgb(0xFF, 0x0E, 0x0E, 0x10), Color.FromArgb(0xFF, 0x2C, 0x12, 0x0F), blast * 0.72d);
            panelColor = MixColor(Color.FromArgb(0xFF, 0x17, 0x17, 0x1A), Color.FromArgb(0xFF, 0x3A, 0x1A, 0x14), blast * 0.72d);
            borderColor = MixColor(Color.FromArgb(0xFF, 0x4A, 0x4A, 0x4F), Color.FromArgb(0xFF, 0xD5, 0x6C, 0x2E), blast * 0.90d);
            accentColor = MixColor(Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A), Color.FromArgb(0xFF, 0xF0, 0x7D, 0x2D), blast * 0.92d);
            accentStrongColor = MixColor(accentColor, Color.FromArgb(0xFF, 0xFF, 0xC8, 0x72), blast * 0.58d);
            textPrimaryColor = MixColor(Color.FromArgb(0xFF, 0xD0, 0xD0, 0xD2), Color.FromArgb(0xFF, 0xFF, 0xD5, 0xBA), blast * 0.36d);
            textMutedColor = MixColor(Color.FromArgb(0xFF, 0xA4, 0xA4, 0xA8), Color.FromArgb(0xFF, 0xD8, 0x9C, 0x79), blast * 0.4d);
        }
        else
        {
            var craterPulse = (Math.Sin(seconds * 4d) + 1d) / 2d;
            pageColor = Color.FromArgb(0xFF, 0x06, 0x06, 0x07);
            panelColor = MixColor(Color.FromArgb(0xFF, 0x11, 0x11, 0x13), Color.FromArgb(0xFF, 0x17, 0x13, 0x18), craterPulse * 0.35d);
            borderColor = MixColor(Color.FromArgb(0xFF, 0x34, 0x34, 0x39), Color.FromArgb(0xFF, 0x52, 0x3D, 0x57), craterPulse * 0.45d);
            accentColor = MixColor(Color.FromArgb(0xFF, 0x6A, 0x5A, 0x73), Color.FromArgb(0xFF, 0x8D, 0x6B, 0x47), craterPulse * 0.45d);
            accentStrongColor = MixColor(accentColor, Color.FromArgb(0xFF, 0xB7, 0x8A, 0x55), craterPulse * 0.35d);
            textPrimaryColor = Color.FromArgb(0xFF, 0xCA, 0xCA, 0xCD);
            textMutedColor = Color.FromArgb(0xFF, 0x95, 0x95, 0x9A);
        }

        SetBrushColorValue(resources, "PageBackgroundBrush", pageColor);
        SetBrushColorValue(resources, "PanelBackgroundBrush", panelColor);
        SetBrushColorValue(resources, "PanelBorderBrush", borderColor);
        SetBrushColorValue(resources, "TextPrimaryBrush", textPrimaryColor);
        SetBrushColorValue(resources, "TextMutedBrush", textMutedColor);
        SetBrushColorValue(resources, "AccentBrush", accentColor);
        SetBrushColorValue(resources, "AccentStrongBrush", accentStrongColor);
        SetBrushColorValue(resources, "AccentTextBrush", Color.FromArgb(0xFF, 0x18, 0x18, 0x1A));
        SetBrushColorValue(resources, "SurfaceTopBrush", MixColor(panelColor, pageColor, 0.38d));
        SetBrushColorValue(resources, "SurfaceBottomBrush", MixColor(pageColor, Color.FromArgb(0xFF, 0x05, 0x05, 0x06), 0.35d));
        SetBrushColorValue(resources, "SurfaceSoftBrush", MixColor(panelColor, pageColor, 0.24d));
        SetBrushColorValue(resources, "AccentSoftBrush", MixColor(Color.FromArgb(0x74, accentColor.R, accentColor.G, accentColor.B), Color.FromArgb(0x74, 0xA5, 0x55, 0x2D), seconds >= 20d ? 0.45d : 0.2d));

        if (_mainWindow?.AppWindow?.TitleBar is AppWindowTitleBar titleBar)
        {
            var titleBg = MixColor(panelColor, pageColor, 0.42d);
            var titleFg = textPrimaryColor;
            titleBar.BackgroundColor = titleBg;
            titleBar.InactiveBackgroundColor = titleBg;
            titleBar.ButtonBackgroundColor = titleBg;
            titleBar.ButtonHoverBackgroundColor = MixColor(titleBg, accentColor, 0.35d);
            titleBar.ButtonPressedBackgroundColor = MixColor(titleBg, accentStrongColor, 0.45d);
            titleBar.ForegroundColor = titleFg;
            titleBar.InactiveForegroundColor = titleFg;
            titleBar.ButtonForegroundColor = titleFg;
            titleBar.ButtonHoverForegroundColor = Colors.White;
            titleBar.ButtonPressedForegroundColor = Colors.White;
        }
    }

    private void EnsureTrollOverlayLayer()
    {
        if (_mainWindow is null || _mainWindow.Content is not UIElement currentContent)
        {
            return;
        }

        if (_trollOverlayCanvas is not null && _trollOverlayHost is not null)
        {
            return;
        }

        Grid hostGrid;
        var sceneContainer = new Grid();
        if (currentContent is Grid existingGrid)
        {
            hostGrid = existingGrid;
            CopyGridLayout(existingGrid, sceneContainer);

            var existingChildren = existingGrid.Children.ToList();
            existingGrid.Children.Clear();
            foreach (var child in existingChildren)
            {
                sceneContainer.Children.Add(child);
            }

            if (existingGrid.RowDefinitions.Count > 0)
            {
                Grid.SetRowSpan(sceneContainer, existingGrid.RowDefinitions.Count);
            }

            if (existingGrid.ColumnDefinitions.Count > 0)
            {
                Grid.SetColumnSpan(sceneContainer, existingGrid.ColumnDefinitions.Count);
            }

            existingGrid.Children.Add(sceneContainer);
            _trollSceneTarget = sceneContainer;
        }
        else
        {
            hostGrid = new Grid();
            _mainWindow.Content = hostGrid;
            sceneContainer.Children.Add(currentContent);
            hostGrid.Children.Add(sceneContainer);
            _trollSceneTarget = sceneContainer;
        }

        _trollOverlayHost = hostGrid;
        _trollOverlayDimmer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x05, 0x05, 0x06)),
            Opacity = 0,
            IsHitTestVisible = false
        };

        _trollOverlayCrater = new Border
        {
            Width = 300,
            Height = 210,
            CornerRadius = new CornerRadius(160),
            BorderThickness = new Thickness(3),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0x8F, 0x5C, 0x37)),
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x03, 0x03, 0x04)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = new RotateTransform()
        };

        _trollOverlayCanvas = new Canvas
        {
            Opacity = 0,
            IsHitTestVisible = false
        };

        _trollOverlayBoss = new TextBlock
        {
            Text = "🧌",
            FontSize = 230,
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = new ScaleTransform()
        };

        _trollOverlayStatus = new TextBlock
        {
            Text = string.Empty,
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 26, 0, 0),
            IsHitTestVisible = false,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xC1, 0x88))
        };

        if (_trollSceneTarget is not null)
        {
            _trollSceneTranslate = new TranslateTransform();
            _trollSceneRotate = new RotateTransform();

            var group = new TransformGroup();
            group.Children.Add(_trollSceneRotate);
            group.Children.Add(_trollSceneTranslate);

            _trollSceneTarget.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            _trollSceneTarget.RenderTransform = group;
        }

        Canvas.SetZIndex(_trollOverlayDimmer, 7000);
        Canvas.SetZIndex(_trollOverlayCrater, 7001);
        Canvas.SetZIndex(_trollOverlayCanvas, 7002);
        Canvas.SetZIndex(_trollOverlayBoss, 7003);
        Canvas.SetZIndex(_trollOverlayStatus, 7004);

        ApplyOverlayElementPlacement(hostGrid, _trollOverlayDimmer);
        ApplyOverlayElementPlacement(hostGrid, _trollOverlayCrater);
        ApplyOverlayElementPlacement(hostGrid, _trollOverlayCanvas);
        ApplyOverlayElementPlacement(hostGrid, _trollOverlayBoss);
        ApplyOverlayElementPlacement(hostGrid, _trollOverlayStatus);

        hostGrid.Children.Add(_trollOverlayDimmer);
        hostGrid.Children.Add(_trollOverlayCrater);
        hostGrid.Children.Add(_trollOverlayCanvas);
        hostGrid.Children.Add(_trollOverlayBoss);
        hostGrid.Children.Add(_trollOverlayStatus);
    }

    private static void ApplyOverlayElementPlacement(Grid hostGrid, FrameworkElement element)
    {
        var rowSpan = Math.Max(1, hostGrid.RowDefinitions.Count);
        var columnSpan = Math.Max(1, hostGrid.ColumnDefinitions.Count);

        Grid.SetRow(element, 0);
        Grid.SetColumn(element, 0);
        Grid.SetRowSpan(element, rowSpan);
        Grid.SetColumnSpan(element, columnSpan);
    }

    private static void CopyGridLayout(Grid source, Grid target)
    {
        target.RowSpacing = source.RowSpacing;
        target.ColumnSpacing = source.ColumnSpacing;

        target.RowDefinitions.Clear();
        foreach (var row in source.RowDefinitions)
        {
            target.RowDefinitions.Add(new RowDefinition
            {
                Height = row.Height,
                MinHeight = row.MinHeight,
                MaxHeight = row.MaxHeight
            });
        }

        target.ColumnDefinitions.Clear();
        foreach (var column in source.ColumnDefinitions)
        {
            target.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = column.Width,
                MinWidth = column.MinWidth,
                MaxWidth = column.MaxWidth
            });
        }
    }

    private void ResetTrollOverlayScene()
    {
        if (_trollOverlayCanvas is null)
        {
            return;
        }

        _trollOverlayCanvas.Children.Clear();
        _trollOverlayActors.Clear();

        if (_trollOverlayDimmer is not null)
        {
            _trollOverlayDimmer.Opacity = 0;
        }

        if (_trollOverlayCrater is not null)
        {
            _trollOverlayCrater.Opacity = 0;
        }

        if (_trollOverlayBoss is not null)
        {
            _trollOverlayBoss.Opacity = 0;
        }

        if (_trollOverlayStatus is not null)
        {
            _trollOverlayStatus.Opacity = 0;
            _trollOverlayStatus.Text = string.Empty;
        }

        _trollOverlayCanvas.Opacity = 0;
    }

    private void UpdateTrollOverlayScene(double seconds)
    {
        if (_trollOverlayHost is null || _trollOverlayCanvas is null)
        {
            return;
        }

        var width = _trollOverlayHost.ActualWidth;
        var height = _trollOverlayHost.ActualHeight;
        if (width < 64 || height < 64)
        {
            return;
        }

        var dimOpacity = seconds switch
        {
            < 8d => 0.16 + (seconds / 8d) * 0.32,
            < 20d => 0.46,
            < 25d => 0.62,
            _ => 0.66
        };

        var blastFlash = seconds >= 18d && ((int)(seconds * 15d) % 11 == 0);

        if (_trollOverlayDimmer is not null)
        {
            _trollOverlayDimmer.Opacity = dimOpacity;
            if (_trollOverlayDimmer.Background is SolidColorBrush dimBrush)
            {
                dimBrush.Color = blastFlash
                    ? Color.FromArgb(0xEE, 0x30, 0x13, 0x0C)
                    : Color.FromArgb(0xFF, 0x05, 0x05, 0x06);
            }
        }

        _trollOverlayCanvas.Opacity = seconds >= 5d ? 1 : 0;

        if (_trollOverlayCrater is not null)
        {
            if (seconds >= 22d)
            {
                var pulse = (Math.Sin(seconds * 5d) + 1d) / 2d;
                _trollOverlayCrater.Opacity = 0.45 + pulse * 0.35;
                _trollOverlayCrater.Width = 260 + pulse * 120;
                _trollOverlayCrater.Height = 180 + pulse * 90;

                if (_trollOverlayCrater.RenderTransform is RotateTransform rotate)
                {
                    rotate.Angle = Math.Sin(seconds * 1.7d) * 4d;
                }
            }
            else
            {
                _trollOverlayCrater.Opacity = 0;
            }
        }

        var targetActors = seconds switch
        {
            < 8d => 10,
            < 20d => 26,
            < 25d => 40,
            < 27d => 28,
            _ => 54
        };

        var isFinalBossPhase = seconds >= 27d;
        if (_trollOverlayBoss is not null)
        {
            if (isFinalBossPhase)
            {
                var pulse = (Math.Sin(seconds * 20d) + 1d) / 2d;
                _trollOverlayBoss.Opacity = 0.52 + pulse * 0.46;
                _trollOverlayBoss.Text = pulse > 0.62 ? "🧌" : "💥";
                if (_trollOverlayBoss.RenderTransform is ScaleTransform scale)
                {
                    scale.ScaleX = 1.12 + pulse * 0.82;
                    scale.ScaleY = 1.12 + pulse * 0.82;
                }
            }
            else
            {
                _trollOverlayBoss.Opacity = 0;
            }
        }

        if (_trollOverlayStatus is not null)
        {
            if (isFinalBossPhase)
            {
                _trollOverlayStatus.Text = "TROLL CORE MELTDOWN";
                _trollOverlayStatus.Opacity = 0.88;
                if (_trollOverlayStatus.Foreground is SolidColorBrush fg)
                {
                    fg.Color = ((int)(seconds * 18d) % 2 == 0)
                        ? Color.FromArgb(0xFF, 0xFF, 0xB3, 0x6A)
                        : Color.FromArgb(0xFF, 0xFF, 0xE2, 0xA8);
                }
            }
            else
            {
                _trollOverlayStatus.Opacity = 0;
                _trollOverlayStatus.Text = string.Empty;
            }
        }

        while (_trollOverlayActors.Count < targetActors)
        {
            SpawnTrollActor(width, height);
        }

        while (_trollOverlayActors.Count > targetActors && _trollOverlayActors.Count > 0)
        {
            var last = _trollOverlayActors[^1];
            _trollOverlayCanvas.Children.Remove(last.Sprite);
            _trollOverlayActors.RemoveAt(_trollOverlayActors.Count - 1);
        }

        foreach (var actor in _trollOverlayActors)
        {
            var velocityBoost = seconds >= 20d ? 1.55d : (seconds >= 12d ? 1.2d : 1d);
            actor.X += actor.VX * velocityBoost;
            actor.Y += actor.VY * velocityBoost;
            actor.Angle += actor.Spin;

            if (string.Equals(actor.Glyph, "🔥", StringComparison.Ordinal))
            {
                actor.Y -= (seconds >= 20d ? 0.9d : 0.35d);
            }

            if (string.Equals(actor.Glyph, "💥", StringComparison.Ordinal)
                && seconds >= 18d)
            {
                actor.Angle += actor.Spin * 0.8d;
            }

            if (actor.X < -80)
            {
                actor.X = width + 20;
            }
            else if (actor.X > width + 40)
            {
                actor.X = -60;
            }

            if (actor.Y < -80)
            {
                actor.Y = height + 20;
            }
            else if (actor.Y > height + 40)
            {
                actor.Y = -60;
            }

            if (actor.Sprite.RenderTransform is RotateTransform rotate)
            {
                rotate.Angle = actor.Angle;
            }

            Canvas.SetLeft(actor.Sprite, actor.X);
            Canvas.SetTop(actor.Sprite, actor.Y);
        }
    }

    private void SpawnTrollActor(double width, double height)
    {
        if (_trollOverlayCanvas is null)
        {
            return;
        }

        var glyph = TrollSprites[_trollRandom.Next(TrollSprites.Length)];
        var spriteSize = glyph switch
        {
            "🧌" => _trollRandom.Next(64, 110),
            "🔥" => _trollRandom.Next(52, 94),
            "💥" => _trollRandom.Next(58, 102),
            _ => _trollRandom.Next(42, 86)
        };

        var sprite = new TextBlock
        {
            Text = glyph,
            FontSize = spriteSize,
            Opacity = _trollRandom.NextDouble() * 0.35 + 0.58,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = new RotateTransform()
        };

        var actor = new TrollActorState
        {
            Sprite = sprite,
            Glyph = glyph,
            X = _trollRandom.NextDouble() * Math.Max(80, width - 40),
            Y = _trollRandom.NextDouble() * Math.Max(80, height - 40),
            VX = (_trollRandom.NextDouble() * 4.2d + 0.9d) * (_trollRandom.Next(0, 2) == 0 ? -1 : 1),
            VY = (_trollRandom.NextDouble() * 3.0d + 0.5d) * (_trollRandom.Next(0, 2) == 0 ? -1 : 1),
            Angle = _trollRandom.NextDouble() * 360d,
            Spin = (_trollRandom.NextDouble() * 7d + 1.2d) * (_trollRandom.Next(0, 2) == 0 ? -1 : 1)
        };

        _trollOverlayCanvas.Children.Add(sprite);
        _trollOverlayActors.Add(actor);
        Canvas.SetLeft(sprite, actor.X);
        Canvas.SetTop(sprite, actor.Y);
    }

    private void HideTrollOverlay()
    {
        ResetTrollSceneWarp();

        if (_trollOverlayHost is not null)
        {
            if (_trollOverlayDimmer is not null)
            {
                _trollOverlayHost.Children.Remove(_trollOverlayDimmer);
            }

            if (_trollOverlayCrater is not null)
            {
                _trollOverlayHost.Children.Remove(_trollOverlayCrater);
            }

            if (_trollOverlayCanvas is not null)
            {
                _trollOverlayHost.Children.Remove(_trollOverlayCanvas);
            }

            if (_trollOverlayBoss is not null)
            {
                _trollOverlayHost.Children.Remove(_trollOverlayBoss);
            }

            if (_trollOverlayStatus is not null)
            {
                _trollOverlayHost.Children.Remove(_trollOverlayStatus);
            }
        }

        if (_trollOverlayCanvas is not null)
        {
            _trollOverlayCanvas.Children.Clear();
            _trollOverlayCanvas.Opacity = 0;
        }

        _trollOverlayActors.Clear();

        if (_trollOverlayCrater is not null)
        {
            _trollOverlayCrater.Opacity = 0;
        }

        if (_trollOverlayDimmer is not null)
        {
            _trollOverlayDimmer.Opacity = 0;
        }

        if (_trollOverlayBoss is not null)
        {
            _trollOverlayBoss.Opacity = 0;
        }

        if (_trollOverlayStatus is not null)
        {
            _trollOverlayStatus.Opacity = 0;
            _trollOverlayStatus.Text = string.Empty;
        }

        _trollOverlayHost = null;
        _trollOverlayDimmer = null;
        _trollOverlayCrater = null;
        _trollOverlayCanvas = null;
        _trollOverlayBoss = null;
        _trollOverlayStatus = null;
        _trollSceneTarget = null;
        _trollSceneTranslate = null;
        _trollSceneRotate = null;
    }

    private void ApplyTrollSceneWarp(double seconds)
    {
        if (_trollSceneTranslate is null || _trollSceneRotate is null)
        {
            return;
        }

        if (seconds < 4d)
        {
            ResetTrollSceneWarp();
            return;
        }

        var intensity = seconds switch
        {
            < 9d => 2.4d,
            < 20d => 5.4d,
            < 25d => 8.2d,
            < 27d => 4.2d,
            _ => 11.5d
        };

        var wobbleX = Math.Sin(seconds * 31d) * intensity
                      + Math.Sin(seconds * 67d) * (intensity * 0.45d);
        var wobbleY = Math.Cos(seconds * 28d) * (intensity * 0.75d)
                      + Math.Sin(seconds * 53d) * (intensity * 0.32d);

        _trollSceneTranslate.X = wobbleX;
        _trollSceneTranslate.Y = wobbleY;

        var maxAngle = intensity * 0.35d;
        _trollSceneRotate.Angle = Math.Clamp(Math.Sin(seconds * 9.5d) * maxAngle, -8d, 8d);
    }

    private void ResetTrollSceneWarp()
    {
        if (_trollSceneTranslate is not null)
        {
            _trollSceneTranslate.X = 0;
            _trollSceneTranslate.Y = 0;
        }

        if (_trollSceneRotate is not null)
        {
            _trollSceneRotate.Angle = 0;
        }
    }

    private async Task PlayTrollRecoveryMomentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_trollOverlayDimmer is not null)
        {
            _trollOverlayDimmer.Opacity = 0.42;
            if (_trollOverlayDimmer.Background is SolidColorBrush dimBrush)
            {
                dimBrush.Color = Color.FromArgb(0xEA, 0x09, 0x1B, 0x10);
            }
        }

        if (_trollOverlayBoss is not null)
        {
            _trollOverlayBoss.Text = "✅";
            _trollOverlayBoss.Opacity = 0.78;
            if (_trollOverlayBoss.RenderTransform is ScaleTransform scale)
            {
                scale.ScaleX = 1.12;
                scale.ScaleY = 1.12;
            }
        }

        if (_trollOverlayStatus is not null)
        {
            _trollOverlayStatus.Text = "SYSTEM RECOVERED";
            _trollOverlayStatus.Opacity = 0.96;
            if (_trollOverlayStatus.Foreground is SolidColorBrush fg)
            {
                fg.Color = Color.FromArgb(0xFF, 0x9D, 0xFF, 0xBE);
            }
        }

        await Task.Delay(900, cancellationToken);

        if (_trollOverlayBoss is not null)
        {
            _trollOverlayBoss.Opacity = 0;
        }

        if (_trollOverlayStatus is not null)
        {
            _trollOverlayStatus.Opacity = 0;
            _trollOverlayStatus.Text = string.Empty;
        }
    }

    private async Task FadeOutTrollOverlayForRestartAsync()
    {
        if (_trollOverlayDimmer is null)
        {
            return;
        }

        if (_trollOverlayDimmer.Background is SolidColorBrush dimBrush)
        {
            dimBrush.Color = Color.FromArgb(0xFF, 0x03, 0x03, 0x04);
        }

        var initialDim = _trollOverlayDimmer.Opacity;
        for (var step = 0; step < 7; step++)
        {
            var t = (step + 1) / 7d;
            _trollOverlayDimmer.Opacity = Math.Clamp(initialDim + (0.9d - initialDim) * t, 0d, 0.92d);

            if (_trollOverlayCanvas is not null)
            {
                _trollOverlayCanvas.Opacity = Math.Max(0d, _trollOverlayCanvas.Opacity * (1d - 0.2d));
            }

            if (_trollOverlayBoss is not null)
            {
                _trollOverlayBoss.Opacity = Math.Max(0d, _trollOverlayBoss.Opacity * (1d - 0.24d));
            }

            if (_trollOverlayStatus is not null)
            {
                _trollOverlayStatus.Opacity = Math.Max(0d, _trollOverlayStatus.Opacity * (1d - 0.24d));
            }

            await Task.Delay(24);
        }

        await Task.Delay(80);
    }

    private void ApplyTrollShake(double seconds, PointInt32? basePosition)
    {
        RestoreMainWindowPosition(basePosition);
    }

    private void RestoreMainWindowPosition(PointInt32? basePosition)
    {
        if (basePosition is null || _mainWindow?.AppWindow is not AppWindow appWindow)
        {
            return;
        }

        try
        {
            appWindow.Move(basePosition.Value);
        }
        catch
        {
        }
    }

    private static Color MixColor(Color from, Color to, double amount)
    {
        var t = Math.Clamp(amount, 0d, 1d);
        return Color.FromArgb(
            LerpByte(from.A, to.A, t),
            LerpByte(from.R, to.R, t),
            LerpByte(from.G, to.G, t),
            LerpByte(from.B, to.B, t));
    }

    private static byte LerpByte(byte from, byte to, double amount)
    {
        return (byte)Math.Clamp((int)Math.Round(from + (to - from) * amount), 0, 255);
    }

    private static void SetBrushColorValue(ResourceDictionary resources, string key, Color color)
    {
        if (resources.TryGetValue(key, out var existingValue) && existingValue is SolidColorBrush existingBrush)
        {
            existingBrush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private static Color HsvToColor(double hue, double saturation, double value, byte alpha)
    {
        var normalizedHue = hue % 360d;
        if (normalizedHue < 0d)
        {
            normalizedHue += 360d;
        }

        var chroma = value * saturation;
        var x = chroma * (1d - Math.Abs((normalizedHue / 60d) % 2d - 1d));
        var m = value - chroma;

        double rPrime;
        double gPrime;
        double bPrime;

        if (normalizedHue < 60d)
        {
            rPrime = chroma;
            gPrime = x;
            bPrime = 0d;
        }
        else if (normalizedHue < 120d)
        {
            rPrime = x;
            gPrime = chroma;
            bPrime = 0d;
        }
        else if (normalizedHue < 180d)
        {
            rPrime = 0d;
            gPrime = chroma;
            bPrime = x;
        }
        else if (normalizedHue < 240d)
        {
            rPrime = 0d;
            gPrime = x;
            bPrime = chroma;
        }
        else if (normalizedHue < 300d)
        {
            rPrime = x;
            gPrime = 0d;
            bPrime = chroma;
        }
        else
        {
            rPrime = chroma;
            gPrime = 0d;
            bPrime = x;
        }

        var r = (byte)Math.Clamp((int)Math.Round((rPrime + m) * 255d), 0, 255);
        var g = (byte)Math.Clamp((int)Math.Round((gPrime + m) * 255d), 0, 255);
        var b = (byte)Math.Clamp((int)Math.Round((bPrime + m) * 255d), 0, 255);
        return Color.FromArgb(alpha, r, g, b);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            EnsureControlResources();

            var parsedArgs = SplitArgs(args.Arguments);
            var skipStartScreen = parsedArgs.Any(arg => string.Equals(arg, "--skip-start-screen", StringComparison.OrdinalIgnoreCase));

            if (IsCommandMode(parsedArgs))
            {
                var code = await GuestCli.ExecuteAsync(parsedArgs);
                Environment.ExitCode = code;
                Exit();
                return;
            }

            if (!TryInitializeSingleInstance())
            {
                Exit();
                return;
            }

            _configPath = ResolveConfigPath(parsedArgs);
            GuestConfigService.TryMigrateLegacyConfig(_configPath);
            _config = GuestConfigService.LoadOrCreate(_configPath, out _);
            _config.Usb ??= new GuestUsbSettings();
            _config.Usb.UseHyperVSocket = true;
            _currentUsbTransportUseHyperVSocket = true;
            GuestLogger.Initialize(_config.Logging, _config.Ui.DebugLoggingEnabled);
            RegisterSessionEndingHook();
            GuestLogger.Info("ui.startup", "HyperTool Guest UI gestartet.", new
            {
                configPath = _configPath,
                skipStartScreen,
                startMinimized = _config.Ui.StartMinimized,
                minimizeToTray = _config.Ui.MinimizeToTray,
                debugLoggingEnabled = _config.Ui.DebugLoggingEnabled
            });
            GuestLogger.Debug("ui.startup.steps", "Initialisiere Guest-Hintergrunddienste.");
            await RefreshUsbClientAvailabilityAsync();
            UpdateUsbTransportBridge();
            StartUsbDiagnosticsLoop();
            StartUsbAutoRefreshLoop();
            StartUsbChangeNotificationSubscriber();
            StartSharedFolderAutoMountLoop();
            StartHyperVSocketMonitorLoop();
            StartResourceMonitorAgent();

            ApplyStartWithWindows(_config);

            _mainWindow = new GuestMainWindow(
                _config,
                RefreshUsbDevicesAsync,
                busId => ConnectUsbAsync(busId),
                DisconnectUsbAsync,
                SaveConfigAsync,
                ReloadConfigSnapshotAsync,
                RestartForThemeChangeAsync,
                ExitForUpdateInstallAsync,
                RunTransportDiagnosticsTestAsync,
                DiscoverUsbHostAddressAsync,
                FetchHostSharedFoldersAsync,
                _isUsbClientAvailable);
            // Keep UI and app state in sync when a window is newly created and
            // the in-memory USB list is already populated.
            UpdateUsbViews();
            _mainWindow.UpdateHostFeatureAvailability(
                usbFeatureEnabledByHost: _config.Usb?.HostFeatureEnabled != false,
                sharedFoldersFeatureEnabledByHost: _config.SharedFolders?.HostFeatureEnabled != false,
                hostName: _config.Usb?.HostName);
            UpdateUsbDiagnosticsPanel();
            UpdateSharedFolderReconnectStatusPanel();

            _mainWindow.AppWindow.Closing += OnMainWindowClosing;

            _minimizeToTray = _config.Ui.MinimizeToTray;
            _mainWindow.ApplyTheme(_config.Ui.Theme);

            TryInitializeTray();

            var startHiddenInTray = _config.Ui.StartMinimized && _isTrayFunctional && !_pendingSingleInstanceShow;

            if (!skipStartScreen && !startHiddenInTray)
            {
                try
                {
                    _mainWindow.PrepareStartupSplash();
                }
                catch
                {
                }
            }

            if (startHiddenInTray)
            {
                _mainWindow.ForceDismissStartupSplash();
                _mainWindow.AppWindow.Hide();
            }
            else
            {
                _mainWindow.Activate();
                _mainWindow.ApplyTheme(_config.Ui.Theme);

                if (!skipStartScreen)
                {
                    try
                    {
                        await _mainWindow.PlayStartupAnimationAsync();
                        _mainWindow.ApplyTheme(_config.Ui.Theme);
                    }
                    catch (Exception ex)
                    {
                        GuestLogger.Warn("startup.splash.failed", ex.Message, new
                        {
                            exceptionType = ex.GetType().FullName
                        });
                    }
                    finally
                    {
                        _mainWindow.ForceDismissStartupSplash();
                    }
                }
            }

            _ = EnsureStartupUsbListInitializedAsync();
            _ = RunDeferredStartupTasksAsync();

            if (_pendingSingleInstanceShow)
            {
                BringMainWindowToFront();
            }

            GuestLogger.Debug("ui.startup.ready", "Guest UI vollständig initialisiert.");
        }
        catch (Exception ex)
        {
            try
            {
                GuestLogger.Error("ui.startup.fatal", ex.Message, new
                {
                    exceptionType = ex.GetType().FullName,
                    stackTrace = ex.StackTrace,
                    exception = ex.ToString()
                });
            }
            catch
            {
            }
        }
    }

    private async Task SaveConfigAsync(GuestConfig config)
    {
        config.Usb ??= new GuestUsbSettings();
        config.Usb.UseHyperVSocket = true;

        var previousTransportMode = _currentUsbTransportUseHyperVSocket;
        var nextTransportMode = true;

        GuestLogger.Debug("config.save.begin", "Speichere Guest-Konfiguration.", new
        {
            debugLoggingEnabled = config.Ui.DebugLoggingEnabled,
            previousTransportMode,
            nextTransportMode
        });

        if (previousTransportMode != nextTransportMode)
        {
            var attachedCount = _usbDevices.Count(device => device.IsAttached);
            GuestLogger.Info("usb.transport.switch.preserve", "Transportmodus gewechselt ohne erzwungenen USB-Disconnect.", new
            {
                previousTransportMode = previousTransportMode ? "hyperv" : "legacy",
                nextTransportMode = "hyperv",
                attachedCount
            });
        }

        _config = config;
        GuestConfigService.Save(_configPath, config);
        TrackConfigSaveForDiagnostics();
        GuestLogger.Initialize(config.Logging, config.Ui.DebugLoggingEnabled);

        ApplyStartWithWindows(config);
        _minimizeToTray = config.Ui.MinimizeToTray;
        UpdateUsbTransportBridge();
        _currentUsbTransportUseHyperVSocket = nextTransportMode;
        UpdateTrayControlCenterView();
        TriggerSharedFolderReconnectCycle();
        UpdateSharedFolderReconnectStatusPanel();
        StartResourceMonitorAgent();
        GuestLogger.Info("config.save.done", "Guest-Konfiguration gespeichert.", new
        {
            debugLoggingEnabled = config.Ui.DebugLoggingEnabled,
            transportMode = "hyperv"
        });

        await Task.CompletedTask;
    }

    private Task<GuestConfig> ReloadConfigSnapshotAsync()
    {
        _config = GuestConfigService.LoadOrCreate(_configPath, out _);
        _config.Usb ??= new GuestUsbSettings();
        _config.Usb.UseHyperVSocket = true;
        _currentUsbTransportUseHyperVSocket = true;
        GuestLogger.Initialize(_config.Logging, _config.Ui.DebugLoggingEnabled);
        GuestLogger.Debug("config.reload", "Guest-Konfiguration aus Datei neu geladen.", new { configPath = _configPath, debugLoggingEnabled = _config.Ui.DebugLoggingEnabled });
        StartResourceMonitorAgent();
        return Task.FromResult(_config);
    }

    private async Task RunDeferredStartupTasksAsync()
    {
        GuestLogger.Debug("startup.deferred.begin", "Starte verzögerte Hintergrundaufgaben.");

        try
        {
            var identity = await FetchHostIdentityViaHyperVSocketAsync(CancellationToken.None);
            if (identity is not null)
            {
                ApplyHostIdentityToConfig(identity, persistConfig: true);
            }
        }
        catch
        {
        }

        await EnsureStartupUsbListInitializedAsync();

        try
        {
            if (_mainWindow is not null)
            {
                await _mainWindow.CheckForUpdatesOnStartupIfEnabledAsync();
            }
        }
        catch (Exception ex)
        {
            GuestLogger.Warn("startup.updatecheck_failed", ex.Message);
        }

        GuestLogger.Debug("startup.deferred.done", "Verzögerte Hintergrundaufgaben abgeschlossen.");
    }

    private async Task EnsureStartupUsbListInitializedAsync()
    {
        if (_config?.Usb?.Enabled == false || _usbDevices.Count > 0)
        {
            GuestLogger.Debug("startup.usb_refresh.skipped", "Initiale USB-Liste wurde übersprungen.", new
            {
                usbEnabled = _config?.Usb?.Enabled != false,
                existingCount = _usbDevices.Count
            });
            return;
        }

        try
        {
            GuestLogger.Debug("startup.usb_refresh.begin", "Initiale USB-Liste wird geladen.");
            await RefreshUsbDevicesAsync(emitLogs: false);
        }
        catch (Exception ex)
        {
            GuestLogger.Warn("startup.usb_refresh_failed", ex.Message);
        }
    }

    private async Task ExitForUpdateInstallAsync()
    {
        await ExitWithAnimationAsync(showExitScreen: false);
    }

    private void ApplyStartWithWindows(GuestConfig config)
    {
        var startupService = new StartupService();
        if (!startupService.SetStartWithWindows(config.Ui.StartWithWindows, "HyperTool.Guest", Environment.ProcessPath ?? string.Empty, out var startupError)
            && !string.IsNullOrWhiteSpace(startupError))
        {
            GuestLogger.Warn("startup.apply_failed", startupError);
        }
    }

    private void OnMainWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        GuestLogger.Debug("ui.window.closing", "MainWindow Closing ausgelöst.", new
        {
            isExitRequested = _isExitRequested,
            isThemeWindowReopenInProgress = _isThemeWindowReopenInProgress,
            minimizeToTray = _minimizeToTray,
            trayFunctional = _isTrayFunctional
        });

        if (_isExitRequested || _isThemeWindowReopenInProgress)
        {
            return;
        }

        if (_isTrayFunctional && _minimizeToTray)
        {
            args.Cancel = true;
            sender.Hide();
            UpdateTrayControlCenterView();
            return;
        }

        args.Cancel = true;
        GuestLogger.Info("ui.exit.sequence.begin", "Guest-App startet Exit-Sequenz über Window-Closing.");
        _ = ExitWithAnimationAsync();
    }

    private async Task ExitWithAnimationAsync(bool showExitScreen = true)
    {
        if (_isExitAnimationRunning)
        {
            return;
        }

        _isExitAnimationRunning = true;

        try
        {
            GuestLogger.Info("ui.exit.sequence.running", "Guest-App Exit-Sequenz läuft.", new
            {
                showExitScreen
            });

            var exitAnimationStopwatch = Stopwatch.StartNew();
            const int inlineExitAnimationDurationMs = 2000;

            _isExitRequested = true;
            UnregisterSessionEndingHook();

            _trayControlCenterWindow?.Close();
            _trayControlCenterWindow = null;

            StopUsbDiagnosticsLoop();
            StopUsbAutoRefreshLoop();
            StopUsbChangeNotificationSubscriber();
            StopSharedFolderAutoMountLoop();
            StopHyperVSocketMonitorLoop();
            StopResourceMonitorAgent();

            // Run USB detach first to maximize successful host cleanup during OS shutdown.
            await DisconnectAllAttachedUsbOnExitAsync();

            if (_mainWindow is not null)
            {
                try
                {
                    await _mainWindow.PlayExitAnimationAsync();
                }
                catch
                {
                }
            }

            _usbHyperVSocketProxy?.Dispose();
            _usbHyperVSocketProxy = null;
            _hyperVEventChannel.Dispose();

            _winFspMountService.Dispose();
            _resourceMonitorAgent.Dispose();

            _trayService?.Dispose();
            _trayService = null;

            ShutdownSingleInstanceInfrastructure();

            if (showExitScreen)
            {
                var remainingMs = inlineExitAnimationDurationMs - (int)exitAnimationStopwatch.ElapsedMilliseconds;
                if (remainingMs > 0)
                {
                    try
                    {
                        await Task.Delay(remainingMs);
                    }
                    catch
                    {
                    }
                }
            }

            try
            {
                _mainWindow?.AppWindow.Hide();
            }
            catch
            {
            }

            _mainWindow?.CloseAuxiliaryWindows();
            _mainWindow?.Close();
            Exit();
        }
        finally
        {
            _isExitAnimationRunning = false;
        }
    }

    private void RegisterSessionEndingHook()
    {
        lock (_sessionEndingHookSync)
        {
            if (_sessionEndingHookRegistered)
            {
                return;
            }

            try
            {
                SystemEvents.SessionEnding += OnSystemSessionEnding;
                _sessionEndingHookRegistered = true;
                GuestLogger.Debug("ui.session_ending_hook.registered", "Windows SessionEnding-Hook registriert.");
            }
            catch (Exception ex)
            {
                GuestLogger.Warn("ui.session_ending_hook.register_failed", ex.Message, new
                {
                    exceptionType = ex.GetType().FullName
                });
            }
        }
    }

    private void UnregisterSessionEndingHook()
    {
        lock (_sessionEndingHookSync)
        {
            if (!_sessionEndingHookRegistered)
            {
                return;
            }

            try
            {
                SystemEvents.SessionEnding -= OnSystemSessionEnding;
            }
            catch
            {
            }

            _sessionEndingHookRegistered = false;
        }
    }

    private void OnSystemSessionEnding(object sender, SessionEndingEventArgs e)
    {
        if (_isExitRequested)
        {
            return;
        }

        if (_config?.Usb?.DisconnectOnExit == false)
        {
            return;
        }

        if (Interlocked.Exchange(ref _sessionEndingDisconnectStarted, 1) == 1)
        {
            return;
        }

        GuestLogger.Info("ui.session_ending", "Windows-Session wird beendet. Starte letzten aggressiven USB-Disconnect-Versuch.", new
        {
            reason = e.Reason.ToString(),
            disconnectOnExit = _config?.Usb?.DisconnectOnExit != false
        });

        try
        {
            var sessionEndingTask = Task.Run(TryDisconnectAllAttachedUsbOnSessionEndingAsync);
            if (!sessionEndingTask.Wait(TimeSpan.FromSeconds(6)))
            {
                GuestLogger.Warn("usb.session_ending_disconnect.timeout", "USB-Disconnect beim SessionEnding hat das Zeitbudget erreicht.", new
                {
                    timeoutSeconds = 6
                });
            }
        }
        catch (Exception ex)
        {
            GuestLogger.Warn("usb.session_ending_disconnect.failed", ex.Message, new
            {
                exceptionType = ex.GetType().FullName
            });
        }
    }

    private async Task TryDisconnectAllAttachedUsbOnSessionEndingAsync()
    {
        if (_config?.Usb?.DisconnectOnExit == false)
        {
            return;
        }

        var attachedBusIds = _usbDevices
            .Where(device => IsGuestLocalAttachedDevice(device) && !string.IsNullOrWhiteSpace(device.BusId))
            .Select(device => device.BusId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (attachedBusIds.Count == 0)
        {
            GuestLogger.Debug("usb.session_ending_disconnect.skipped", "Kein lokales USB-Attach für SessionEnding-Disconnect gefunden.");
            return;
        }

        var disconnectedCount = 0;
        var failedCount = 0;
        var deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(8);

        foreach (var busId in attachedBusIds)
        {
            if (DateTimeOffset.UtcNow >= deadlineUtc)
            {
                break;
            }

            try
            {
                var result = await DisconnectUsbAsync(busId, bypassHostFeaturePolicy: true);
                if (result == 0)
                {
                    disconnectedCount++;
                }
                else
                {
                    failedCount++;
                }
            }
            catch (Exception ex)
            {
                failedCount++;
                GuestLogger.Warn("usb.session_ending_disconnect.item_failed", ex.Message, new
                {
                    busId,
                    exceptionType = ex.GetType().FullName
                });
            }
        }

        GuestLogger.Info("usb.session_ending_disconnect.done", "SessionEnding-USB-Disconnect abgeschlossen.", new
        {
            requestedCount = attachedBusIds.Count,
            disconnectedCount,
            failedCount,
            timedOut = DateTimeOffset.UtcNow >= deadlineUtc
        });
    }

    private async Task RestartForThemeChangeAsync(string targetTheme)
    {
        if (_isThemeWindowReopenInProgress)
        {
            return;
        }

        if (_mainWindow is null || _config is null)
        {
            return;
        }

        ExtendUsbListClearingSuppression(TimeSpan.FromSeconds(120), "theme-reopen");

        _isThemeWindowReopenInProgress = true;
        var previousWindow = _mainWindow;

        try
        {
            PointInt32 previousPosition;
            SizeInt32 previousSize;
            bool previousVisible;
            var previousMenuIndex = previousWindow.SelectedMenuIndex;

            try
            {
                previousPosition = previousWindow.AppWindow.Position;
                previousSize = previousWindow.AppWindow.Size;
                previousVisible = previousWindow.AppWindow.IsVisible;
            }
            catch
            {
                previousPosition = new PointInt32(120, 120);
                previousSize = new SizeInt32(GuestMainWindow.DefaultWindowWidth, GuestMainWindow.DefaultWindowHeight);
                previousVisible = true;
            }

            _trayControlCenterWindow?.Close();
            _trayControlCenterWindow = null;

            _trayService?.Dispose();
            _trayService = null;

            try
            {
                previousWindow.AppWindow.Show();
            }
            catch
            {
            }

            try
            {
                previousWindow.Activate();
            }
            catch
            {
            }

            try
            {
                await Task.Delay(70);
                await previousWindow.PlayThemeReloadSplashAsync(targetTheme);
            }
            catch
            {
            }

            _config.Ui.Theme = GuestConfigService.NormalizeTheme(targetTheme);

            var nextWindow = new GuestMainWindow(
                _config,
                RefreshUsbDevicesAsync,
                busId => ConnectUsbAsync(busId),
                DisconnectUsbAsync,
                SaveConfigAsync,
                ReloadConfigSnapshotAsync,
                RestartForThemeChangeAsync,
                ExitForUpdateInstallAsync,
                RunTransportDiagnosticsTestAsync,
                DiscoverUsbHostAddressAsync,
                FetchHostSharedFoldersAsync,
                _isUsbClientAvailable);

            try
            {
                nextWindow.PrepareLifecycleGuard("Design wird neu geladen …");
            }
            catch
            {
            }

            nextWindow.AppWindow.Closing += OnMainWindowClosing;
            _mainWindow = nextWindow;
            _minimizeToTray = _config.Ui.MinimizeToTray;
            _mainWindow.ApplyTheme(_config.Ui.Theme);
            _mainWindow.SelectMenuIndex(previousMenuIndex);
            // Push current USB data into the new window before refresh logic,
            // because refresh may short-circuit when list content is unchanged.
            UpdateUsbViews();

            TryInitializeTray();
            await RefreshUsbDevicesAsync();

            try
            {
                nextWindow.AppWindow.Move(previousPosition);
                nextWindow.AppWindow.Resize(previousSize);
            }
            catch
            {
            }

            if (previousVisible)
            {
                try
                {
                    previousWindow.AppWindow.Hide();
                }
                catch
                {
                }

                await Task.Delay(70);

                try
                {
                    previousWindow.Close();
                }
                catch
                {
                }

                await Task.Delay(50);

                try
                {
                    nextWindow.AppWindow.Show();
                }
                catch
                {
                }

                nextWindow.Activate();
                nextWindow.ApplyTheme(_config.Ui.Theme);

                try
                {
                    await nextWindow.DismissLifecycleGuardAsync();
                }
                catch
                {
                }
            }
            else if (_isTrayFunctional && _minimizeToTray)
            {
                try
                {
                    nextWindow.AppWindow.Hide();
                }
                catch
                {
                }
            }
            else
            {
                nextWindow.Activate();
                nextWindow.ApplyTheme(_config.Ui.Theme);

                try
                {
                    await nextWindow.DismissLifecycleGuardAsync();
                }
                catch
                {
                }
            }

            UpdateTrayControlCenterView();
            UpdateUsbDiagnosticsPanel();
            UpdateSharedFolderReconnectStatusPanel();

            SafeFireAndForget.Run(
                SchedulePostThemeReopenUsbRefreshAsync(),
                operation: "usb-refresh-post-theme-reopen");
        }
        catch (Exception ex)
        {
            GuestLogger.Warn("theme.reopen_failed", ex.Message, new { targetTheme });

            try
            {
                _mainWindow = previousWindow;
                _mainWindow.AppWindow.Show();
                _mainWindow.Activate();
            }
            catch
            {
            }
        }
        finally
        {
            _isThemeWindowReopenInProgress = false;
        }
    }

    private void TryInitializeTray()
    {
        if (_mainWindow is null)
        {
            return;
        }

        try
        {
            _trayService = new HyperTool.Services.TrayService();
            InitializeTrayServiceCompat(_trayService);

            _isTrayFunctional = true;

            EnsureTrayControlCenterWindow();
            UpdateTrayControlCenterView();
        }
        catch (Exception ex)
        {
            _isTrayFunctional = false;
            GuestLogger.Warn("tray.init_failed", ex.Message);
        }
    }

    private void InitializeTrayServiceCompat(ITrayService trayService)
    {
        var initializeMethod = trayService.GetType().GetMethod("Initialize");
        if (initializeMethod is null)
        {
            throw new InvalidOperationException("TrayService.Initialize wurde nicht gefunden.");
        }

        var commonArguments = new object?[]
        {
            (Action)(() =>
            {
                _mainWindow!.ForceDismissStartupSplash();
                _mainWindow!.AppWindow.Show();
                _mainWindow.Activate();
                UpdateTrayControlCenterView();
            }),
            (Action)(() =>
            {
                _mainWindow!.AppWindow.Hide();
                UpdateTrayControlCenterView();
            }),
            (Action)ToggleTrayControlCenter,
            (Action)ToggleTrayControlCenter,
            (Action)HideTrayControlCenter,
            (Func<string>)(() => _mainWindow!.CurrentTheme),
            (Func<bool>)(() => _mainWindow!.AppWindow.IsVisible),
            (Func<bool>)(() => false),
            (Func<IReadOnlyList<VmDefinition>>)(() => Array.Empty<VmDefinition>()),
            (Func<IReadOnlyList<HyperVSwitchInfo>>)(() => Array.Empty<HyperVSwitchInfo>()),
            (Func<Task>)(async () => await RefreshUsbDevicesAsync()),
            (Action<EventHandler>)(_ => { }),
            (Action<EventHandler>)(_ => { }),
            (Func<string, Task>)(_ => Task.CompletedTask),
            (Func<string, Task>)(_ => Task.CompletedTask),
            (Func<string, Task>)(_ => Task.CompletedTask),
            (Func<string, Task>)(_ => Task.CompletedTask),
            (Func<string, Task>)(_ => Task.CompletedTask),
            (Func<string, string, Task>)((_, _) => Task.CompletedTask),
            (Func<string, Task>)(_ => Task.CompletedTask)
        };

        var parameterCount = initializeMethod.GetParameters().Length;
        if (parameterCount == 25)
        {
            var argsWithSelectedUsb = new List<object?>(commonArguments)
            {
                (Func<UsbIpDeviceInfo?>)GetSelectedUsbDevice,
                (Func<Task>)(async () => await RefreshUsbDevicesAsync()),
                (Func<Task>)(async () => await ConnectSelectedUsbFromTrayAsync()),
                (Func<Task>)(async () => await DisconnectSelectedUsbFromTrayAsync()),
                (Action)(() => _ = ExitWithAnimationAsync())
            };

            initializeMethod.Invoke(trayService, argsWithSelectedUsb.ToArray());
            return;
        }

        if (parameterCount == 24)
        {
            var argsWithoutSelectedUsb = new List<object?>(commonArguments)
            {
                (Func<Task>)(async () => await RefreshUsbDevicesAsync()),
                (Func<Task>)(async () => await ConnectSelectedUsbFromTrayAsync()),
                (Func<Task>)(async () => await DisconnectSelectedUsbFromTrayAsync()),
                (Action)(() => _ = ExitWithAnimationAsync())
            };

            initializeMethod.Invoke(trayService, argsWithoutSelectedUsb.ToArray());
            return;
        }

        throw new InvalidOperationException($"Unbekannte TrayService.Initialize-Signatur mit {parameterCount} Parametern.");
    }

    private void EnsureTrayControlCenterWindow()
    {
        if (_trayControlCenterWindow is not null)
        {
            return;
        }

        var window = new GuestTrayControlCenterWindow();
        _trayControlCenterWindow = window;

        window.CloseRequested += () =>
        {
            try
            {
                if (window.AppWindow.IsVisible)
                {
                    window.AppWindow.Hide();
                }
            }
            catch
            {
            }
        };

        window.RefreshUsbRequested += async () => await RefreshUsbDevicesAsync();
        window.UsbSelected += busId => _selectedUsbBusId = busId;
        window.UsbConnectRequested += async () => await ConnectSelectedUsbFromTrayAsync();
        window.UsbDisconnectRequested += async () => await DisconnectSelectedUsbFromTrayAsync();
        window.ToggleVisibilityRequested += () =>
        {
            if (_mainWindow is null)
            {
                return;
            }

            if (_mainWindow.AppWindow.IsVisible)
            {
                _mainWindow.AppWindow.Hide();
            }
            else
            {
                _mainWindow.ForceDismissStartupSplash();
                _mainWindow.AppWindow.Show();
                _mainWindow.Activate();
            }

            UpdateTrayControlCenterView();
        };
        window.ExitRequested += () => _ = ExitWithAnimationAsync();
    }

    private void ToggleTrayControlCenter()
    {
        if (_mainWindow is null)
        {
            return;
        }

        EnsureTrayControlCenterWindow();
        if (_trayControlCenterWindow is null)
        {
            return;
        }

        if (_trayControlCenterWindow.AppWindow.IsVisible)
        {
            _trayControlCenterWindow.AppWindow.Hide();
            return;
        }

        _ = RefreshUsbDevicesAsync();
        UpdateTrayControlCenterView();

        PositionTrayControlCenterNearTray();

        _trayControlCenterWindow.AppWindow.Show();
        _trayControlCenterWindow.Activate();
    }

    private void PositionTrayControlCenterNearTray()
    {
        if (_trayControlCenterWindow is null)
        {
            return;
        }

        var popupWidth = GuestTrayControlCenterWindow.PopupWidth;
        var popupHeight = _minimizeToTray
            ? GuestTrayControlCenterWindow.PopupHeightWithUsb
            : GuestTrayControlCenterWindow.PopupHeightCompact;
        var scaledPanelSize = _trayControlCenterWindow.CurrentScaledPanelSize;
        var scaledPopupWidth = scaledPanelSize.Width > 0 ? scaledPanelSize.Width : popupWidth;
        var scaledPopupHeight = scaledPanelSize.Height > 0 ? scaledPanelSize.Height : popupHeight;

        if (!GetCursorPos(out var cursorPos))
        {
            cursorPos = new NativePoint { X = 0, Y = 0 };
        }

        var displayArea = DisplayArea.GetFromPoint(new PointInt32(cursorPos.X, cursorPos.Y), DisplayAreaFallback.Primary);
        var work = displayArea.WorkArea;
        var bounds = displayArea.OuterBounds;

        var x = cursorPos.X - scaledPopupWidth + 24;
        var y = work.Y + work.Height - scaledPopupHeight - 8;

        var taskbarAtBottom = work.Y + work.Height < bounds.Y + bounds.Height;
        var taskbarAtTop = work.Y > bounds.Y;
        var taskbarAtLeft = work.X > bounds.X;
        var taskbarAtRight = work.X + work.Width < bounds.X + bounds.Width;

        if (taskbarAtTop)
        {
            y = work.Y + 8;
        }
        else if (taskbarAtBottom)
        {
            y = work.Y + work.Height - scaledPopupHeight - 8;
        }

        if (taskbarAtLeft)
        {
            x = work.X + 8;
        }
        else if (taskbarAtRight)
        {
            x = work.X + work.Width - scaledPopupWidth - 8;
        }

        x = Math.Clamp(x, work.X + 8, work.X + work.Width - scaledPopupWidth - 8);
        y = Math.Clamp(y, work.Y + 8, work.Y + work.Height - scaledPopupHeight - 8);

        try
        {
            _trayControlCenterWindow.AppWindow.Move(new PointInt32(x, y));
        }
        catch
        {
        }
    }

    private void HideTrayControlCenter()
    {
        if (_trayControlCenterWindow?.AppWindow.IsVisible == true)
        {
            _trayControlCenterWindow.AppWindow.Hide();
        }
    }

    private void UpdateTrayControlCenterView()
    {
        if (_mainWindow?.DispatcherQueue is { } queue && !queue.HasThreadAccess)
        {
            _ = queue.TryEnqueue(UpdateTrayControlCenterView);
            return;
        }

        if (_trayControlCenterWindow is null || _mainWindow is null || _config is null)
        {
            return;
        }

        _trayControlCenterWindow.ApplyTheme(_mainWindow.CurrentTheme == "dark");
        _trayControlCenterWindow.UpdateView(_usbDevices, _selectedUsbBusId, _mainWindow.AppWindow.IsVisible, _minimizeToTray, _isUsbClientAvailable);
    }

    private UsbIpDeviceInfo? GetSelectedUsbDevice()
    {
        if (!string.IsNullOrWhiteSpace(_selectedUsbBusId))
        {
            var selectedFromTray = _usbDevices.FirstOrDefault(item =>
                string.Equals(item.BusId, _selectedUsbBusId, StringComparison.OrdinalIgnoreCase));
            if (selectedFromTray is not null)
            {
                return selectedFromTray;
            }
        }

        var selectedFromMain = _mainWindow?.GetSelectedUsbDevice();
        if (selectedFromMain is not null)
        {
            _selectedUsbBusId = selectedFromMain.BusId;
            return selectedFromMain;
        }

        return null;
    }

    private void UpdateUsbViews()
    {
        void apply()
        {
            _mainWindow?.UpdateUsbDevices(_usbDevices);
            UpdateTrayControlCenterView();
        }

        if (_mainWindow?.DispatcherQueue is { } queue && !queue.HasThreadAccess)
        {
            _ = queue.TryEnqueue(apply);
            return;
        }

        apply();
    }

    private Task<IReadOnlyList<UsbIpDeviceInfo>> RefreshUsbDevicesAsync()
    {
        return RefreshUsbDevicesAsync(emitLogs: true, bypassRateLimit: false, forceHostIdentityRefresh: false);
    }

    private async Task<IReadOnlyList<UsbIpDeviceInfo>> RefreshUsbDevicesAsync(bool emitLogs)
    {
        return await RefreshUsbDevicesAsync(emitLogs, bypassRateLimit: false, forceHostIdentityRefresh: false);
    }

    private async Task<IReadOnlyList<UsbIpDeviceInfo>> RefreshUsbDevicesAsync(bool emitLogs, bool bypassRateLimit)
    {
        return await RefreshUsbDevicesAsync(emitLogs, bypassRateLimit, forceHostIdentityRefresh: false);
    }

    private async Task<IReadOnlyList<UsbIpDeviceInfo>> RefreshUsbDevicesAsync(bool emitLogs, bool bypassRateLimit, bool forceHostIdentityRefresh)
    {
        if (!bypassRateLimit && !emitLogs && (DateTimeOffset.UtcNow - _lastUsbRefreshCompletedUtc) < TimeSpan.FromMilliseconds(1200))
        {
            if (emitLogs)
            {
                GuestLogger.Debug("usb.refresh.skipped_rate_limit", "USB-Refresh wegen Rate-Limit übersprungen.", new
                {
                    elapsedSinceLastRefreshMs = (DateTimeOffset.UtcNow - _lastUsbRefreshCompletedUtc).TotalMilliseconds
                });
            }
            return _usbDevices;
        }

        if (!await _usbRefreshGate.WaitAsync(0))
        {
            Interlocked.Exchange(ref _usbRefreshReplayRequested, 1);
            if (emitLogs)
            {
                GuestLogger.Debug("usb.refresh.skipped_busy", "USB-Refresh übersprungen, weil bereits ein Lauf aktiv ist.");
            }
            return _usbDevices;
        }

        try
        {
        if (emitLogs)
        {
            ExtendAutoUsbDisconnectSignalSuppression(TimeSpan.FromSeconds(30), "interactive-usb-refresh");
        }

        await RefreshUsbClientAvailabilityAsync();

        if (emitLogs)
        {
            GuestLogger.Debug("usb.refresh.begin", "USB-Refresh gestartet.", new
            {
                emitLogs,
                currentCount = _usbDevices.Count,
                selectedBusId = _selectedUsbBusId,
                useHyperVSocket = _config?.Usb?.UseHyperVSocket != false,
                hostFeatureEnabled = _config?.Usb?.HostFeatureEnabled != false
            });
        }

        if (_config?.Usb?.Enabled == false)
        {
            if (_usbDevices.Count > 0)
            {
                _usbDevices.Clear();
                UpdateUsbViews();
            }

            if (emitLogs)
            {
                GuestLogger.Info("usb.refresh.blocked_guest_policy", "USB Refresh blockiert: Guest hat USB lokal deaktiviert.");
            }

            return _usbDevices;
        }

        var useHyperVSocket = _config?.Usb?.UseHyperVSocket != false;

        if (useHyperVSocket && _config?.Usb?.HostFeatureEnabled == false)
        {
            if (_usbDevices.Count > 0
                && IsUsbListClearingSuppressed(out var remainingSuppressionSeconds, out var suppressionReason))
            {
                GuestLogger.Debug("usb.refresh.blocked_host_policy.deferred_clear", "USB-Liste wird trotz Host-Policy=off temporär beibehalten (Übergangsfenster).", new
                {
                    remainingSuppressionSeconds,
                    suppressionReason,
                    currentCount = _usbDevices.Count
                });

                return _usbDevices;
            }

            if (_usbDevices.Count > 0)
            {
                _usbDevices.Clear();
                UpdateUsbViews();
            }

            if (emitLogs)
            {
                GuestLogger.Info("usb.refresh.blocked_host_policy", "USB Refresh blockiert: Host hat USB Share deaktiviert.");
            }

            return _usbDevices;
        }

        if (!_isUsbClientAvailable)
        {
            if (_usbDevices.Count > 0
                && IsUsbListClearingSuppressed(out var remainingSuppressionSeconds, out var suppressionReason))
            {
                GuestLogger.Debug("usb.refresh.client_missing.deferred_clear", "USB-Liste wird trotz fehlendem USB-Client temporär beibehalten (Übergangsfenster).", new
                {
                    remainingSuppressionSeconds,
                    suppressionReason,
                    currentCount = _usbDevices.Count
                });

                return _usbDevices;
            }

            if (_usbDevices.Count > 0)
            {
                _usbDevices.Clear();
                UpdateUsbViews();
            }

            if (!_usbClientMissingLogged)
            {
                _usbClientMissingLogged = true;
                GuestLogger.Warn("usb.client.missing", "USB/IP-Client nicht installiert. USB-Funktionen sind deaktiviert.", new
                {
                    source = "https://github.com/vadimgrn/usbip-win2"
                });
            }

            return _usbDevices;
        }

        var operationId = CreateUsbOperationId("refresh");
        var stopwatch = Stopwatch.StartNew();
        var hostResolution = ResolveUsbHostAddressDiagnostics();

        if (emitLogs)
        {
            GuestLogger.Debug("usb.refresh.host_resolution", "USB-Host-Auflösung abgeschlossen.", new
            {
                operationId,
                hostResolution.ResolvedIpv4,
                hostResolution.Source,
                hostResolution.RawInput
            });
        }

        ApplyUsbTransportResolution(hostResolution);
        var previousCount = _usbDevices.Count;
        var previouslyAttachedBusIds = _usbDevices
            .Where(device => IsGuestLocalAttachedDevice(device) && !string.IsNullOrWhiteSpace(device.BusId))
            .Select(device => device.BusId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fallbackToIpUsedForLog = false;
        string? fallbackReason = null;
        string? fallbackExceptionType = null;

        try
        {
            IReadOnlyList<UsbIpDeviceInfo> list = Array.Empty<UsbIpDeviceInfo>();
            var useRemoteHostList = !string.IsNullOrWhiteSpace(hostResolution.ResolvedIpv4);
            var fallbackToIpUsed = false;
            string? fallbackHostAddress = null;

            if (useHyperVSocket
                && useRemoteHostList
                && (forceHostIdentityRefresh
                    || (DateTimeOffset.UtcNow - _lastHostIdentityBackgroundAttemptUtc) >= TimeSpan.FromSeconds(GuestHostIdentityUsbRefreshSeconds)))
            {
                _lastHostIdentityBackgroundAttemptUtc = DateTimeOffset.UtcNow;
                try
                {
                    var identity = await FetchHostIdentityViaHyperVSocketAsync(CancellationToken.None, forceRefresh: forceHostIdentityRefresh);
                    if (identity is not null)
                    {
                        ApplyHostIdentityToConfig(identity, persistConfig: false);
                    }
                }
                catch
                {
                }
            }

            if (useRemoteHostList)
            {
                try
                {
                    list = await GetRemoteUsbDevicesWithTimeoutAsync(hostResolution.ResolvedIpv4, CancellationToken.None);
                }
                catch (Exception hyperVEx) when (string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase))
                {
                    var recoveredByRetry = false;

                    if (IsTransientHyperVTransportFailure(hyperVEx))
                    {
                        var retryDelaysMs = new[] { 220, 480, 900 };
                        foreach (var retryDelayMs in retryDelaysMs)
                        {
                            await Task.Delay(retryDelayMs);
                            try
                            {
                                list = await GetRemoteUsbDevicesWithTimeoutAsync(hostResolution.ResolvedIpv4, CancellationToken.None);
                                recoveredByRetry = true;
                                break;
                            }
                            catch (Exception retryEx)
                            {
                                hyperVEx = retryEx;
                                if (!IsTransientHyperVTransportFailure(hyperVEx))
                                {
                                    break;
                                }
                            }
                        }
                    }

                    if (!recoveredByRetry)
                    {
                        var hasAttachedDevices = HasLocalAttachedUsbDevices();
                        var ipFallbackResolution = ResolveUsbHostAddressDiagnostics(preferHyperVSocket: false);
                        if (string.IsNullOrWhiteSpace(ipFallbackResolution.ResolvedIpv4))
                        {
                            throw;
                        }

                        fallbackHostAddress = ipFallbackResolution.ResolvedIpv4;
                        fallbackReason = hyperVEx.Message;
                        fallbackExceptionType = hyperVEx.GetType().FullName;

                        // If a device is currently attached in the guest, keep Hyper-V as
                        // authoritative transport state and use IP only as list fallback.
                        if (!hasAttachedDevices)
                        {
                            fallbackToIpUsed = true;
                            fallbackToIpUsedForLog = true;
                            MarkHyperVDataPathFailure();
                        }

                        list = await GetRemoteUsbDevicesWithTimeoutAsync(ipFallbackResolution.ResolvedIpv4, CancellationToken.None);
                    }
                }
            }
            else
            {
                list = await GetLocalUsbDevicesWithTimeoutAsync(CancellationToken.None);
            }

            var retryTriggered = false;
            if (useRemoteHostList && list.Count == 0 && previousCount > 0)
            {
                retryTriggered = true;
                await Task.Delay(900);
                list = await GetRemoteUsbDevicesWithTimeoutAsync(hostResolution.ResolvedIpv4, CancellationToken.None);
            }

            foreach (var device in list)
            {
                ApplyHostUsbMetadata(device);
            }

            SyncUsbManualDisconnectPendingState(list);

            var effectiveHostAddress = fallbackToIpUsed && !string.IsNullOrWhiteSpace(fallbackHostAddress)
                ? fallbackHostAddress
                : hostResolution.ResolvedIpv4;
            var effectiveRemoteList = !string.IsNullOrWhiteSpace(effectiveHostAddress);

            if (!fallbackToIpUsed
                && string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase))
            {
                MarkHyperVDataPathSuccess();
            }

            if (effectiveRemoteList)
            {
                await CleanupDanglingGuestAttachmentsAsync(list, effectiveHostAddress!);
            }

            var autoConnectApplied = await ApplyUsbAutoConnectAsync(list);
            if (autoConnectApplied > 0)
            {
                await Task.Delay(260);

                if (effectiveRemoteList)
                {
                    list = await _usbService.GetRemoteDevicesAsync(effectiveHostAddress!, CancellationToken.None);
                }
                else
                {
                    list = await _usbService.GetDevicesAsync(CancellationToken.None);
                }
            }

            foreach (var device in list)
            {
                ApplyHostUsbMetadata(device);
            }

            var transientRemoteListGap = useRemoteHostList
                && previouslyAttachedBusIds.Count > 0
                && list.Count == 0;

            if (transientRemoteListGap)
            {
                GuestLogger.Warn("usb.refresh.transient_remote_gap", "Host lieferte temporär eine leere USB-Liste bei zuvor attached Geräten. Bestehende Liste wird beibehalten und Recheck geplant.", new
                {
                    operationId,
                    attachedCountBefore = previouslyAttachedBusIds.Count,
                    hostAddress = hostResolution.ResolvedIpv4,
                    hostSource = hostResolution.Source
                });

                SafeFireAndForget.Run(Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(1100));
                        await RefreshUsbDevicesAsync(emitLogs: false, bypassRateLimit: true, forceHostIdentityRefresh: true);
                    }
                    catch
                    {
                    }
                }), operation: "usb-refresh-recheck-after-transient-gap");

                return _usbDevices;
            }

            if (previouslyAttachedBusIds.Count > 0)
            {
                var currentlyAttachedBusIds = list
                    .Where(device => IsGuestLocalAttachedDevice(device) && !string.IsNullOrWhiteSpace(device.BusId))
                    .Select(device => device.BusId.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var lostAttachedBusIds = previouslyAttachedBusIds
                    .Where(busId => !currentlyAttachedBusIds.Contains(busId))
                    .ToList();

                if (lostAttachedBusIds.Count > 0)
                {
                    await TryProcessPotentialLocalDetachSignalsAsync(
                        lostAttachedBusIds,
                        operationId,
                        hostResolution,
                        fallbackToIpUsed);
                }

                foreach (var stillAttachedBusId in currentlyAttachedBusIds)
                {
                    _usbPotentialLocalDetachStateByBusId.Remove(stillAttachedBusId);
                }
            }

            var hasUsbListChanged = !UsbDeviceListsMatch(_usbDevices, list);

            if (hasUsbListChanged)
            {
                _usbDevices.Clear();
                _usbDevices.AddRange(list);
            }

            var selectedBusIdChanged = false;
            if (!string.IsNullOrWhiteSpace(_selectedUsbBusId)
                && !_usbDevices.Any(item => string.Equals(item.BusId, _selectedUsbBusId, StringComparison.OrdinalIgnoreCase)))
            {
                _selectedUsbBusId = _usbDevices.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.BusId))?.BusId;
                selectedBusIdChanged = true;
            }

            ApplyUsbTransportResolution(hostResolution, fallbackToIpUsed);

            if (hasUsbListChanged || selectedBusIdChanged)
            {
                UpdateUsbViews();
            }

            if (useRemoteHostList && string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase))
            {
                if (forceHostIdentityRefresh)
                {
                    var forcedIdentity = await FetchHostIdentityViaHyperVSocketAsync(CancellationToken.None, forceRefresh: true);
                    if (forcedIdentity is not null)
                    {
                        ApplyHostIdentityToConfig(forcedIdentity, persistConfig: false);
                    }
                }
                else
                {
                    await TryRefreshHostIdentityAfterUsbRefreshAsync(CancellationToken.None);
                }
            }

            if (emitLogs)
            {
                var shouldLogRefreshSuccess = ShouldLogUsbRefreshSuccess(
                    _usbDevices,
                    _selectedUsbBusId,
                    autoConnectApplied,
                    retryTriggered,
                    out var refreshStateKey);

                if (shouldLogRefreshSuccess)
                {
                    GuestLogger.Info("usb.refresh.success", "USB-Geräte aktualisiert.", new
                    {
                        operationId,
                        elapsedMs = stopwatch.ElapsedMilliseconds,
                        count = _usbDevices.Count,
                        selectedBusId = _selectedUsbBusId,
                        attachedCount = _usbDevices.Count(item => item.IsAttached),
                        sharedCount = _usbDevices.Count(item => item.IsShared),
                        autoConnectApplied,
                        retryTriggered,
                        previousCount,
                        listSource = useRemoteHostList ? "remote-host" : "local-state",
                        hostAddress = hostResolution.ResolvedIpv4,
                        hostSource = hostResolution.Source,
                        hostInput = hostResolution.RawInput,
                        transportPath = fallbackToIpUsed
                            ? "hyperv"
                            : (string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase) ? "hyperv" : "hyperv"),
                        fallbackToIpUsed,
                        fallbackHostAddress,
                        fallbackReason,
                        fallbackExceptionType
                    });

                    _usbRefreshSuccessLastStateKey = refreshStateKey;
                    _usbRefreshSuccessLastLoggedUtc = DateTimeOffset.UtcNow;
                }
            }

            return _usbDevices;
        }
        catch (Exception ex)
        {
            if (string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase))
            {
                MarkHyperVDataPathFailure();
            }

            if (emitLogs)
            {
                GuestLogger.Warn("usb.refresh.failed", ex.Message, new
                {
                    operationId,
                    elapsedMs = stopwatch.ElapsedMilliseconds,
                    selectedBusId = _selectedUsbBusId,
                    exceptionType = ex.GetType().FullName,
                    hResult = ex.HResult,
                    hostAddress = hostResolution.ResolvedIpv4,
                    hostSource = hostResolution.Source,
                    hostInput = hostResolution.RawInput,
                    transportPath = fallbackToIpUsedForLog
                        ? "hyperv"
                        : (string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase) ? "hyperv" : "hyperv"),
                    hostResolutionFailure = hostResolution.FailureReason
                });
            }
            return _usbDevices;
        }
        }
        finally
        {
            _lastUsbRefreshCompletedUtc = DateTimeOffset.UtcNow;
            _usbRefreshGate.Release();

            if (Interlocked.Exchange(ref _usbRefreshReplayRequested, 0) == 1)
            {
                SafeFireAndForget.Run(
                    RefreshUsbDevicesAsync(emitLogs: false),
                    operation: "usb-refresh-replay");
            }
        }
    }

    private static bool UsbDeviceListsMatch(IReadOnlyList<UsbIpDeviceInfo> current, IReadOnlyList<UsbIpDeviceInfo> next)
    {
        if (ReferenceEquals(current, next))
        {
            return true;
        }

        if (current.Count != next.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            var left = current[i];
            var right = next[i];

            if (!string.Equals(left.BusId, right.BusId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(left.Description, right.Description, StringComparison.Ordinal)
                || !string.Equals(left.HardwareId, right.HardwareId, StringComparison.Ordinal)
                || !string.Equals(left.InstanceId, right.InstanceId, StringComparison.Ordinal)
                || !string.Equals(left.PersistedGuid, right.PersistedGuid, StringComparison.OrdinalIgnoreCase)
                || left.IsConnected != right.IsConnected
                || left.IsShared != right.IsShared
                || left.IsAttached != right.IsAttached
                || left.IsAttachedByOtherGuest != right.IsAttachedByOtherGuest
                || !string.Equals(left.ClientIpAddress, right.ClientIpAddress, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(left.AttachedGuestComputerName, right.AttachedGuestComputerName, StringComparison.Ordinal)
                || !string.Equals(left.StateText, right.StateText, StringComparison.Ordinal)
                || !string.Equals(left.CustomName, right.CustomName, StringComparison.Ordinal)
                || !string.Equals(left.CustomComment, right.CustomComment, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private async Task CleanupDanglingGuestAttachmentsAsync(IReadOnlyList<UsbIpDeviceInfo> remoteDevices, string hostAddress)
    {
        if (remoteDevices.Count == 0 && _usbDevices.Count == 0)
        {
            return;
        }

        var remoteBusIds = new HashSet<string>(
            remoteDevices
                .Where(device => !string.IsNullOrWhiteSpace(device.BusId))
                .Select(device => device.BusId.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var danglingAttachedBusIds = _usbDevices
            .Where(device =>
                IsGuestLocalAttachedDevice(device)
                && !string.IsNullOrWhiteSpace(device.BusId)
                && !remoteBusIds.Contains(device.BusId.Trim()))
            .Select(device => device.BusId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (danglingAttachedBusIds.Count == 0)
        {
            return;
        }

        foreach (var busId in danglingAttachedBusIds)
        {
            GuestLogger.Debug("usb.cleanup.dangling_detach.skipped", "Dangling-Detach aus Refresh wurde zur Stabilisierung unterdrückt.", new
            {
                busId,
                hostAddress,
                reason = "stability-guard"
            });
        }
    }

    private async Task TryProcessPotentialLocalDetachSignalsAsync(
        IReadOnlyList<string> lostAttachedBusIds,
        string operationId,
        UsbHostResolution hostResolution,
        bool fallbackToIpUsed)
    {
        if (IsAutoUsbDisconnectSignalSuppressed(out var suppressionRemainingSeconds, out var reason))
        {
            if (lostAttachedBusIds.Count > 0)
            {
                GuestLogger.Debug("usb.refresh.local_detach_suppressed_window", "Automatisches usb-disconnected temporär unterdrückt (Schutzfenster).", new
                {
                    operationId,
                    reason,
                    remainingSuppressionSeconds = suppressionRemainingSeconds,
                    candidateCount = lostAttachedBusIds.Count
                });
            }

            return;
        }

        var now = DateTimeOffset.UtcNow;
        var confirmedDetachBusIds = new List<string>();
        var analyzedBusIds = new List<string>();

        foreach (var rawBusId in lostAttachedBusIds)
        {
            var normalizedBusId = (rawBusId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedBusId))
            {
                continue;
            }

            if (_usbManualDisconnectPendingByBusId.ContainsKey(normalizedBusId))
            {
                _usbPotentialLocalDetachStateByBusId.Remove(normalizedBusId);
                continue;
            }

            analyzedBusIds.Add(normalizedBusId);

            if (IsAutoDisconnectSuppressedAfterAttach(normalizedBusId, out var remainingSeconds))
            {
                GuestLogger.Debug("usb.refresh.local_detach_suppressed_recent_attach", "Automatisches usb-disconnected nach frischem Attach unterdrückt.", new
                {
                    operationId,
                    busId = normalizedBusId,
                    remainingSuppressionSeconds = remainingSeconds
                });
                continue;
            }

            var state = _usbPotentialLocalDetachStateByBusId.TryGetValue(normalizedBusId, out var existing)
                ? existing
                : (FirstSeenUtc: now, LastSeenUtc: now, Count: 0, LastSignalUtc: DateTimeOffset.MinValue);

            state = (state.FirstSeenUtc, now, state.Count + 1, state.LastSignalUtc);
            _usbPotentialLocalDetachStateByBusId[normalizedBusId] = state;

            if (state.Count < GuestLocalDetachSignalThreshold)
            {
                continue;
            }

            if ((now - state.FirstSeenUtc) < TimeSpan.FromSeconds(GuestLocalDetachSignalMinAgeSeconds))
            {
                continue;
            }

            if (state.LastSignalUtc != DateTimeOffset.MinValue
                && (now - state.LastSignalUtc) < UsbStaleExportHintMinInterval)
            {
                continue;
            }

            var confirmedLocalDetach = await ConfirmGuestLocalDetachAsync(normalizedBusId, CancellationToken.None);
            if (!confirmedLocalDetach)
            {
                _usbPotentialLocalDetachStateByBusId.Remove(normalizedBusId);
                GuestLogger.Debug("usb.refresh.local_detach_recheck_attached", "Lokaler Recheck zeigte weiterhin Attach; usb-disconnected wurde verworfen.", new
                {
                    operationId,
                    busId = normalizedBusId
                });
                continue;
            }

            await TrySendUsbConnectionEventAckAsync(normalizedBusId, "usb-disconnected", CancellationToken.None);

            state = (state.FirstSeenUtc, state.LastSeenUtc, state.Count, now);
            _usbPotentialLocalDetachStateByBusId[normalizedBusId] = state;
            confirmedDetachBusIds.Add(normalizedBusId);
        }

        if (confirmedDetachBusIds.Count > 0)
        {
            GuestLogger.Warn("usb.refresh.detected_local_detach", "USB im Guest nicht mehr attached; Host wurde per usb-disconnected informiert.", new
            {
                operationId,
                busIds = confirmedDetachBusIds,
                count = confirmedDetachBusIds.Count,
                hostAddress = hostResolution.ResolvedIpv4,
                hostSource = hostResolution.Source,
                transportPath = fallbackToIpUsed
                    ? "hyperv"
                    : (string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase) ? "hyperv" : "hyperv")
            });

            return;
        }

        if (analyzedBusIds.Count == 0)
        {
            return;
        }

        GuestLogger.Debug("usb.refresh.local_detach_debounced", "Kurzzeitiger Attach-Verlust erkannt; usb-disconnected wurde noch nicht gesendet.", new
        {
            operationId,
            busIds = analyzedBusIds,
            count = analyzedBusIds.Count,
            threshold = GuestLocalDetachSignalThreshold,
            minAgeSeconds = GuestLocalDetachSignalMinAgeSeconds,
            hostAddress = hostResolution.ResolvedIpv4,
            hostSource = hostResolution.Source
        });
    }

    private async Task<int> ConnectUsbAsync(string busId, bool skipCachedAttachedCheck = false)
    {
        var useHyperVSocket = _config?.Usb?.UseHyperVSocket != false;
        if (useHyperVSocket && _config?.Usb?.HostFeatureEnabled == false)
        {
            GuestLogger.Warn("usb.connect.blocked_host_policy", "USB Connect blockiert: Host hat USB Share deaktiviert.", new
            {
                busId
            });
            return 1;
        }

        if (!_isUsbClientAvailable)
        {
            GuestLogger.Warn("usb.connect.blocked", "USB/IP-Client nicht installiert. Connect ist deaktiviert.", new
            {
                busId,
                source = "https://github.com/vadimgrn/usbip-win2"
            });
            return 1;
        }

        if (string.IsNullOrWhiteSpace(busId))
        {
            GuestLogger.Warn("usb.connect.invalid", "USB Connect mit leerer BUSID angefordert.");
            return 1;
        }

        var operationId = CreateUsbOperationId("connect");
        var stopwatch = Stopwatch.StartNew();
        var hostResolution = ResolveUsbHostAddressDiagnostics();
        ApplyUsbTransportResolution(hostResolution);
        var beforeDevice = FindUsbByBusId(busId);

        if (!skipCachedAttachedCheck && IsGuestLocalAttachedDevice(beforeDevice))
        {
            _selectedUsbBusId = busId;
            await TrySendUsbConnectionEventAckAsync(busId, "usb-heartbeat", CancellationToken.None);
            return 0;
        }

        GuestLogger.Info("usb.connect.begin", "USB Host-Attach gestartet.", new
        {
            operationId,
            busId,
            selectedBusId = _selectedUsbBusId,
            hostAddress = hostResolution.ResolvedIpv4,
            hostSource = hostResolution.Source,
            hostInput = hostResolution.RawInput,
            sharePath = _config?.SharePath,
            beforeState = beforeDevice?.StateText,
            beforeClientIp = beforeDevice?.ClientIpAddress,
            beforePersistedGuid = beforeDevice?.PersistedGuid
        });

        if (string.IsNullOrWhiteSpace(hostResolution.ResolvedIpv4))
        {
            GuestLogger.Warn("usb.connect.failed", "USB-Host-Adresse fehlt oder konnte nicht auf IPv4 aufgelöst werden. Bitte usb.hostAddress oder SharePath konfigurieren.", new
            {
                operationId,
                busId,
                elapsedMs = stopwatch.ElapsedMilliseconds,
                hostSource = hostResolution.Source,
                hostInput = hostResolution.RawInput,
                transportPath = string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase)
                    ? "hyperv"
                    : "hyperv",
                hostResolutionFailure = hostResolution.FailureReason,
                sharePath = _config?.SharePath
            });
            return 1;
        }

        try
        {
            await _usbService.AttachToHostAsync(busId, hostResolution.ResolvedIpv4, CancellationToken.None);
            _selectedUsbBusId = busId;
            MarkUsbAttachSuccess(busId);
            _usbAlreadyExportedNotAttachedStateByBusId.Remove(busId);
            _usbPotentialLocalDetachStateByBusId.Remove(busId);
            ApplyUsbTransportResolution(hostResolution);

            GuestLogger.Info("usb.connect.success", "USB Host-Attach erfolgreich.", new
            {
                operationId,
                busId,
                elapsedMs = stopwatch.ElapsedMilliseconds,
                hostAddress = hostResolution.ResolvedIpv4,
                hostSource = hostResolution.Source,
                transportPath = string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase)
                    ? "hyperv"
                    : "hyperv"
            });

                    await TrySendUsbConnectionEventAckAsync(busId, "usb-connected", CancellationToken.None);

            return 0;
        }
        catch (Exception ex)
        {
            if (ShouldMarkUsbClientUnavailableFromException(ex))
            {
                MarkUsbClientUnavailableAndRefreshUi("usb.connect.client_missing", busId, ex.Message);
                return 1;
            }

            if (IsUsbAlreadyExportedError(ex))
            {
                try
                {
                    await RefreshUsbDevicesAsync(emitLogs: false);
                }
                catch
                {
                }

                var currentDevice = FindUsbByBusId(busId);
                if (IsGuestLocalAttachedDevice(currentDevice))
                {
                    await TrySendUsbConnectionEventAckAsync(busId, "usb-heartbeat", CancellationToken.None);

                    _selectedUsbBusId = busId;
                    MarkUsbAttachSuccess(busId);
                    _usbAlreadyExportedNotAttachedStateByBusId.Remove(busId);
                    _usbPotentialLocalDetachStateByBusId.Remove(busId);
                    ApplyUsbTransportResolution(hostResolution);

                    GuestLogger.Info("usb.connect.success", "USB Host-Attach bereits aktiv (already exported).", new
                    {
                        operationId,
                        busId,
                        elapsedMs = stopwatch.ElapsedMilliseconds,
                        hostAddress = hostResolution.ResolvedIpv4,
                        hostSource = hostResolution.Source,
                        transportPath = string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase)
                            ? "hyperv"
                            : "hyperv",
                        recovery = "already-exported-as-attached"
                    });

                    return 0;
                }

                WarnRateLimited(
                    eventName: "usb.connect.already_exported.not_attached",
                    message: "Host meldet 'already exported', aber Gerät ist im Guest nicht als Attached sichtbar (stale Export wahrscheinlich).",
                    data: new
                    {
                        operationId,
                        busId,
                        elapsedMs = stopwatch.ElapsedMilliseconds,
                        hostAddress = hostResolution.ResolvedIpv4,
                        hostSource = hostResolution.Source,
                        transportPath = string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase)
                            ? "hyperv"
                            : "hyperv",
                        beforeState = beforeDevice?.StateText,
                        currentState = currentDevice?.StateText,
                        currentClientIp = currentDevice?.ClientIpAddress
                    },
                    scopeKey: busId.Trim().ToLowerInvariant(),
                    minInterval: RecurringWarnRateLimitInterval);

                await TrySignalStaleAlreadyExportedDisconnectAsync(busId, operationId, hostResolution, stopwatch.ElapsedMilliseconds);

            }

            if (ShouldRetryUsbAttach(ex))
            {
                try
                {
                    GuestLogger.Warn("usb.connect.retry", "USB Attach wird nach Recovery erneut versucht.", new
                    {
                        operationId,
                        busId,
                        elapsedMs = stopwatch.ElapsedMilliseconds,
                        hostAddress = hostResolution.ResolvedIpv4,
                        hostSource = hostResolution.Source,
                        reason = ex.Message,
                        transportPath = string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase)
                            ? "hyperv"
                            : "hyperv"
                    });

                    await TryRecoverAndRetryUsbAttachAsync(busId, hostResolution.ResolvedIpv4, CancellationToken.None);
                    _selectedUsbBusId = busId;
                    MarkUsbAttachSuccess(busId);
                    _usbAlreadyExportedNotAttachedStateByBusId.Remove(busId);
                    _usbPotentialLocalDetachStateByBusId.Remove(busId);
                    ApplyUsbTransportResolution(hostResolution);

                    GuestLogger.Info("usb.connect.success", "USB Host-Attach erfolgreich (Retry).", new
                    {
                        operationId,
                        busId,
                        elapsedMs = stopwatch.ElapsedMilliseconds,
                        hostAddress = hostResolution.ResolvedIpv4,
                        hostSource = hostResolution.Source,
                        transportPath = string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase)
                            ? "hyperv"
                            : "hyperv",
                        recovery = "retry-after-detach"
                    });

                    await TrySendUsbConnectionEventAckAsync(busId, "usb-connected", CancellationToken.None);
                    return 0;
                }
                catch (Exception retryEx)
                {
                    GuestLogger.Warn("usb.connect.retry.failed", retryEx.Message, new
                    {
                        operationId,
                        busId,
                        elapsedMs = stopwatch.ElapsedMilliseconds,
                        hostAddress = hostResolution.ResolvedIpv4,
                        hostSource = hostResolution.Source,
                        transportPath = string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase)
                            ? "hyperv"
                            : "hyperv",
                        exceptionType = retryEx.GetType().FullName
                    });
                }
            }

            if (string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase))
            {
                var ipFallbackResolution = ResolveUsbHostAddressDiagnostics(preferHyperVSocket: false);
                if (!string.IsNullOrWhiteSpace(ipFallbackResolution.ResolvedIpv4))
                {
                    try
                    {
                        await _usbService.AttachToHostAsync(busId, ipFallbackResolution.ResolvedIpv4, CancellationToken.None);
                        _selectedUsbBusId = busId;
                        MarkUsbAttachSuccess(busId);
                        _usbAlreadyExportedNotAttachedStateByBusId.Remove(busId);
                        _usbPotentialLocalDetachStateByBusId.Remove(busId);
                        ApplyUsbTransportResolution(ipFallbackResolution, fallbackToIp: true);

                        GuestLogger.Info("usb.connect.success", "USB Host-Attach erfolgreich (Hyper-V).", new
                        {
                            operationId,
                            busId,
                            elapsedMs = stopwatch.ElapsedMilliseconds,
                            hostAddress = ipFallbackResolution.ResolvedIpv4,
                            hostSource = ipFallbackResolution.Source,
                            transportPath = "hyperv",
                            fallback = "ip"
                        });

                        await TrySendUsbConnectionEventAckAsync(busId, "usb-connected", CancellationToken.None);

                        return 0;
                    }
                    catch (Exception fallbackEx)
                    {
                        GuestLogger.Warn("usb.connect.fallback.failed", fallbackEx.Message, new
                        {
                            operationId,
                            busId,
                            elapsedMs = stopwatch.ElapsedMilliseconds,
                            fallbackHostAddress = ipFallbackResolution.ResolvedIpv4,
                            fallbackHostSource = ipFallbackResolution.Source,
                            transportPath = "hyperv",
                            exceptionType = fallbackEx.GetType().FullName
                        });
                    }
                }
            }

            GuestLogger.Warn("usb.connect.failed", ex.Message, new
            {
                operationId,
                busId,
                elapsedMs = stopwatch.ElapsedMilliseconds,
                hostAddress = hostResolution.ResolvedIpv4,
                hostSource = hostResolution.Source,
                transportPath = string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase)
                    ? "hyperv"
                    : "hyperv",
                exceptionType = ex.GetType().FullName
            });
            return 1;
        }
    }

    private async Task TryRecoverAndRetryUsbAttachAsync(string busId, string hostAddress, CancellationToken cancellationToken)
    {
        try
        {
            await _usbService.DetachFromHostAsync(busId, hostAddress, cancellationToken);
        }
        catch
        {
        }

        await Task.Delay(TimeSpan.FromMilliseconds(850), cancellationToken);
        await _usbService.AttachToHostAsync(busId, hostAddress, cancellationToken);
    }

    private async Task TrySignalStaleAlreadyExportedDisconnectAsync(string busId, string operationId, UsbHostResolution hostResolution, long elapsedMs)
    {
        var normalizedBusId = (busId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedBusId))
        {
            return;
        }

        if (IsAutoUsbDisconnectSignalSuppressed(out var suppressionRemainingSeconds, out var reason))
        {
            GuestLogger.Debug("usb.connect.already_exported.recovery_signal_suppressed_window", "Stale-Export-Recovery-Signal temporär unterdrückt (Schutzfenster).", new
            {
                operationId,
                busId = normalizedBusId,
                reason,
                remainingSuppressionSeconds = suppressionRemainingSeconds
            });
            return;
        }

        if (IsAutoDisconnectSuppressedAfterAttach(normalizedBusId, out var remainingSeconds))
        {
            GuestLogger.Debug("usb.connect.already_exported.recovery_signal_suppressed_recent_attach", "Stale-Export-Recovery unterdrückt, da Gerät frisch attached wurde.", new
            {
                operationId,
                busId = normalizedBusId,
                remainingSuppressionSeconds = remainingSeconds
            });
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var state = _usbAlreadyExportedNotAttachedStateByBusId.TryGetValue(normalizedBusId, out var existing)
            ? existing
            : (FirstSeenUtc: now, LastSeenUtc: now, Count: 0, LastSignalUtc: DateTimeOffset.MinValue);

        state = (state.FirstSeenUtc, now, state.Count + 1, state.LastSignalUtc);
        _usbAlreadyExportedNotAttachedStateByBusId[normalizedBusId] = state;

        if (state.Count < GuestStaleExportDisconnectSignalThreshold)
        {
            return;
        }

        if ((now - state.FirstSeenUtc) < TimeSpan.FromSeconds(GuestStaleExportDisconnectSignalMinAgeSeconds))
        {
            return;
        }

        if (state.LastSignalUtc != DateTimeOffset.MinValue
            && (now - state.LastSignalUtc) < UsbStaleExportHintMinInterval)
        {
            return;
        }

        var confirmedLocalDetach = await ConfirmGuestLocalDetachAsync(normalizedBusId, CancellationToken.None);
        if (!confirmedLocalDetach)
        {
            _usbAlreadyExportedNotAttachedStateByBusId.Remove(normalizedBusId);
            GuestLogger.Debug("usb.connect.already_exported.recovery_signal_recheck_attached", "Stale-Export-Recovery verworfen, da lokaler Recheck weiterhin Attach zeigt.", new
            {
                operationId,
                busId = normalizedBusId,
                count = state.Count
            });
            return;
        }

        try
        {
            await TrySendUsbConnectionEventAckAsync(normalizedBusId, "usb-disconnected", CancellationToken.None);

            state = (state.FirstSeenUtc, state.LastSeenUtc, state.Count, now);
            _usbAlreadyExportedNotAttachedStateByBusId[normalizedBusId] = state;

            GuestLogger.Warn("usb.connect.already_exported.recovery_signal_sent", "Stale-Export-Verdacht bestätigt: usb-disconnected Signal an Host gesendet.", new
            {
                operationId,
                busId = normalizedBusId,
                elapsedMs,
                count = state.Count,
                threshold = GuestStaleExportDisconnectSignalThreshold,
                firstSeenAgeSeconds = (int)Math.Clamp((now - state.FirstSeenUtc).TotalSeconds, 0, int.MaxValue),
                hostAddress = hostResolution.ResolvedIpv4,
                hostSource = hostResolution.Source,
                transportPath = string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase)
                    ? "hyperv"
                    : "hyperv"
            });
        }
        catch (Exception ex)
        {
            GuestLogger.Debug("usb.connect.already_exported.recovery_signal_failed", ex.Message, new
            {
                operationId,
                busId = normalizedBusId,
                count = state.Count,
                exceptionType = ex.GetType().FullName
            });
        }
    }

    private static bool ShouldRetryUsbAttach(Exception exception)
    {
        var message = exception.Message ?? string.Empty;
        return message.Contains("device in error state", StringComparison.OrdinalIgnoreCase)
               || message.Contains("error state", StringComparison.OrdinalIgnoreCase)
               || message.Contains("resource busy", StringComparison.OrdinalIgnoreCase)
               || message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)
               || message.Contains("already in use", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsbAlreadyExportedError(Exception exception)
    {
        var message = exception.Message ?? string.Empty;
        return message.Contains("already exported", StringComparison.OrdinalIgnoreCase)
               || message.Contains("device busy", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldMarkUsbClientUnavailableFromException(Exception exception)
    {
        var message = exception.Message ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("'usbip' not found", StringComparison.OrdinalIgnoreCase)
               || message.Contains("'usbip.exe' not found", StringComparison.OrdinalIgnoreCase)
               || message.Contains("not recognized", StringComparison.OrdinalIgnoreCase)
               || message.Contains("wurde nicht als name eines cmdlet", StringComparison.OrdinalIgnoreCase)
               || message.Contains("konnte nicht gefunden werden", StringComparison.OrdinalIgnoreCase)
               || message.Contains("file not found", StringComparison.OrdinalIgnoreCase)
               || message.Contains("no such file", StringComparison.OrdinalIgnoreCase)
               || message.Contains("could not open vhci", StringComparison.OrdinalIgnoreCase)
               || message.Contains("vhci driver", StringComparison.OrdinalIgnoreCase)
               || message.Contains("usbip_vhci", StringComparison.OrdinalIgnoreCase)
               || message.Contains("driver is not installed", StringComparison.OrdinalIgnoreCase)
               || message.Contains("treiber ist nicht installiert", StringComparison.OrdinalIgnoreCase)
               || message.Contains("stelle sicher, dass usbip-win2 installiert ist", StringComparison.OrdinalIgnoreCase);
    }

    private void MarkUsbAttachSuccess(string busId)
    {
        var normalizedBusId = (busId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedBusId))
        {
            return;
        }

        _usbLastAttachSuccessUtcByBusId[normalizedBusId] = DateTimeOffset.UtcNow;
        _usbDetachHintSuppressionUntilUtcByBusId.TryRemove(normalizedBusId, out _);
        _usbManualDisconnectPendingByBusId.TryRemove(normalizedBusId, out _);
    }

    private void MarkUsbLocalDetachInProgress(string busId)
    {
        var normalizedBusId = (busId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedBusId))
        {
            return;
        }

        _usbLastAttachSuccessUtcByBusId.Remove(normalizedBusId);
        _usbPotentialLocalDetachStateByBusId.Remove(normalizedBusId);
        _usbAlreadyExportedNotAttachedStateByBusId.Remove(normalizedBusId);
        _usbDetachHintSuppressionUntilUtcByBusId[normalizedBusId] = DateTimeOffset.UtcNow.AddSeconds(GuestLocalDetachHintSuppressionSeconds);
        _usbManualDisconnectPendingByBusId[normalizedBusId] = DateTimeOffset.UtcNow;
    }

    private void SyncUsbManualDisconnectPendingState(IReadOnlyList<UsbIpDeviceInfo> remoteDevices)
    {
        if (_usbManualDisconnectPendingByBusId.IsEmpty)
        {
            return;
        }

        var snapshot = _usbManualDisconnectPendingByBusId.ToArray();
        foreach (var entry in snapshot)
        {
            if (entry.Key is null)
            {
                continue;
            }

            var busId = entry.Key.Trim();
            if (string.IsNullOrWhiteSpace(busId))
            {
                _usbManualDisconnectPendingByBusId.TryRemove(entry.Key, out _);
                continue;
            }

            var matching = remoteDevices.FirstOrDefault(device =>
                string.Equals(device.BusId?.Trim(), busId, StringComparison.OrdinalIgnoreCase));

            if (matching is not null && IsGuestLocalAttachedDevice(matching))
            {
                continue;
            }

            if (_usbManualDisconnectPendingByBusId.TryRemove(busId, out var startedUtc))
            {
                GuestLogger.Debug("usb.disconnect.host_state_confirmed", "Host-Status nach manuellem Disconnect bestätigt; Pending-Markierung wurde entfernt.", new
                {
                    busId,
                    pendingForMs = (int)Math.Max(0, (DateTimeOffset.UtcNow - startedUtc).TotalMilliseconds),
                    remoteState = matching is null ? "missing" : (matching.IsAttached ? "attached-without-local-owner" : "detached")
                });
            }
        }
    }

    private bool IsUsbDetachHintSuppressed(string busId)
    {
        var normalizedBusId = (busId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedBusId))
        {
            return false;
        }

        if (!_usbDetachHintSuppressionUntilUtcByBusId.TryGetValue(normalizedBusId, out var untilUtc))
        {
            return false;
        }

        if (untilUtc <= DateTimeOffset.UtcNow)
        {
            _usbDetachHintSuppressionUntilUtcByBusId.TryRemove(normalizedBusId, out _);
            return false;
        }

        return true;
    }

    private bool IsAutoDisconnectSuppressedAfterAttach(string busId, out int remainingSeconds)
    {
        remainingSeconds = 0;

        var normalizedBusId = (busId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedBusId))
        {
            return false;
        }

        if (!_usbLastAttachSuccessUtcByBusId.TryGetValue(normalizedBusId, out var attachedAtUtc))
        {
            return false;
        }

        var elapsed = DateTimeOffset.UtcNow - attachedAtUtc;
        var suppressionWindow = TimeSpan.FromSeconds(GuestAutoDisconnectSuppressionSecondsAfterAttach);
        if (elapsed >= suppressionWindow)
        {
            _usbLastAttachSuccessUtcByBusId.Remove(normalizedBusId);
            return false;
        }

        remainingSeconds = (int)Math.Ceiling((suppressionWindow - elapsed).TotalSeconds);
        return true;
    }

    private void ExtendAutoUsbDisconnectSignalSuppression(TimeSpan duration, string reason)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var untilUtc = DateTimeOffset.UtcNow.Add(duration);
        if (untilUtc > _suppressAutoUsbDisconnectSignalsUntilUtc)
        {
            _suppressAutoUsbDisconnectSignalsUntilUtc = untilUtc;
        }

        GuestLogger.Debug("usb.auto_disconnect.suppression.extend", "Schutzfenster für automatische usb-disconnected Signale verlängert.", new
        {
            reason,
            untilUtc = _suppressAutoUsbDisconnectSignalsUntilUtc,
            durationSeconds = (int)Math.Max(1, Math.Round(duration.TotalSeconds))
        });
    }

    private bool IsAutoUsbDisconnectSignalSuppressed(out int remainingSeconds, out string reason)
    {
        reason = string.Empty;
        remainingSeconds = 0;

        var remaining = _suppressAutoUsbDisconnectSignalsUntilUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return false;
        }

        reason = "transient-protection-window";
        remainingSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
        return true;
    }

    private void ExtendUsbListClearingSuppression(TimeSpan duration, string reason)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var untilUtc = DateTimeOffset.UtcNow.Add(duration);
        if (untilUtc > _suppressUsbListClearingUntilUtc)
        {
            _suppressUsbListClearingUntilUtc = untilUtc;
        }

        GuestLogger.Debug("usb.refresh.list_clear_suppression.extend", "Schutzfenster zum Erhalt der USB-Liste verlängert.", new
        {
            reason,
            untilUtc = _suppressUsbListClearingUntilUtc,
            durationSeconds = (int)Math.Max(1, Math.Round(duration.TotalSeconds))
        });
    }

    private bool IsUsbListClearingSuppressed(out int remainingSeconds, out string reason)
    {
        reason = string.Empty;
        remainingSeconds = 0;

        var remaining = _suppressUsbListClearingUntilUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return false;
        }

        reason = "transient-reopen-window";
        remainingSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
        return true;
    }

    private async Task SchedulePostThemeReopenUsbRefreshAsync()
    {
        if (_isExitRequested)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1100));
            await RefreshUsbDevicesAsync(emitLogs: false, bypassRateLimit: true, forceHostIdentityRefresh: true);

            await Task.Delay(TimeSpan.FromMilliseconds(1500));
            await RefreshUsbDevicesAsync(emitLogs: false, bypassRateLimit: true, forceHostIdentityRefresh: true);
        }
        catch (Exception ex)
        {
            GuestLogger.Debug("usb.refresh.post_theme_reopen.failed", ex.Message, new
            {
                exceptionType = ex.GetType().FullName
            });
        }
    }

    private async Task<bool> ConfirmGuestLocalDetachAsync(string busId, CancellationToken cancellationToken)
    {
        var normalizedBusId = (busId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedBusId))
        {
            return false;
        }

        for (var attempt = 1; attempt <= GuestLocalDetachConfirmationAttempts; attempt++)
        {
            IReadOnlyList<UsbIpDeviceInfo> localDevices;
            try
            {
                localDevices = await GetLocalUsbDevicesWithTimeoutAsync(cancellationToken);
            }
            catch
            {
                return false;
            }

            var stillAttached = localDevices.Any(device =>
                string.Equals(device.BusId?.Trim(), normalizedBusId, StringComparison.OrdinalIgnoreCase)
                && IsGuestLocalAttachedDevice(device));

            if (stillAttached)
            {
                return false;
            }

            if (attempt < GuestLocalDetachConfirmationAttempts)
            {
                try
                {
                    await Task.Delay(GuestLocalDetachConfirmationDelayMs, cancellationToken);
                }
                catch
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsTransientHyperVTransportFailure(Exception exception)
    {
        var message = exception.Message ?? string.Empty;
        return message.Contains("Pufferspeicher", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Warteschlange voll", StringComparison.OrdinalIgnoreCase)
               || message.Contains("No buffer space", StringComparison.OrdinalIgnoreCase)
               || message.Contains("queue full", StringComparison.OrdinalIgnoreCase)
               || message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)
               || message.Contains("existing connection was forcibly closed", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Eine vorhandene Verbindung wurde vom Remotehost geschlossen", StringComparison.OrdinalIgnoreCase)
               || message.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
               || message.Contains("connection aborted", StringComparison.OrdinalIgnoreCase)
               || message.Contains("connection timed out", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Verbindungsversuch", StringComparison.OrdinalIgnoreCase)
               || message.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
               || message.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase);
    }

    private void MarkUsbClientUnavailableAndRefreshUi(string eventName, string? busId, string reason)
    {
        _isUsbClientAvailable = false;
        _usbClientMissingLogged = true;

        GuestLogger.Warn(eventName, "USB/IP-Client fehlt oder ist nicht funktionsfähig. Bitte usbip-win2 neu installieren.", new
        {
            busId,
            reason,
            source = "https://github.com/vadimgrn/usbip-win2"
        });

        void apply()
        {
            _mainWindow?.UpdateUsbClientAvailability(false);
            UpdateTrayControlCenterView();
        }

        if (_mainWindow?.DispatcherQueue is { } queue && !queue.HasThreadAccess)
        {
            _ = queue.TryEnqueue(apply);
            return;
        }

        apply();
    }

    private async Task<string?> DiscoverUsbHostAddressAsync()
    {
        var hostIdentity = await FetchHostIdentityViaHyperVSocketAsync(CancellationToken.None);
        if (hostIdentity is not null)
        {
            ApplyHostIdentityToConfig(hostIdentity, persistConfig: true);

            var discoveredHostName = (hostIdentity.HostName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(discoveredHostName))
            {
                GuestLogger.Info("usb.host.discovery.hostname", "Hostname per Hyper-V Socket gefunden.", new
                {
                    requester = Environment.MachineName,
                    discoveredHostName
                });

                return discoveredHostName;
            }
        }

        GuestLogger.Warn("usb.host.discovery.empty", "Kein Hostname über Hyper-V Socket gefunden.", new
        {
            requester = Environment.MachineName
        });

        return null;
    }

    private async Task<HostIdentityInfo?> FetchHostIdentityViaHyperVSocketAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        lock (_hyperVSocketMetricsSync)
        {
            _metricHostIdentityFetchAttempts++;
        }

        try
        {
            var client = new HyperVSocketHostIdentityGuestClient();
            var identity = await client.FetchHostIdentityAsync(cancellationToken, forceRefresh);
            ResetRateLimitedWarning("guest.hostidentity.fetch_failed", "hyperv");

            if (identity is not null)
            {
                lock (_hyperVSocketMetricsSync)
                {
                    _metricHostIdentityFetchSuccesses++;
                    _lastHostIdentitySuccessUtc = DateTimeOffset.UtcNow;
                    _lastHostIdentityFailure = string.Empty;
                }
            }

            return identity;
        }
        catch (Exception ex)
        {
            MarkHyperVServiceUnreachableImmediate();
            var socketException = GetSocketException(ex);
            lock (_hyperVSocketMetricsSync)
            {
                _metricHostIdentityFetchFailures++;
                _lastHostIdentityFailure = $"{ex.GetType().Name}:{ex.Message} (Socket={socketException?.SocketErrorCode}, Native={socketException?.NativeErrorCode})";
            }

            WarnRateLimited(
                "guest.hostidentity.fetch_failed",
                ex.Message,
                new
                {
                    exceptionType = ex.GetType().FullName,
                    socketErrorCode = socketException?.SocketErrorCode.ToString(),
                    nativeErrorCode = socketException?.NativeErrorCode,
                    hResult = socketException?.HResult
                },
                "hyperv",
                TimeSpan.FromMinutes(2));
            UpdateUsbDiagnosticsPanel();
            return null;
        }
    }

    private async Task TryRefreshHostIdentityAfterUsbRefreshAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if ((now - _lastHostIdentityBackgroundAttemptUtc) < HostIdentityRefreshAfterUsbRefreshInterval)
        {
            return;
        }

        _lastHostIdentityBackgroundAttemptUtc = now;
        var identity = await FetchHostIdentityViaHyperVSocketAsync(cancellationToken);
        if (identity is null)
        {
            return;
        }

        ApplyHostIdentityToConfig(identity, persistConfig: false);
    }

    private Task<int> DisconnectUsbAsync(string busId)
    {
        return DisconnectUsbAsync(busId, bypassHostFeaturePolicy: false);
    }

    private async Task<int> DisconnectUsbAsync(string busId, bool bypassHostFeaturePolicy)
    {
        var useHyperVSocket = _config?.Usb?.UseHyperVSocket != false;
        if (!bypassHostFeaturePolicy && useHyperVSocket && _config?.Usb?.HostFeatureEnabled == false)
        {
            GuestLogger.Warn("usb.disconnect.blocked_host_policy", "USB Disconnect blockiert: Host hat USB Share deaktiviert.", new
            {
                busId
            });
            return 1;
        }

        if (!_isUsbClientAvailable)
        {
            GuestLogger.Warn("usb.disconnect.blocked", "USB/IP-Client nicht installiert. Disconnect ist deaktiviert.", new
            {
                busId,
                source = "https://github.com/vadimgrn/usbip-win2"
            });
            return 1;
        }

        if (string.IsNullOrWhiteSpace(busId))
        {
            GuestLogger.Warn("usb.disconnect.invalid", "USB Disconnect mit leerer BUSID angefordert.");
            return 1;
        }

        var operationId = CreateUsbOperationId("disconnect");
        var stopwatch = Stopwatch.StartNew();
        var hostResolution = ResolveUsbHostAddressDiagnostics();
        ApplyUsbTransportResolution(hostResolution);
        var beforeDevice = FindUsbByBusId(busId);

        GuestLogger.Info("usb.disconnect.begin", "USB Host-Detach gestartet.", new
        {
            operationId,
            busId,
            selectedBusId = _selectedUsbBusId,
            hostAddress = hostResolution.ResolvedIpv4,
            hostSource = hostResolution.Source,
            hostInput = hostResolution.RawInput,
            beforeState = beforeDevice?.StateText,
            beforeClientIp = beforeDevice?.ClientIpAddress,
            beforePersistedGuid = beforeDevice?.PersistedGuid
        });

        try
        {
            await _usbService.DetachFromHostAsync(busId, hostResolution.ResolvedIpv4, CancellationToken.None);
            _selectedUsbBusId = busId;
            MarkUsbLocalDetachInProgress(busId);
            ApplyUsbTransportResolution(hostResolution);

            GuestLogger.Info("usb.disconnect.success", "USB Host-Detach erfolgreich.", new
            {
                operationId,
                busId,
                elapsedMs = stopwatch.ElapsedMilliseconds,
                hostAddress = hostResolution.ResolvedIpv4,
                hostSource = hostResolution.Source,
                transportPath = string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase)
                    ? "hyperv"
                    : "hyperv"
            });

                    await TrySendUsbConnectionEventAckAsync(busId, "usb-disconnected", CancellationToken.None);
            SafeFireAndForget.Run(
                        RefreshUsbDevicesAsync(emitLogs: false, bypassRateLimit: true, forceHostIdentityRefresh: true),
                operation: "usb-refresh-post-disconnect");

            return 0;
        }
        catch (Exception ex)
        {
            if (string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase))
            {
                var ipFallbackResolution = ResolveUsbHostAddressDiagnostics(preferHyperVSocket: false);
                if (!string.IsNullOrWhiteSpace(ipFallbackResolution.ResolvedIpv4))
                {
                    try
                    {
                        await _usbService.DetachFromHostAsync(busId, ipFallbackResolution.ResolvedIpv4, CancellationToken.None);
                        _selectedUsbBusId = busId;
                        MarkUsbLocalDetachInProgress(busId);
                        ApplyUsbTransportResolution(ipFallbackResolution, fallbackToIp: true);

                        GuestLogger.Info("usb.disconnect.success", "USB Host-Detach erfolgreich (Hyper-V).", new
                        {
                            operationId,
                            busId,
                            elapsedMs = stopwatch.ElapsedMilliseconds,
                            hostAddress = ipFallbackResolution.ResolvedIpv4,
                            hostSource = ipFallbackResolution.Source,
                            transportPath = "hyperv",
                            fallback = "ip"
                        });

                        await TrySendUsbConnectionEventAckAsync(busId, "usb-disconnected", CancellationToken.None);
                        SafeFireAndForget.Run(
                            RefreshUsbDevicesAsync(emitLogs: false, bypassRateLimit: true, forceHostIdentityRefresh: true),
                            operation: "usb-refresh-post-disconnect-fallback");

                        return 0;
                    }
                    catch (Exception fallbackEx)
                    {
                        GuestLogger.Warn("usb.disconnect.fallback.failed", fallbackEx.Message, new
                        {
                            operationId,
                            busId,
                            elapsedMs = stopwatch.ElapsedMilliseconds,
                            fallbackHostAddress = ipFallbackResolution.ResolvedIpv4,
                            fallbackHostSource = ipFallbackResolution.Source,
                            transportPath = "hyperv",
                            exceptionType = fallbackEx.GetType().FullName
                        });
                    }
                }
            }

            GuestLogger.Warn("usb.disconnect.failed", ex.Message, new
            {
                operationId,
                busId,
                elapsedMs = stopwatch.ElapsedMilliseconds,
                hostAddress = hostResolution.ResolvedIpv4,
                hostSource = hostResolution.Source,
                transportPath = string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase)
                    ? "hyperv"
                    : "hyperv",
                exceptionType = ex.GetType().FullName
            });
            return 1;
        }
    }

    private UsbHostResolution ResolveUsbHostAddressDiagnostics(bool preferHyperVSocket = true)
    {
        UsbHostResolution Track(UsbHostResolution resolution)
        {
            lock (_hyperVSocketMetricsSync)
            {
                _metricHostResolutionCalls++;

                var source = (resolution.Source ?? string.Empty).Trim().ToLowerInvariant();
                if (source == "hyperv-socket")
                {
                    _metricHostResolutionHyperV++;
                }
                else
                {
                    _metricHostResolutionOther++;
                }

                if (!string.IsNullOrWhiteSpace(resolution.FailureReason))
                {
                    _metricHostResolutionFailures++;
                }
            }

            return resolution;
        }

        if (preferHyperVSocket && _usbHyperVSocketProxy?.IsRunning == true)
        {
            return Track(new UsbHostResolution(
                HyperVSocketUsbTunnelDefaults.LoopbackAddress,
                "hyperv-socket",
                "AF_HYPERV",
                string.Empty));
        }

        return Track(new UsbHostResolution(string.Empty, "hyperv-socket", "AF_HYPERV", "Hyper-V Socket USB-Proxy ist nicht aktiv."));
    }

    private async Task RefreshUsbClientAvailabilityAsync()
    {
        try
        {
            _isUsbClientAvailable = await _usbService.IsUsbClientAvailableAsync(CancellationToken.None);
            if (_isUsbClientAvailable)
            {
                _usbClientMissingLogged = false;
            }
        }
        catch
        {
            _isUsbClientAvailable = false;
        }

        if (_mainWindow?.DispatcherQueue is { } queue && !queue.HasThreadAccess)
        {
            _ = queue.TryEnqueue(() => _mainWindow?.UpdateUsbClientAvailability(_isUsbClientAvailable));
        }
        else
        {
            _mainWindow?.UpdateUsbClientAvailability(_isUsbClientAvailable);
        }

        UpdateTrayControlCenterView();
    }

    private static string ResolveToIpv4(string value)
    {
        var candidate = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        if (IPAddress.TryParse(candidate, out var parsedIp) && parsedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return parsedIp.ToString();
        }

        try
        {
            var addresses = Dns.GetHostAddresses(candidate);
            var ipv4 = addresses.FirstOrDefault(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return ipv4?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ResolveDefaultGatewayIpv4()
    {
        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var properties = networkInterface.GetIPProperties();
                var gateway = properties.GatewayAddresses
                    .Select(item => item.Address)
                    .FirstOrDefault(address =>
                        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        && !IPAddress.Any.Equals(address)
                        && !IPAddress.None.Equals(address));

                if (gateway is not null)
                {
                    return gateway.ToString();
                }
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private UsbIpDeviceInfo? FindUsbByBusId(string busId)
    {
        if (string.IsNullOrWhiteSpace(busId))
        {
            return null;
        }

        return _usbDevices.FirstOrDefault(item => string.Equals(item.BusId, busId, StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateUsbOperationId(string action)
    {
        return $"usb-{action}-{Guid.NewGuid():N}";
    }

    private sealed record UsbHostResolution(string ResolvedIpv4, string Source, string RawInput, string FailureReason);

    private async Task ConnectSelectedUsbFromTrayAsync()
    {
        var selected = GetSelectedUsbDevice();
        if (selected is null)
        {
            return;
        }

        await ConnectUsbAsync(selected.BusId);
        await RefreshUsbDevicesAsync();
    }

    private async Task DisconnectSelectedUsbFromTrayAsync()
    {
        var selected = GetSelectedUsbDevice();
        if (selected is null)
        {
            return;
        }

        var result = await DisconnectUsbAsync(selected.BusId);
        if (result == 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        await RefreshUsbDevicesAsync();
    }

    private async Task DisconnectAllAttachedUsbOnExitAsync()
    {
        if (_config?.Usb?.DisconnectOnExit == false)
        {
            GuestLogger.Info("usb.exit_disconnect.skipped", "USB-Disconnect beim Beenden per Einstellung deaktiviert.");
            return;
        }

        try
        {
            await RefreshUsbDevicesAsync();
        }
        catch (Exception ex)
        {
            GuestLogger.Warn("usb.exit_refresh.failed", ex.Message, new
            {
                exceptionType = ex.GetType().FullName
            });
        }

        var attachedBusIds = _usbDevices
            .Where(device => IsGuestLocalAttachedDevice(device) && !string.IsNullOrWhiteSpace(device.BusId))
            .Select(device => device.BusId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (attachedBusIds.Count == 0)
        {
            return;
        }

        GuestLogger.Info("usb.exit_disconnect.begin", "Guest-App beendet, trenne verbundene USB-Geräte.", new
        {
            count = attachedBusIds.Count,
            busIds = attachedBusIds
        });

        var disconnectedCount = 0;
        var failedCount = 0;

        foreach (var busId in attachedBusIds)
        {
            try
            {
                var result = await DisconnectUsbAsync(busId, bypassHostFeaturePolicy: true);
                if (result == 0)
                {
                    disconnectedCount++;
                }
                else
                {
                    failedCount++;
                }
            }
            catch (Exception ex)
            {
                failedCount++;
                GuestLogger.Warn("usb.exit_disconnect.item_failed", ex.Message, new
                {
                    busId,
                    exceptionType = ex.GetType().FullName
                });
            }
        }

        GuestLogger.Info("usb.exit_disconnect.done", "USB-Disconnect beim Beenden abgeschlossen.", new
        {
            requestedCount = attachedBusIds.Count,
            disconnectedCount,
            failedCount
        });
    }

    private async Task DisconnectAllAttachedUsbForTransportSwitchAsync()
    {
        try
        {
            await RefreshUsbDevicesAsync(emitLogs: false);
        }
        catch
        {
        }

        var attachedBusIds = _usbDevices
            .Where(device => IsGuestLocalAttachedDevice(device) && !string.IsNullOrWhiteSpace(device.BusId))
            .Select(device => device.BusId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (attachedBusIds.Count == 0)
        {
            return;
        }

        var hostResolution = ResolveUsbHostAddressDiagnostics();
        var disconnectedCount = 0;
        var failedCount = 0;

        foreach (var busId in attachedBusIds)
        {
            try
            {
                await _usbService.DetachFromHostAsync(busId, hostResolution.ResolvedIpv4, CancellationToken.None);
                await TrySendUsbConnectionEventAckAsync(busId, "usb-disconnected", CancellationToken.None);
                disconnectedCount++;
                continue;
            }
            catch
            {
            }

            if (string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase))
            {
                var ipFallbackResolution = ResolveUsbHostAddressDiagnostics(preferHyperVSocket: false);
                if (!string.IsNullOrWhiteSpace(ipFallbackResolution.ResolvedIpv4))
                {
                    try
                    {
                        await _usbService.DetachFromHostAsync(busId, ipFallbackResolution.ResolvedIpv4, CancellationToken.None);
                        await TrySendUsbConnectionEventAckAsync(busId, "usb-disconnected", CancellationToken.None);
                        disconnectedCount++;
                        continue;
                    }
                    catch
                    {
                    }
                }
            }

            failedCount++;
        }

        try
        {
            await RefreshUsbDevicesAsync(emitLogs: false);
        }
        catch
        {
        }

        GuestLogger.Info("usb.transport.switch.disconnect", "USB-Geräte vor Transportwechsel getrennt.", new
        {
            requestedCount = attachedBusIds.Count,
            disconnectedCount,
            failedCount
        });
    }

    private void StartUsbAutoRefreshLoop()
    {
        StopUsbAutoRefreshLoop();

        var cts = new CancellationTokenSource();
        _usbAutoRefreshCts = cts;
        _usbAutoRefreshTask = Task.Run(() => RunUsbAutoRefreshLoopAsync(cts.Token));
    }

    private void StopUsbAutoRefreshLoop()
    {
        try
        {
            _usbAutoRefreshCts?.Cancel();
        }
        catch
        {
        }

        _usbAutoRefreshCts?.Dispose();
        _usbAutoRefreshCts = null;
        _usbAutoRefreshTask = null;
    }

    private void StartUsbChangeNotificationSubscriber()
    {
        StopUsbChangeNotificationSubscriber();

        var cts = new CancellationTokenSource();
        _usbChangeNotificationSubscriberCts = cts;
        _usbChangeNotificationSubscriberTask = Task.Run(() => RunUsbChangeNotificationSubscriberAsync(cts.Token));
    }

    private void StopUsbChangeNotificationSubscriber()
    {
        try
        {
            _usbChangeNotificationSubscriberCts?.Cancel();
        }
        catch
        {
        }

        _usbChangeNotificationSubscriberCts?.Dispose();
        _usbChangeNotificationSubscriberCts = null;
        _usbChangeNotificationSubscriberTask = null;

        try
        {
            _usbPushFollowUpRefreshCts?.Cancel();
        }
        catch
        {
        }

        _usbPushFollowUpRefreshCts?.Dispose();
        _usbPushFollowUpRefreshCts = null;
    }

    private async Task RunUsbChangeNotificationSubscriberAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var subscriber = new HyperVSocketUsbChangeNotificationGuestSubscriber();
                await subscriber.SubscribeAsync(OnUsbShareChangedPushNotification, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Connection failed or dropped; retry after backoff.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void OnUsbShareChangedPushNotification()
    {
        if (_isExitRequested || !_isUsbClientAvailable || _config?.Usb?.Enabled == false)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if ((now - _lastUsbPushRefreshTriggeredUtc) < UsbPushRefreshMinInterval)
        {
            return;
        }

        _lastUsbPushRefreshTriggeredUtc = now;

        GuestLogger.Debug("usb.push.share_changed", "Host hat USB-Share-Änderung gemeldet. Sofortiger Refresh wird ausgelöst.");
        _ = RefreshUsbDevicesAsync(emitLogs: false, bypassRateLimit: true, forceHostIdentityRefresh: true);
        ScheduleUsbPushFollowUpRefresh();
    }

    private void ScheduleUsbPushFollowUpRefresh()
    {
        try
        {
            _usbPushFollowUpRefreshCts?.Cancel();
        }
        catch
        {
        }

        _usbPushFollowUpRefreshCts?.Dispose();
        _usbPushFollowUpRefreshCts = new CancellationTokenSource();
        var token = _usbPushFollowUpRefreshCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                // Delayed second pass to catch host-side detach/unshare propagation.
                await Task.Delay(TimeSpan.FromSeconds(3), token);
                await RefreshUsbDevicesAsync(emitLogs: false, bypassRateLimit: true, forceHostIdentityRefresh: true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                GuestLogger.Debug("usb.push.followup_refresh_failed", ex.Message, new
                {
                    exceptionType = ex.GetType().FullName
                });
            }
        }, token);
    }

    private async Task RunUsbAutoRefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var hasAttachedDevice = HasAttachedUsbDevice();

                if (!_isExitRequested && _config?.Usb?.Enabled != false)
                {
                    var wasHostUsbEnabled = _config?.Usb?.HostFeatureEnabled != false;
                    var shouldRefreshHostIdentity = !hasAttachedDevice
                        || (DateTimeOffset.UtcNow - _lastHostIdentityBackgroundAttemptUtc) >= TimeSpan.FromSeconds(GuestHostIdentityBackgroundRefreshSeconds);

                    if (shouldRefreshHostIdentity)
                    {
                        _lastHostIdentityBackgroundAttemptUtc = DateTimeOffset.UtcNow;

                        try
                        {
                            var identity = await FetchHostIdentityViaHyperVSocketAsync(cancellationToken);
                            if (identity is not null)
                            {
                                ApplyHostIdentityToConfig(identity, persistConfig: false);
                            }
                        }
                        catch
                        {
                        }
                    }

                    var isHostUsbEnabled = _config?.Usb?.HostFeatureEnabled != false;
                    if (!wasHostUsbEnabled && isHostUsbEnabled && _isUsbClientAvailable)
                    {
                        await RefreshUsbDevicesAsync(emitLogs: false);
                    }
                }

                if (!_isExitRequested && _isUsbClientAvailable && _config?.Usb?.Enabled != false)
                {
                    var shouldRunBackgroundUsbRefresh = !hasAttachedDevice
                        || (DateTimeOffset.UtcNow - _lastUsbBackgroundRefreshAttemptUtc) >= TimeSpan.FromSeconds(GuestUsbAttachedBackgroundRefreshSeconds);

                    if (shouldRunBackgroundUsbRefresh)
                    {
                        _lastUsbBackgroundRefreshAttemptUtc = DateTimeOffset.UtcNow;
                        await RefreshUsbDevicesAsync(emitLogs: false);
                    }
                }

                if (!_isExitRequested && _config?.Usb?.Enabled != false)
                {
                    await TrySendAttachedUsbHeartbeatsAsync(cancellationToken);
                }
            }
            catch
            {
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(GetUsbAutoRefreshIntervalSeconds()), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private int GetUsbAutoRefreshIntervalSeconds()
    {
        if (_config?.Usb?.Enabled == false || !_isUsbClientAvailable)
        {
            return GuestUsbAutoRefreshSlowSeconds;
        }

        if (!HasConfiguredUsbAutoConnect())
        {
            return GuestUsbAutoRefreshNoAutoConnectSeconds;
        }

        var hasAttachedDevice = HasAttachedUsbDevice();
        return hasAttachedDevice
            ? GuestUsbAutoRefreshSlowSeconds
            : GuestUsbAutoRefreshFastSeconds;
    }

    private bool HasAttachedUsbDevice()
    {
        return _usbDevices.Any(device => IsGuestLocalAttachedDevice(device) && !string.IsNullOrWhiteSpace(device.BusId));
    }

    private static bool IsGuestLocalAttachedDevice(UsbIpDeviceInfo? device)
    {
        if (device is null || !device.IsAttached || device.IsAttachedByOtherGuest)
        {
            return false;
        }

        var clientIp = (device.ClientIpAddress ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(clientIp);
    }

    private void StartSharedFolderAutoMountLoop()
    {
        StopSharedFolderAutoMountLoop();

        var cts = new CancellationTokenSource();
        _sharedFolderAutoMountCts = cts;
        _sharedFolderAutoMountTask = Task.Run(() => RunSharedFolderAutoMountLoopAsync(cts.Token));
        UpdateSharedFolderReconnectStatusPanel();
    }

    private void StartHyperVSocketMonitorLoop()
    {
        StopHyperVSocketMonitorLoop();

        var cts = new CancellationTokenSource();
        _hyperVSocketMonitorCts = cts;
        _hyperVSocketMonitorTask = Task.Run(() => RunHyperVSocketMonitorLoopAsync(cts.Token));
    }

    private void StopHyperVSocketMonitorLoop()
    {
        try
        {
            _hyperVSocketMonitorCts?.Cancel();
        }
        catch
        {
        }

        _hyperVSocketMonitorCts?.Dispose();
        _hyperVSocketMonitorCts = null;
        _hyperVSocketMonitorTask = null;
    }

    private async Task RunHyperVSocketMonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_isExitRequested && _config?.Ui?.DebugLoggingEnabled == true)
                {
                    var gate = HyperVSocketClientConcurrencyGate.GetSnapshot();
                    var hostResolution = ResolveUsbHostAddressDiagnostics();
                    var metrics = BuildHyperVSocketMetricsSnapshot();

                    GuestLogger.Debug("hyperv.monitor.heartbeat", "Hyper-V Socket Monitor Heartbeat.", new
                    {
                        transport = new
                        {
                            useHyperVSocket = _config?.Usb?.UseHyperVSocket != false,
                            hyperVProxyRunning = _usbHyperVSocketProxy?.IsRunning == true,
                            hyperVTransportActive = _usbHyperVSocketTransportActive,
                            hyperVServiceReachable = _usbHyperVSocketServiceReachable,
                            ipFallbackActive = _usbIpFallbackActive,
                            hostFeatureEnabled = _config?.Usb?.HostFeatureEnabled != false,
                            sharedFoldersFeatureEnabled = _config?.SharedFolders?.HostFeatureEnabled != false
                        },
                        hostResolution = new
                        {
                            resolvedIpv4 = hostResolution.ResolvedIpv4,
                            source = hostResolution.Source,
                            rawInput = hostResolution.RawInput,
                            resolutionError = hostResolution.FailureReason
                        },
                        tasks = new
                        {
                            usbDiagnosticsRunning = _usbDiagnosticsTask is { IsCompleted: false },
                            usbAutoRefreshRunning = _usbAutoRefreshTask is { IsCompleted: false },
                            usbPushSubscriberRunning = _usbChangeNotificationSubscriberTask is { IsCompleted: false },
                            sharedFolderAutoMountRunning = _sharedFolderAutoMountTask is { IsCompleted: false },
                            resourceMonitorRunning = _resourceMonitorAgent.IsRunning
                        },
                        socketGate = gate,
                        metrics
                    });
                }
            }
            catch
            {
            }

            try
            {
                await Task.Delay(HyperVSocketMonitorHeartbeatInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private object BuildHyperVSocketMetricsSnapshot()
    {
        lock (_hyperVSocketMetricsSync)
        {
            var connectionMetrics = HyperVSocketConnectionMetrics.GetSnapshot();
            return new
            {
                hostIdentityFetchAttempts = _metricHostIdentityFetchAttempts,
                hostIdentityFetchSuccesses = _metricHostIdentityFetchSuccesses,
                hostIdentityFetchFailures = _metricHostIdentityFetchFailures,
                lastHostIdentitySuccessUtc = _lastHostIdentitySuccessUtc,
                lastHostIdentityFailure = _lastHostIdentityFailure,
                sharedFolderCatalogFetchAttempts = _metricSharedFolderCatalogFetchAttempts,
                sharedFolderCatalogFetchSuccesses = _metricSharedFolderCatalogFetchSuccesses,
                sharedFolderCatalogFetchFailures = _metricSharedFolderCatalogFetchFailures,
                lastSharedFolderCatalogSuccessUtc = _lastSharedFolderCatalogSuccessUtc,
                lastSharedFolderCatalogFailure = _lastSharedFolderCatalogFailure,
                transportSelfTestRuns = _metricTransportSelfTestRuns,
                transportSelfTestFailures = _metricTransportSelfTestFailures,
                lastTransportSelfTestUtc = _lastTransportSelfTestUtc,
                lastTransportSelfTestFailure = _lastTransportSelfTestFailure,
                diagnosticsAckSent = _metricDiagnosticsAckSent,
                diagnosticsAckFailed = _metricDiagnosticsAckFailed,
                lastDiagnosticsAckSuccessUtc = _lastDiagnosticsAckSuccessUtc,
                lastDiagnosticsAckFailure = _lastDiagnosticsAckFailure,
                usbEventAckSent = _metricUsbEventAckSent,
                usbEventAckFailed = _metricUsbEventAckFailed,
                lastUsbEventAckSuccessUtc = _lastUsbEventAckSuccessUtc,
                lastUsbEventAckFailure = _lastUsbEventAckFailure,
                hostResolutionCalls = _metricHostResolutionCalls,
                hostResolutionHyperV = _metricHostResolutionHyperV,
                hostResolutionOther = _metricHostResolutionOther,
                hostResolutionFailures = _metricHostResolutionFailures,
                openedConnections = connectionMetrics.OpenedConnections,
                closedConnections = connectionMetrics.ClosedConnections,
                activeConnections = connectionMetrics.ActiveConnections,
                failedConnects = connectionMetrics.FailedConnects,
                reconnectAttempts = connectionMetrics.ReconnectAttempts,
                requestsCoalesced = connectionMetrics.RequestsCoalesced,
                backoffActivations = connectionMetrics.BackoffActivations,
                circuitBreakerOpen = connectionMetrics.CircuitBreakerOpen,
                openConnectionsByPurpose = connectionMetrics.OpenConnectionsByPurpose
            };
        }
    }

    private void StopSharedFolderAutoMountLoop()
    {
        try
        {
            _sharedFolderAutoMountCts?.Cancel();
        }
        catch
        {
        }

        _sharedFolderAutoMountCts?.Dispose();
        _sharedFolderAutoMountCts = null;
        _sharedFolderAutoMountTask = null;
        UpdateSharedFolderReconnectStatusPanel();
    }

    private void TriggerSharedFolderReconnectCycle()
    {
        SafeFireAndForget.Run(Task.Run(async () =>
        {
            if (!await _sharedFolderReconnectTriggerGate.WaitAsync(0))
            {
                GuestLogger.Debug("sharedfolders.reconnect.trigger_skipped_busy", "Shared-Folder Reconnect-Trigger übersprungen, weil bereits ein Trigger-Lauf aktiv ist.", BuildSharedFolderSocketDiagnostics("trigger_skipped_busy", null, null));
                return;
            }

            try
            {
                if (!_isExitRequested)
                {
                    await RefreshSharedFolderHostNameFromSocketAsync(CancellationToken.None);
                }

                if (!_isExitRequested && HasConfiguredSharedFolderAutoMount())
                {
                    var result = await EnsureEnabledSharedFolderMappingsAsync(CancellationToken.None);
                    _sharedFolderReconnectLastRunUtc = DateTimeOffset.UtcNow;
                    _sharedFolderReconnectLastSummary = BuildSharedFolderReconnectSummary(result);
                }
                else
                {
                    _sharedFolderReconnectLastSummary = "Keine aktiven Mappings konfiguriert.";
                }
            }
            catch (Exception ex)
            {
                var socketException = GetSocketException(ex);
                WarnRateLimited(
                    "sharedfolders.reconnect.trigger_failed",
                    ex.Message,
                    new
                    {
                        exceptionType = ex.GetType().FullName,
                        socketErrorCode = socketException?.SocketErrorCode.ToString(),
                        nativeErrorCode = socketException?.NativeErrorCode,
                        diagnostics = BuildSharedFolderSocketDiagnostics("trigger_failed", null, null)
                    },
                    "reconnect-trigger",
                    RecurringWarnRateLimitInterval);
            }
            finally
            {
                _sharedFolderReconnectTriggerGate.Release();
                UpdateSharedFolderReconnectStatusPanel();
            }
        }), operation: "sharedfolders-reconnect-trigger");
    }

    private async Task RunSharedFolderAutoMountLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_isExitRequested)
                {
                    await RefreshSharedFolderHostNameFromSocketAsync(cancellationToken);
                }

                if (!_isExitRequested && HasConfiguredSharedFolderAutoMount())
                {
                    var result = await EnsureEnabledSharedFolderMappingsAsync(cancellationToken);
                    _sharedFolderReconnectLastRunUtc = DateTimeOffset.UtcNow;
                    _sharedFolderReconnectLastSummary = BuildSharedFolderReconnectSummary(result);
                    UpdateSharedFolderReconnectStatusPanel();
                }
                else
                {
                    _sharedFolderReconnectLastSummary = "Keine aktiven Mappings konfiguriert.";
                    UpdateSharedFolderReconnectStatusPanel();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(GetSharedFolderReconnectIntervalSeconds()), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private bool HasConfiguredSharedFolderAutoMount()
    {
        if (_config?.SharedFolders is null || !_config.SharedFolders.Enabled || _config.SharedFolders.HostFeatureEnabled == false)
        {
            return false;
        }

        var mappings = _config?.SharedFolders?.Mappings;
        return mappings is not null
               && mappings.Any(mapping => mapping.Enabled
                                           && !string.IsNullOrWhiteSpace(mapping.SharePath)
                                           && !string.IsNullOrWhiteSpace(mapping.DriveLetter));
    }

    private int GetSharedFolderReconnectIntervalSeconds()
    {
        return Math.Clamp(_config?.PollIntervalSeconds ?? 15, 5, 3600);
    }

    private static string BuildSharedFolderReconnectSummary(SharedFolderReconnectCycleResult result)
    {
        return $"Lauf: {result.Attempted} geprüft, {result.NewlyMounted} neu verbunden, {result.Failed} Fehler";
    }

    private void UpdateSharedFolderReconnectStatusPanel()
    {
        var reconnectActive = !_isExitRequested && HasConfiguredSharedFolderAutoMount();
        var lastRunUtc = _sharedFolderReconnectLastRunUtc;
        var summary = _sharedFolderReconnectLastSummary;

        void apply()
        {
            _mainWindow?.UpdateSharedFolderReconnectStatus(reconnectActive, lastRunUtc, summary);
        }

        if (_mainWindow?.DispatcherQueue is { } queue && !queue.HasThreadAccess)
        {
            _ = queue.TryEnqueue(apply);
            return;
        }

        apply();
    }

    private async Task<SharedFolderReconnectCycleResult> EnsureEnabledSharedFolderMappingsAsync(CancellationToken cancellationToken)
    {
        await _sharedFolderReconnectGate.WaitAsync(cancellationToken);
        try
        {
            var attempted = 0;
            var newlyMounted = 0;
            var failed = 0;

            if (_config?.SharedFolders is null || !_config.SharedFolders.Enabled)
            {
                var legacyLetters = _config?.SharedFolders?.Mappings?
                    .Select(mapping => GuestConfigService.NormalizeDriveLetter(mapping.DriveLetter))
                    .Where(letter => !string.IsNullOrWhiteSpace(letter))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    ?? [];

                if (!string.IsNullOrWhiteSpace(_config?.SharedFolders?.BaseDriveLetter))
                {
                    legacyLetters.Add(GuestConfigService.NormalizeDriveLetter(_config.SharedFolders.BaseDriveLetter));
                }

                await _winFspMountService.UnmountManyAsync(legacyLetters, cancellationToken);
                return new SharedFolderReconnectCycleResult();
            }

            var mappings = _config?.SharedFolders?.Mappings?
                .Where(mapping => mapping.Enabled
                                  && !string.IsNullOrWhiteSpace(mapping.SharePath)
                                  && !string.IsNullOrWhiteSpace(mapping.DriveLetter))
                .Select(mapping => new GuestSharedFolderMapping
                {
                    Id = mapping.Id,
                    Label = mapping.Label,
                    SharePath = mapping.SharePath,
                    DriveLetter = mapping.DriveLetter,
                    Persistent = mapping.Persistent,
                    Enabled = mapping.Enabled
                })
                .ToList()
                ?? [];

            if (mappings.Count == 0)
            {
                var baseDrive = GuestConfigService.NormalizeDriveLetter(_config?.SharedFolders?.BaseDriveLetter);
                await _winFspMountService.UnmountAsync(baseDrive, cancellationToken);
                return new SharedFolderReconnectCycleResult();
            }

            var baseDriveLetter = GuestConfigService.NormalizeDriveLetter(_config?.SharedFolders?.BaseDriveLetter);
            attempted = mappings.Count;

            try
            {
                await _winFspMountService.EnsureCatalogMountedAsync(baseDriveLetter, mappings, cancellationToken);
                newlyMounted = mappings.Count;
                ResetRateLimitedWarning("sharedfolders.reconnect.failed", baseDriveLetter);
                var now = DateTimeOffset.UtcNow;
                var stateKey = $"{baseDriveLetter}:{mappings.Count}";
                var shouldLogCatalogReady = !string.Equals(_sharedFolderCatalogReadyLastStateKey, stateKey, StringComparison.Ordinal)
                                           || !_sharedFolderCatalogReadyLastLoggedUtc.HasValue
                                           || (now - _sharedFolderCatalogReadyLastLoggedUtc.Value) >= SharedFolderCatalogReadyLogHeartbeatInterval;

                if (shouldLogCatalogReady)
                {
                    GuestLogger.Info("sharedfolders.reconnect.catalog_ready", "HyperTool-File-Katalog bereit.", new
                    {
                        driveLetter = baseDriveLetter,
                        count = mappings.Count,
                        mode = "hypertool-file-catalog"
                    });

                    _sharedFolderCatalogReadyLastStateKey = stateKey;
                    _sharedFolderCatalogReadyLastLoggedUtc = now;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _sharedFolderCatalogReadyLastStateKey = string.Empty;
                _sharedFolderCatalogReadyLastLoggedUtc = null;
                WarnRateLimited(
                    "sharedfolders.reconnect.failed",
                    ex.Message,
                    new
                    {
                        driveLetter = baseDriveLetter,
                        count = mappings.Count,
                        exceptionType = ex.GetType().FullName
                    },
                    baseDriveLetter,
                    RecurringWarnRateLimitInterval);
                failed = mappings.Count;
            }

            return new SharedFolderReconnectCycleResult
            {
                Attempted = attempted,
                NewlyMounted = newlyMounted,
                Failed = failed
            };
        }
        finally
        {
            _sharedFolderReconnectGate.Release();
        }
    }

    private async Task RefreshSharedFolderHostNameFromSocketAsync(CancellationToken cancellationToken)
    {
        var identity = await FetchHostIdentityViaHyperVSocketAsync(cancellationToken);
        if (identity is null)
        {
            return;
        }

        ApplyHostIdentityToConfig(identity, persistConfig: true);
    }

    private void ApplyHostIdentityToConfig(HostIdentityInfo identity, bool persistConfig)
    {
        if (_config is null)
        {
            return;
        }

        _config.Usb ??= new GuestUsbSettings();
        _config.SharedFolders ??= new GuestSharedFolderSettings();

        var normalizedHostName = (identity.HostName ?? string.Empty).Trim();

        var changed = false;
        if (!string.IsNullOrWhiteSpace(normalizedHostName)
            && !string.Equals(_config.Usb.HostName, normalizedHostName, StringComparison.OrdinalIgnoreCase))
        {
            _config.Usb.HostName = normalizedHostName;
            changed = true;
        }

        var useHyperVSocket = _config.Usb.UseHyperVSocket;

        // In IP mode, keep the manually configured host target stable.
        // Host-identity refresh should not rewrite usb.hostAddress from IP to hostname.
        if (useHyperVSocket
            && !string.IsNullOrWhiteSpace(normalizedHostName)
            && !string.Equals(_config.Usb.HostAddress, normalizedHostName, StringComparison.OrdinalIgnoreCase))
        {
            _config.Usb.HostAddress = normalizedHostName;
            changed = true;
        }

        var usbEnabled = identity.Features?.UsbSharingEnabled != false;
        if (_config.Usb.HostFeatureEnabled != usbEnabled)
        {
            _config.Usb.HostFeatureEnabled = usbEnabled;
            changed = true;
        }

        var sharedFoldersEnabled = identity.Features?.SharedFoldersEnabled != false;
        if (_config.SharedFolders.HostFeatureEnabled != sharedFoldersEnabled)
        {
            _config.SharedFolders.HostFeatureEnabled = sharedFoldersEnabled;
            changed = true;
        }

        _hostUsbMetadataByKey.Clear();
        _hostUsbDescriptionsByKey.Clear();
        _hostUsbAttachmentsByBusId.Clear();
        foreach (var entry in identity.Features?.UsbDeviceMetadata ?? [])
        {
            if (entry is null)
            {
                continue;
            }

            var key = (entry.DeviceKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var customName = (entry.CustomName ?? string.Empty).Trim();
            var comment = (entry.Comment ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(customName) && string.IsNullOrWhiteSpace(comment))
            {
                continue;
            }

            _hostUsbMetadataByKey[key] = new UsbDeviceMetadataEntry
            {
                DeviceKey = key,
                CustomName = customName,
                Comment = comment
            };
        }

        foreach (var entry in identity.Features?.UsbDeviceDescriptions ?? [])
        {
            if (entry is null)
            {
                continue;
            }

            var key = (entry.DeviceKey ?? string.Empty).Trim();
            var description = (entry.Description ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            _hostUsbDescriptionsByKey[key] = description;
        }

        foreach (var entry in identity.Features?.UsbDeviceAttachments ?? [])
        {
            if (entry is null)
            {
                continue;
            }

            var busId = (entry.BusId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(busId))
            {
                continue;
            }

            _hostUsbAttachmentsByBusId[busId] = new UsbDeviceAttachmentEntry
            {
                BusId = busId,
                GuestComputerName = (entry.GuestComputerName ?? string.Empty).Trim(),
                SourceVmId = (entry.SourceVmId ?? string.Empty).Trim(),
                GuestVmName = (entry.GuestVmName ?? string.Empty).Trim(),
                ClientIpAddress = (entry.ClientIpAddress ?? string.Empty).Trim()
            };
        }

        var usbVisualChanged = false;
        foreach (var usb in _usbDevices)
        {
            var beforeDescription = usb.Description;
            var beforeCustomName = usb.CustomName;
            var beforeCustomComment = usb.CustomComment;
            var beforeIdentityKey = usb.DeviceIdentityKey;
            var beforeAttachedGuest = usb.AttachedGuestComputerName;
            var beforeClientIp = usb.ClientIpAddress;

            ApplyHostUsbMetadata(usb);

            if (!string.Equals(beforeDescription, usb.Description, StringComparison.Ordinal)
                || !string.Equals(beforeCustomName, usb.CustomName, StringComparison.Ordinal)
                || !string.Equals(beforeCustomComment, usb.CustomComment, StringComparison.Ordinal)
                || !string.Equals(beforeIdentityKey, usb.DeviceIdentityKey, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(beforeAttachedGuest, usb.AttachedGuestComputerName, StringComparison.Ordinal)
                || !string.Equals(beforeClientIp, usb.ClientIpAddress, StringComparison.OrdinalIgnoreCase))
            {
                usbVisualChanged = true;
            }
        }

        if (usbVisualChanged)
        {
            UpdateUsbViews();
        }

        if (!changed)
        {
            return;
        }

        if (persistConfig)
        {
            GuestConfigService.Save(_configPath, _config);
        }

        _mainWindow?.UpdateHostFeatureAvailability(
            usbFeatureEnabledByHost: _config.Usb.HostFeatureEnabled,
            sharedFoldersFeatureEnabledByHost: _config.SharedFolders.HostFeatureEnabled,
            hostName: _config.Usb.HostName);

        GuestLogger.Info("guest.hostidentity.updated", "Host-Feature-Status per Hyper-V Socket aktualisiert.", new
        {
            hostName = _config.Usb.HostName,
            usbSharingEnabled = _config.Usb.HostFeatureEnabled,
            sharedFoldersEnabled = _config.SharedFolders.HostFeatureEnabled,
            usbMetadataCount = _hostUsbMetadataByKey.Count,
            usbDescriptionCount = _hostUsbDescriptionsByKey.Count,
            usbAttachmentCount = _hostUsbAttachmentsByBusId.Count
        });
    }

    private bool HasConfiguredUsbAutoConnect()
    {
        var keys = _config?.Usb?.AutoConnectDeviceKeys;
        return keys is not null && keys.Any(static key => !string.IsNullOrWhiteSpace(key));
    }

    private string? ResolveSharedFolderHostTarget()
    {
        var hostName = (_config?.Usb?.HostName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            return hostName;
        }

        return (_config?.Usb?.HostAddress ?? string.Empty).Trim();
    }

    private async Task<int> ApplyUsbAutoConnectAsync(IReadOnlyList<UsbIpDeviceInfo> devices)
    {
        var configuredKeys = _config?.Usb?.AutoConnectDeviceKeys;
        if (configuredKeys is null || configuredKeys.Count == 0)
        {
            return 0;
        }

        var keySet = new HashSet<string>(
            configuredKeys
                .Where(static key => !string.IsNullOrWhiteSpace(key))
                .Select(static key => key.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (keySet.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var backoffSkipped = 0;

        var expiredBackoffKeys = _usbAutoConnectBackoffUntilUtc
            .Where(entry => entry.Value <= now)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var key in expiredBackoffKeys)
        {
            _usbAutoConnectBackoffUntilUtc.Remove(key);
        }

        var candidates = devices
            .Where(device =>
                !string.IsNullOrWhiteSpace(device.BusId)
                && device.IsRemoteAvailable
                && !device.IsAttached
                && !device.IsAttachedByOtherGuest
                && IsConfiguredAutoConnectForDevice(keySet, devices, device)
                && IsUsbAutoConnectAllowedNow(device.BusId!.Trim(), now, ref backoffSkipped))
            .Select(device => device.BusId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (backoffSkipped > 0)
        {
            WarnRateLimited(
                eventName: "usb.autoconnect.backoff.active",
                message: "USB Auto-Connect wartet wegen vorherigen Attach-Fehlern (Backoff aktiv).",
                data: new
                {
                    skipped = backoffSkipped,
                    backoffSeconds = GuestUsbAutoConnectFailureBackoffSeconds
                },
                scopeKey: "global",
                minInterval: RecurringWarnRateLimitInterval);
        }

        var connectedCount = 0;
        foreach (var busId in candidates)
        {
            var result = await ConnectUsbAsync(busId, skipCachedAttachedCheck: true);
            if (result == 0)
            {
                _usbAutoConnectBackoffUntilUtc.Remove(busId);
                connectedCount++;
            }
            else
            {
                _usbAutoConnectBackoffUntilUtc[busId] = DateTimeOffset.UtcNow.AddSeconds(GuestUsbAutoConnectFailureBackoffSeconds);
            }
        }

        return connectedCount;
    }

    private bool ShouldLogUsbRefreshSuccess(
        IReadOnlyList<UsbIpDeviceInfo> devices,
        string? selectedBusId,
        int autoConnectApplied,
        bool retryTriggered,
        out string stateKey)
    {
        var stateParts = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.BusId))
            .Select(device =>
            {
                var state = device.IsAttached ? "A" : (device.IsShared ? "S" : "F");
                return $"{device.BusId.Trim()}:{state}";
            })
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        stateKey = string.Join("|", stateParts) + "|sel:" + (selectedBusId ?? string.Empty).Trim();

        if (!string.Equals(_usbRefreshSuccessLastStateKey, stateKey, StringComparison.Ordinal)
            || autoConnectApplied > 0
            || retryTriggered)
        {
            return true;
        }

        if (!_usbRefreshSuccessLastLoggedUtc.HasValue)
        {
            return true;
        }

        return (DateTimeOffset.UtcNow - _usbRefreshSuccessLastLoggedUtc.Value) >= UsbRefreshSuccessLogHeartbeatInterval;
    }

    private bool IsUsbAutoConnectAllowedNow(string busId, DateTimeOffset now, ref int backoffSkipped)
    {
        if (!_usbAutoConnectBackoffUntilUtc.TryGetValue(busId, out var blockedUntilUtc))
        {
            return true;
        }

        if (blockedUntilUtc <= now)
        {
            _usbAutoConnectBackoffUntilUtc.Remove(busId);
            return true;
        }

        backoffSkipped++;
        return false;
    }

    private async Task<IReadOnlyList<UsbIpDeviceInfo>> GetRemoteUsbDevicesWithTimeoutAsync(string hostAddress, CancellationToken cancellationToken)
    {
        var operationTask = _usbService.GetRemoteDevicesAsync(hostAddress, CancellationToken.None);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(UsbServiceOperationTimeoutSeconds), cancellationToken);
        var completedTask = await Task.WhenAny(operationTask, timeoutTask);

        if (!ReferenceEquals(completedTask, operationTask))
        {
            throw new TimeoutException($"USB Remote-Refresh Timeout nach {UsbServiceOperationTimeoutSeconds}s (Host={hostAddress}).");
        }

        return await operationTask;
    }

    private async Task<IReadOnlyList<UsbIpDeviceInfo>> GetLocalUsbDevicesWithTimeoutAsync(CancellationToken cancellationToken)
    {
        var operationTask = _usbService.GetDevicesAsync(CancellationToken.None);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(UsbServiceOperationTimeoutSeconds), cancellationToken);
        var completedTask = await Task.WhenAny(operationTask, timeoutTask);

        if (!ReferenceEquals(completedTask, operationTask))
        {
            throw new TimeoutException($"USB Local-Refresh Timeout nach {UsbServiceOperationTimeoutSeconds}s.");
        }

        return await operationTask;
    }

    private static string BuildUsbDeviceIdentityKey(UsbIpDeviceInfo device)
    {
        if (!string.IsNullOrWhiteSpace(device.DeviceIdentityKey))
        {
            return device.DeviceIdentityKey.Trim();
        }

        var preciseHardwareId = NormalizeUsbHardwareId(device.HardwareIdentityKey);
        if (!string.IsNullOrWhiteSpace(preciseHardwareId)
            && preciseHardwareId.Contains("&REV_", StringComparison.OrdinalIgnoreCase))
        {
            return "hardware:" + preciseHardwareId;
        }

        if (!string.IsNullOrWhiteSpace(device.InstanceId))
        {
            return "instance:" + device.InstanceId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(device.PersistedGuid))
        {
            return "guid:" + device.PersistedGuid.Trim();
        }

        var hardwareId = NormalizeUsbHardwareId(device.HardwareId);
        if (!string.IsNullOrWhiteSpace(hardwareId))
        {
            return "hardware:" + hardwareId;
        }

        if (!string.IsNullOrWhiteSpace(device.BusId))
        {
            return "busid:" + device.BusId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(device.Description))
        {
            return "description:" + device.Description.Trim();
        }

        return string.Empty;
    }

    private static IEnumerable<string> BuildUsbIdentityAliasKeys(UsbIpDeviceInfo device)
    {
        if (!string.IsNullOrWhiteSpace(device.DeviceIdentityKey))
        {
            yield return device.DeviceIdentityKey.Trim();
        }

        var preciseHardwareId = NormalizeUsbHardwareId(device.HardwareIdentityKey);
        if (!string.IsNullOrWhiteSpace(preciseHardwareId))
        {
            yield return "hardware:" + preciseHardwareId;
        }

        if (!string.IsNullOrWhiteSpace(device.InstanceId))
        {
            yield return "instance:" + device.InstanceId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(device.PersistedGuid))
        {
            yield return "guid:" + device.PersistedGuid.Trim();
        }

        var hardwareId = NormalizeUsbHardwareId(device.HardwareId);
        if (!string.IsNullOrWhiteSpace(hardwareId))
        {
            yield return "hardware:" + hardwareId;
        }

        if (!string.IsNullOrWhiteSpace(device.BusId))
        {
            yield return "busid:" + device.BusId.Trim();
        }

    }

    private static string BuildUsbAutoConnectKey(UsbIpDeviceInfo device)
    {
        var specific = BuildUsbAutoConnectSpecificKey(device);
        if (!string.IsNullOrWhiteSpace(specific))
        {
            return specific;
        }

        return BuildUsbDeviceIdentityKey(device);
    }

    private static string BuildUsbAutoConnectBaseKey(UsbIpDeviceInfo device)
    {
        return BuildUsbDeviceIdentityKey(device);
    }

    private static string BuildUsbAutoConnectSpecificKey(UsbIpDeviceInfo device)
    {
        var baseKey = BuildUsbAutoConnectBaseKey(device);
        if (string.IsNullOrWhiteSpace(baseKey))
        {
            return string.Empty;
        }

        var label = !string.IsNullOrWhiteSpace(device.CustomName)
            ? device.CustomName
            : device.CustomComment;

        return string.IsNullOrWhiteSpace(label)
            ? string.Empty
            : baseKey + "|meta:" + label.Trim().ToUpperInvariant();
    }

    private static bool IsConfiguredAutoConnectForDevice(HashSet<string> configuredKeys, IReadOnlyList<UsbIpDeviceInfo> devices, UsbIpDeviceInfo device)
    {
        var baseKey = BuildUsbAutoConnectBaseKey(device);
        if (string.IsNullOrWhiteSpace(baseKey))
        {
            return false;
        }

        var specificKey = BuildUsbAutoConnectSpecificKey(device);
        if (!string.IsNullOrWhiteSpace(specificKey) && configuredKeys.Contains(specificKey))
        {
            return true;
        }

        // If the base key is ambiguous (same identity for multiple devices),
        // only a specific metadata key may trigger auto-connect.
        var baseGroupCount = devices.Count(candidate =>
            !string.IsNullOrWhiteSpace(candidate.BusId)
            && string.Equals(BuildUsbAutoConnectBaseKey(candidate), baseKey, StringComparison.OrdinalIgnoreCase));

        if (baseGroupCount > 1)
        {
            return false;
        }

        if (configuredKeys.Contains(baseKey))
        {
            return true;
        }

        var legacyMetaPrefix = baseKey + "|meta:";
        return configuredKeys.Any(configuredKey =>
            configuredKey.StartsWith(legacyMetaPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeUsbHardwareId(string? hardwareId)
    {
        if (string.IsNullOrWhiteSpace(hardwareId))
        {
            return string.Empty;
        }

        var normalized = hardwareId.Trim().ToUpperInvariant();
        if (normalized.StartsWith("USB\\", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(4);
        }

        return normalized;
    }

    private void ApplyHostUsbMetadata(UsbIpDeviceInfo device)
    {
        if (device is null)
        {
            return;
        }

        foreach (var lookupKey in BuildUsbIdentityAliasKeys(device))
        {
            if (_hostUsbDescriptionsByKey.TryGetValue(lookupKey, out var hostDescription)
                && !string.IsNullOrWhiteSpace(hostDescription))
            {
                device.Description = hostDescription;
                break;
            }
        }

        var key = BuildUsbDeviceIdentityKey(device);
        device.DeviceIdentityKey = key;

        UsbDeviceMetadataEntry? metadata = null;
        foreach (var lookupKey in BuildUsbIdentityAliasKeys(device))
        {
            if (_hostUsbMetadataByKey.TryGetValue(lookupKey, out metadata))
            {
                break;
            }
        }

        if (metadata is null)
        {
            device.CustomName = string.Empty;
            device.CustomComment = string.Empty;
            ApplyHostUsbAttachmentHint(device);
            return;
        }

        device.CustomName = (metadata.CustomName ?? string.Empty).Trim();
        device.CustomComment = (metadata.Comment ?? string.Empty).Trim();

        ApplyHostUsbAttachmentHint(device);
    }

    private void ApplyHostUsbAttachmentHint(UsbIpDeviceInfo device)
    {
        device.IsAttachedByOtherGuest = false;

        var busId = (device.BusId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(busId))
        {
            return;
        }

        var hasLocalPortAttachment = !string.IsNullOrWhiteSpace((device.ClientIpAddress ?? string.Empty).Trim());

        if (!_hostUsbAttachmentsByBusId.TryGetValue(busId, out var attachment))
        {
            if (!hasLocalPortAttachment)
            {
                device.AttachedGuestComputerName = string.Empty;
                device.ClientIpAddress = string.Empty;
            }

            return;
        }

        var attachedGuest = !string.IsNullOrWhiteSpace(attachment.GuestVmName)
            ? attachment.GuestVmName.Trim()
            : (attachment.GuestComputerName ?? string.Empty).Trim();

        var currentGuestName = (Environment.MachineName ?? string.Empty).Trim();
        var attachmentGuestComputerName = (attachment.GuestComputerName ?? string.Empty).Trim();
        var attachmentGuestVmName = (attachment.GuestVmName ?? string.Empty).Trim();
        var hintTargetsCurrentGuest = !string.IsNullOrWhiteSpace(currentGuestName)
            && (
                string.Equals(attachmentGuestComputerName, currentGuestName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(attachmentGuestVmName, currentGuestName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(attachedGuest, currentGuestName, StringComparison.OrdinalIgnoreCase)
            );

        var hasRemoteAttachmentHint = !string.IsNullOrWhiteSpace(attachedGuest)
            || !string.IsNullOrWhiteSpace((attachment.SourceVmId ?? string.Empty).Trim())
            || !string.IsNullOrWhiteSpace((attachment.ClientIpAddress ?? string.Empty).Trim());

        if (hasLocalPortAttachment)
        {
            // Preserve local attachment state from usbip client output.
            return;
        }

        if (IsUsbDetachHintSuppressed(busId))
        {
            // During local detach propagation, ignore stale host hints that would show Busy.
            device.IsAttachedByOtherGuest = false;
            device.AttachedGuestComputerName = string.Empty;
            device.ClientIpAddress = string.Empty;
            return;
        }

        if (hintTargetsCurrentGuest)
        {
            // Stale self-hints must not be treated as foreign attachment.
            device.IsAttachedByOtherGuest = false;
            device.AttachedGuestComputerName = string.Empty;
            device.ClientIpAddress = string.Empty;
            return;
        }

        // Host attachment hint is authoritative for busy/available decision.
        device.IsAttachedByOtherGuest = hasRemoteAttachmentHint || _hostUsbAttachmentsByBusId.ContainsKey(busId);
        device.AttachedGuestComputerName = device.IsAttachedByOtherGuest
            ? (!string.IsNullOrWhiteSpace(attachedGuest) ? attachedGuest : "(anderer Guest)")
            : string.Empty;
        // Keep ClientIpAddress empty for remote ownership hints so local attach logic stays deterministic.
        device.ClientIpAddress = string.Empty;
    }

    private void StartUsbDiagnosticsLoop()
    {
        StopUsbDiagnosticsLoop();
        var cts = new CancellationTokenSource();
        _usbDiagnosticsCts = cts;
        _usbDiagnosticsTask = Task.Run(() => RunUsbDiagnosticsLoopAsync(cts.Token));
    }

    private void StopUsbDiagnosticsLoop()
    {
        try
        {
            _usbDiagnosticsCts?.Cancel();
        }
        catch
        {
        }

        _usbDiagnosticsCts?.Dispose();
        _usbDiagnosticsCts = null;
        _usbDiagnosticsTask = null;
    }

    private async Task RunUsbDiagnosticsLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var probeSucceeded = await ProbeHyperVSocketServiceAsync(cancellationToken);
                if (probeSucceeded)
                {
                    _usbHyperVSocketProbeFailureCount = 0;
                    _usbHyperVSocketServiceReachable = true;
                }
                else
                {
                    if (HasLocalAttachedUsbDevices())
                    {
                        // Keep the transport bridge stable while an attachment is active;
                        // failed probes are expected under some idle/power transitions.
                        _usbHyperVSocketServiceReachable = true;
                        _usbHyperVSocketProbeFailureCount = Math.Min(
                            _usbHyperVSocketProbeFailureCount,
                            UsbHyperVProbeFailureThresholdBeforeRestart - 1);

                        UpdateUsbDiagnosticsPanel();
                        continue;
                    }

                    if (HasRecentHyperVDataPathSuccess())
                    {
                        _usbHyperVSocketProbeFailureCount = 0;
                        _usbHyperVSocketServiceReachable = true;
                    }
                    else
                    {
                    _usbHyperVSocketProbeFailureCount++;
                    if (_usbHyperVSocketProbeFailureCount >= UsbHyperVProbeFailureThresholdBeforeRestart)
                    {
                        _usbHyperVSocketServiceReachable = false;
                        TrySelfHealUsbHyperVSocketProxy();
                    }
                    }
                }

                UpdateUsbDiagnosticsPanel();
            }
            catch
            {
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private bool HasLocalAttachedUsbDevices()
    {
        return _usbDevices.Any(device => IsGuestLocalAttachedDevice(device) && !string.IsNullOrWhiteSpace(device.BusId));
    }

    private async Task<bool> ProbeHyperVSocketServiceAsync(CancellationToken cancellationToken)
    {
        if (_usbHyperVSocketProxy?.IsRunning != true)
        {
            return false;
        }

        var serviceIdText = (_config?.Usb?.HyperVSocketServiceId ?? HyperVSocketUsbTunnelDefaults.ServiceIdString).Trim();
        if (!Guid.TryParse(serviceIdText, out var serviceId))
        {
            return false;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromMilliseconds(1500));

            try
            {
                using var socket = new System.Net.Sockets.Socket((System.Net.Sockets.AddressFamily)34, System.Net.Sockets.SocketType.Stream, (System.Net.Sockets.ProtocolType)1);
                linkedCts.Token.ThrowIfCancellationRequested();
                var endpoint = new HyperVSocketEndPoint(HyperVSocketUsbTunnelDefaults.VmIdParent, serviceId);
                var connectTask = Task.Run(() => socket.Connect(endpoint), CancellationToken.None);
                await connectTask.WaitAsync(linkedCts.Token);
                linkedCts.Token.ThrowIfCancellationRequested();
                return true;
            }
            catch
            {
                if (attempt == 0)
                {
                    await Task.Delay(120, cancellationToken);
                }
            }
        }

        return false;
    }

    private void TrySelfHealUsbHyperVSocketProxy()
    {
        if (_config?.Usb?.UseHyperVSocket != true)
        {
            return;
        }

        if (_usbHyperVSocketProxy?.IsRunning != true)
        {
            UpdateUsbTransportBridge();
            return;
        }

        var attachedBusIds = _usbDevices
            .Where(device => IsGuestLocalAttachedDevice(device) && !string.IsNullOrWhiteSpace(device.BusId))
            .Select(device => device.BusId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var now = DateTimeOffset.UtcNow;
        var restartBackoffActive = (now - _usbHyperVSocketProxyLastRestartAttemptUtc) < UsbHyperVProxyRestartBackoffInterval;

        if (attachedBusIds.Count > 0)
        {
            WarnRateLimited(
                eventName: "usb.transport.hyperv.self_heal.skipped_attached",
                message: "Hyper-V Socket Selbstheilung ausgesetzt, solange USB im Guest verbunden ist.",
                data: new
                {
                    failureCount = _usbHyperVSocketProbeFailureCount,
                    threshold = UsbHyperVProbeFailureThresholdBeforeRestart,
                    attachedBusIds,
                    restartBackoffSeconds = (int)UsbHyperVProxyRestartBackoffInterval.TotalSeconds
                },
                scopeKey: "attached",
                minInterval: RecurringWarnRateLimitInterval);
            return;
        }

        if (restartBackoffActive)
        {
            return;
        }

        _usbHyperVSocketProxyLastRestartAttemptUtc = now;

        try
        {
            _usbHyperVSocketProxy.Dispose();

            var serviceIdText = string.IsNullOrWhiteSpace(_config.Usb.HyperVSocketServiceId)
                ? HyperVSocketUsbTunnelDefaults.ServiceIdString
                : _config.Usb.HyperVSocketServiceId;
            if (!Guid.TryParse(serviceIdText, out var serviceId))
            {
                return;
            }

            _usbHyperVSocketProxy = new HyperVSocketUsbGuestProxy(serviceId);
            _usbHyperVSocketProxy.Start();
            _usbHyperVSocketProbeFailureCount = 0;

            GuestLogger.Info("usb.transport.hyperv.self_heal.restart", "Hyper-V Socket Proxy wurde zur Selbstheilung neu gestartet.", new
            {
                serviceId = serviceId.ToString("D"),
                failureCount = _usbHyperVSocketProbeFailureCount,
                threshold = UsbHyperVProbeFailureThresholdBeforeRestart,
                restartBackoffSeconds = (int)UsbHyperVProxyRestartBackoffInterval.TotalSeconds
            });
        }
        catch (Exception ex)
        {
            _usbHyperVSocketProxy = null;
            GuestLogger.Warn("usb.transport.hyperv.self_heal.failed", ex.Message, new
            {
                failureCount = _usbHyperVSocketProbeFailureCount,
                exceptionType = ex.GetType().FullName
            });
        }
    }

    private async Task<(bool hyperVSocketActive, bool registryServiceOk)> RunTransportDiagnosticsTestAsync()
    {
        lock (_hyperVSocketMetricsSync)
        {
            _metricTransportSelfTestRuns++;
            _lastTransportSelfTestUtc = DateTimeOffset.UtcNow;
        }

        UpdateUsbTransportBridge();

        var hyperVSocketActive = _usbHyperVSocketProxy?.IsRunning == true;
        var registryServiceOk = false;

        if (hyperVSocketActive)
        {
            try
            {
                registryServiceOk = await ProbeHyperVSocketServiceAsync(CancellationToken.None);
            }
            catch
            {
                registryServiceOk = false;
            }
        }

        if (registryServiceOk)
        {
            _usbHyperVSocketProbeFailureCount = 0;
            _usbHyperVSocketServiceReachable = true;
            lock (_hyperVSocketMetricsSync)
            {
                _lastTransportSelfTestFailure = string.Empty;
            }
        }
        else
        {
            _usbHyperVSocketProbeFailureCount = Math.Max(_usbHyperVSocketProbeFailureCount + 1, 3);
            lock (_hyperVSocketMetricsSync)
            {
                _metricTransportSelfTestFailures++;
                _lastTransportSelfTestFailure = "registryServiceOk=false";
            }
        }

        ApplyUsbTransportResolution(ResolveUsbHostAddressDiagnostics());
        UpdateUsbDiagnosticsPanel();

        GuestLogger.Info("usb.transport.hyperv.test", "Hyper-V Socket Test ausgeführt.", new
        {
            hyperVSocketActive,
            registryServiceOk
        });

        if (hyperVSocketActive && registryServiceOk)
        {
            GuestLogger.Info("usb.transport.hyperv.test.stable", "Hyper-V Socket ist stabil erreichbar.", new
            {
                hyperVSocketActive,
                registryServiceOk,
                serviceReachable = _usbHyperVSocketServiceReachable,
                probeFailureCount = _usbHyperVSocketProbeFailureCount
            });
        }

        try
        {
            await SendHyperVDiagnosticsAckAsync(Environment.MachineName, hyperVSocketActive, registryServiceOk, CancellationToken.None);
            lock (_hyperVSocketMetricsSync)
            {
                _metricDiagnosticsAckSent++;
                _lastDiagnosticsAckSuccessUtc = DateTimeOffset.UtcNow;
                _lastDiagnosticsAckFailure = string.Empty;
            }
            GuestLogger.Info("usb.transport.hyperv.test.host_ack_sent", "Hyper-V Socket Test an Host übermittelt.", new
            {
                guestComputerName = Environment.MachineName,
                hyperVSocketActive,
                registryServiceOk
            });

            if (hyperVSocketActive && registryServiceOk)
            {
                GuestLogger.Info("usb.transport.hyperv.test.stable.host_ack", "Hyper-V Socket Stabilitäts-Status an Host bestätigt.", new
                {
                    guestComputerName = Environment.MachineName,
                    hyperVSocketActive,
                    registryServiceOk
                });
            }
        }
        catch (Exception ex)
        {
            MarkHyperVServiceUnreachableImmediate();
            UpdateUsbDiagnosticsPanel();
            lock (_hyperVSocketMetricsSync)
            {
                _metricDiagnosticsAckFailed++;
                _lastDiagnosticsAckFailure = $"{ex.GetType().Name}:{ex.Message}";
            }
            GuestLogger.Warn("usb.transport.hyperv.test.host_ack_failed", ex.Message, new
            {
                guestComputerName = Environment.MachineName,
                hyperVSocketActive,
                registryServiceOk,
                exceptionType = ex.GetType().FullName
            });
        }

        return (hyperVSocketActive, registryServiceOk);
    }

    private async Task SendHyperVDiagnosticsAckAsync(string guestComputerName, bool hyperVSocketActive, bool registryServiceOk, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(guestComputerName))
        {
            return;
        }

        var guestNetworks = ResolveGuestIpv4Settings();
        var guestNetwork = guestNetworks.Count > 0
            ? guestNetworks[0]
            : (string.Empty, string.Empty, string.Empty, string.Empty);
        var (guestCpuPercent, guestRamUsedGb, guestRamTotalGb) = _diagnosticsResourceSampler.Sample();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromMilliseconds(3200));

        var diagnosticsAck = new HyperVSocketDiagnosticsAck
        {
            GuestComputerName = guestComputerName,
            HyperVSocketActive = hyperVSocketActive,
            RegistryServiceOk = registryServiceOk,
            GuestIpv4Address = guestNetwork.Item1,
            GuestIpv4SubnetMask = guestNetwork.Item2,
            GuestIpv4Gateway = guestNetwork.Item3,
            GuestNetworkAdapterName = guestNetwork.Item4,
            GuestCpuPercent = Math.Round(guestCpuPercent, 1),
            GuestRamUsedGb = Math.Round(guestRamUsedGb, 2),
            GuestRamTotalGb = Math.Round(guestRamTotalGb, 2),
            GuestIpv4Entries = guestNetworks.Select(entry => new HyperVSocketGuestIpv4Entry
            {
                Ipv4Address = entry.ipv4Address,
                SubnetMask = entry.subnetMask,
                Gateway = entry.gateway,
                AdapterName = entry.adapterName
            }).ToList(),
            SentAtUtc = DateTime.UtcNow.ToString("O")
        };

        await _hyperVEventChannel.SendDiagnosticsAckAsync(diagnosticsAck, linkedCts.Token);
    }

    private async Task TrySendUsbConnectionEventAckAsync(string busId, string eventType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(busId) || string.IsNullOrWhiteSpace(eventType))
        {
            return;
        }

        Exception? lastError = null;
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linkedCts.CancelAfter(TimeSpan.FromMilliseconds(3600));

                var guestNetworks = ResolveGuestIpv4Settings();
                var guestNetwork = guestNetworks.Count > 0
                    ? guestNetworks[0]
                    : (string.Empty, string.Empty, string.Empty, string.Empty);
                var (guestCpuPercent, guestRamUsedGb, guestRamTotalGb) = _diagnosticsResourceSampler.Sample();
                var currentDevice = FindUsbByBusId(busId);

                var diagnosticsAck = new HyperVSocketDiagnosticsAck
                {
                    GuestComputerName = Environment.MachineName,
                    BusId = busId.Trim(),
                    HardwareId = !string.IsNullOrWhiteSpace(currentDevice?.HardwareIdentityKey)
                        ? currentDevice!.HardwareIdentityKey
                        : currentDevice?.HardwareId,
                    InstanceId = currentDevice?.InstanceId,
                    PersistedGuid = currentDevice?.PersistedGuid,
                    EventType = eventType.Trim(),
                    SentAtUtc = DateTime.UtcNow.ToString("O"),
                    GuestIpv4Address = guestNetwork.Item1,
                    GuestIpv4SubnetMask = guestNetwork.Item2,
                    GuestIpv4Gateway = guestNetwork.Item3,
                    GuestNetworkAdapterName = guestNetwork.Item4,
                    GuestCpuPercent = Math.Round(guestCpuPercent, 1),
                    GuestRamUsedGb = Math.Round(guestRamUsedGb, 2),
                    GuestRamTotalGb = Math.Round(guestRamTotalGb, 2),
                    GuestIpv4Entries = guestNetworks.Select(entry => new HyperVSocketGuestIpv4Entry
                    {
                        Ipv4Address = entry.ipv4Address,
                        SubnetMask = entry.subnetMask,
                        Gateway = entry.gateway,
                        AdapterName = entry.adapterName
                    }).ToList()
                };

                await _hyperVEventChannel.SendDiagnosticsAckAsync(diagnosticsAck, linkedCts.Token);
                lock (_hyperVSocketMetricsSync)
                {
                    _metricUsbEventAckSent++;
                    _lastUsbEventAckSuccessUtc = DateTimeOffset.UtcNow;
                    _lastUsbEventAckFailure = string.Empty;
                }
                ResetRateLimitedWarning("usb.ack.send.failed", $"{eventType.Trim().ToLowerInvariant()}:{busId.Trim().ToLowerInvariant()}");
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt < maxAttempts)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(220), cancellationToken);
                    }
                    catch
                    {
                    }
                }
            }
        }

        if (lastError is not null)
        {
            lock (_hyperVSocketMetricsSync)
            {
                _metricUsbEventAckFailed++;
                _lastUsbEventAckFailure = $"{lastError.GetType().Name}:{lastError.Message}";
            }

            if (string.Equals(eventType.Trim(), "usb-heartbeat", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var scopeKey = $"{eventType.Trim().ToLowerInvariant()}:{busId.Trim().ToLowerInvariant()}";
            WarnRateLimited(
                "usb.ack.send.failed",
                lastError.Message,
                new
                {
                    busId = busId.Trim(),
                    eventType = eventType.Trim(),
                    attempts = maxAttempts,
                    exceptionType = lastError.GetType().FullName
                },
                scopeKey,
                TimeSpan.FromMinutes(10));
        }
    }

    private async Task TrySendUsbStaleExportHintAsync(string busId, UsbHostResolution hostResolution, string operationId, long elapsedMs)
    {
        var normalizedBusId = (busId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedBusId))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (_usbStaleExportHintLastSentUtc.TryGetValue(normalizedBusId, out var lastSentUtc)
            && (now - lastSentUtc) < UsbStaleExportHintMinInterval)
        {
            return;
        }

        _usbStaleExportHintLastSentUtc[normalizedBusId] = now;

        try
        {
            await TrySendUsbConnectionEventAckAsync(normalizedBusId, "usb-export-stale-suspect", CancellationToken.None);
            GuestLogger.Debug("usb.connect.stale_export_hint_sent", "Stale-Export-Hinweis an Host gesendet.", new
            {
                operationId,
                busId = normalizedBusId,
                elapsedMs,
                hostAddress = hostResolution.ResolvedIpv4,
                hostSource = hostResolution.Source,
                transportPath = string.Equals(hostResolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase)
                    ? "hyperv"
                    : "hyperv"
            });
        }
        catch
        {
        }
    }

    private static List<(string ipv4Address, string subnetMask, string gateway, string adapterName)> ResolveGuestIpv4Settings()
    {
        try
        {
            var candidates = new List<(string ipv4, string mask, string gateway, string adapter, int score)>();

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var properties = nic.GetIPProperties();
                var unicast = properties.UnicastAddresses
                    .FirstOrDefault(address => address.Address.AddressFamily == AddressFamily.InterNetwork);
                if (unicast is null)
                {
                    continue;
                }

                var gateway = properties.GatewayAddresses
                    .Select(item => item.Address)
                    .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork
                                               && !IPAddress.Any.Equals(address)
                                               && !IPAddress.None.Equals(address));

                var mask = unicast.IPv4Mask?.ToString() ?? PrefixLengthToSubnetMask(unicast.PrefixLength);
                var score = 0;
                if (gateway is not null)
                {
                    score += 10;
                }

                if (!string.IsNullOrWhiteSpace(mask))
                {
                    score += 3;
                }

                if (nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
                {
                    score += 2;
                }

                candidates.Add((
                    unicast.Address.ToString(),
                    mask,
                    gateway?.ToString() ?? string.Empty,
                    nic.Name ?? string.Empty,
                    score));
            }

            var best = candidates
                .OrderByDescending(item => item.score)
                .ThenBy(item => item.adapter, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            var ordered = candidates
                .OrderByDescending(item => item.score)
                .ThenBy(item => item.adapter, StringComparer.OrdinalIgnoreCase)
                .Select(item => (item.ipv4, item.mask, item.gateway, item.adapter))
                .ToList();

            if (ordered.Count > 0)
            {
                return ordered;
            }
        }
        catch
        {
        }

        return [];
    }

    private static string PrefixLengthToSubnetMask(int prefixLength)
    {
        if (prefixLength <= 0 || prefixLength > 32)
        {
            return string.Empty;
        }

        var mask = uint.MaxValue << (32 - prefixLength);
        return string.Join('.',
            (mask >> 24) & 0xFF,
            (mask >> 16) & 0xFF,
            (mask >> 8) & 0xFF,
            mask & 0xFF);
    }

    private void StartResourceMonitorAgent()
    {
        var monitoring = _config?.Monitoring;
        if (monitoring?.Enabled == false)
        {
            StopResourceMonitorAgent();
            return;
        }

        var intervalMs = monitoring?.MonitorIntervalMs ?? 1000;
        if (_resourceMonitorAgent.IsRunning)
        {
            _resourceMonitorAgent.UpdateInterval(intervalMs);
            return;
        }

        _resourceMonitorAgent.Start(intervalMs);
    }

    private void StopResourceMonitorAgent()
    {
        _resourceMonitorAgent.Stop();
    }

    private async Task TrySendAttachedUsbHeartbeatsAsync(CancellationToken cancellationToken)
    {
        if (_usbDevices.Count == 0)
        {
            _usbHeartbeatLastSentByBusId.Clear();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var attachedBusIds = _usbDevices
            .Where(device => IsGuestLocalAttachedDevice(device) && !string.IsNullOrWhiteSpace(device.BusId))
            .Select(device => device.BusId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (attachedBusIds.Count == 0)
        {
            _usbHeartbeatLastSentByBusId.Clear();
            return;
        }

        var obsoleteBusIds = _usbHeartbeatLastSentByBusId.Keys
            .Where(busId => !attachedBusIds.Contains(busId, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var obsoleteBusId in obsoleteBusIds)
        {
            _usbHeartbeatLastSentByBusId.Remove(obsoleteBusId);
        }

        foreach (var busId in attachedBusIds)
        {
            if (_usbHeartbeatLastSentByBusId.TryGetValue(busId, out var lastSentUtc)
                && (now - lastSentUtc) < TimeSpan.FromSeconds(GuestUsbHeartbeatIntervalSeconds))
            {
                continue;
            }

            await TrySendUsbConnectionEventAckAsync(busId, "usb-heartbeat", cancellationToken);
            _usbHeartbeatLastSentByBusId[busId] = DateTimeOffset.UtcNow;
        }
    }

    private void ApplyUsbTransportResolution(UsbHostResolution resolution, bool fallbackToIp = false)
    {
        var usesHyperVSocket = string.Equals(resolution.Source, "hyperv-socket", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(resolution.ResolvedIpv4);
        var effectiveHyperVActive = usesHyperVSocket && _usbHyperVSocketServiceReachable && !fallbackToIp;

        var changed = _usbHyperVSocketTransportActive != effectiveHyperVActive
            || _usbIpFallbackActive != fallbackToIp;
        _usbHyperVSocketTransportActive = effectiveHyperVActive;
        _usbIpFallbackActive = fallbackToIp;

        if (!changed)
        {
            return;
        }

        UpdateUsbDiagnosticsPanel();
    }

    private void UpdateUsbDiagnosticsPanel()
    {
        void apply()
        {
            _mainWindow?.UpdateTransportDiagnostics(
                hyperVSocketActive: _usbHyperVSocketTransportActive,
                registryServicePresent: _usbHyperVSocketServiceReachable,
                fallbackActive: _usbIpFallbackActive);
        }

        if (_mainWindow?.DispatcherQueue is { } queue && !queue.HasThreadAccess)
        {
            _ = queue.TryEnqueue(apply);
            return;
        }

        apply();
    }

    private void UpdateUsbTransportBridge()
    {
        var usbSettings = _config?.Usb;
        if (usbSettings is null)
        {
            _config ??= new GuestConfig();
            _config.Usb = new GuestUsbSettings();
            usbSettings = _config.Usb;
        }

        usbSettings.UseHyperVSocket = true;

        if (_usbHyperVSocketProxy?.IsRunning == true)
        {
            return;
        }

        var serviceIdText = string.IsNullOrWhiteSpace(usbSettings.HyperVSocketServiceId)
            ? HyperVSocketUsbTunnelDefaults.ServiceIdString
            : usbSettings.HyperVSocketServiceId;

        if (!Guid.TryParse(serviceIdText, out var serviceId))
        {
            _usbHyperVSocketProxy?.Dispose();
            _usbHyperVSocketProxy = null;
            _usbHyperVSocketTransportActive = false;
            _usbHyperVSocketServiceReachable = false;
            _usbHyperVSocketProbeFailureCount = 0;
            UpdateUsbDiagnosticsPanel();

            GuestLogger.Warn("usb.transport.hyperv.invalid_guid", "Hyper-V Socket ServiceId ist ungültig.", new
            {
                serviceId = serviceIdText
            });
            return;
        }

        try
        {
            _usbHyperVSocketProxy?.Dispose();
            _usbHyperVSocketProxy = new HyperVSocketUsbGuestProxy(serviceId);
            _usbHyperVSocketProxy.Start();
            _usbHyperVSocketTransportActive = false;
            UpdateUsbDiagnosticsPanel();

            GuestLogger.Info("usb.transport.hyperv.started", "Hyper-V Socket USB Proxy aktiv (primärer Transport).", new
            {
                serviceId = serviceId.ToString("D"),
                loopback = HyperVSocketUsbTunnelDefaults.LoopbackAddress,
                port = HyperVSocketUsbTunnelDefaults.UsbIpTcpPort
            });
        }
        catch (Exception ex)
        {
            _usbHyperVSocketProxy?.Dispose();
            _usbHyperVSocketProxy = null;
            _usbHyperVSocketTransportActive = false;
            _usbHyperVSocketServiceReachable = false;
            _usbHyperVSocketProbeFailureCount = 0;
            UpdateUsbDiagnosticsPanel();

            GuestLogger.Warn("usb.transport.hyperv.failed", ex.Message, new
            {
                serviceId = serviceId.ToString("D")
            });
        }
    }

    private void MarkHyperVDataPathSuccess()
    {
        _lastHyperVDataPathSuccessUtc = DateTimeOffset.UtcNow;
        _hyperVDataPathConsecutiveFailures = 0;
        _usbHyperVSocketProbeFailureCount = 0;
        _usbHyperVSocketServiceReachable = true;
    }

    private void MarkHyperVDataPathFailure()
    {
        _hyperVDataPathConsecutiveFailures++;

        if (_hyperVDataPathConsecutiveFailures >= 3)
        {
            _usbHyperVSocketServiceReachable = false;
        }
    }

    private void MarkHyperVServiceUnreachableImmediate()
    {
        _usbHyperVSocketServiceReachable = false;
        _usbHyperVSocketTransportActive = false;
        _lastHyperVDataPathSuccessUtc = DateTimeOffset.MinValue;
        _hyperVDataPathConsecutiveFailures = Math.Max(_hyperVDataPathConsecutiveFailures, 3);
    }

    private bool HasRecentHyperVDataPathSuccess()
    {
        return (DateTimeOffset.UtcNow - _lastHyperVDataPathSuccessUtc) <= TimeSpan.FromSeconds(HyperVProbeGraceAfterDataPathSuccessSeconds);
    }

    private static string BuildSharedFolderReconnectRateLimitKey(GuestSharedFolderMapping mapping)
    {
        if (!string.IsNullOrWhiteSpace(mapping.Id))
        {
            return mapping.Id.Trim();
        }

        var driveLetter = GuestConfigService.NormalizeDriveLetter(mapping.DriveLetter ?? string.Empty);
        var sharePath = (mapping.SharePath ?? string.Empty).Trim();
        return $"{driveLetter}|{sharePath}";
    }

    private void WarnRateLimited(string eventName, string message, object? data, string scopeKey, TimeSpan minInterval)
    {
        var key = $"{eventName}:{scopeKey}";
        var now = DateTimeOffset.UtcNow;

        while (true)
        {
            var existing = _rateLimitedWarnStates.GetOrAdd(
                key,
                _ => new RateLimitedWarnState
                {
                    LastLoggedAtUtc = now,
                    SuppressedCount = 0
                });

            var sinceLastLog = now - existing.LastLoggedAtUtc;
            if (sinceLastLog >= minInterval)
            {
                var suppressedCount = existing.SuppressedCount;
                var replacement = new RateLimitedWarnState
                {
                    LastLoggedAtUtc = now,
                    SuppressedCount = 0
                };

                if (_rateLimitedWarnStates.TryUpdate(key, replacement, existing))
                {
                    var finalMessage = suppressedCount > 0
                        ? $"{message} (weitere {suppressedCount} gleichartige Warnung(en) unterdrückt)"
                        : message;
                    GuestLogger.Warn(eventName, finalMessage, data);
                    return;
                }

                continue;
            }

            var suppressedReplacement = new RateLimitedWarnState
            {
                LastLoggedAtUtc = existing.LastLoggedAtUtc,
                SuppressedCount = existing.SuppressedCount + 1
            };

            if (_rateLimitedWarnStates.TryUpdate(key, suppressedReplacement, existing))
            {
                return;
            }
        }
    }

    private void ResetRateLimitedWarning(string eventName, string scopeKey)
    {
        var key = $"{eventName}:{scopeKey}";
        _rateLimitedWarnStates.TryRemove(key, out _);
    }

    private async Task<IReadOnlyList<HostSharedFolderDefinition>> FetchHostSharedFoldersAsync()
    {
        lock (_hyperVSocketMetricsSync)
        {
            _metricSharedFolderCatalogFetchAttempts++;
        }

        if (_config?.SharedFolders?.HostFeatureEnabled == false)
        {
            throw new InvalidOperationException("Shared-Folder Funktion ist durch den Host deaktiviert.");
        }

        var client = new HyperVSocketSharedFolderCatalogGuestClient();
        Exception? lastError = null;

        for (var attempt = 1; attempt <= SharedFolderCatalogFetchMaxAttempts; attempt++)
        {
            try
            {
                var catalog = await client.FetchCatalogAsync(CancellationToken.None);
                lock (_hyperVSocketMetricsSync)
                {
                    _metricSharedFolderCatalogFetchSuccesses++;
                    _lastSharedFolderCatalogSuccessUtc = DateTimeOffset.UtcNow;
                    _lastSharedFolderCatalogFailure = string.Empty;
                }
                return catalog;
            }
            catch (Exception ex) when (attempt < SharedFolderCatalogFetchMaxAttempts && IsTransientCatalogFetchError(ex))
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt));
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        var errorMessage = lastError?.Message ?? "Unbekannter Fehler";
        var socketDetails = GetSocketException(lastError);
        lock (_hyperVSocketMetricsSync)
        {
            _metricSharedFolderCatalogFetchFailures++;
            _lastSharedFolderCatalogFailure = $"{lastError?.GetType().Name}:{errorMessage} (Socket={socketDetails?.SocketErrorCode}, Native={socketDetails?.NativeErrorCode})";
        }
        MarkHyperVServiceUnreachableImmediate();
        UpdateUsbDiagnosticsPanel();
        GuestLogger.Warn("sharedfolders.catalog.fetch_failed", errorMessage, new
        {
            transport = "hyperv-socket",
            attempts = SharedFolderCatalogFetchMaxAttempts,
            exceptionType = lastError?.GetType().FullName,
            socketErrorCode = socketDetails?.SocketErrorCode.ToString(),
            nativeErrorCode = socketDetails?.NativeErrorCode,
            hResult = socketDetails?.HResult,
            diagnostics = BuildSharedFolderSocketDiagnostics("catalog_fetch_failed", null, null)
        });

        var userMessage = "Host-Liste konnte nicht geladen werden (Hyper-V Socket abgelehnt). Prüfe, ob HyperTool Host läuft, und starte Host/Guest ggf. neu.";
        if (lastError is SocketException socketException
            && socketException.SocketErrorCode == SocketError.ConnectionRefused)
        {
            userMessage = "Host-Liste konnte nicht geladen werden (Hyper-V Socket verweigert). Starte HyperTool Host einmalig als Administrator, damit der Shared-Folder-Hyper-V-Dienst registriert wird.";
        }

        throw new InvalidOperationException(
            userMessage,
            lastError);
    }

    private static bool IsTransientCatalogFetchError(Exception exception)
    {
        if (exception is not SocketException socketException)
        {
            return false;
        }

        return socketException.SocketErrorCode is SocketError.ConnectionRefused
            or SocketError.NoBufferSpaceAvailable
            or SocketError.TimedOut
            or SocketError.NetworkDown
            or SocketError.NetworkUnreachable
            or SocketError.HostDown
            or SocketError.HostUnreachable
            or SocketError.TryAgain
            or SocketError.WouldBlock;
    }

    private static SocketException? GetSocketException(Exception? exception)
    {
        var current = exception;
        while (current is not null)
        {
            if (current is SocketException socketException)
            {
                return socketException;
            }

            current = current.InnerException;
        }

        return null;
    }

    private void TrackConfigSaveForDiagnostics()
    {
        var now = DateTimeOffset.UtcNow;
        _configSaveTimestampsUtc.Enqueue(now);

        while (_configSaveTimestampsUtc.TryPeek(out var timestamp)
               && (now - timestamp) > TimeSpan.FromMinutes(5))
        {
            _configSaveTimestampsUtc.TryDequeue(out _);
        }
    }

    private object BuildSharedFolderSocketDiagnostics(string stage, string? driveLetter, int? mappingCount)
    {
        var now = DateTimeOffset.UtcNow;
        var socketGate = HyperVSocketClientConcurrencyGate.GetSnapshot();
        while (_configSaveTimestampsUtc.TryPeek(out var timestamp)
               && (now - timestamp) > TimeSpan.FromMinutes(5))
        {
            _configSaveTimestampsUtc.TryDequeue(out _);
        }

        var configSavesLastMinute = _configSaveTimestampsUtc.Count(value => (now - value) <= TimeSpan.FromMinutes(1));
        var configSavesLastFiveMinutes = _configSaveTimestampsUtc.Count;

        return new
        {
            stage,
            driveLetter,
            mappingCount,
            reconnectGateBusy = _sharedFolderReconnectGate.CurrentCount == 0,
            reconnectTriggerGateBusy = _sharedFolderReconnectTriggerGate.CurrentCount == 0,
            reconnectTaskRunning = _sharedFolderAutoMountTask is { IsCompleted: false },
            reconnectIntervalSeconds = Math.Clamp(_config?.PollIntervalSeconds ?? 15, 5, 3600),
            usbRefreshGateBusy = _usbRefreshGate.CurrentCount == 0,
            configSavesLastMinute,
            configSavesLastFiveMinutes,
            socketGateMax = socketGate.MaxConcurrentOutboundSockets,
            socketGateAvailable = socketGate.AvailableSlots,
            socketGateInflight = socketGate.Inflight,
            socketGatePeakInflight = socketGate.PeakInflight,
            socketGateWaiters = socketGate.Waiters,
            socketGateTotalAcquires = socketGate.TotalAcquireCount,
            socketGateAverageWaitMs = socketGate.AverageAcquireWaitMs,
            socketGateSlowAcquireCount = socketGate.SlowAcquireCount
        };
    }

    private bool TryInitializeSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
            if (!createdNew)
            {
                try
                {
                    _singleInstanceMutex.Dispose();
                }
                catch
                {
                }

                _singleInstanceMutex = null;
                TryNotifyRunningInstanceToShow();
                return false;
            }

            _singleInstanceOwned = true;
            _singleInstanceServerCts = new CancellationTokenSource();
            _singleInstanceServerTask = Task.Run(() => RunSingleInstancePipeServerAsync(_singleInstanceServerCts.Token));
            return true;
        }
        catch
        {
            return true;
        }
    }

    private void TryNotifyRunningInstanceToShow()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", SingleInstancePipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            client.Connect(450);
            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: false);
            writer.WriteLine("SHOW");
            writer.Flush();
        }
        catch
        {
        }
    }

    private async Task RunSingleInstancePipeServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    SingleInstancePipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);

                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
                var command = (await reader.ReadLineAsync())?.Trim();
                if (string.IsNullOrWhiteSpace(command))
                {
                    continue;
                }

                if (string.Equals(command, "SHOW", StringComparison.OrdinalIgnoreCase))
                {
                    if (_mainWindow is null)
                    {
                        _pendingSingleInstanceShow = true;
                        continue;
                    }

                    _ = _mainWindow.DispatcherQueue.TryEnqueue(BringMainWindowToFront);
                    continue;
                }

                if (string.Equals(command, "REFRESH_HOST_FEATURES", StringComparison.OrdinalIgnoreCase))
                {
                    _ = Task.Run(() => HandleHostFeatureRefreshSignalAsync(cancellationToken), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                try
                {
                    await Task.Delay(250, cancellationToken);
                }
                catch
                {
                }
            }
        }
    }

    private async Task HandleHostFeatureRefreshSignalAsync(CancellationToken cancellationToken)
    {
        try
        {
            var identity = await FetchHostIdentityViaHyperVSocketAsync(cancellationToken);
            if (identity is not null)
            {
                ApplyHostIdentityToConfig(identity, persistConfig: true);
            }

            await RefreshUsbDevicesAsync(emitLogs: false);
            TriggerSharedFolderReconnectCycle();
        }
        catch
        {
        }
    }

    private void BringMainWindowToFront()
    {
        _pendingSingleInstanceShow = false;

        if (_mainWindow is null)
        {
            return;
        }

        try
        {
            _mainWindow.ForceDismissStartupSplash();
            _mainWindow.AppWindow.Show();
        }
        catch
        {
        }

        try
        {
            _mainWindow.Activate();
        }
        catch
        {
        }

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);
            if (hwnd != nint.Zero)
            {
                if (IsIconic(hwnd))
                {
                    _ = ShowWindow(hwnd, SwRestore);
                }
                else
                {
                    _ = ShowWindow(hwnd, SwShow);
                }

                _ = SetForegroundWindow(hwnd);
            }
        }
        catch
        {
        }
    }

    private void ShutdownSingleInstanceInfrastructure()
    {
        try
        {
            _singleInstanceServerCts?.Cancel();
        }
        catch
        {
        }

        _singleInstanceServerCts?.Dispose();
        _singleInstanceServerCts = null;
        _singleInstanceServerTask = null;

        if (_singleInstanceMutex is null)
        {
            return;
        }

        try
        {
            if (_singleInstanceOwned)
            {
                _singleInstanceMutex.ReleaseMutex();
            }
        }
        catch
        {
        }
        finally
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            _singleInstanceOwned = false;
        }
    }

    private static bool IsCommandMode(string[] args)
    {
        if (args.Length == 0)
        {
            return false;
        }

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "status",
            "once",
            "run",
            "unmap",
            "handshake",
            "install-autostart",
            "remove-autostart",
            "autostart-status"
        };

        return args.Any(arg => known.Contains(arg));
    }

    private static string ResolveConfigPath(string[] args)
    {
        var index = Array.FindIndex(args, item => string.Equals(item, "--config", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Length)
        {
            return Path.GetFullPath(args[index + 1]);
        }

        return GuestConfigService.DefaultConfigPath;
    }

    private static string[] SplitArgs(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in commandLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result.ToArray();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}


