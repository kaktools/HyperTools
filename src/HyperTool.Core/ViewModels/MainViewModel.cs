using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTool.Models;
using HyperTool.Services;
using Serilog;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace HyperTool.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const string NotConnectedSwitchDisplay = "Nicht verbunden";
    private const int ConfigMenuIndex = 4;
    private const int VkNumLock = 0x90;
    private const uint KeyeventfExtendedKey = 0x0001;
    private const uint KeyeventfKeyUp = 0x0002;
    private static readonly TimeSpan DefaultStaleUsbAttachGracePeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LoopbackManagedUsbAttachGraceFloor = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan StaleUsbAttachFinalRecheckDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GuestAckChannelHealthyWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan GuestNetworkDiagnosticsFreshness = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan UsbMetadataBusAliasTtl = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan VmAutoDetachDelayAfterVmStop = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UsbStaleExportHintEscalationWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan UsbDisconnectRecoveryProbeInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan UsbDisconnectRecoveryFreshnessWindow = TimeSpan.FromSeconds(15);
    private const int UsbStaleExportHintEscalationCount = 2;
    private const int DefaultStaleUsbDetachRetryThreshold = 12;
    private const int LoopbackManagedUsbDetachRetryFloor = 20;
    private static readonly TimeSpan MonitorAgentTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HostMonitorTimeout = TimeSpan.FromSeconds(8);

    [ObservableProperty]
    private string _windowTitle = "HyperTool";

    [ObservableProperty]
    private string _statusText = "Bereit";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _configurationNotice;

    [ObservableProperty]
    private VmDefinition? _selectedVm;

    [ObservableProperty]
    private HyperVSwitchInfo? _selectedSwitch;

    [ObservableProperty]
    private HyperVVmNetworkAdapterInfo? _selectedVmNetworkAdapter;

    [ObservableProperty]
    private HyperVCheckpointInfo? _selectedCheckpoint;

    [ObservableProperty]
    private HyperVCheckpointTreeItem? _selectedCheckpointNode;

    [ObservableProperty]
    private string _newCheckpointName = string.Empty;

    [ObservableProperty]
    private string _newCheckpointDescription = string.Empty;

    [ObservableProperty]
    private string _selectedVmState = "Unbekannt";

    [ObservableProperty]
    private string _selectedVmCurrentSwitch = NotConnectedSwitchDisplay;

    [ObservableProperty]
    private string _busyText = "Bitte warten...";

    [ObservableProperty]
    private int _busyProgressPercent = -1;

    [ObservableProperty]
    private VmDefinition? _selectedVmForConfig;

    [ObservableProperty]
    private bool _selectedVmOpenConsoleWithSessionEdit;

    [ObservableProperty]
    private VmTrayAdapterOption? _selectedVmTrayAdapterOption;

    [ObservableProperty]
    private HyperVVmNetworkAdapterInfo? _selectedVmAdapterForRename;

    [ObservableProperty]
    private string _newVmAdapterName = string.Empty;

    [ObservableProperty]
    private string _vmAdapterRenameValidationMessage = string.Empty;

    [ObservableProperty]
    private VmDefinition? _selectedDefaultVmForConfig;

    [ObservableProperty]
    private string _newVmName = string.Empty;

    [ObservableProperty]
    private string _newVmLabel = string.Empty;

    [ObservableProperty]
    private string _importVmRequestedName = string.Empty;

    [ObservableProperty]
    private string _defaultVmImportDestinationPath = string.Empty;

    [ObservableProperty]
    private VmImportModeOption? _selectedVmImportModeOption;

    [ObservableProperty]
    private bool _hnsEnabled;

    [ObservableProperty]
    private bool _hnsAutoRestartAfterDefaultSwitch;

    [ObservableProperty]
    private bool _hnsAutoRestartAfterAnyConnect;

    [ObservableProperty]
    private string _defaultVmName = string.Empty;

    [ObservableProperty]
    private string _vmConnectComputerName = "";

    [ObservableProperty]
    private string _lastSelectedVmName = string.Empty;

    [ObservableProperty]
    private bool _uiEnableTrayIcon = true;

    [ObservableProperty]
    private bool _uiEnableTrayMenu = true;

    [ObservableProperty]
    private bool _uiStartMinimized;

    [ObservableProperty]
    private bool _uiStartWithWindows;

    [ObservableProperty]
    private bool _uiOpenConsoleAfterVmStart = true;

    [ObservableProperty]
    private bool _uiRestoreNumLockAfterVmStart;

    [ObservableProperty]
    private bool _uiDebugLoggingEnabled;

    [ObservableProperty]
    private bool _uiOpenVmConnectWithSessionEdit;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [ObservableProperty]
    private string _uiTheme = "Dark";

    [ObservableProperty]
    private bool _updateCheckOnStartup = true;

    [ObservableProperty]
    private string _githubOwner = "KaKTools";

    [ObservableProperty]
    private string _githubRepo = "HyperTool";

    [ObservableProperty]
    private string _appVersion = "0.0.0";

    [ObservableProperty]
    private string _updateStatus = "Noch nicht geprüft";

    [ObservableProperty]
    private string _releaseUrl = string.Empty;

    [ObservableProperty]
    private string _installerDownloadUrl = string.Empty;

    [ObservableProperty]
    private string _installerFileName = string.Empty;

    [ObservableProperty]
    private bool _updateInstallAvailable;

    [ObservableProperty]
    private int _selectedMenuIndex;

    [ObservableProperty]
    private bool _isLogExpanded;

    [ObservableProperty]
    private bool _areSwitchesLoaded;

    [ObservableProperty]
    private string _networkSwitchStatusHint = string.Empty;

    [ObservableProperty]
    private string _hostNetworkProfileCategory = "Unknown";

    [ObservableProperty]
    private string _hostNetworkProfileDisplayText = "Host-Netzprofil: Unbekannt";

    [ObservableProperty]
    private bool _hasPendingConfigChanges;

    [ObservableProperty]
    private UsbIpDeviceInfo? _selectedUsbDevice;

    [ObservableProperty]
    private string _usbStatusText = "Noch nicht geladen";

    [ObservableProperty]
    private bool _hostUsbSharingEnabled = true;

    [ObservableProperty]
    private bool _usbAutoDetachOnClientDisconnect = true;

    [ObservableProperty]
    private bool _usbUnshareOnExit = true;

    [ObservableProperty]
    private bool _usbRuntimeAvailable = true;

    [ObservableProperty]
    private string _usbRuntimeHintText = string.Empty;

    [ObservableProperty]
    private bool _hostSharedFoldersEnabled = true;

    [ObservableProperty]
    private string _usbDiagnosticsHyperVSocketText = "Unbekannt";

    [ObservableProperty]
    private string _usbDiagnosticsRegistryServiceText = "Unbekannt";

    [ObservableProperty]
    private string _usbDiagnosticsFallbackText = "Nein";

    [ObservableProperty]
    private bool _selectedUsbDeviceAutoShareEnabled;

    [ObservableProperty]
    private long _resourceMonitorVersion;

    public ObservableCollection<VmDefinition> AvailableVms { get; } = [];

    public ObservableCollection<HyperVSwitchInfo> AvailableSwitches { get; } = [];

    public ObservableCollection<HyperVVmNetworkAdapterInfo> AvailableVmNetworkAdapters { get; } = [];

    public ObservableCollection<VmTrayAdapterOption> AvailableVmTrayAdapterOptions { get; } = [];

    public ObservableCollection<HyperVVmNetworkAdapterInfo> AvailableVmAdaptersForRename { get; } = [];

    public ObservableCollection<HyperVCheckpointInfo> AvailableCheckpoints { get; } = [];

    public ObservableCollection<HyperVCheckpointTreeItem> AvailableCheckpointTree { get; } = [];

    public ObservableCollection<UiNotification> Notifications { get; } = [];

    public ObservableCollection<UsbIpDeviceInfo> UsbDevices { get; } = [];

    public ObservableCollection<HostSharedFolderDefinition> HostSharedFolders { get; } = [];

    public IReadOnlyList<VmImportModeOption> VmImportModeOptions { get; } =
    [
        new() { Key = "copy", Label = "Kopieren (Quellordner + Zielordner)", Description = "Fragt Quellordner und Zielordner ab, kopiert die VM und erzeugt eine neue VM-ID." },
        new() { Key = "register", Label = "Direkt registrieren (nur Quellordner)", Description = "Fragt nur den Quellordner ab und registriert die VM am bestehenden Speicherort." },
        new() { Key = "restore", Label = "Wiederherstellen (nur Quellordner)", Description = "Fragt nur den Quellordner ab und importiert ohne Kopie mit bestehender VM-ID." }
    ];

    public string DefaultSwitchName { get; }

    public string ConfigPath => _configPath;

    public IReadOnlyList<string> AvailableUiThemes { get; } = ["Dark", "Light"];

    public bool HasConfigurationNotice => !string.IsNullOrWhiteSpace(ConfigurationNotice);

    public UiNotification? LatestNotification => Notifications.FirstOrDefault();

    public string LastNotificationText
    {
        get
        {
            var latest = LatestNotification;
            if (latest is null)
            {
                return "Keine Notifications";
            }

            var text = $"[{latest.Timestamp:HH:mm:ss}] [{latest.Level}] {latest.Message}";
            return text.Length <= 120 ? text : $"{text[..117]}...";
        }
    }

    public string LogToggleText => IsLogExpanded ? "▾ Log einklappen" : "▸ Log ausklappen";

    public string SelectedVmDisplayName => SelectedVm?.DisplayLabel ?? "-";

    public bool HasBusyProgress => IsBusy && BusyProgressPercent >= 0;

    public string SelectedVmAdapterSwitchDisplay
    {
        get
        {
            if (SelectedVm is null)
            {
                return "-";
            }

            if (SelectedVmNetworkAdapter is null)
            {
                return SelectedVmCurrentSwitch;
            }

            var adapterName = GetAdapterDisplayName(SelectedVmNetworkAdapter);
            var switchName = NormalizeSwitchDisplayName(SelectedVmNetworkAdapter.SwitchName);
            return $"{adapterName} | {switchName}";
        }
    }

    public IAsyncRelayCommand StartDefaultVmCommand { get; }

    public IAsyncRelayCommand StopDefaultVmCommand { get; }

    public IAsyncRelayCommand CreateCheckpointCommand { get; }

    public IAsyncRelayCommand StartSelectedVmCommand { get; }

    public IAsyncRelayCommand StopSelectedVmCommand { get; }

    public IAsyncRelayCommand TurnOffSelectedVmCommand { get; }

    public IAsyncRelayCommand RestartSelectedVmCommand { get; }

    public IAsyncRelayCommand OpenConsoleCommand { get; }

    public IAsyncRelayCommand ReopenConsoleWithSessionEditCommand { get; }

    public IAsyncRelayCommand ExportSelectedVmCommand { get; }

    public IAsyncRelayCommand ImportVmCommand { get; }

    public IAsyncRelayCommand LoadSwitchesCommand { get; }

    public IAsyncRelayCommand RefreshSwitchesCommand { get; }

    public IAsyncRelayCommand ConnectSelectedSwitchCommand { get; }

    public IAsyncRelayCommand DisconnectSwitchCommand { get; }

    public IAsyncRelayCommand<string> ConnectAdapterToSwitchByKeyCommand { get; }

    public IAsyncRelayCommand<string> DisconnectAdapterByNameCommand { get; }

    public IAsyncRelayCommand RefreshVmStatusCommand { get; }

    public IAsyncRelayCommand LoadCheckpointsCommand { get; }

    public IAsyncRelayCommand ApplyCheckpointCommand { get; }

    public IAsyncRelayCommand DeleteCheckpointCommand { get; }

    public IRelayCommand AddVmCommand { get; }

    public IRelayCommand RemoveVmCommand { get; }

    public IRelayCommand SetDefaultVmCommand { get; }

    public IAsyncRelayCommand RenameVmAdapterCommand { get; }

    public IAsyncRelayCommand SaveConfigCommand { get; }

    public IAsyncRelayCommand ReloadConfigCommand { get; }

    public IAsyncRelayCommand RestartHnsCommand { get; }

    public IAsyncRelayCommand CheckForUpdatesCommand { get; }

    public IAsyncRelayCommand InstallUpdateCommand { get; }

    public IRelayCommand OpenReleasePageCommand { get; }

    public IRelayCommand ToggleLogCommand { get; }

    public IRelayCommand OpenLogFileCommand { get; }

    public IRelayCommand<VmDefinition> SelectVmFromChipCommand { get; }

    public IRelayCommand ClearNotificationsCommand { get; }

    public IRelayCommand CopyNotificationsCommand { get; }

    public IAsyncRelayCommand<string> StartVmByNameCommand { get; }

    public IAsyncRelayCommand<string> StopVmByNameCommand { get; }

    public IAsyncRelayCommand<string> TurnOffVmByNameCommand { get; }

    public IAsyncRelayCommand<string> RestartVmByNameCommand { get; }

    public IAsyncRelayCommand<string> OpenConsoleByNameCommand { get; }

    public IAsyncRelayCommand<string> CreateSnapshotByNameCommand { get; }

    public IAsyncRelayCommand RefreshUsbDevicesCommand { get; }

    public IAsyncRelayCommand BindUsbDeviceCommand { get; }

    public IAsyncRelayCommand UnbindUsbDeviceCommand { get; }

    public IAsyncRelayCommand DetachUsbDeviceCommand { get; }

    private readonly IHyperVService _hyperVService;
    private readonly IHnsService _hnsService;
    private readonly IConfigService _configService;
    private readonly IStartupService _startupService;
    private readonly IUpdateService _updateService;
    private readonly IUsbIpService _usbIpService;
    private readonly IUiInteropService _uiInteropService;
    private readonly string _configPath;

    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private List<string> _trayVmNames = [];
    private readonly Dictionary<string, VmDefinition> _configuredVmDefinitions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly char[] VmAdapterInvalidNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];
    private int _selectedVmChangeSuppressionDepth;
    private int _configChangeSuppressionDepth;
    private int _lastSelectedMenuIndex;
    private bool _isHandlingMenuSelectionChange;
    private bool _suppressUsbAutoShareToggleHandling;
    private readonly SemaphoreSlim _usbTrayRefreshGate = new(1, 1);
    private readonly SemaphoreSlim _vmStatusRefreshGate = new(1, 1);
    private readonly HashSet<string> _usbAutoShareDeviceKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UsbDeviceMetadataEntry> _usbDeviceMetadataByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UsbDeviceMetadataEntry> _usbMetadataBusAliasByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _usbMetadataBusAliasExpiresUtc = new(StringComparer.OrdinalIgnoreCase);
    private bool _usbHardwareIdentityMigrationCompleted;
    private bool _usbConfigResetMigrationApplied;
    private bool _usbResetMigrationInfoShown;
    private readonly Dictionary<string, DateTimeOffset> _usbAttachedWithoutAckSinceUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _usbAttachedWithoutAckAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _usbForceDetachFallbackBusIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _usbGuestManagedBusIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _usbVmNotRunningSinceUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _usbVmOffDetachManualRequiredBusIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc, int Count)> _usbStaleExportHintStateByBusId = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _resourceMonitorSync = new();
    private readonly Dictionary<string, VmResourceMonitorRuntimeState> _vmMonitorStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<double> _hostCpuHistory = new();
    private readonly Queue<double> _hostRamPressureHistory = new();
    private readonly Dictionary<string, string> _checkpointDescriptionOverridesByKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient UpdateDownloadClient = new();
    private int _uiNumLockWatcherIntervalSeconds = 30;
    private bool _lastAppliedDebugLoggingEnabled;
    private int _usbAutoDetachRetryAttempts = DefaultStaleUsbDetachRetryThreshold;
    private TimeSpan _usbAutoDetachGracePeriod = DefaultStaleUsbAttachGracePeriod;
    private TimeSpan _usbAutoDetachRetryDelay = TimeSpan.FromMilliseconds(450);
    private bool _monitorEnabled = true;
    private int _monitorIntervalMs = 1000;
    private int _monitorHistoryMinutes = 5;
    private int _monitorHistorySize = 300;
    private readonly SystemResourceSampler _resourceMonitorHostSampler = new();
    private DateTimeOffset _lastHostResourceSampleUtc = DateTimeOffset.MinValue;
    private double _hostCpuPercent;
    private double _hostRamUsedGb;
    private double _hostRamTotalGb;

    private sealed class VmResourceMonitorRuntimeState
    {
        public string VmName { get; set; } = string.Empty;

        public string State { get; set; } = "Guest nicht erreichbar";

        public string ActiveSource { get; set; } = "none";

        public double GuestCpuPercent { get; set; }

        public double GuestRamUsedGb { get; set; }

        public double GuestRamTotalGb { get; set; }

        public DateTimeOffset LastGuestSeenUtc { get; set; } = DateTimeOffset.MinValue;

        public double HostCpuPercent { get; set; }

        public double HostRamUsedGb { get; set; }

        public double HostRamTotalGb { get; set; }

        public DateTimeOffset LastHostSeenUtc { get; set; } = DateTimeOffset.MinValue;

        public double CpuPercent { get; set; }

        public double RamUsedGb { get; set; }

        public double RamTotalGb { get; set; }

        public Queue<double> CpuHistory { get; } = new();

        public Queue<double> RamPressureHistory { get; } = new();
    }

    public event EventHandler? TrayStateChanged;

    public bool CanPromptSaveOnClose => HasPendingConfigChanges && !IsBusy;

    public bool HasUsbAutoShareConfigured => _usbAutoShareDeviceKeys.Count > 0;

    public bool MonitoringEnabled => _monitorEnabled;

    public int MonitoringIntervalMs => _monitorIntervalMs;

    public int MonitoringHistoryMinutes => _monitorHistoryMinutes;

    public int MonitoringHistorySize => _monitorHistorySize;

    public MainViewModel(
        ConfigLoadResult configResult,
        IHyperVService hyperVService,
        IHnsService hnsService,
        IConfigService configService,
        IStartupService startupService,
        IUpdateService updateService,
        IUsbIpService usbIpService,
        IUiInteropService uiInteropService)
    {
        _hyperVService = hyperVService;
        _hnsService = hnsService;
        _configService = configService;
        _startupService = startupService;
        _updateService = updateService;
        _usbIpService = usbIpService;
        _uiInteropService = uiInteropService;
        SelectedVmImportModeOption = VmImportModeOptions.FirstOrDefault();
        _configPath = configResult.ConfigPath;

        _configChangeSuppressionDepth++;

        WindowTitle = "HyperTool";
        DefaultVmName = configResult.Config.DefaultVmName;
        LastSelectedVmName = configResult.Config.LastSelectedVmName;
        DefaultSwitchName = configResult.Config.DefaultSwitchName;
        VmConnectComputerName = NormalizeVmConnectComputerName(configResult.Config.VmConnectComputerName);
        ConfigurationNotice = configResult.Notice;
        HnsEnabled = configResult.Config.Hns.Enabled;
        HnsAutoRestartAfterDefaultSwitch = configResult.Config.Hns.AutoRestartAfterDefaultSwitch;
        HnsAutoRestartAfterAnyConnect = configResult.Config.Hns.AutoRestartAfterAnyConnect;
        UiEnableTrayIcon = true;
        UiEnableTrayMenu = configResult.Config.Ui.EnableTrayMenu;
        UiStartMinimized = configResult.Config.Ui.StartMinimized;
        UiStartWithWindows = configResult.Config.Ui.StartWithWindows;
        UiOpenConsoleAfterVmStart = configResult.Config.Ui.OpenConsoleAfterVmStart;
        UiRestoreNumLockAfterVmStart = configResult.Config.Ui.RestoreNumLockAfterVmStart;
        UiDebugLoggingEnabled = configResult.Config.Ui.DebugLoggingEnabled;
        _lastAppliedDebugLoggingEnabled = UiDebugLoggingEnabled;
        _uiNumLockWatcherIntervalSeconds = Math.Clamp(configResult.Config.Ui.NumLockWatcherIntervalSeconds, 5, 600);
        UiOpenVmConnectWithSessionEdit = configResult.Config.Ui.OpenVmConnectWithSessionEdit;
        UiTheme = NormalizeUiTheme(configResult.Config.Ui.Theme);
        UpdateCheckOnStartup = configResult.Config.Update.CheckOnStartup;
        GithubOwner = configResult.Config.Update.GitHubOwner;
        GithubRepo = configResult.Config.Update.GitHubRepo;
        HostUsbSharingEnabled = configResult.Config.Usb.Enabled;
        UsbAutoDetachOnClientDisconnect = configResult.Config.Usb.AutoDetachOnClientDisconnect;
        _usbAutoDetachRetryAttempts = Math.Clamp(configResult.Config.Usb.AutoDetachRetryAttempts, 1, 10);
        _usbAutoDetachGracePeriod = TimeSpan.FromSeconds(Math.Clamp(configResult.Config.Usb.AutoDetachGracePeriodSeconds, 5, 300));
        _usbAutoDetachRetryDelay = TimeSpan.FromMilliseconds(Math.Clamp(configResult.Config.Usb.AutoDetachRetryDelayMs, 100, 5000));
        UsbUnshareOnExit = configResult.Config.Usb.UnshareOnExit;
        _monitorEnabled = configResult.Config.Monitoring.Enabled;
        _monitorIntervalMs = configResult.Config.Monitoring.IntervalMs;
        _monitorHistoryMinutes = configResult.Config.Monitoring.GraphHistoryMinutes;
        _monitorHistorySize = configResult.Config.Monitoring.GraphHistorySize;
        HostSharedFoldersEnabled = configResult.Config.SharedFolders.Enabled;
        DefaultVmImportDestinationPath = configResult.Config.DefaultVmImportDestinationPath?.Trim() ?? string.Empty;
        _usbAutoShareDeviceKeys.Clear();
        foreach (var key in configResult.Config.Usb.AutoShareDeviceKeys)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                _usbAutoShareDeviceKeys.Add(key.Trim());
            }
        }
        _usbHardwareIdentityMigrationCompleted = configResult.Config.Usb.HardwareIdentityMigrationCompleted;
        _usbConfigResetMigrationApplied = configResult.Config.Usb.UsbConfigResetMigrationApplied;
        _usbDeviceMetadataByKey.Clear();
        _usbMetadataBusAliasByKey.Clear();
        _usbMetadataBusAliasExpiresUtc.Clear();
        LoadCheckpointDescriptionOverrides(configResult.Config);
        foreach (var metadata in configResult.Config.Usb.DeviceMetadata)
        {
            if (metadata is null)
            {
                continue;
            }

            var deviceKey = (metadata.DeviceKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(deviceKey))
            {
                continue;
            }

            var customName = (metadata.CustomName ?? string.Empty).Trim();
            var comment = (metadata.Comment ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(customName) && string.IsNullOrWhiteSpace(comment))
            {
                continue;
            }

            _usbDeviceMetadataByKey[deviceKey] = new UsbDeviceMetadataEntry
            {
                DeviceKey = deviceKey,
                CustomName = customName,
                Comment = comment
            };
        }

        if (!_usbConfigResetMigrationApplied)
        {
            _usbAutoShareDeviceKeys.Clear();
            _usbDeviceMetadataByKey.Clear();
            _usbMetadataBusAliasByKey.Clear();
            _usbMetadataBusAliasExpiresUtc.Clear();
            _usbHardwareIdentityMigrationCompleted = true;
            _usbConfigResetMigrationApplied = true;
            PersistUsbAutoShareConfig();
            AddNotification("USB-Konfiguration wurde für das neue Mapping einmalig zurückgesetzt. Bitte Auto-Share und Kommentare neu setzen.", "Info");
            ShowUsbResetMigrationInfoOnce();
        }

        var removedLegacyUsbKeysOnLoad = PurgeLegacyUsbBusIdKeysInMemory();
        ApplyConfiguredSharedFolders(configResult.Config.SharedFolders.HostDefinitions);

        if (removedLegacyUsbKeysOnLoad)
        {
            PersistUsbAutoShareConfig();
            AddNotification("Alte USB-BUSID-Einträge wurden bereinigt, um falsche Zuordnungen zu vermeiden.", "Info");
        }

        ApplyConfiguredVmDefinitions(configResult.Config.Vms);
        _trayVmNames = NormalizeTrayVmNames(configResult.Config.Ui.TrayVmNames);
        AppVersion = ResolveAppVersion();
        UpdateStatus = "Noch nicht geprüft";

        if (configResult.IsGenerated)
        {
            StatusText = "Beispiel-Konfiguration erzeugt";
        }
        else if (configResult.HasValidationFixes)
        {
            StatusText = "Konfiguration korrigiert geladen";
        }
        else
        {
            StatusText = "Konfiguration geladen";
        }

        StartSelectedVmCommand = new AsyncRelayCommand(StartSelectedVmAsync, CanExecuteStartVmAction);
        StopSelectedVmCommand = new AsyncRelayCommand(StopSelectedVmAsync, CanExecuteStopVmAction);
        TurnOffSelectedVmCommand = new AsyncRelayCommand(TurnOffSelectedVmAsync, CanExecuteStopVmAction);
        RestartSelectedVmCommand = new AsyncRelayCommand(RestartSelectedVmAsync, CanExecuteRestartVmAction);
        OpenConsoleCommand = new AsyncRelayCommand(OpenConsoleAsync, CanExecuteStopVmAction);
        ReopenConsoleWithSessionEditCommand = new AsyncRelayCommand(ReopenConsoleWithSessionEditAsync, CanExecuteVmAction);
        ExportSelectedVmCommand = new AsyncRelayCommand(ExportSelectedVmAsync, () => !IsBusy && SelectedVmForConfig is not null);
        ImportVmCommand = new AsyncRelayCommand(ImportVmAsync, () => !IsBusy);

        LoadSwitchesCommand = new AsyncRelayCommand(RefreshSwitchesAsync, () => !IsBusy);
        RefreshSwitchesCommand = new AsyncRelayCommand(RefreshSwitchesAsync, () => !IsBusy);
        ConnectSelectedSwitchCommand = new AsyncRelayCommand(ConnectSelectedSwitchAsync, () => !IsBusy && SelectedVm is not null && SelectedVmNetworkAdapter is not null && SelectedSwitch is not null && AreSwitchesLoaded);
        DisconnectSwitchCommand = new AsyncRelayCommand(DisconnectSwitchAsync, () => !IsBusy && SelectedVm is not null && SelectedVmNetworkAdapter is not null);
        ConnectAdapterToSwitchByKeyCommand = new AsyncRelayCommand<string>(ConnectAdapterToSwitchByKeyAsync, _ => !IsBusy && SelectedVm is not null && AreSwitchesLoaded);
        DisconnectAdapterByNameCommand = new AsyncRelayCommand<string>(DisconnectAdapterByNameAsync, _ => !IsBusy && SelectedVm is not null);
        RefreshVmStatusCommand = new AsyncRelayCommand(RefreshRuntimeDataAsync, () => !IsBusy);

        LoadCheckpointsCommand = new AsyncRelayCommand(LoadCheckpointsAsync, () => SelectedVm is not null);
        ApplyCheckpointCommand = new AsyncRelayCommand(ApplyCheckpointAsync, () => !IsBusy && SelectedVm is not null && SelectedCheckpoint is not null);
        DeleteCheckpointCommand = new AsyncRelayCommand(DeleteCheckpointAsync, () => !IsBusy && SelectedVm is not null && SelectedCheckpoint is not null);

        AddVmCommand = new RelayCommand(AddVm);
        RemoveVmCommand = new RelayCommand(RemoveSelectedVm);
        SetDefaultVmCommand = new RelayCommand(SetDefaultVmFromSelection);
        RenameVmAdapterCommand = new AsyncRelayCommand(RenameVmAdapterAsync, CanExecuteRenameVmAdapter);
        SaveConfigCommand = new AsyncRelayCommand(SaveConfigAsync, () => !IsBusy);
        ReloadConfigCommand = new AsyncRelayCommand(ReloadConfigAsync, () => !IsBusy);
        RestartHnsCommand = new AsyncRelayCommand(RestartHnsAsync, () => !IsBusy);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsBusy);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, () => !IsBusy && UpdateInstallAvailable && !string.IsNullOrWhiteSpace(InstallerDownloadUrl));
        OpenReleasePageCommand = new RelayCommand(OpenReleasePage);
        ToggleLogCommand = new RelayCommand(ToggleLog);
        OpenLogFileCommand = new RelayCommand(OpenLogFile);
        SelectVmFromChipCommand = new RelayCommand<VmDefinition>(SelectVmFromChip);
        ClearNotificationsCommand = new RelayCommand(ClearNotifications);
        CopyNotificationsCommand = new RelayCommand(CopyNotificationsToClipboard);

        StartVmByNameCommand = new AsyncRelayCommand<string>(StartVmByNameAsync, _ => !IsBusy);
        StopVmByNameCommand = new AsyncRelayCommand<string>(StopVmByNameAsync, _ => !IsBusy);
        TurnOffVmByNameCommand = new AsyncRelayCommand<string>(TurnOffVmByNameAsync, _ => !IsBusy);
        RestartVmByNameCommand = new AsyncRelayCommand<string>(RestartVmByNameAsync, _ => !IsBusy);
        OpenConsoleByNameCommand = new AsyncRelayCommand<string>(OpenConsoleByNameAsync, _ => !IsBusy);
        CreateSnapshotByNameCommand = new AsyncRelayCommand<string>(CreateSnapshotByNameAsync, _ => !IsBusy);

        RefreshUsbDevicesCommand = new AsyncRelayCommand(RefreshUsbDevicesAsync, () => !IsBusy && UsbRuntimeAvailable && HostUsbSharingEnabled);
        BindUsbDeviceCommand = new AsyncRelayCommand(BindSelectedUsbDeviceAsync, CanExecuteBindUsbAction);
        UnbindUsbDeviceCommand = new AsyncRelayCommand(UnbindSelectedUsbDeviceAsync, CanExecuteUnbindUsbAction);
        DetachUsbDeviceCommand = new AsyncRelayCommand(DetachSelectedUsbDeviceAsync, CanExecuteDetachUsbAction);

        StartDefaultVmCommand = new AsyncRelayCommand(StartDefaultVmAsync, () => !IsBusy);
        StopDefaultVmCommand = new AsyncRelayCommand(StopDefaultVmAsync, () => !IsBusy);
        CreateCheckpointCommand = new AsyncRelayCommand(CreateCheckpointAsync, CanExecuteVmAction);

        SelectedVmForConfig = SelectedVm;
        SelectedDefaultVmForConfig = SelectedVm;

        IsLogExpanded = false;
        Notifications.CollectionChanged += OnNotificationsChanged;

        _configChangeSuppressionDepth--;
        HasPendingConfigChanges = false;
        _lastSelectedMenuIndex = SelectedMenuIndex;

        _ = InitializeAsync();
        _ = RunNumLockWatcherAsync();
    }

    partial void OnIsLogExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(LogToggleText));
    }

    partial void OnAreSwitchesLoadedChanged(bool value)
    {
        ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();
    }

    partial void OnConfigurationNoticeChanged(string? value)
    {
        OnPropertyChanged(nameof(HasConfigurationNotice));
    }

    partial void OnIsBusyChanged(bool value)
    {
        StartSelectedVmCommand.NotifyCanExecuteChanged();
        StopSelectedVmCommand.NotifyCanExecuteChanged();
        TurnOffSelectedVmCommand.NotifyCanExecuteChanged();
        RestartSelectedVmCommand.NotifyCanExecuteChanged();
        OpenConsoleCommand.NotifyCanExecuteChanged();
        ReopenConsoleWithSessionEditCommand.NotifyCanExecuteChanged();
        ExportSelectedVmCommand.NotifyCanExecuteChanged();
        ImportVmCommand.NotifyCanExecuteChanged();
        LoadSwitchesCommand.NotifyCanExecuteChanged();
        RefreshSwitchesCommand.NotifyCanExecuteChanged();
        ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();
        DisconnectSwitchCommand.NotifyCanExecuteChanged();
        ConnectAdapterToSwitchByKeyCommand.NotifyCanExecuteChanged();
        DisconnectAdapterByNameCommand.NotifyCanExecuteChanged();
        RefreshVmStatusCommand.NotifyCanExecuteChanged();
        CreateCheckpointCommand.NotifyCanExecuteChanged();
        LoadCheckpointsCommand.NotifyCanExecuteChanged();
        ApplyCheckpointCommand.NotifyCanExecuteChanged();
        DeleteCheckpointCommand.NotifyCanExecuteChanged();
        LoadCheckpointsCommand.NotifyCanExecuteChanged();
        StartDefaultVmCommand.NotifyCanExecuteChanged();
        StopDefaultVmCommand.NotifyCanExecuteChanged();
        SaveConfigCommand.NotifyCanExecuteChanged();
        ReloadConfigCommand.NotifyCanExecuteChanged();
        RestartHnsCommand.NotifyCanExecuteChanged();
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        InstallUpdateCommand.NotifyCanExecuteChanged();
        StartVmByNameCommand.NotifyCanExecuteChanged();
        StopVmByNameCommand.NotifyCanExecuteChanged();
        TurnOffVmByNameCommand.NotifyCanExecuteChanged();
        RestartVmByNameCommand.NotifyCanExecuteChanged();
        OpenConsoleByNameCommand.NotifyCanExecuteChanged();
        CreateSnapshotByNameCommand.NotifyCanExecuteChanged();
        RenameVmAdapterCommand.NotifyCanExecuteChanged();
        RefreshUsbDevicesCommand.NotifyCanExecuteChanged();
        BindUsbDeviceCommand.NotifyCanExecuteChanged();
        UnbindUsbDeviceCommand.NotifyCanExecuteChanged();
        DetachUsbDeviceCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasBusyProgress));
    }

    partial void OnBusyProgressPercentChanged(int value)
    {
        OnPropertyChanged(nameof(HasBusyProgress));
    }

    partial void OnSelectedMenuIndexChanged(int value)
    {
        if (_isHandlingMenuSelectionChange)
        {
            _lastSelectedMenuIndex = value;

            if (value == 0)
            {
                _ = HandleNetworkTabActivatedAsync();
            }

            return;
        }

        if (ShouldPromptSaveWhenLeavingConfig(value) && !TryPromptSaveConfigChanges())
        {
            _isHandlingMenuSelectionChange = true;
            try
            {
                SelectedMenuIndex = ConfigMenuIndex;
            }
            finally
            {
                _isHandlingMenuSelectionChange = false;
            }

            _lastSelectedMenuIndex = ConfigMenuIndex;
            return;
        }

        _lastSelectedMenuIndex = value;

        if (value == 0)
        {
            _ = HandleNetworkTabActivatedAsync();
        }
        else if (value == 1)
        {
            _ = LoadUsbDevicesAsync(showNotification: false);
        }
    }

    partial void OnHnsAutoRestartAfterDefaultSwitchChanged(bool value) => MarkConfigDirty();

    partial void OnHnsAutoRestartAfterAnyConnectChanged(bool value) => MarkConfigDirty();

    partial void OnUiEnableTrayMenuChanged(bool value)
    {
        NotifyTrayStateChanged();
        MarkConfigDirty();
    }

    partial void OnUiStartMinimizedChanged(bool value) => MarkConfigDirty();

    partial void OnUiStartWithWindowsChanged(bool value) => MarkConfigDirty();

    partial void OnUiOpenConsoleAfterVmStartChanged(bool value) => MarkConfigDirty();

    partial void OnUiRestoreNumLockAfterVmStartChanged(bool value) => MarkConfigDirty();

    partial void OnUiDebugLoggingEnabledChanged(bool value) => MarkConfigDirty();

    partial void OnUiOpenVmConnectWithSessionEditChanged(bool value) => MarkConfigDirty();

    partial void OnDefaultVmImportDestinationPathChanged(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            DefaultVmImportDestinationPath = normalized;
            return;
        }

        MarkConfigDirty();
    }

    partial void OnUiThemeChanged(string value)
    {
        MarkConfigDirty();
    }

    partial void OnVmConnectComputerNameChanged(string value) => MarkConfigDirty();

    partial void OnUpdateCheckOnStartupChanged(bool value) => MarkConfigDirty();

    partial void OnGithubOwnerChanged(string value) => MarkConfigDirty();

    partial void OnGithubRepoChanged(string value) => MarkConfigDirty();

    partial void OnHostUsbSharingEnabledChanged(bool value)
    {
        RefreshUsbDevicesCommand?.NotifyCanExecuteChanged();
        BindUsbDeviceCommand?.NotifyCanExecuteChanged();
        UnbindUsbDeviceCommand?.NotifyCanExecuteChanged();
        DetachUsbDeviceCommand?.NotifyCanExecuteChanged();

        if (!value)
        {
            UsbDevices.Clear();
            SelectedUsbDevice = null;
            UsbStatusText = "USB Share ist global im Host deaktiviert.";
            UsbRuntimeHintText = string.Empty;
        }

        MarkConfigDirty();
        NotifyTrayStateChanged();
    }

    partial void OnUsbAutoDetachOnClientDisconnectChanged(bool value) => MarkConfigDirty();

    partial void OnUsbUnshareOnExitChanged(bool value) => MarkConfigDirty();

    partial void OnHostSharedFoldersEnabledChanged(bool value)
    {
        MarkConfigDirty();
    }

    public async Task SetHostUsbSharingEnabledAsync(bool enabled)
    {
        if (HostUsbSharingEnabled == enabled)
        {
            return;
        }

        if (!enabled)
        {
            if (!UsbRuntimeAvailable)
            {
                HostUsbSharingEnabled = false;
                return;
            }

            try
            {
                await UnshareAllSharedUsbAsync(
                    timeout: TimeSpan.FromSeconds(20),
                    successMessage: "Beim Deaktivieren wurden {0} USB-Freigabe(n) entfernt.",
                    failedMessage: "Beim Deaktivieren konnten {0} USB-Freigabe(n) nicht entfernt werden.",
                    logContext: "host-usb-disable");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "USB-Freigaben konnten beim Deaktivieren nicht vollständig entfernt werden.");
                AddNotification("USB-Freigaben konnten nicht vollständig entfernt werden (usbipd ggf. nicht verfügbar).", "Warning");
            }
            finally
            {
                try
                {
                    await _usbIpService.ShutdownElevatedSessionAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Elevated USB session could not be closed after disabling host USB sharing.");
                }
            }
        }

        HostUsbSharingEnabled = enabled;
    }

    public Task SetHostSharedFoldersEnabledAsync(bool enabled)
    {
        if (HostSharedFoldersEnabled != enabled)
        {
            HostSharedFoldersEnabled = enabled;
        }

        return Task.CompletedTask;
    }

    partial void OnSelectedVmChanged(VmDefinition? value)
    {
        ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();
        CreateCheckpointCommand.NotifyCanExecuteChanged();
        ReopenConsoleWithSessionEditCommand.NotifyCanExecuteChanged();
        SelectedVmForConfig = value;
        OnPropertyChanged(nameof(SelectedVmDisplayName));
        OnPropertyChanged(nameof(SelectedVmAdapterSwitchDisplay));
        NotifyTrayStateChanged();

        if (value is null)
        {
            SelectedVmState = "Unbekannt";
            SelectedVmCurrentSwitch = NotConnectedSwitchDisplay;
            AvailableVmNetworkAdapters.Clear();
            SelectedVmNetworkAdapter = null;
            SelectedSwitch = null;
            NetworkSwitchStatusHint = "Keine VM ausgewählt.";
            AvailableCheckpoints.Clear();
            AvailableCheckpointTree.Clear();
            SelectedCheckpoint = null;
            SelectedCheckpointNode = null;
            return;
        }

        if (_selectedVmChangeSuppressionDepth > 0)
        {
            return;
        }

        LastSelectedVmName = value.Name;
        SelectedVmState = string.IsNullOrWhiteSpace(value.RuntimeState) ? "Unbekannt" : value.RuntimeState;
        SelectedVmCurrentSwitch = NormalizeSwitchDisplayName(value.RuntimeSwitchName);
        SyncSelectedSwitchWithCurrentVm(showNotificationOnMissingSwitch: false);
        _ = PersistSelectedVmAsync(value.Name);

        _ = EnsureSelectedVmNetworkSelectionAsync(showNotificationOnMissingSwitch: false);
        _ = RefreshSelectedVmStatusAfterSelectionAsync(value.Name);
        _ = LoadCheckpointsAsync();
    }

    private async Task RefreshSelectedVmStatusAfterSelectionAsync(string selectedVmName)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (SelectedVm is null
                || !string.Equals(SelectedVm.Name, selectedVmName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!IsBusy)
            {
                await RefreshVmStatusAsync();
                return;
            }

            await Task.Delay(200);
        }
    }

    private async Task HandleNetworkTabActivatedAsync()
    {
        await EnsureSelectedVmNetworkSelectionAsync(showNotificationOnMissingSwitch: false);

        if (!AreSwitchesLoaded)
        {
            await RefreshSwitchesAsync();
            return;
        }

        SyncSelectedSwitchWithCurrentVm(showNotificationOnMissingSwitch: false);
    }

    partial void OnUpdateInstallAvailableChanged(bool value)
    {
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnInstallerDownloadUrlChanged(string value)
    {
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedVmStateChanged(string value)
    {
        StartSelectedVmCommand.NotifyCanExecuteChanged();
        StopSelectedVmCommand.NotifyCanExecuteChanged();
        TurnOffSelectedVmCommand.NotifyCanExecuteChanged();
        RestartSelectedVmCommand.NotifyCanExecuteChanged();
        OpenConsoleCommand.NotifyCanExecuteChanged();
        ReopenConsoleWithSessionEditCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedVmCurrentSwitchChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedVmAdapterSwitchDisplay));
    }

    partial void OnSelectedSwitchChanged(HyperVSwitchInfo? value)
    {
        ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedVmNetworkAdapterChanged(HyperVVmNetworkAdapterInfo? value)
    {
        ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();
        DisconnectSwitchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedVmAdapterSwitchDisplay));

        if (SelectedVm is null || value is null)
        {
            if (SelectedVm is not null && AvailableVmNetworkAdapters.Count == 0)
            {
                SelectedVmCurrentSwitch = NotConnectedSwitchDisplay;
                NetworkSwitchStatusHint = "Keine VM-Netzwerkkarten gefunden.";
                SelectedSwitch = null;
            }

            return;
        }

        SelectedVmCurrentSwitch = NormalizeSwitchDisplayName(value.SwitchName);

        var selectedVmEntry = AvailableVms.FirstOrDefault(vm =>
            string.Equals(vm.Name, SelectedVm.Name, StringComparison.OrdinalIgnoreCase));

        if (selectedVmEntry is not null)
        {
            selectedVmEntry.RuntimeSwitchName = SelectedVmCurrentSwitch;
        }

        SyncSelectedSwitchWithCurrentVm(showNotificationOnMissingSwitch: false);
    }

    partial void OnSelectedCheckpointChanged(HyperVCheckpointInfo? value)
    {
        ApplyCheckpointCommand.NotifyCanExecuteChanged();
        DeleteCheckpointCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedCheckpointNodeChanged(HyperVCheckpointTreeItem? value)
    {
        SelectedCheckpoint = value?.Checkpoint;
    }

    partial void OnSelectedUsbDeviceChanged(UsbIpDeviceInfo? value)
    {
        BindUsbDeviceCommand.NotifyCanExecuteChanged();
        UnbindUsbDeviceCommand.NotifyCanExecuteChanged();
        DetachUsbDeviceCommand.NotifyCanExecuteChanged();
        _suppressUsbAutoShareToggleHandling = true;
        try
        {
            SelectedUsbDeviceAutoShareEnabled = value is not null && IsUsbAutoShareEnabledForDevice(value);
        }
        finally
        {
            _suppressUsbAutoShareToggleHandling = false;
        }

        NotifyTrayStateChanged();
    }

    partial void OnSelectedUsbDeviceAutoShareEnabledChanged(bool value)
    {
        if (_suppressUsbAutoShareToggleHandling || SelectedUsbDevice is null)
        {
            return;
        }

        var key = BuildUsbAutoShareKey(SelectedUsbDevice);
        if (string.IsNullOrWhiteSpace(key))
        {
            AddNotification("Auto-Share konnte für dieses USB-Gerät nicht gespeichert werden (kein stabiler Schlüssel).", "Warning");
            return;
        }

        var changed = false;
        if (value)
        {
            changed = _usbAutoShareDeviceKeys.Add(key);

            foreach (var aliasKey in BuildUsbIdentityAliasKeys(SelectedUsbDevice)
                         .Where(aliasKey => !string.IsNullOrWhiteSpace(aliasKey)
                                            && !string.Equals(aliasKey, key, StringComparison.OrdinalIgnoreCase))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // Remove stale alternate keys (especially broad hardware keys)
                // when a more stable key is now available.
                changed = _usbAutoShareDeviceKeys.Remove(aliasKey) || changed;
            }

            foreach (var legacyKey in BuildUsbLegacyAutoShareKeys(SelectedUsbDevice)
                         .Where(legacyKey => !string.IsNullOrWhiteSpace(legacyKey)
                                             && !string.Equals(legacyKey, key, StringComparison.OrdinalIgnoreCase))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                changed = _usbAutoShareDeviceKeys.Remove(legacyKey) || changed;
            }
        }
        else
        {
            changed = _usbAutoShareDeviceKeys.Remove(key);

            foreach (var aliasKey in BuildUsbIdentityAliasKeys(SelectedUsbDevice)
                         .Where(aliasKey => !string.IsNullOrWhiteSpace(aliasKey))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                changed = _usbAutoShareDeviceKeys.Remove(aliasKey) || changed;
            }

            foreach (var legacyKey in BuildUsbLegacyAutoShareKeys(SelectedUsbDevice))
            {
                changed = _usbAutoShareDeviceKeys.Remove(legacyKey) || changed;
            }
        }

        if (!changed)
        {
            return;
        }

        PersistUsbAutoShareConfig();
        AddNotification(value
                ? $"Auto-Share für USB-Gerät '{SelectedUsbDevice.Description}' aktiviert."
                : $"Auto-Share für USB-Gerät '{SelectedUsbDevice.Description}' deaktiviert.",
            "Info");

        if (value
            && !IsBusy
            && !SelectedUsbDevice.IsShared
            && !string.IsNullOrWhiteSpace(SelectedUsbDevice.BusId))
        {
            _ = BindSelectedUsbDeviceAsync();
        }
    }

    partial void OnSelectedVmForConfigChanged(VmDefinition? value)
    {
        RemoveVmCommand.NotifyCanExecuteChanged();
        ExportSelectedVmCommand.NotifyCanExecuteChanged();
        RenameVmAdapterCommand.NotifyCanExecuteChanged();
        UpdateVmAdapterRenameValidationState();

        _configChangeSuppressionDepth++;
        try
        {
            SelectedVmOpenConsoleWithSessionEdit = value?.OpenConsoleWithSessionEdit ?? false;
        }
        finally
        {
            _configChangeSuppressionDepth--;
        }

        _ = LoadVmAdaptersForConfigAsync(value);
    }

    partial void OnSelectedVmOpenConsoleWithSessionEditChanged(bool value)
    {
        if (SelectedVmForConfig is null)
        {
            return;
        }

        SelectedVmForConfig.OpenConsoleWithSessionEdit = value;
        MarkConfigDirty();
    }

    partial void OnSelectedVmTrayAdapterOptionChanged(VmTrayAdapterOption? value)
    {
        if (SelectedVmForConfig is null || value is null)
        {
            return;
        }

        SelectedVmForConfig.TrayAdapterName = value.AdapterName?.Trim() ?? string.Empty;
        NotifyTrayStateChanged();
        MarkConfigDirty();
    }

    partial void OnSelectedVmAdapterForRenameChanged(HyperVVmNetworkAdapterInfo? value)
    {
        UpdateVmAdapterRenameValidationState();
        RenameVmAdapterCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewVmAdapterNameChanged(string value)
    {
        UpdateVmAdapterRenameValidationState();
        RenameVmAdapterCommand.NotifyCanExecuteChanged();
    }

    private bool CanExecuteVmAction() => !IsBusy && SelectedVm is not null;

    private bool CanExecuteBindUsbAction()
    {
         return !IsBusy
                         && HostUsbSharingEnabled
             && UsbRuntimeAvailable
               && SelectedUsbDevice is not null
               && !string.IsNullOrWhiteSpace(SelectedUsbDevice.BusId)
               && !SelectedUsbDevice.IsShared;
    }

    private bool CanExecuteUnbindUsbAction()
    {
         return !IsBusy
                         && HostUsbSharingEnabled
             && UsbRuntimeAvailable
               && SelectedUsbDevice is not null
               && !string.IsNullOrWhiteSpace(SelectedUsbDevice.BusId)
               && SelectedUsbDevice.IsShared;
    }

    private bool CanExecuteDetachUsbAction()
    {
         return !IsBusy
                         && HostUsbSharingEnabled
             && UsbRuntimeAvailable
               && SelectedUsbDevice is not null
               && !string.IsNullOrWhiteSpace(SelectedUsbDevice.BusId)
               && SelectedUsbDevice.IsAttached;
    }

    private bool CanExecuteStartVmAction() => CanExecuteVmAction() && !IsRunningState(SelectedVmState);


    partial void OnUsbRuntimeAvailableChanged(bool value)
    {
        RefreshUsbDevicesCommand.NotifyCanExecuteChanged();
        BindUsbDeviceCommand.NotifyCanExecuteChanged();
        UnbindUsbDeviceCommand.NotifyCanExecuteChanged();
        DetachUsbDeviceCommand.NotifyCanExecuteChanged();

        if (!value && HostUsbSharingEnabled)
        {
            HostUsbSharingEnabled = false;
        }
    }
    private bool CanExecuteStopVmAction() => CanExecuteVmAction() && IsRunningState(SelectedVmState);

    private bool CanExecuteRestartVmAction() => CanExecuteVmAction() && IsRunningState(SelectedVmState);

    private static bool IsRunningState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        return state.Contains("Running", StringComparison.OrdinalIgnoreCase)
               || state.Contains("Läuft", StringComparison.OrdinalIgnoreCase);
    }

    private async Task InitializeAsync()
    {
        await RefreshUsbRuntimeAvailabilityAsync();
        await LoadVmsFromHyperVWithRetryAsync();
        await RefreshSwitchesAsync();
        await RefreshHostNetworkProfileAsync();
        await RefreshVmStatusAsync();
        await LoadCheckpointsAsync();

        if (UpdateCheckOnStartup)
        {
            await CheckForUpdatesAsync();
        }
    }

    public async Task RefreshUsbRuntimeAvailabilityAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await _usbIpService.GetDevicesAsync(cts.Token);

            UsbRuntimeAvailable = true;
            UsbRuntimeHintText = string.Empty;
        }
        catch (Exception ex) when (IsUsbRuntimeMissing(ex.Message))
        {
            UsbRuntimeAvailable = false;
            UsbRuntimeHintText = "USB-Funktion deaktiviert: usbipd-win ist nicht installiert. Quelle: https://github.com/dorssel/usbipd-win";
            UsbStatusText = BuildUsbUnavailableStatus(ex.Message);

            if (HostUsbSharingEnabled)
            {
                HostUsbSharingEnabled = false;
            }
        }
        catch
        {
        }
    }

    private async Task LoadVmsFromHyperVWithRetryAsync()
    {
        var retryDelays = new[] { 300, 700, 1500 };
        Exception? lastException = null;

        for (var attempt = 0; attempt <= retryDelays.Length; attempt++)
        {
            try
            {
                await LoadVmsFromHyperVAsync();

                if (StatusText.Equals("Keine Berechtigung", StringComparison.OrdinalIgnoreCase))
                {
                    lastException = new UnauthorizedAccessException("Keine Berechtigung für Hyper-V.");
                    break;
                }

                if (AvailableVms.Count > 0)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            if (attempt < retryDelays.Length)
            {
                await Task.Delay(retryDelays[attempt]);
            }
        }

        if (lastException is not null)
        {
            var message = lastException is UnauthorizedAccessException
                ? "Hyper-V Zugriff verweigert. Bitte HyperTool als Administrator starten oder Benutzerrechte für Hyper-V setzen."
                : $"Hyper-V scheint nicht verfügbar: {lastException.Message}";

            AddNotification(message, "Warning");
            StatusText = "Hyper-V nicht verfügbar";
            return;
        }

        AddNotification("Keine Hyper-V VMs gefunden. Bitte Hyper-V aktivieren/installieren.", "Warning");
        StatusText = "Keine Hyper-V VMs gefunden";
    }

    private async Task LoadVmsFromHyperVAsync()
    {
        await ExecuteBusyActionAsync("Hyper-V VMs werden geladen...", async token =>
        {
            var vms = await _hyperVService.GetVmsAsync(token);
            if (vms.Count == 0)
            {
                AvailableVms.Clear();
                SelectedVm = null;
                SelectedVmForConfig = null;
                SelectedDefaultVmForConfig = null;
                SelectedVmState = "Unbekannt";
                SelectedVmCurrentSwitch = NotConnectedSwitchDisplay;
                return;
            }

            var existingLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var existingTrayAdapters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var existingSessionEditPreference = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var configured in _configuredVmDefinitions.Values)
            {
                if (!string.IsNullOrWhiteSpace(configured.Name))
                {
                    existingLabels[configured.Name] = configured.Label;
                    existingTrayAdapters[configured.Name] = configured.TrayAdapterName;
                    existingSessionEditPreference[configured.Name] = configured.OpenConsoleWithSessionEdit;
                }
            }

            foreach (var vm in AvailableVms)
            {
                existingLabels[vm.Name] = vm.Label;
                existingTrayAdapters[vm.Name] = vm.TrayAdapterName;
                existingSessionEditPreference[vm.Name] = vm.OpenConsoleWithSessionEdit;
            }

            var orderedVms = vms.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();

            AvailableVms.Clear();
            foreach (var vmInfo in orderedVms)
            {
                var label = existingLabels.TryGetValue(vmInfo.Name, out var existingLabel) && !string.IsNullOrWhiteSpace(existingLabel)
                    ? existingLabel
                    : vmInfo.Name;

                AvailableVms.Add(new VmDefinition
                {
                    Name = vmInfo.Name,
                    VmId = vmInfo.VmId,
                    Label = label,
                    RuntimeState = vmInfo.State,
                    RuntimeSwitchName = NormalizeSwitchDisplayName(vmInfo.CurrentSwitchName),
                    HasMountedIso = vmInfo.HasMountedIso,
                    MountedIsoPath = vmInfo.MountedIsoPath,
                    TrayAdapterName = existingTrayAdapters.TryGetValue(vmInfo.Name, out var trayAdapterName) ? trayAdapterName : string.Empty,
                    OpenConsoleWithSessionEdit = existingSessionEditPreference.TryGetValue(vmInfo.Name, out var openWithSessionEdit) && openWithSessionEdit
                });
            }

            if (AvailableVms.Count > 0 && !AvailableVms.Any(vm => string.Equals(vm.Name, DefaultVmName, StringComparison.OrdinalIgnoreCase)))
            {
                DefaultVmName = AvailableVms[0].Name;
            }

            var preferredVmName = !string.IsNullOrWhiteSpace(LastSelectedVmName)
                ? LastSelectedVmName
                : DefaultVmName;

            SetSelectedVmInternal(AvailableVms.FirstOrDefault(vm => string.Equals(vm.Name, preferredVmName, StringComparison.OrdinalIgnoreCase))
                                  ?? AvailableVms.FirstOrDefault());
            SelectedVmForConfig = SelectedVm;
            SelectedDefaultVmForConfig = AvailableVms.FirstOrDefault(vm => string.Equals(vm.Name, DefaultVmName, StringComparison.OrdinalIgnoreCase))
                                       ?? SelectedVm;
            ReconcileVmMonitoringRuntimeStates();
            NotifyTrayStateChanged();

            AddNotification($"{AvailableVms.Count} Hyper-V VM(s) automatisch geladen.", "Info");
        }, showNotificationOnErrorOnly: true);
    }

    private async Task StartSelectedVmAsync()
    {
        await ExecuteBusyActionAsync("VM wird gestartet...", async token =>
        {
            await _hyperVService.StartVmAsync(SelectedVm!.Name, token);
            AddNotification($"VM '{SelectedVm.Name}' gestartet.", "Success");

            if (UiOpenConsoleAfterVmStart)
            {
                await _hyperVService.OpenVmConnectAsync(SelectedVm.Name, VmConnectComputerName, ShouldOpenConsoleWithSessionEdit(SelectedVm.Name), token);
                AddNotification($"Konsole für '{SelectedVm.Name}' geöffnet.", "Info");
            }
        });
        await RefreshVmStatusAsync();
    }

    private async Task StopSelectedVmAsync()
    {
        await ExecuteBusyActionAsync("VM wird gestoppt...", async token =>
        {
            await _hyperVService.StopVmGracefulAsync(SelectedVm!.Name, token);
            AddNotification($"VM '{SelectedVm.Name}' graceful gestoppt.", "Success");
        });
        await RefreshVmStatusAsync();
    }

    private async Task TurnOffSelectedVmAsync()
    {
        await ExecuteBusyActionAsync("VM wird hart ausgeschaltet...", async token =>
        {
            await _hyperVService.TurnOffVmAsync(SelectedVm!.Name, token);
            AddNotification($"VM '{SelectedVm.Name}' hart ausgeschaltet.", "Warning");
        });
        await RefreshVmStatusAsync();
    }

    private async Task RestartSelectedVmAsync()
    {
        await ExecuteBusyActionAsync("VM wird neu gestartet...", async token =>
        {
            await _hyperVService.RestartVmAsync(SelectedVm!.Name, token);
            AddNotification($"VM '{SelectedVm.Name}' neu gestartet.", "Success");
        });
        await RefreshVmStatusAsync();
    }

    private async Task OpenConsoleAsync()
    {
        await ExecuteBusyActionAsync("vmconnect wird geöffnet...", async token =>
        {
            await _hyperVService.OpenVmConnectAsync(SelectedVm!.Name, VmConnectComputerName, ShouldOpenConsoleWithSessionEdit(SelectedVm!.Name), token);
            AddNotification($"Konsole für '{SelectedVm.Name}' geöffnet.", "Info");
        });
    }

    private async Task ReopenConsoleWithSessionEditAsync()
    {
        if (SelectedVm is null)
        {
            return;
        }

        var vmName = SelectedVm.Name;

        await ExecuteBusyActionAsync("Konsole wird mit Sitzungsbearbeitung neu aufgebaut...", async token =>
        {
            await _hyperVService.ReopenVmConnectWithSessionEditAsync(vmName, VmConnectComputerName, token);
            AddNotification($"Konsole für '{vmName}' wurde mit Sitzungsbearbeitung neu aufgebaut.", "Info");
        });
    }

    private async Task ExportSelectedVmAsync()
    {
        if (SelectedVmForConfig is null)
        {
            return;
        }

        var vmName = SelectedVmForConfig.Name;

        try
        {
            var selectedFolder = PickFolderPath($"Zielordner für Backup-Export von '{vmName}' auswählen");
            if (string.IsNullOrWhiteSpace(selectedFolder))
            {
                AddNotification("VM-Export abgebrochen.", "Info");
                return;
            }

            var exportPath = Path.Combine(selectedFolder, $"{vmName}-{DateTime.Now:yyyyMMdd-HHmmss}");

            var spaceCheck = await _hyperVService.CheckExportDiskSpaceAsync(vmName, exportPath, _lifetimeCancellation.Token);
            if (!spaceCheck.HasEnoughSpace)
            {
                AddNotification(
                    $"Zu wenig Speicherplatz auf {spaceCheck.TargetDrive}: benötigt {FormatByteSize(spaceCheck.RequiredBytes)}, verfügbar {FormatByteSize(spaceCheck.AvailableBytes)}.",
                    "Error");
                return;
            }

            Directory.CreateDirectory(exportPath);

            var hasReliableProgress = false;
            var progress = new Progress<int>(percent =>
            {
                var clampedPercent = Math.Clamp(percent, 0, 100);

                if (!hasReliableProgress)
                {
                    if (clampedPercent < 2)
                    {
                        BusyProgressPercent = -1;
                        BusyText = $"VM '{vmName}' wird exportiert... (Fortschritt wird von Hyper-V ermittelt)";
                        return;
                    }

                    hasReliableProgress = true;
                }

                BusyProgressPercent = clampedPercent;
                BusyText = $"VM '{vmName}' wird exportiert... {clampedPercent}%";
            });

            await ExecuteBusyActionAsync($"VM '{vmName}' wird exportiert...", async token =>
            {
                BusyProgressPercent = -1;
                BusyText = $"VM '{vmName}' wird exportiert... (Fortschritt wird von Hyper-V ermittelt)";
                await _hyperVService.ExportVmAsync(vmName, exportPath, progress, token);
                AddNotification($"VM '{vmName}' exportiert nach: {exportPath}", "Success");
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "VM export failed for {VmName}.", vmName);
            AddNotification($"VM-Export fehlgeschlagen: {ex.Message}", "Error");
        }
    }

    private async Task ImportVmAsync()
    {
        var importPath = PickFolderPath("Schritt 1/2: Ordner der zu importierenden Hyper-V VM auswählen");
        if (string.IsNullOrWhiteSpace(importPath))
        {
            AddNotification("VM-Import abgebrochen.", "Info");
            return;
        }

        try
        {
            var hasVmConfig = Directory.EnumerateFiles(importPath, "*.vmcx", SearchOption.AllDirectories).Any()
                              || Directory.EnumerateFiles(importPath, "*.xml", SearchOption.AllDirectories).Any();

            if (!hasVmConfig)
            {
                AddNotification("Im gewählten Quellordner wurde keine Hyper-V Konfigurationsdatei (.vmcx/.xml) gefunden.", "Error");
                return;
            }
        }
        catch (Exception ex)
        {
            AddNotification($"Quellordner konnte nicht geprüft werden: {ex.Message}", "Error");
            return;
        }

        var importMode = (SelectedVmImportModeOption?.Key ?? "copy").Trim().ToLowerInvariant();
        var isCopyMode = string.Equals(importMode, "copy", StringComparison.OrdinalIgnoreCase);
        var destinationPath = (DefaultVmImportDestinationPath ?? string.Empty).Trim();

        if (isCopyMode && string.IsNullOrWhiteSpace(destinationPath))
        {
            destinationPath = PickFolderPath("Schritt 2/2: Zielordner für den VM-Import (Kopieren) auswählen")?.Trim() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(destinationPath))
            {
                DefaultVmImportDestinationPath = destinationPath;
                AddNotification("Import-Zielordner für künftige VM-Imports gesetzt.", "Info");
            }
        }

        if (isCopyMode && string.IsNullOrWhiteSpace(destinationPath))
        {
            AddNotification("VM-Import abgebrochen (kein Zielordner ausgewählt).", "Info");
            return;
        }

        var progress = new Progress<int>(percent =>
        {
            var clampedPercent = Math.Clamp(percent, 0, 100);

            BusyProgressPercent = -1;
            BusyText = clampedPercent >= 100
                ? "VM-Import wird abgeschlossen..."
                : "VM wird importiert... (Fortschritt wird von Hyper-V ermittelt)";
        });

        var requestedVmName = string.IsNullOrWhiteSpace(ImportVmRequestedName) ? null : ImportVmRequestedName.Trim();
        var requestedFolderName = requestedVmName;

        AddNotification($"VM-Import gestartet · Modus: {SelectedVmImportModeOption?.Label ?? importMode}", "Info");

        await ExecuteBusyActionAsync("VM wird importiert...", async token =>
        {
            BusyProgressPercent = -1;
            BusyText = "VM wird importiert... (Fortschritt wird von Hyper-V ermittelt)";

            var importResult = await _hyperVService.ImportVmAsync(
                importPath,
                destinationPath,
                requestedVmName,
                requestedFolderName,
                importMode,
                progress,
                token);

            var modeLabel = SelectedVmImportModeOption?.Label ?? importResult.ImportMode;
            var destinationInfo = string.IsNullOrWhiteSpace(importResult.DestinationFolderPath)
                ? (isCopyMode ? destinationPath : "aktueller Speicherort (keine Kopie)")
                : importResult.DestinationFolderPath;

            AddNotification($"VM '{importResult.VmName}' importiert ({modeLabel}) · Ziel: {destinationInfo}", "Success");

            if (importResult.RenamedDueToConflict
                && !string.Equals(importResult.OriginalName, importResult.VmName, StringComparison.OrdinalIgnoreCase))
            {
                AddNotification($"Namenskonflikt erkannt: '{importResult.OriginalName}' wurde automatisch zu '{importResult.VmName}' umbenannt.", "Warning");
            }

            if (importResult.SwitchFallbackAdjustedAdapters > 0)
            {
                if (string.Equals(importResult.SwitchFallbackMode, "default", StringComparison.OrdinalIgnoreCase))
                {
                    AddNotification(
                        $"Import-Netzwerk angepasst: {importResult.SwitchFallbackAdjustedAdapters} Adapter auf 'Default Switch' umgestellt.",
                        "Info");
                }
                else if (string.Equals(importResult.SwitchFallbackMode, "disconnected", StringComparison.OrdinalIgnoreCase))
                {
                    AddNotification(
                        $"Import-Netzwerk angepasst: {importResult.SwitchFallbackAdjustedAdapters} Adapter ohne verfügbaren Switch getrennt belassen.",
                        "Warning");
                }
                else if (string.Equals(importResult.SwitchFallbackMode, "unresolved", StringComparison.OrdinalIgnoreCase))
                {
                    AddNotification(
                        $"Import-Netzwerk nur teilweise angepasst ({importResult.SwitchFallbackAdjustedAdapters} Adapter). Bitte VM-Netzwerk manuell prüfen.",
                        "Warning");
                }
            }
        });

        await LoadVmsFromHyperVAsync();
        await RefreshVmStatusAsync();
    }

    private async Task RefreshSwitchesAsync()
    {
        AreSwitchesLoaded = false;

        await ExecuteBusyActionAsync("Switches werden geladen...", async token =>
        {
            var switches = await _hyperVService.GetVmSwitchesAsync(token);

            AvailableSwitches.Clear();
            foreach (var vmSwitch in switches.OrderBy(item => item.Name))
            {
                AvailableSwitches.Add(vmSwitch);
            }

            AreSwitchesLoaded = true;
            SyncSelectedSwitchWithCurrentVm(showNotificationOnMissingSwitch: true);
            NotifyTrayStateChanged();

            AddNotification($"{AvailableSwitches.Count} Switch(es) geladen.", "Info");
        });
    }

    private async Task RefreshRuntimeDataAsync()
    {
        await LoadVmsFromHyperVWithRetryAsync();
        await RefreshSwitchesAsync();
        await RefreshHostNetworkProfileAsync();
        await RefreshVmStatusAsync();
    }

    private async Task RefreshHostNetworkProfileAsync()
    {
        try
        {
            var profileCategory = await _hyperVService.GetHostNetworkProfileCategoryAsync(_lifetimeCancellation.Token);
            ApplyHostNetworkProfileCategory(profileCategory);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Host-Netzprofil konnte nicht ermittelt werden.");
            ApplyHostNetworkProfileCategory("Unknown");
        }
    }

    private void ApplyHostNetworkProfileCategory(string? category)
    {
        HostNetworkProfileCategory = NormalizeHostNetworkProfileCategory(category);

        HostNetworkProfileDisplayText = HostNetworkProfileCategory switch
        {
            "Public" => "Host-Netzprofil: Öffentlich (Default Switch oft problematisch)",
            "DomainAuthenticated" => "Host-Netzprofil: Domäne",
            "Private" => "Host-Netzprofil: Privat",
            _ => "Host-Netzprofil: Unbekannt"
        };
    }

    private static string NormalizeHostNetworkProfileCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return "Unknown";
        }

        return category.Trim() switch
        {
            "Public" => "Public",
            "Private" => "Private",
            "DomainAuthenticated" => "DomainAuthenticated",
            _ => "Unknown"
        };
    }

    private async Task RefreshUsbDevicesAsync()
    {
        if (!HostUsbSharingEnabled)
        {
            UsbDevices.Clear();
            SelectedUsbDevice = null;
            UsbStatusText = "USB Share ist global im Host deaktiviert.";
            AddNotification(UsbStatusText, "Info");
            return;
        }

        if (!UsbRuntimeAvailable)
        {
            UsbStatusText = string.IsNullOrWhiteSpace(UsbRuntimeHintText)
                ? "USB ist deaktiviert. usbipd-win ist nicht installiert."
                : UsbRuntimeHintText;
            AddNotification(UsbStatusText, "Warning");
            return;
        }

        await LoadUsbDevicesAsync(showNotification: true);
    }

    private async Task LoadUsbDevicesAsync(bool showNotification, bool applyAutoShare = true, bool useBusyIndicator = true)
    {
        if (!HostUsbSharingEnabled)
        {
            UsbDevices.Clear();
            SelectedUsbDevice = null;
            UsbStatusText = "USB Share ist global im Host deaktiviert.";
            UsbRuntimeHintText = string.Empty;

            if (showNotification)
            {
                AddNotification(UsbStatusText, "Info");
            }

            NotifyTrayStateChanged();
            return;
        }

        var hadPreviousSelection = SelectedUsbDevice is not null;
        var previouslySelectedSelectionKey = hadPreviousSelection
            ? BuildUsbSelectionKey(SelectedUsbDevice!)
            : string.Empty;
        var previousUsbSnapshot = UsbDevices.ToList();
        UsbStatusText = "USB-Geräte werden geladen...";

        async Task loadAction(CancellationToken token)
        {
            IReadOnlyList<UsbIpDeviceInfo> devices;
            try
            {
                devices = await _usbIpService.GetDevicesAsync(token);

                var staleDetachedCount = await TryDetachStaleDisconnectedAttachmentsAsync(devices, token);
                if (staleDetachedCount > 0)
                {
                    devices = await _usbIpService.GetDevicesAsync(token);
                }

                var canApplyAutoShareNow = applyAutoShare;

                var autoShareCandidates = canApplyAutoShareNow
                    ? devices
                        .Where(device =>
                            !string.IsNullOrWhiteSpace(device.BusId)
                            && !device.IsShared
                            && !device.IsAttached
                            && IsUsbAutoShareEnabledForDevice(device))
                        .ToList()
                    : [];

                if (autoShareCandidates.Count > 0)
                {
                    var autoShareApplied = 0;
                    var autoShareFailed = 0;

                    foreach (var candidate in autoShareCandidates)
                    {
                        try
                        {
                            await _usbIpService.BindAsync(candidate.BusId, force: false, token);
                            autoShareApplied++;
                        }
                        catch (Exception ex)
                        {
                            autoShareFailed++;
                            Log.Warning(ex, "Auto-Share für USB-Gerät {BusId} fehlgeschlagen.", candidate.BusId);
                        }
                    }

                    devices = await _usbIpService.GetDevicesAsync(token);

                    if (autoShareApplied > 0)
                    {
                        AddNotification($"Auto-Share: {autoShareApplied} USB-Gerät(e) automatisch freigegeben.", "Info");
                    }

                    if (autoShareFailed > 0)
                    {
                        AddNotification($"Auto-Share: {autoShareFailed} USB-Gerät(e) konnten nicht freigegeben werden.", "Warning");
                    }
                }

                if (UsbAutoDetachOnClientDisconnect)
                {
                    var detachedNoVmCount = await TryDetachLoopbackAttachedDevicesWhenNoVmRunningAsync(devices, token);
                    if (detachedNoVmCount > 0)
                    {
                        devices = await _usbIpService.GetDevicesAsync(token);
                    }
                }
                else
                {
                    _usbAttachedWithoutAckSinceUtc.Clear();
                    _usbAttachedWithoutAckAttempts.Clear();
                    _usbVmNotRunningSinceUtc.Clear();
                    _usbVmOffDetachManualRequiredBusIds.Clear();
                }

                await TryUnshareRemovedHostDevicesAsync(previousUsbSnapshot, devices, token);
            }
            catch (Exception ex)
            {
                UsbDevices.Clear();
                SelectedUsbDevice = null;
                UsbStatusText = BuildUsbUnavailableStatus(ex.Message);

                if (IsUsbRuntimeMissing(ex.Message))
                {
                    UsbRuntimeAvailable = false;
                    UsbRuntimeHintText = "USB-Funktion deaktiviert: usbipd-win ist nicht installiert. Quelle: https://github.com/dorssel/usbipd-win";
                }

                NotifyTrayStateChanged();

                if (showNotification)
                {
                    AddNotification(UsbStatusText, "Warning");
                }

                return;
            }

            var preparedDevices = devices
                .OrderBy(GetUsbStatusSortRank)
                .ThenBy(device => device.BusId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(device => device.Description, StringComparer.OrdinalIgnoreCase)
                .ToList();
            TryMigrateLegacyUsbIdentityKeys(preparedDevices);
            foreach (var device in preparedDevices)
            {
                device.DeviceIdentityKey = BuildUsbDeviceIdentityKey(device);
                ApplyUsbDeviceMetadata(device);

                var hasFreshVmId = false;
                if (device.IsAttached
                    && UsbGuestConnectionRegistry.TryGetFreshGuestVmId(device, GuestAckChannelHealthyWindow, out var sourceVmId))
                {
                    hasFreshVmId = true;
                    var vmNameById = ResolveVmNameByVmId(sourceVmId);
                    device.AttachedGuestComputerName = !string.IsNullOrWhiteSpace(vmNameById)
                        ? vmNameById
                        : sourceVmId;
                    continue;
                }

                if (device.IsAttached
                    && !hasFreshVmId
                    && !string.IsNullOrWhiteSpace(device.BusId)
                    && UsbGuestConnectionRegistry.TryGetFreshGuestComputerName(device, GuestAckChannelHealthyWindow, out var guestComputerName))
                {
                    device.AttachedGuestComputerName = guestComputerName;
                }
            }

            if (!UsbDeviceListsMatch(UsbDevices, preparedDevices))
            {
                if (UsbDeviceIdentityOrderMatch(UsbDevices, preparedDevices))
                {
                    for (var i = 0; i < preparedDevices.Count; i++)
                    {
                        UsbDevices[i] = preparedDevices[i];
                    }
                }
                else
                {
                    UsbDevices.Clear();
                    foreach (var device in preparedDevices)
                    {
                        UsbDevices.Add(device);
                    }
                }
            }

            var restoredSelection = UsbDevices.FirstOrDefault(device =>
                !string.IsNullOrWhiteSpace(previouslySelectedSelectionKey)
                && string.Equals(BuildUsbSelectionKey(device), previouslySelectedSelectionKey, StringComparison.OrdinalIgnoreCase));

            SelectedUsbDevice = hadPreviousSelection ? restoredSelection : null;

            UsbStatusText = UsbDevices.Count == 0
                ? "Keine USB-Geräte gefunden."
                : $"{UsbDevices.Count} USB-Gerät(e) geladen.";

            UsbRuntimeAvailable = true;
            UsbRuntimeHintText = string.Empty;

            NotifyTrayStateChanged();

            if (showNotification)
            {
                AddNotification(UsbStatusText, UsbDevices.Count == 0 ? "Warning" : "Info");
            }
        }

        if (useBusyIndicator)
        {
            await ExecuteBusyActionAsync("USB-Geräte werden geladen...", loadAction, showNotificationOnErrorOnly: true);
        }
        else
        {
            await loadAction(_lifetimeCancellation.Token);
        }
    }

    private async Task<int> TryUnshareRemovedHostDevicesAsync(
        IReadOnlyList<UsbIpDeviceInfo> previousDevices,
        IReadOnlyList<UsbIpDeviceInfo> currentDevices,
        CancellationToken token)
    {
        if (previousDevices.Count == 0)
        {
            return 0;
        }

        var currentIdentityKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var current in currentDevices)
        {
            var key = BuildUsbDeviceIdentityKey(current);
            if (!string.IsNullOrWhiteSpace(key))
            {
                currentIdentityKeys.Add(key);
            }

            foreach (var aliasKey in BuildUsbIdentityAliasKeys(current))
            {
                if (!string.IsNullOrWhiteSpace(aliasKey))
                {
                    currentIdentityKeys.Add(aliasKey);
                }
            }
        }

        var removedSharedDevices = previousDevices
            .Where(device => device is not null && (device.IsShared || device.IsAttached))
            .Where(device =>
            {
                var identityKeys = BuildUsbIdentityAliasKeys(device)
                    .Where(aliasKey => !string.IsNullOrWhiteSpace(aliasKey))
                    .ToList();

                var primaryKey = BuildUsbDeviceIdentityKey(device);
                if (!string.IsNullOrWhiteSpace(primaryKey))
                {
                    identityKeys.Add(primaryKey);
                }

                if (identityKeys.Count == 0)
                {
                    return false;
                }

                return !identityKeys.Any(currentIdentityKeys.Contains);
            })
            .ToList();

        if (removedSharedDevices.Count == 0)
        {
            return 0;
        }

        var releasedCount = 0;
        foreach (var removed in removedSharedDevices)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(removed.PersistedGuid))
                {
                    await _usbIpService.UnbindByPersistedGuidAsync(removed.PersistedGuid.Trim(), token);
                    releasedCount++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(removed.BusId))
                {
                    await _usbIpService.UnbindAsync(removed.BusId.Trim(), token);
                    releasedCount++;
                }
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                return releasedCount;
            }
            catch (Exception ex)
            {
                if (IsBenignUsbUnshareCleanupFailure(ex))
                {
                    continue;
                }

                Log.Debug(ex,
                    "USB share cleanup for removed device failed. BusId={BusId}; Guid={Guid}",
                    removed.BusId,
                    removed.PersistedGuid);
            }
        }

        if (releasedCount > 0)
        {
            Log.Information("Released share for removed USB devices. Count={Count}", releasedCount);
        }

        return releasedCount;
    }

    private async Task<int> TryDetachStaleDisconnectedAttachmentsAsync(IReadOnlyList<UsbIpDeviceInfo> devices, CancellationToken token)
    {
        if (devices.Count == 0)
        {
            return 0;
        }

        var staleAttachedDevices = devices
            .Where(device => device.IsAttached
                             && !device.IsConnected
                             && !string.IsNullOrWhiteSpace(device.BusId))
            .ToList();

        if (staleAttachedDevices.Count == 0)
        {
            return 0;
        }

        var detachedCount = 0;
        foreach (var device in staleAttachedDevices)
        {
            var busId = device.BusId.Trim();
            try
            {
                await _usbIpService.DetachAsync(busId, token);
                detachedCount++;
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                return detachedCount;
            }
            catch (Exception ex) when (IsUsbDetachNoOpError(ex))
            {
                detachedCount++;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "USB stale-attach cleanup detach failed. BusId={BusId}", busId);
            }
        }

        if (detachedCount > 0)
        {
            Log.Information(
                "Detached stale USB attachments for physically disconnected devices. Count={Count}",
                detachedCount);
        }

        return detachedCount;
    }

    private static int GetUsbStatusSortRank(UsbIpDeviceInfo device)
    {
        if (device is null)
        {
            return 4;
        }

        if (device.IsAttached || device.IsAttachedByOtherGuest)
        {
            return 0;
        }

        if (device.IsShared)
        {
            return 1;
        }

        if (device.IsConnected)
        {
            return 2;
        }

        return 3;
    }

    private static bool IsBenignUsbUnshareCleanupFailure(Exception ex)
    {
        if (ex is not InvalidOperationException)
        {
            return false;
        }

        var message = ex.Message ?? string.Empty;
        return message.Contains("konnte nicht entfernt werden", StringComparison.OrdinalIgnoreCase)
               && message.Contains("ExitCode=1", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> TryDetachLoopbackAttachedDevicesWhenNoVmRunningAsync(IReadOnlyList<UsbIpDeviceInfo> devices, CancellationToken token)
    {
        if (devices.Count == 0)
        {
            _usbVmNotRunningSinceUtc.Clear();
            _usbVmOffDetachManualRequiredBusIds.Clear();
            return 0;
        }

        var loopbackAttachedDevices = devices
            .Where(device => device.IsAttached
                             && !string.IsNullOrWhiteSpace(device.BusId)
                             && string.Equals(device.ClientIpAddress?.Trim(), HyperVSocketUsbTunnelDefaults.LoopbackAddress, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (loopbackAttachedDevices.Count == 0)
        {
            _usbVmNotRunningSinceUtc.Clear();
            _usbVmOffDetachManualRequiredBusIds.Clear();
            return 0;
        }

        IReadOnlyList<HyperVVmInfo> runtimeVms;
        try
        {
            runtimeVms = await _hyperVService.GetVmsAsync(token);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not refresh VM runtime states before VM-off USB detach evaluation.");
            return 0;
        }

        var runningVmIds = runtimeVms
            .Where(vm => IsRunningState(vm.State) && !string.IsNullOrWhiteSpace(vm.VmId))
            .Select(vm => vm.VmId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var activeLoopbackBusIds = loopbackAttachedDevices
            .Select(device => device.BusId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var obsoleteBusId in _usbVmNotRunningSinceUtc.Keys.Where(key => !activeLoopbackBusIds.Contains(key)).ToList())
        {
            _usbVmNotRunningSinceUtc.Remove(obsoleteBusId);
            _usbVmOffDetachManualRequiredBusIds.Remove(obsoleteBusId);
        }

        var now = DateTimeOffset.UtcNow;
        var detachedCount = 0;

        foreach (var device in loopbackAttachedDevices)
        {
            var busId = device.BusId.Trim();

            if (!UsbGuestConnectionRegistry.TryGetGuestVmId(busId, out var sourceVmId)
                || string.IsNullOrWhiteSpace(sourceVmId))
            {
                _usbVmNotRunningSinceUtc.Remove(busId);
                _usbVmOffDetachManualRequiredBusIds.Remove(busId);
                continue;
            }

            if (runningVmIds.Contains(sourceVmId))
            {
                _usbVmNotRunningSinceUtc.Remove(busId);
                _usbVmOffDetachManualRequiredBusIds.Remove(busId);
                continue;
            }

            if (_usbVmOffDetachManualRequiredBusIds.Contains(busId))
            {
                continue;
            }

            if (!_usbVmNotRunningSinceUtc.TryGetValue(busId, out var vmNotRunningSinceUtc))
            {
                _usbVmNotRunningSinceUtc[busId] = now;
                continue;
            }

            if ((now - vmNotRunningSinceUtc) < VmAutoDetachDelayAfterVmStop)
            {
                continue;
            }

            try
            {
                var detached = await TryDetachBusWithRetryAsync(
                    busId,
                    initialDelay: TimeSpan.Zero,
                    token,
                    context: "vm-not-running");

                if (!detached)
                {
                    _usbVmNotRunningSinceUtc.Remove(busId);
                    _usbVmOffDetachManualRequiredBusIds.Add(busId);
                    continue;
                }

                _usbVmNotRunningSinceUtc.Remove(busId);
                _usbVmOffDetachManualRequiredBusIds.Remove(busId);
                detachedCount++;

                Log.Information(
                    "USB auto-detach executed because owning VM is not running for {DelaySeconds}s. BusId={BusId}; SourceVmId={SourceVmId}",
                    (int)VmAutoDetachDelayAfterVmStop.TotalSeconds,
                    busId,
                    sourceVmId);
            }
            catch (Exception ex)
            {
                _usbVmNotRunningSinceUtc.Remove(busId);
                _usbVmOffDetachManualRequiredBusIds.Add(busId);
                Log.Warning(
                    ex,
                    "USB auto-detach after VM stop failed; manual detach/unshare required. BusId={BusId}; SourceVmId={SourceVmId}",
                    busId,
                    sourceVmId);
            }
        }

        return detachedCount;
    }

    private static bool UsbDeviceListsMatch(IList<UsbIpDeviceInfo> current, IReadOnlyList<UsbIpDeviceInfo> next)
    {
        if (current.Count != next.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!UsbDeviceVisualMatch(current[i], next[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool UsbDeviceIdentityOrderMatch(IList<UsbIpDeviceInfo> current, IReadOnlyList<UsbIpDeviceInfo> next)
    {
        if (current.Count != next.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!string.Equals(BuildUsbSelectionKey(current[i]), BuildUsbSelectionKey(next[i]), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool UsbDeviceVisualMatch(UsbIpDeviceInfo left, UsbIpDeviceInfo right)
    {
        return string.Equals(left.BusId?.Trim(), right.BusId?.Trim(), StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.Description?.Trim(), right.Description?.Trim(), StringComparison.Ordinal)
               && string.Equals(left.HardwareId?.Trim(), right.HardwareId?.Trim(), StringComparison.OrdinalIgnoreCase)
             && string.Equals(left.HardwareIdentityKey?.Trim(), right.HardwareIdentityKey?.Trim(), StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.InstanceId?.Trim(), right.InstanceId?.Trim(), StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.PersistedGuid?.Trim(), right.PersistedGuid?.Trim(), StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.ClientIpAddress?.Trim(), right.ClientIpAddress?.Trim(), StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.AttachedGuestComputerName?.Trim(), right.AttachedGuestComputerName?.Trim(), StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.DeviceIdentityKey?.Trim(), right.DeviceIdentityKey?.Trim(), StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.CustomName?.Trim(), right.CustomName?.Trim(), StringComparison.Ordinal)
               && string.Equals(left.CustomComment?.Trim(), right.CustomComment?.Trim(), StringComparison.Ordinal);
    }

    private static string BuildUsbUnavailableStatus(string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return "USB nicht verfügbar (usbipd fehlt oder Dienst läuft nicht).";
        }

        if (details.Contains("nicht installiert", StringComparison.OrdinalIgnoreCase)
            || details.Contains("nicht gefunden", StringComparison.OrdinalIgnoreCase)
            || details.Contains("winget", StringComparison.OrdinalIgnoreCase))
        {
            return "USB nicht verfügbar: usbipd-win fehlt. Optional im Setup installieren oder manuell von https://github.com/dorssel/usbipd-win";
        }

        if (details.Contains("Dienst", StringComparison.OrdinalIgnoreCase)
            || details.Contains("service", StringComparison.OrdinalIgnoreCase))
        {
            return "USB nicht verfügbar: usbipd-Dienst läuft nicht.";
        }

        return "USB nicht verfügbar. Details im Log/Benachrichtigung prüfen.";
    }

    private static bool IsUsbRuntimeMissing(string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return false;
        }

        return details.Contains("nicht installiert", StringComparison.OrdinalIgnoreCase)
               || details.Contains("nicht gefunden", StringComparison.OrdinalIgnoreCase)
               || details.Contains("usbipd-win", StringComparison.OrdinalIgnoreCase)
               || details.Contains("usbipd.exe", StringComparison.OrdinalIgnoreCase)
               || details.Contains("winget", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildUsbDeviceIdentityKey(UsbIpDeviceInfo device)
    {
        var hardwareId = GetUsbHardwareIdentityCandidate(device);
        if (!string.IsNullOrWhiteSpace(hardwareId) && IsPreciseUsbHardwareIdentity(hardwareId))
        {
            return "hardware:" + hardwareId;
        }

        if (!string.IsNullOrWhiteSpace(device.InstanceId))
        {
            return "instance:" + device.InstanceId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(device.PersistedGuid))
        {
            return "guid:" + device.PersistedGuid.Trim();
        }

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
        var hardwareId = GetUsbHardwareIdentityCandidate(device);
        if (!string.IsNullOrWhiteSpace(hardwareId))
        {
            yield return "hardware:" + hardwareId;
        }

        var fallbackHardwareId = NormalizeUsbHardwareId(device.HardwareId);
        if (!string.IsNullOrWhiteSpace(fallbackHardwareId)
            && !string.Equals(fallbackHardwareId, hardwareId, StringComparison.OrdinalIgnoreCase))
        {
            yield return "hardware:" + fallbackHardwareId;
        }

        if (!string.IsNullOrWhiteSpace(device.InstanceId))
        {
            yield return "instance:" + device.InstanceId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(device.PersistedGuid))
        {
            yield return "guid:" + device.PersistedGuid.Trim();
        }

    }

    private static string BuildUsbAutoShareKey(UsbIpDeviceInfo device)
    {
        return BuildUsbDeviceIdentityKey(device);
    }

    private static IEnumerable<string> BuildUsbLegacyAutoShareKeys(UsbIpDeviceInfo device)
    {
        if (!string.IsNullOrWhiteSpace(device.BusId))
        {
            yield return "busid:" + device.BusId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(device.InstanceId))
        {
            yield return "instance:" + device.InstanceId.Trim();
        }

        var hardwareId = NormalizeUsbHardwareId(device.HardwareId);
        if (!string.IsNullOrWhiteSpace(hardwareId))
        {
            yield return "hardware:" + hardwareId;
        }

        var preciseHardwareId = NormalizeUsbHardwareId(device.HardwareIdentityKey);
        if (!string.IsNullOrWhiteSpace(preciseHardwareId)
            && !string.Equals(preciseHardwareId, hardwareId, StringComparison.OrdinalIgnoreCase))
        {
            yield return "hardware:" + preciseHardwareId;
        }

    }

    private void TryMigrateLegacyUsbIdentityKeys(IReadOnlyList<UsbIpDeviceInfo> devices)
    {
        var hasLegacyBusIdKeys = _usbAutoShareDeviceKeys.Any(key => IsLegacyBusIdKey(key))
                                 || _usbDeviceMetadataByKey.Keys.Any(key => IsLegacyBusIdKey(key));
        var hasLegacyGuidKeys = _usbAutoShareDeviceKeys.Any(key => IsLegacyGuidKey(key))
                                || _usbDeviceMetadataByKey.Keys.Any(key => IsLegacyGuidKey(key));

        if (_usbHardwareIdentityMigrationCompleted && !hasLegacyBusIdKeys && !hasLegacyGuidKeys)
        {
            return;
        }

        if (!hasLegacyBusIdKeys && !hasLegacyGuidKeys)
        {
            _usbHardwareIdentityMigrationCompleted = true;
            PersistUsbAutoShareConfig();
            return;
        }

        var hardwareByBusIdKey = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.BusId))
            .Select(device => new
            {
                BusKey = "busid:" + device.BusId.Trim(),
                HardwareKey = BuildUsbHardwareIdentityKey(device)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.HardwareKey))
            .ToDictionary(item => item.BusKey, item => item.HardwareKey, StringComparer.OrdinalIgnoreCase);

        var preferredIdentityByGuidKey = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.PersistedGuid))
            .Select(device => new
            {
                GuidKey = "guid:" + device.PersistedGuid.Trim(),
                PreferredKey = BuildPreferredUsbMigrationKey(device)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.PreferredKey)
                           && !string.Equals(item.GuidKey, item.PreferredKey, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(item => item.GuidKey, item => item.PreferredKey, StringComparer.OrdinalIgnoreCase);

        if (hardwareByBusIdKey.Count == 0 && preferredIdentityByGuidKey.Count == 0)
        {
            return;
        }

        var changed = false;

        foreach (var legacyKey in _usbAutoShareDeviceKeys.Where(IsLegacyBusIdKey).ToList())
        {
            if (!hardwareByBusIdKey.TryGetValue(legacyKey, out var hardwareKey))
            {
                continue;
            }

            changed = _usbAutoShareDeviceKeys.Remove(legacyKey) || changed;
            changed = _usbAutoShareDeviceKeys.Add(hardwareKey) || changed;
        }

        foreach (var legacyKey in _usbAutoShareDeviceKeys.Where(IsLegacyGuidKey).ToList())
        {
            if (!preferredIdentityByGuidKey.TryGetValue(legacyKey, out var mappedKey))
            {
                continue;
            }

            changed = _usbAutoShareDeviceKeys.Remove(legacyKey) || changed;
            changed = _usbAutoShareDeviceKeys.Add(mappedKey) || changed;
        }

        foreach (var legacyKey in _usbDeviceMetadataByKey.Keys.Where(IsLegacyBusIdKey).ToList())
        {
            if (!hardwareByBusIdKey.TryGetValue(legacyKey, out var hardwareKey)
                || !_usbDeviceMetadataByKey.TryGetValue(legacyKey, out var legacyMetadata))
            {
                continue;
            }

            if (_usbDeviceMetadataByKey.TryGetValue(hardwareKey, out var existingMetadata))
            {
                var mergedName = string.IsNullOrWhiteSpace(existingMetadata.CustomName)
                    ? legacyMetadata.CustomName
                    : existingMetadata.CustomName;
                var mergedComment = string.IsNullOrWhiteSpace(existingMetadata.Comment)
                    ? legacyMetadata.Comment
                    : existingMetadata.Comment;

                _usbDeviceMetadataByKey[hardwareKey] = new UsbDeviceMetadataEntry
                {
                    DeviceKey = hardwareKey,
                    CustomName = (mergedName ?? string.Empty).Trim(),
                    Comment = (mergedComment ?? string.Empty).Trim()
                };
            }
            else
            {
                _usbDeviceMetadataByKey[hardwareKey] = new UsbDeviceMetadataEntry
                {
                    DeviceKey = hardwareKey,
                    CustomName = (legacyMetadata.CustomName ?? string.Empty).Trim(),
                    Comment = (legacyMetadata.Comment ?? string.Empty).Trim()
                };
            }

            changed = _usbDeviceMetadataByKey.Remove(legacyKey) || changed;
        }

        foreach (var legacyKey in _usbDeviceMetadataByKey.Keys.Where(IsLegacyGuidKey).ToList())
        {
            if (!preferredIdentityByGuidKey.TryGetValue(legacyKey, out var mappedKey)
                || !_usbDeviceMetadataByKey.TryGetValue(legacyKey, out var legacyMetadata))
            {
                continue;
            }

            if (_usbDeviceMetadataByKey.TryGetValue(mappedKey, out var existingMetadata))
            {
                var mergedName = string.IsNullOrWhiteSpace(existingMetadata.CustomName)
                    ? legacyMetadata.CustomName
                    : existingMetadata.CustomName;
                var mergedComment = string.IsNullOrWhiteSpace(existingMetadata.Comment)
                    ? legacyMetadata.Comment
                    : existingMetadata.Comment;

                _usbDeviceMetadataByKey[mappedKey] = new UsbDeviceMetadataEntry
                {
                    DeviceKey = mappedKey,
                    CustomName = (mergedName ?? string.Empty).Trim(),
                    Comment = (mergedComment ?? string.Empty).Trim()
                };
            }
            else
            {
                _usbDeviceMetadataByKey[mappedKey] = new UsbDeviceMetadataEntry
                {
                    DeviceKey = mappedKey,
                    CustomName = (legacyMetadata.CustomName ?? string.Empty).Trim(),
                    Comment = (legacyMetadata.Comment ?? string.Empty).Trim()
                };
            }

            changed = _usbDeviceMetadataByKey.Remove(legacyKey) || changed;
        }

        var hasRemainingLegacyBusIdKeys = _usbAutoShareDeviceKeys.Any(IsLegacyBusIdKey)
                                          || _usbDeviceMetadataByKey.Keys.Any(IsLegacyBusIdKey);
        var hasRemainingLegacyGuidKeys = _usbAutoShareDeviceKeys.Any(IsLegacyGuidKey)
                                         || _usbDeviceMetadataByKey.Keys.Any(IsLegacyGuidKey);
        if (!hasRemainingLegacyBusIdKeys && !hasRemainingLegacyGuidKeys)
        {
            _usbHardwareIdentityMigrationCompleted = true;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        PersistUsbAutoShareConfig();
    }

    private static bool IsLegacyBusIdKey(string? key)
    {
        return !string.IsNullOrWhiteSpace(key)
               && key.StartsWith("busid:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacyGuidKey(string? key)
    {
        return !string.IsNullOrWhiteSpace(key)
               && key.StartsWith("guid:", StringComparison.OrdinalIgnoreCase);
    }

    private bool PurgeLegacyUsbBusIdKeysInMemory()
    {
        var changed = false;

        foreach (var legacyKey in _usbAutoShareDeviceKeys.Where(IsLegacyBusIdKey).ToList())
        {
            changed = _usbAutoShareDeviceKeys.Remove(legacyKey) || changed;
        }

        foreach (var legacyKey in _usbDeviceMetadataByKey.Keys.Where(IsLegacyBusIdKey).ToList())
        {
            changed = _usbDeviceMetadataByKey.Remove(legacyKey) || changed;
        }

        if (changed)
        {
            _usbHardwareIdentityMigrationCompleted = true;
        }

        return changed;
    }

    private static string BuildUsbHardwareIdentityKey(UsbIpDeviceInfo device)
    {
        var hardwareId = GetUsbHardwareIdentityCandidate(device);
        return string.IsNullOrWhiteSpace(hardwareId) ? string.Empty : "hardware:" + hardwareId;
    }

    private static string BuildPreciseUsbHardwareIdentityKey(UsbIpDeviceInfo device)
    {
        var preciseHardwareId = NormalizeUsbHardwareId(device.HardwareIdentityKey);
        if (string.IsNullOrWhiteSpace(preciseHardwareId) || !IsPreciseUsbHardwareIdentity(preciseHardwareId))
        {
            return string.Empty;
        }

        return "hardware:" + preciseHardwareId;
    }

    private static string BuildPreferredUsbMigrationKey(UsbIpDeviceInfo device)
    {
        var preciseHardwareKey = BuildPreciseUsbHardwareIdentityKey(device);
        if (!string.IsNullOrWhiteSpace(preciseHardwareKey))
        {
            return preciseHardwareKey;
        }

        if (!string.IsNullOrWhiteSpace(device.InstanceId))
        {
            return "instance:" + device.InstanceId.Trim();
        }

        return string.Empty;
    }

    private bool IsUsbAutoShareEnabledForDevice(UsbIpDeviceInfo device)
    {
        var key = BuildUsbAutoShareKey(device);
        if (!string.IsNullOrWhiteSpace(key) && _usbAutoShareDeviceKeys.Contains(key))
        {
            return true;
        }

        foreach (var aliasKey in BuildUsbIdentityAliasKeys(device))
        {
            if (_usbAutoShareDeviceKeys.Contains(aliasKey))
            {
                return true;
            }
        }

        foreach (var legacyKey in BuildUsbLegacyAutoShareKeys(device))
        {
            if (_usbAutoShareDeviceKeys.Contains(legacyKey))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyUsbDeviceMetadata(UsbIpDeviceInfo device)
    {
        if (device is null)
        {
            return;
        }

        var deviceKey = !string.IsNullOrWhiteSpace(device.DeviceIdentityKey)
            ? device.DeviceIdentityKey.Trim()
            : BuildUsbDeviceIdentityKey(device);

        device.DeviceIdentityKey = deviceKey;

        _usbDeviceMetadataByKey.TryGetValue(deviceKey, out var metadata);
        if (metadata is null)
        {
            foreach (var lookupKey in BuildUsbIdentityAliasKeys(device))
            {
                if (_usbDeviceMetadataByKey.TryGetValue(lookupKey, out metadata))
                {
                    break;
                }
            }
        }

        if (metadata is null)
        {
            device.CustomName = string.Empty;
            device.CustomComment = string.Empty;
            return;
        }

        device.CustomName = (metadata.CustomName ?? string.Empty).Trim();
        device.CustomComment = (metadata.Comment ?? string.Empty).Trim();
    }

    private void PersistUsbAutoShareConfig()
    {
        try
        {
            var configResult = _configService.LoadOrCreate(_configPath);
            configResult.Config.Usb.AutoShareDeviceKeys = _usbAutoShareDeviceKeys
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            configResult.Config.Usb.DeviceMetadata = _usbDeviceMetadataByKey.Values
                .Select(entry => new UsbDeviceMetadataEntry
                {
                    DeviceKey = (entry.DeviceKey ?? string.Empty).Trim(),
                    CustomName = (entry.CustomName ?? string.Empty).Trim(),
                    Comment = (entry.Comment ?? string.Empty).Trim()
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.DeviceKey)
                                && (!string.IsNullOrWhiteSpace(entry.CustomName)
                                    || !string.IsNullOrWhiteSpace(entry.Comment)))
                .OrderBy(entry => entry.DeviceKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
            configResult.Config.Usb.HardwareIdentityMigrationCompleted = _usbHardwareIdentityMigrationCompleted;
            configResult.Config.Usb.UsbConfigResetMigrationApplied = _usbConfigResetMigrationApplied;

            if (!_configService.TrySave(_configPath, configResult.Config, out var errorMessage))
            {
                AddNotification($"USB Auto-Share-Konfiguration konnte nicht gespeichert werden: {errorMessage}", "Warning");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "USB Auto-Share-Konfiguration konnte nicht gespeichert werden.");
            AddNotification("USB Auto-Share-Konfiguration konnte nicht gespeichert werden.", "Warning");
        }
    }

    private async Task BindSelectedUsbDeviceAsync()
    {
        if (SelectedUsbDevice is null || string.IsNullOrWhiteSpace(SelectedUsbDevice.BusId))
        {
            return;
        }

        var busId = SelectedUsbDevice.BusId;
        if (!IsProcessElevated())
        {
            AddNotification("UAC wird angefordert für USB Share...", "Info");
        }

        await ExecuteBusyActionAsync($"USB-Gerät {busId} wird freigegeben...", async token =>
        {
            await _usbIpService.BindAsync(busId, force: false, token);
            AddNotification($"USB-Gerät '{busId}' wurde freigegeben.", "Success");
        });

        try
        {
            await LoadUsbDevicesAsync(showNotification: false);

            await Task.Delay(TimeSpan.FromSeconds(2), _lifetimeCancellation.Token);
            await LoadUsbDevicesAsync(showNotification: false, useBusyIndicator: false);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "USB-Liste konnte nach Share nicht vollständig aktualisiert werden. BusId={BusId}", busId);
            AddNotification("USB-Liste konnte nach Share nicht vollständig aktualisiert werden.", "Warning");
        }
    }

    private async Task UnbindSelectedUsbDeviceAsync()
    {
        if (SelectedUsbDevice is null || string.IsNullOrWhiteSpace(SelectedUsbDevice.BusId))
        {
            return;
        }

        var busId = SelectedUsbDevice.BusId;
        if (!IsProcessElevated())
        {
            AddNotification("UAC wird angefordert für USB Unshare...", "Info");
        }

        await ExecuteBusyActionAsync($"USB-Freigabe für {busId} wird entfernt...", async token =>
        {
            await _usbIpService.UnbindAsync(busId, token);
            AddNotification($"USB-Freigabe für '{busId}' wurde entfernt.", "Success");
        });

        try
        {
            await LoadUsbDevicesAsync(showNotification: false, applyAutoShare: false);

            await Task.Delay(TimeSpan.FromSeconds(2), _lifetimeCancellation.Token);
            await LoadUsbDevicesAsync(showNotification: false, applyAutoShare: false, useBusyIndicator: false);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "USB-Liste konnte nach Unshare nicht vollständig aktualisiert werden. BusId={BusId}", busId);
            AddNotification("USB-Liste konnte nach Unshare nicht vollständig aktualisiert werden.", "Warning");
        }
    }

    private async Task DetachSelectedUsbDeviceAsync()
    {
        if (SelectedUsbDevice is null || string.IsNullOrWhiteSpace(SelectedUsbDevice.BusId))
        {
            return;
        }

        var busId = SelectedUsbDevice.BusId;

        await ExecuteBusyActionAsync($"USB-Gerät {busId} wird getrennt...", async token =>
        {
            await _usbIpService.DetachAsync(busId, token);
            AddNotification($"USB-Gerät '{busId}' wurde getrennt.", "Success");
        });

        await LoadUsbDevicesAsync(showNotification: false);
    }

    private async Task EnsureSelectedVmNetworkSelectionAsync(bool showNotificationOnMissingSwitch)
    {
        if (SelectedVm is null)
        {
            AvailableVmNetworkAdapters.Clear();
            SelectedVmNetworkAdapter = null;
            SelectedSwitch = null;
            NetworkSwitchStatusHint = "Keine VM ausgewählt.";
            return;
        }

        try
        {
            var selectedVmName = SelectedVm.Name;
            var previouslySelectedAdapterName = SelectedVmNetworkAdapter?.Name;
            var adapters = await _hyperVService.GetVmNetworkAdaptersAsync(selectedVmName, _lifetimeCancellation.Token);

            if (SelectedVm is null || !string.Equals(SelectedVm.Name, selectedVmName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            AvailableVmNetworkAdapters.Clear();
            foreach (var adapter in adapters.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                ApplyGuestNetworkDiagnosticsToAdapter(adapter);
                AvailableVmNetworkAdapters.Add(adapter);
            }

            if (AvailableVmNetworkAdapters.Count == 0)
            {
                SelectedVmNetworkAdapter = null;
                SelectedVmCurrentSwitch = NotConnectedSwitchDisplay;
                SelectedSwitch = null;
                NetworkSwitchStatusHint = "Keine VM-Netzwerkkarten gefunden.";
                ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();
                DisconnectSwitchCommand.NotifyCanExecuteChanged();
                return;
            }

            SelectedVmNetworkAdapter = AvailableVmNetworkAdapters.FirstOrDefault(item =>
                                          string.Equals(item.Name, previouslySelectedAdapterName, StringComparison.OrdinalIgnoreCase))
                                      ?? AvailableVmNetworkAdapters.FirstOrDefault();

            SelectedVmCurrentSwitch = NormalizeSwitchDisplayName(SelectedVmNetworkAdapter?.SwitchName);

            var selectedVmEntry = AvailableVms.FirstOrDefault(vm =>
                string.Equals(vm.Name, selectedVmName, StringComparison.OrdinalIgnoreCase));

            if (selectedVmEntry is not null)
            {
                selectedVmEntry.RuntimeSwitchName = SelectedVmCurrentSwitch;
            }

            SyncSelectedSwitchWithCurrentVm(showNotificationOnMissingSwitch);
            NotifyTrayStateChanged();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Netzwerkkarten für VM {VmName} konnten nicht gelesen werden.", SelectedVm.Name);
        }
    }

    private void SyncSelectedSwitchWithCurrentVm(bool showNotificationOnMissingSwitch)
    {
        if (!AreSwitchesLoaded)
        {
            SelectedSwitch = null;
            NetworkSwitchStatusHint = "Switch-Liste noch nicht geladen.";
            ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();
            return;
        }

        if (SelectedVmNetworkAdapter is null)
        {
            SelectedSwitch = null;
            NetworkSwitchStatusHint = "Bitte VM-Netzwerkkarte auswählen.";
            ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();
            return;
        }

        if (AvailableSwitches.Count == 0)
        {
            SelectedSwitch = null;
            NetworkSwitchStatusHint = "Keine Switches verfügbar.";
            ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();
            return;
        }

        if (IsNotConnectedSwitchDisplay(SelectedVmCurrentSwitch))
        {
            SelectedSwitch = null;
            NetworkSwitchStatusHint = $"{GetAdapterDisplayName(SelectedVmNetworkAdapter)}: {NotConnectedSwitchDisplay}";
            ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();
            return;
        }

        var matchingSwitch = AvailableSwitches.FirstOrDefault(item =>
            string.Equals(item.Name, SelectedVmCurrentSwitch, StringComparison.OrdinalIgnoreCase));

        if (matchingSwitch is null)
        {
            SelectedSwitch = null;
            NetworkSwitchStatusHint = $"Aktiver Switch '{SelectedVmCurrentSwitch}' für '{GetAdapterDisplayName(SelectedVmNetworkAdapter)}' ist nicht in der aktuellen Liste.";
            if (showNotificationOnMissingSwitch)
            {
                AddNotification(NetworkSwitchStatusHint, "Warning");
            }

            ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();
            return;
        }

        SelectedSwitch = matchingSwitch;
        NetworkSwitchStatusHint = $"Aktiver Switch ({GetAdapterDisplayName(SelectedVmNetworkAdapter)}): {matchingSwitch.Name}";
        ConnectSelectedSwitchCommand.NotifyCanExecuteChanged();
    }

    private static string NormalizeSwitchDisplayName(string? switchName)
    {
        return string.IsNullOrWhiteSpace(switchName) ? NotConnectedSwitchDisplay : switchName.Trim();
    }

    private static bool IsNotConnectedSwitchDisplay(string? switchName)
    {
        return string.IsNullOrWhiteSpace(switchName)
               || string.Equals(switchName, "-", StringComparison.Ordinal)
               || string.Equals(switchName, NotConnectedSwitchDisplay, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAdapterDisplayName(HyperVVmNetworkAdapterInfo? adapter)
    {
        if (adapter is null)
        {
            return "Network Adapter";
        }

        return string.IsNullOrWhiteSpace(adapter.Name) ? "Network Adapter" : adapter.Name;
    }

    private static void ApplyGuestNetworkDiagnosticsToAdapter(HyperVVmNetworkAdapterInfo adapter)
    {
        adapter.Ipv4Address = adapter.IpAddresses
            .Select(address => (address ?? string.Empty).Trim())
            .FirstOrDefault(IsValidIpv4Address)
            ?? string.Empty;

        adapter.Ipv4SubnetMask = string.Empty;
        adapter.Ipv4Gateway = string.Empty;
        adapter.GuestComputerName = string.Empty;

        if (string.IsNullOrWhiteSpace(adapter.Ipv4Address))
        {
            return;
        }

        if (!GuestNetworkDiagnosticsRegistry.TryGetFreshEntryByIpv4(adapter.Ipv4Address, GuestNetworkDiagnosticsFreshness, out var networkEntry))
        {
            if (!GuestNetworkDiagnosticsRegistry.TryGetSingleFreshEntry(GuestNetworkDiagnosticsFreshness, out networkEntry))
            {
                return;
            }
        }

        adapter.Ipv4SubnetMask = networkEntry.SubnetMask;
        adapter.Ipv4Gateway = networkEntry.Gateway;
        adapter.GuestComputerName = networkEntry.GuestComputerName;
    }

    private static bool IsValidIpv4Address(string address)
    {
        return IPAddress.TryParse(address, out var parsed)
               && parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
    }

    public void RefreshVmNetworkDiagnosticsFromRegistry()
    {
        if (AvailableVmNetworkAdapters.Count == 0)
        {
            return;
        }

        var selectedAdapterName = SelectedVmNetworkAdapter?.Name ?? string.Empty;
        var updatedAdapters = AvailableVmNetworkAdapters
            .Select(adapter => new HyperVVmNetworkAdapterInfo
            {
                Name = adapter.Name,
                SwitchName = adapter.SwitchName,
                MacAddress = adapter.MacAddress,
                IpAddresses = [.. adapter.IpAddresses],
                Ipv4Address = adapter.Ipv4Address,
                Ipv4SubnetMask = adapter.Ipv4SubnetMask,
                Ipv4Gateway = adapter.Ipv4Gateway,
                GuestComputerName = adapter.GuestComputerName
            })
            .ToList();

        foreach (var adapter in updatedAdapters)
        {
            ApplyGuestNetworkDiagnosticsToAdapter(adapter);
        }

        AvailableVmNetworkAdapters.Clear();
        foreach (var adapter in updatedAdapters)
        {
            AvailableVmNetworkAdapters.Add(adapter);
        }

        SelectedVmNetworkAdapter = AvailableVmNetworkAdapters.FirstOrDefault(adapter =>
            !string.IsNullOrWhiteSpace(selectedAdapterName)
            && string.Equals(adapter.Name, selectedAdapterName, StringComparison.OrdinalIgnoreCase))
            ?? AvailableVmNetworkAdapters.FirstOrDefault();

        OnPropertyChanged(nameof(SelectedVmAdapterSwitchDisplay));
    }

    private async Task ConnectSelectedSwitchAsync()
    {
        if (SelectedSwitch is null || SelectedVm is null || SelectedVmNetworkAdapter is null)
        {
            return;
        }

        await ExecuteBusyActionAsync("VM-Netzwerk wird verbunden...", async token =>
        {
            await _hyperVService.ConnectVmNetworkAdapterAsync(SelectedVm.Name, SelectedSwitch.Name, SelectedVmNetworkAdapter.Name, token);
            AddNotification($"'{SelectedVm.Name}' Adapter '{GetAdapterDisplayName(SelectedVmNetworkAdapter)}' mit '{SelectedSwitch.Name}' verbunden.", "Success");

            if (ShouldAutoRestartHnsAfterConnect(SelectedSwitch.Name))
            {
                var hnsResult = await _hnsService.RestartHnsElevatedAsync(token);
                AddNotification(
                    hnsResult.Success ? hnsResult.Message : $"HNS Neustart fehlgeschlagen: {hnsResult.Message}",
                    hnsResult.Success ? "Success" : "Error");
            }
        });
        await RefreshVmStatusAsync();
    }

    private async Task DisconnectSwitchAsync()
    {
        if (SelectedVm is null || SelectedVmNetworkAdapter is null)
        {
            return;
        }

        await ExecuteBusyActionAsync("VM-Netzwerk wird getrennt...", async token =>
        {
            await _hyperVService.DisconnectVmNetworkAdapterAsync(SelectedVm.Name, SelectedVmNetworkAdapter.Name, token);
            AddNotification($"Netzwerkkarte '{GetAdapterDisplayName(SelectedVmNetworkAdapter)}' von '{SelectedVm.Name}' getrennt.", "Warning");
        });
        await RefreshVmStatusAsync();
    }

    private async Task ConnectAdapterToSwitchByKeyAsync(string? adapterSwitchKey)
    {
        if (SelectedVm is null || string.IsNullOrWhiteSpace(adapterSwitchKey))
        {
            return;
        }

        var separatorIndex = adapterSwitchKey.IndexOf("|||", StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex >= adapterSwitchKey.Length - 3)
        {
            return;
        }

        var adapterName = adapterSwitchKey[..separatorIndex].Trim();
        var switchName = adapterSwitchKey[(separatorIndex + 3)..].Trim();
        if (string.IsNullOrWhiteSpace(adapterName) || string.IsNullOrWhiteSpace(switchName))
        {
            return;
        }

        var targetAdapter = AvailableVmNetworkAdapters.FirstOrDefault(item => string.Equals(item.Name, adapterName, StringComparison.OrdinalIgnoreCase));
        var targetSwitch = AvailableSwitches.FirstOrDefault(item => string.Equals(item.Name, switchName, StringComparison.OrdinalIgnoreCase));
        if (targetAdapter is null || targetSwitch is null)
        {
            return;
        }

        SelectedVmNetworkAdapter = targetAdapter;
        SelectedSwitch = targetSwitch;
        await ConnectSelectedSwitchAsync();
    }

    private async Task DisconnectAdapterByNameAsync(string? adapterName)
    {
        if (SelectedVm is null || string.IsNullOrWhiteSpace(adapterName))
        {
            return;
        }

        var targetAdapter = AvailableVmNetworkAdapters.FirstOrDefault(item => string.Equals(item.Name, adapterName, StringComparison.OrdinalIgnoreCase));
        if (targetAdapter is null)
        {
            return;
        }

        SelectedVmNetworkAdapter = targetAdapter;
        await DisconnectSwitchAsync();
    }

    private bool ShouldAutoRestartHnsAfterConnect(string connectedSwitch)
    {
        if (HnsAutoRestartAfterAnyConnect)
        {
            return true;
        }

        return HnsAutoRestartAfterDefaultSwitch
               && string.Equals(connectedSwitch, DefaultSwitchName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task StartDefaultVmAsync()
    {
        var targetVm = AvailableVms.FirstOrDefault(vm => string.Equals(vm.Name, DefaultVmName, StringComparison.OrdinalIgnoreCase))?.Name
                       ?? SelectedVm?.Name
                       ?? AvailableVms.FirstOrDefault()?.Name;
        if (string.IsNullOrWhiteSpace(targetVm))
        {
            AddNotification("Keine VM zum Starten gefunden.", "Error");
            return;
        }

        await ExecuteBusyActionAsync("Default VM wird gestartet...", async token =>
        {
            await _hyperVService.StartVmAsync(targetVm, token);
            AddNotification($"'{targetVm}' gestartet.", "Success");

            if (UiOpenConsoleAfterVmStart)
            {
                await _hyperVService.OpenVmConnectAsync(targetVm, VmConnectComputerName, ShouldOpenConsoleWithSessionEdit(targetVm), token);
                AddNotification($"Konsole für '{targetVm}' geöffnet.", "Info");
            }
        });
        await RefreshVmStatusAsync();
    }

    private async Task StopDefaultVmAsync()
    {
        var targetVm = AvailableVms.FirstOrDefault(vm => string.Equals(vm.Name, DefaultVmName, StringComparison.OrdinalIgnoreCase))?.Name
                       ?? SelectedVm?.Name
                       ?? AvailableVms.FirstOrDefault()?.Name;
        if (string.IsNullOrWhiteSpace(targetVm))
        {
            AddNotification("Keine VM zum Stoppen gefunden.", "Error");
            return;
        }

        await ExecuteBusyActionAsync("Default VM wird gestoppt...", async token =>
        {
            await _hyperVService.StopVmGracefulAsync(targetVm, token);
            AddNotification($"'{targetVm}' gestoppt.", "Success");
        });
        await RefreshVmStatusAsync();
    }

    private async Task RefreshVmStatusAsync()
    {
        if (SelectedVm is null)
        {
            return;
        }

        if (!await _vmStatusRefreshGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var targetVmName = SelectedVm.Name;

            await ExecuteBusyActionAsync("VM-Status wird aktualisiert...", async token =>
            {
                var vmInfo = await _hyperVService.GetVmAsync(targetVmName, token);
                if (vmInfo is not null)
                {
                    UpdateSingleVmRuntimeState(vmInfo);
                }

                SelectedVmState = vmInfo?.State ?? "Unbekannt";
                SelectedVmCurrentSwitch = NormalizeSwitchDisplayName(vmInfo?.CurrentSwitchName);
                NotifyTrayStateChanged();
                StatusText = vmInfo is null ? "VM nicht gefunden" : $"{vmInfo.Name}: {vmInfo.State}";
            }, showNotificationOnErrorOnly: true);

            await EnsureSelectedVmNetworkSelectionAsync(showNotificationOnMissingSwitch: false);
        }
        finally
        {
            _vmStatusRefreshGate.Release();
        }
    }

    private void UpdateVmRuntimeStates(IReadOnlyList<HyperVVmInfo> runtimeVms)
    {
        if (runtimeVms.Count == 0 || AvailableVms.Count == 0)
        {
            return;
        }

        var labelsByName = AvailableVms
            .GroupBy(vm => vm.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Label, StringComparer.OrdinalIgnoreCase);

        var trayAdapterByName = AvailableVms
            .GroupBy(vm => vm.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().TrayAdapterName, StringComparer.OrdinalIgnoreCase);

        var vmIdByName = AvailableVms
            .GroupBy(vm => vm.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().VmId, StringComparer.OrdinalIgnoreCase);

        var sessionEditByName = AvailableVms
            .GroupBy(vm => vm.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().OpenConsoleWithSessionEdit, StringComparer.OrdinalIgnoreCase);

        var monitorStateByName = AvailableVms
            .GroupBy(vm => vm.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().MonitorStateText, StringComparer.OrdinalIgnoreCase);

        var monitorCpuByName = AvailableVms
            .GroupBy(vm => vm.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().MonitorCpuText, StringComparer.OrdinalIgnoreCase);

        var monitorRamByName = AvailableVms
            .GroupBy(vm => vm.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().MonitorRamText, StringComparer.OrdinalIgnoreCase);

        var selectedName = SelectedVm?.Name;
        var defaultName = DefaultVmName;

        var rebuilt = runtimeVms
            .OrderBy(vm => vm.Name, StringComparer.OrdinalIgnoreCase)
            .Select(vm => new VmDefinition
            {
                Name = vm.Name,
                VmId = string.IsNullOrWhiteSpace(vm.VmId)
                    ? (vmIdByName.TryGetValue(vm.Name, out var existingVmId) ? existingVmId : string.Empty)
                    : vm.VmId,
                Label = labelsByName.TryGetValue(vm.Name, out var label) && !string.IsNullOrWhiteSpace(label) ? label : vm.Name,
                RuntimeState = vm.State,
                RuntimeSwitchName = NormalizeSwitchDisplayName(vm.CurrentSwitchName),
                HasMountedIso = vm.HasMountedIso,
                MountedIsoPath = vm.MountedIsoPath,
                TrayAdapterName = trayAdapterByName.TryGetValue(vm.Name, out var trayAdapter) ? trayAdapter : string.Empty,
                OpenConsoleWithSessionEdit = sessionEditByName.TryGetValue(vm.Name, out var openWithSessionEdit) && openWithSessionEdit,
                MonitorStateText = monitorStateByName.TryGetValue(vm.Name, out var monitorState) && !string.IsNullOrWhiteSpace(monitorState)
                    ? monitorState
                    : "Guest nicht erreichbar",
                MonitorCpuText = monitorCpuByName.TryGetValue(vm.Name, out var monitorCpu) && !string.IsNullOrWhiteSpace(monitorCpu)
                    ? monitorCpu
                    : "CPU -",
                MonitorRamText = monitorRamByName.TryGetValue(vm.Name, out var monitorRam) && !string.IsNullOrWhiteSpace(monitorRam)
                    ? monitorRam
                    : "RAM -"
            })
            .ToList();

        AvailableVms.Clear();
        foreach (var vm in rebuilt)
        {
            AvailableVms.Add(vm);
        }

        SetSelectedVmInternal(AvailableVms.FirstOrDefault(vm => string.Equals(vm.Name, selectedName, StringComparison.OrdinalIgnoreCase))
                      ?? AvailableVms.FirstOrDefault());
        SelectedVmForConfig = SelectedVm;
        SelectedDefaultVmForConfig = AvailableVms.FirstOrDefault(vm => string.Equals(vm.Name, defaultName, StringComparison.OrdinalIgnoreCase))
                                   ?? SelectedVm;
        NotifyTrayStateChanged();
    }

    private void UpdateSingleVmRuntimeState(HyperVVmInfo runtimeVm)
    {
        if (runtimeVm is null || string.IsNullOrWhiteSpace(runtimeVm.Name))
        {
            return;
        }

        var existing = AvailableVms.FirstOrDefault(vm =>
            string.Equals(vm.Name, runtimeVm.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(runtimeVm.VmId))
        {
            existing.VmId = runtimeVm.VmId;
        }

        existing.RuntimeState = runtimeVm.State;
        existing.RuntimeSwitchName = NormalizeSwitchDisplayName(runtimeVm.CurrentSwitchName);
        existing.HasMountedIso = runtimeVm.HasMountedIso;
        existing.MountedIsoPath = runtimeVm.MountedIsoPath;
    }

    private async Task LoadCheckpointsAsync()
    {
        if (SelectedVm is null)
        {
            return;
        }

        var vmName = SelectedVm.Name;

        try
        {
            var checkpoints = await _hyperVService.GetCheckpointsAsync(vmName, _lifetimeCancellation.Token);
            ApplyCheckpointDescriptionOverrides(vmName, checkpoints);

            if (SelectedVm is null
                || !string.Equals(SelectedVm.Name, vmName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            AvailableCheckpoints.Clear();
            foreach (var checkpoint in checkpoints.OrderByDescending(item => item.Created))
            {
                AvailableCheckpoints.Add(checkpoint);
            }

            RebuildCheckpointTree(checkpoints);

            var newestCheckpoint = checkpoints
                .OrderByDescending(item => item.Created)
                .ThenByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (newestCheckpoint is null)
            {
                SelectedCheckpointNode = null;
                SelectedCheckpoint = null;
            }
            else
            {
                SelectedCheckpointNode = FindCheckpointNodeById(newestCheckpoint.Id);
                SelectedCheckpoint = newestCheckpoint;
            }

            AddNotification($"{AvailableCheckpoints.Count} Checkpoint(s) für '{vmName}' geladen.", "Info");
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Warning(ex, "Checkpoint laden fehlgeschlagen (Berechtigung) für VM {VmName}", vmName);
            AddNotification(ex.Message, "Warning");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Checkpoint laden fehlgeschlagen für VM {VmName}", vmName);
            AddNotification($"Fehler beim Laden der Checkpoints: {ex.Message}", "Error");
        }
    }

    private async Task CreateCheckpointAsync()
    {
        if (SelectedVm is null)
        {
            return;
        }

        var checkpointName = string.IsNullOrWhiteSpace(NewCheckpointName)
            ? $"checkpoint-{DateTime.Now:yyyyMMdd-HHmmss}"
            : NewCheckpointName.Trim();
        var checkpointDescription = (NewCheckpointDescription ?? string.Empty).Trim();
        var vmName = SelectedVm.Name;
        var createStartedUtc = DateTimeOffset.UtcNow;

        await ExecuteBusyActionAsync("Checkpoint wird erstellt...", async token =>
        {
            await _hyperVService.CreateCheckpointAsync(
                vmName,
                checkpointName,
                string.IsNullOrWhiteSpace(checkpointDescription) ? null : checkpointDescription,
                token);

            AddNotification($"Checkpoint '{checkpointName}' für '{vmName}' erstellt.", "Success");
        });

        NewCheckpointName = string.Empty;
        NewCheckpointDescription = string.Empty;
        await LoadCheckpointsAsync();

        if (!string.IsNullOrWhiteSpace(checkpointDescription))
        {
            TryApplyCheckpointDescriptionFallback(vmName, checkpointName, checkpointDescription, createStartedUtc);
        }
    }

    private void ApplyCheckpointDescriptionOverrides(string vmName, IReadOnlyList<HyperVCheckpointInfo> checkpoints)
    {
        if (checkpoints.Count == 0)
        {
            return;
        }

        foreach (var checkpoint in checkpoints)
        {
            if (!string.IsNullOrWhiteSpace(checkpoint.Description))
            {
                continue;
            }

            var key = BuildCheckpointDescriptionOverrideKey(vmName, checkpoint);
            if (_checkpointDescriptionOverridesByKey.TryGetValue(key, out var description)
                && !string.IsNullOrWhiteSpace(description))
            {
                checkpoint.Description = description;
            }
        }
    }

    private void TryApplyCheckpointDescriptionFallback(string vmName, string checkpointName, string description, DateTimeOffset createdAfterUtc)
    {
        if (string.IsNullOrWhiteSpace(vmName)
            || string.IsNullOrWhiteSpace(checkpointName)
            || string.IsNullOrWhiteSpace(description)
            || AvailableCheckpoints.Count == 0)
        {
            return;
        }

        var candidate = AvailableCheckpoints
            .Where(item => string.Equals(item.Name, checkpointName, StringComparison.Ordinal))
            .Where(item => item.Created == default || item.Created >= createdAfterUtc.AddMinutes(-2).LocalDateTime)
            .OrderByDescending(item => item.Created)
            .FirstOrDefault();

        if (candidate is null)
        {
            return;
        }

        var key = BuildCheckpointDescriptionOverrideKey(vmName, candidate);
        var shouldPersistOverride = !_checkpointDescriptionOverridesByKey.TryGetValue(key, out var existingDescription)
            || !string.Equals(existingDescription, description, StringComparison.Ordinal);
        _checkpointDescriptionOverridesByKey[key] = description;

        if (shouldPersistOverride)
        {
            PersistCheckpointDescriptionOverrides();
        }

        if (string.IsNullOrWhiteSpace(candidate.Description))
        {
            candidate.Description = description;
        }

        RebuildCheckpointTree(AvailableCheckpoints.ToList());
        SelectedCheckpointNode = FindCheckpointNodeById(candidate.Id);
        SelectedCheckpoint = candidate;
    }

    private static string BuildCheckpointDescriptionOverrideKey(string vmName, HyperVCheckpointInfo checkpoint)
    {
        var normalizedVm = (vmName ?? string.Empty).Trim();
        var checkpointId = (checkpoint.Id ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(checkpointId))
        {
            return normalizedVm + "|id:" + checkpointId;
        }

        var name = (checkpoint.Name ?? string.Empty).Trim();
        var created = checkpoint.Created == default ? string.Empty : checkpoint.Created.ToString("o");
        return normalizedVm + "|name:" + name + "|created:" + created;
    }

    private void LoadCheckpointDescriptionOverrides(HyperToolConfig config)
    {
        _checkpointDescriptionOverridesByKey.Clear();

        var entries = config.Checkpoints?.DescriptionOverrides;
        if (entries is null || entries.Count == 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (entry is null)
            {
                continue;
            }

            var key = (entry.Key ?? string.Empty).Trim();
            var description = (entry.Description ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            _checkpointDescriptionOverridesByKey[key] = description;
        }
    }

    private static List<CheckpointDescriptionOverrideEntry> BuildCheckpointDescriptionOverrideEntries(IDictionary<string, string> overridesByKey)
    {
        return overridesByKey
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key)
                           && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => new CheckpointDescriptionOverrideEntry
            {
                Key = pair.Key.Trim(),
                Description = pair.Value.Trim()
            })
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void PersistCheckpointDescriptionOverrides()
    {
        try
        {
            var configResult = _configService.LoadOrCreate(_configPath);
            configResult.Config.Checkpoints ??= new CheckpointSettings();
            configResult.Config.Checkpoints.DescriptionOverrides = BuildCheckpointDescriptionOverrideEntries(_checkpointDescriptionOverridesByKey);

            if (!_configService.TrySave(_configPath, configResult.Config, out var errorMessage))
            {
                AddNotification($"Checkpoint-Beschreibungen konnten nicht gespeichert werden: {errorMessage}", "Warning");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Checkpoint-Beschreibungen konnten nicht gespeichert werden.");
            AddNotification("Checkpoint-Beschreibungen konnten nicht gespeichert werden.", "Warning");
        }
    }

    private async Task ApplyCheckpointAsync()
    {
        if (SelectedVm is null || SelectedCheckpoint is null)
        {
            return;
        }

        await ExecuteBusyActionAsync("Checkpoint wird wiederhergestellt...", async token =>
        {
            await _hyperVService.ApplyCheckpointAsync(SelectedVm.Name, SelectedCheckpoint.Name, SelectedCheckpoint.Id, token);
            AddNotification($"Checkpoint '{SelectedCheckpoint.Name}' auf '{SelectedVm.Name}' wiederhergestellt.", "Warning");
        });

        await RefreshVmStatusAsync();
        await LoadCheckpointsAsync();
    }

    private async Task DeleteCheckpointAsync()
    {
        if (SelectedVm is null || SelectedCheckpoint is null)
        {
            return;
        }

        var checkpointName = SelectedCheckpoint.Name;
        var checkpointId = SelectedCheckpoint.Id;
        await ExecuteBusyActionAsync("Checkpoint wird gelöscht...", async token =>
        {
            await _hyperVService.RemoveCheckpointAsync(SelectedVm.Name, checkpointName, checkpointId, token);
            AddNotification($"Checkpoint '{checkpointName}' von '{SelectedVm.Name}' gelöscht.", "Warning");
        });

        await LoadCheckpointsAsync();
    }

    private void RebuildCheckpointTree(IReadOnlyList<HyperVCheckpointInfo> checkpoints)
    {
        AvailableCheckpointTree.Clear();
        if (checkpoints.Count == 0)
        {
            return;
        }

        var latestId = checkpoints
            .OrderByDescending(item => item.Created)
            .ThenByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Id)
            .FirstOrDefault() ?? string.Empty;

        var nodesById = new Dictionary<string, HyperVCheckpointTreeItem>(StringComparer.OrdinalIgnoreCase);
        var allNodes = checkpoints
            .Select(checkpoint => new HyperVCheckpointTreeItem
            {
                Checkpoint = checkpoint,
                IsLatest = !string.IsNullOrWhiteSpace(checkpoint.Id)
                           && string.Equals(checkpoint.Id, latestId, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();

        foreach (var node in allNodes)
        {
            if (!string.IsNullOrWhiteSpace(node.Checkpoint.Id)
                && !nodesById.ContainsKey(node.Checkpoint.Id))
            {
                nodesById.Add(node.Checkpoint.Id, node);
            }
        }

        var rootNodes = new List<HyperVCheckpointTreeItem>();
        foreach (var node in allNodes)
        {
            var parentId = node.Checkpoint.ParentId;
            if (!string.IsNullOrWhiteSpace(parentId)
                && nodesById.TryGetValue(parentId, out var parentNode)
                && !ReferenceEquals(parentNode, node))
            {
                parentNode.Children.Add(node);
            }
            else
            {
                rootNodes.Add(node);
            }
        }

        foreach (var root in rootNodes
                     .OrderBy(item => item.Created)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            SortCheckpointTreeRecursively(root);
            AvailableCheckpointTree.Add(root);
        }
    }

    private static void SortCheckpointTreeRecursively(HyperVCheckpointTreeItem node)
    {
        if (node.Children.Count == 0)
        {
            return;
        }

        var orderedChildren = node.Children
            .OrderBy(item => item.Created)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        node.Children.Clear();
        foreach (var child in orderedChildren)
        {
            SortCheckpointTreeRecursively(child);
            node.Children.Add(child);
        }
    }

    private HyperVCheckpointTreeItem? FindCheckpointNodeById(string checkpointId)
    {
        if (string.IsNullOrWhiteSpace(checkpointId))
        {
            return null;
        }

        foreach (var root in AvailableCheckpointTree)
        {
            var found = FindCheckpointNodeByIdRecursive(root, checkpointId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static HyperVCheckpointTreeItem? FindCheckpointNodeByIdRecursive(HyperVCheckpointTreeItem node, string checkpointId)
    {
        if (string.Equals(node.Checkpoint.Id, checkpointId, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found = FindCheckpointNodeByIdRecursive(child, checkpointId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void AddVm()
    {
        var vmName = NewVmName.Trim();
        if (string.IsNullOrWhiteSpace(vmName))
        {
            AddNotification("VM-Name darf nicht leer sein.", "Error");
            return;
        }

        if (AvailableVms.Any(vm => string.Equals(vm.Name, vmName, StringComparison.OrdinalIgnoreCase)))
        {
            AddNotification("VM existiert bereits in der Konfiguration.", "Warning");
            return;
        }

        var vmLabel = string.IsNullOrWhiteSpace(NewVmLabel) ? vmName : NewVmLabel.Trim();
        var vm = new VmDefinition
        {
            Name = vmName,
            Label = vmLabel
        };

        AvailableVms.Add(vm);
        SelectedVmForConfig = vm;
        SelectedDefaultVmForConfig ??= vm;

        NewVmName = string.Empty;
        NewVmLabel = string.Empty;
        MarkConfigDirty();
        AddNotification($"VM '{vmName}' zur Konfiguration hinzugefügt.", "Success");
    }

    private void RemoveSelectedVm()
    {
        if (SelectedVmForConfig is null)
        {
            AddNotification("Keine VM zum Entfernen ausgewählt.", "Warning");
            return;
        }

        RemoveVmFromConfiguration(SelectedVmForConfig, showNotification: true);
    }

    public async Task<bool> RemoveVmByNameAsync(string? vmName)
    {
        var resolvedVmName = (vmName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(resolvedVmName))
        {
            AddNotification("VM entfernen abgebrochen: VM-Name fehlt.", "Warning");
            return false;
        }

        var removedFromHyperV = false;
        await ExecuteBusyActionAsync($"VM '{resolvedVmName}' wird aus Hyper-V entfernt...", async token =>
        {
            await _hyperVService.RemoveVmAsync(resolvedVmName, token);
            removedFromHyperV = true;
            AddNotification($"VM '{resolvedVmName}' aus Hyper-V entfernt.", "Success");
        }, showNotificationOnErrorOnly: true);

        if (!removedFromHyperV)
        {
            return false;
        }

        var vmInConfig = AvailableVms.FirstOrDefault(item => string.Equals(item.Name, resolvedVmName, StringComparison.OrdinalIgnoreCase));
        if (vmInConfig is not null)
        {
            RemoveVmFromConfiguration(vmInConfig, showNotification: false);
        }

        await LoadVmsFromHyperVAsync();
        await RefreshVmStatusAsync();
        NotifyTrayStateChanged();
        return true;
    }

    private void RemoveVmFromConfiguration(VmDefinition vmToRemove, bool showNotification)
    {
        if (vmToRemove is null)
        {
            return;
        }

        AvailableVms.Remove(vmToRemove);

        if (SelectedVm == vmToRemove)
        {
            SelectedVm = AvailableVms.FirstOrDefault();
        }

        if (SelectedDefaultVmForConfig == vmToRemove)
        {
            SelectedDefaultVmForConfig = AvailableVms.FirstOrDefault();
            DefaultVmName = SelectedDefaultVmForConfig?.Name ?? string.Empty;
        }

        SelectedVmForConfig = AvailableVms.FirstOrDefault();
        MarkConfigDirty();

        if (showNotification)
        {
            AddNotification($"VM '{vmToRemove.Name}' aus Konfiguration entfernt.", "Warning");
        }
    }

    private void SetDefaultVmFromSelection()
    {
        var targetVm = SelectedVmForConfig ?? SelectedDefaultVmForConfig;
        if (targetVm is null)
        {
            AddNotification("Keine Default-VM ausgewählt.", "Warning");
            return;
        }

        DefaultVmName = targetVm.Name;
        SelectedDefaultVmForConfig = targetVm;
        MarkConfigDirty();
        AddNotification($"Default VM gesetzt: '{DefaultVmName}'.", "Info");
    }

    private async Task LoadVmAdaptersForConfigAsync(VmDefinition? vm)
    {
        AvailableVmTrayAdapterOptions.Clear();
        AvailableVmAdaptersForRename.Clear();
        SelectedVmTrayAdapterOption = null;
        SelectedVmAdapterForRename = null;
        NewVmAdapterName = string.Empty;

        if (vm is null || string.IsNullOrWhiteSpace(vm.Name))
        {
            return;
        }

        try
        {
            var adapters = await _hyperVService.GetVmNetworkAdaptersAsync(vm.Name, _lifetimeCancellation.Token);

            if (SelectedVmForConfig is null || !string.Equals(SelectedVmForConfig.Name, vm.Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var orderedAdapters = adapters
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var option in BuildTrayAdapterOptions(orderedAdapters))
            {
                AvailableVmTrayAdapterOptions.Add(option);
            }

            foreach (var adapter in orderedAdapters)
            {
                AvailableVmAdaptersForRename.Add(adapter);
            }

            var initialTrayAdapterOption = AvailableVmTrayAdapterOptions.FirstOrDefault(option =>
                                               string.Equals(option.AdapterName, vm.TrayAdapterName, StringComparison.OrdinalIgnoreCase))
                                           ?? AvailableVmTrayAdapterOptions.FirstOrDefault();

            var initialAdapterForRename = AvailableVmAdaptersForRename.FirstOrDefault(option =>
                string.Equals(option.Name, vm.TrayAdapterName, StringComparison.OrdinalIgnoreCase))
                ?? AvailableVmAdaptersForRename.FirstOrDefault();

            _configChangeSuppressionDepth++;
            try
            {
                SelectedVmTrayAdapterOption = initialTrayAdapterOption;
                SelectedVmAdapterForRename = initialAdapterForRename;
            }
            finally
            {
                _configChangeSuppressionDepth--;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Adapterliste für Config-VM {VmName} konnte nicht geladen werden.", vm.Name);
        }

        UpdateVmAdapterRenameValidationState();
        RenameVmAdapterCommand.NotifyCanExecuteChanged();
    }

    private bool CanExecuteRenameVmAdapter() => !IsBusy && string.IsNullOrWhiteSpace(ValidateVmAdapterRenameInput());

    private void UpdateVmAdapterRenameValidationState()
    {
        VmAdapterRenameValidationMessage = ValidateVmAdapterRenameInput();
    }

    private string ValidateVmAdapterRenameInput()
    {
        if (SelectedVmForConfig is null)
        {
            return "";
        }

        if (SelectedVmAdapterForRename is null)
        {
            return "Bitte zuerst einen Adapter auswählen.";
        }

        var oldName = SelectedVmAdapterForRename.Name?.Trim() ?? string.Empty;
        var newName = NewVmAdapterName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(newName))
        {
            return "Neuer Adaptername darf nicht leer sein.";
        }

        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            return "Neuer Name ist identisch mit dem aktuellen Namen.";
        }

        if (newName.IndexOfAny(VmAdapterInvalidNameChars) >= 0)
        {
            return "Der Name enthält ungültige Zeichen: \\ / : * ? \" < > |";
        }

        if (AvailableVmAdaptersForRename.Any(adapter =>
                !string.Equals(adapter.Name, oldName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(adapter.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            return "Ein Adapter mit diesem Namen existiert bereits auf der VM.";
        }

        return "";
    }

    private static IReadOnlyList<VmTrayAdapterOption> BuildTrayAdapterOptions(IReadOnlyList<HyperVVmNetworkAdapterInfo> adapters)
    {
        var options = new List<VmTrayAdapterOption>
        {
            new()
            {
                AdapterName = string.Empty,
                DisplayName = "Alle Adapter (Standard)"
            }
        };

        options.AddRange(adapters.Select(adapter => new VmTrayAdapterOption
        {
            AdapterName = adapter.Name,
            DisplayName = adapter.DisplayName
        }));

        return options;
    }

    private async Task RenameVmAdapterAsync()
    {
        if (SelectedVmForConfig is null || SelectedVmAdapterForRename is null)
        {
            return;
        }

        var validationMessage = ValidateVmAdapterRenameInput();
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            AddNotification($"Adapter umbenennen abgebrochen: {validationMessage}", "Warning");
            return;
        }

        var vmName = SelectedVmForConfig.Name;
        var oldName = SelectedVmAdapterForRename.Name?.Trim() ?? string.Empty;
        var newName = NewVmAdapterName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        await ExecuteBusyActionAsync($"Adapter '{oldName}' wird umbenannt...", async token =>
        {
            await _hyperVService.RenameVmNetworkAdapterAsync(vmName, oldName, newName, token);

            if (string.Equals(SelectedVmForConfig.TrayAdapterName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                SelectedVmForConfig.TrayAdapterName = newName;
            }

            var liveVm = AvailableVms.FirstOrDefault(vm => string.Equals(vm.Name, vmName, StringComparison.OrdinalIgnoreCase));
            if (liveVm is not null && string.Equals(liveVm.TrayAdapterName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                liveVm.TrayAdapterName = newName;
            }

            AddNotification($"VM-Adapter '{oldName}' wurde in '{newName}' umbenannt.", "Success");
        });

        NewVmAdapterName = string.Empty;
        await LoadVmAdaptersForConfigAsync(SelectedVmForConfig);
        await RefreshVmStatusAsync();
        NotifyTrayStateChanged();
    }

    private async Task SaveConfigAsync()
    {
        await ExecuteBusyActionAsync("Konfiguration wird gespeichert...", _ =>
        {
            var config = new HyperToolConfig
            {
                DefaultVmName = DefaultVmName,
                LastSelectedVmName = SelectedVm?.Name ?? LastSelectedVmName,
                Vms = AvailableVms
                    .Select(vm => new VmDefinition
                    {
                        Name = vm.Name,
                        Label = string.IsNullOrWhiteSpace(vm.Label) ? vm.Name : vm.Label,
                        TrayAdapterName = vm.TrayAdapterName?.Trim() ?? string.Empty,
                        OpenConsoleWithSessionEdit = vm.OpenConsoleWithSessionEdit
                    })
                    .OrderBy(vm => vm.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                DefaultSwitchName = DefaultSwitchName,
                VmConnectComputerName = NormalizeVmConnectComputerName(VmConnectComputerName),
                Hns = new HnsSettings
                {
                    Enabled = HnsEnabled,
                    AutoRestartAfterDefaultSwitch = HnsAutoRestartAfterDefaultSwitch,
                    AutoRestartAfterAnyConnect = HnsAutoRestartAfterAnyConnect
                },
                Ui = new UiSettings
                {
                    WindowTitle = "HyperTool",
                    Theme = NormalizeUiTheme(UiTheme),
                    DebugLoggingEnabled = UiDebugLoggingEnabled,
                    StartMinimized = UiStartMinimized,
                    MinimizeToTray = true,
                    EnableTrayIcon = true,
                    EnableTrayMenu = UiEnableTrayMenu,
                    StartWithWindows = UiStartWithWindows,
                    OpenConsoleAfterVmStart = UiOpenConsoleAfterVmStart,
                    RestoreNumLockAfterVmStart = UiRestoreNumLockAfterVmStart,
                    NumLockWatcherIntervalSeconds = Math.Clamp(_uiNumLockWatcherIntervalSeconds, 5, 600),
                    OpenVmConnectWithSessionEdit = UiOpenVmConnectWithSessionEdit,
                    TrayVmNames = [.. _trayVmNames]
                },
                Update = new UpdateSettings
                {
                    CheckOnStartup = UpdateCheckOnStartup,
                    GitHubOwner = GithubOwner,
                    GitHubRepo = GithubRepo
                },
                Usb = new UsbSettings
                {
                    Enabled = HostUsbSharingEnabled,
                    AutoDetachOnClientDisconnect = UsbAutoDetachOnClientDisconnect,
                    AutoDetachRetryAttempts = Math.Clamp(_usbAutoDetachRetryAttempts, 1, 10),
                    AutoDetachGracePeriodSeconds = Math.Clamp((int)Math.Round(_usbAutoDetachGracePeriod.TotalSeconds), 5, 300),
                    AutoDetachRetryDelayMs = Math.Clamp((int)Math.Round(_usbAutoDetachRetryDelay.TotalMilliseconds), 100, 5000),
                    UnshareOnExit = UsbUnshareOnExit,
                    AutoShareDeviceKeys = _usbAutoShareDeviceKeys
                        .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    DeviceMetadata = _usbDeviceMetadataByKey.Values
                        .Select(entry => new UsbDeviceMetadataEntry
                        {
                            DeviceKey = (entry.DeviceKey ?? string.Empty).Trim(),
                            CustomName = (entry.CustomName ?? string.Empty).Trim(),
                            Comment = (entry.Comment ?? string.Empty).Trim()
                        })
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.DeviceKey)
                                        && (!string.IsNullOrWhiteSpace(entry.CustomName)
                                            || !string.IsNullOrWhiteSpace(entry.Comment)))
                        .OrderBy(entry => entry.DeviceKey, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    HardwareIdentityMigrationCompleted = _usbHardwareIdentityMigrationCompleted
                    ,UsbConfigResetMigrationApplied = _usbConfigResetMigrationApplied
                },
                SharedFolders = new SharedFolderSettings
                {
                    Enabled = HostSharedFoldersEnabled,
                    HostDefinitions = HostSharedFolders
                        .Select(CloneSharedFolderDefinition)
                        .OrderBy(folder => folder.Label, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(folder => folder.ShareName, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                },
                Monitoring = new MonitoringSettings
                {
                    Enabled = _monitorEnabled,
                    IntervalMs = _monitorIntervalMs,
                    GraphHistoryMinutes = _monitorHistoryMinutes,
                    GraphHistorySize = _monitorHistorySize
                },
                Checkpoints = new CheckpointSettings
                {
                    DescriptionOverrides = BuildCheckpointDescriptionOverrideEntries(_checkpointDescriptionOverridesByKey)
                },
                DefaultVmImportDestinationPath = DefaultVmImportDestinationPath.Trim()
            };

            if (_configService.TrySave(_configPath, config, out var errorMessage))
            {
                ApplyConfiguredVmDefinitions(config.Vms);

                var executablePath = Environment.ProcessPath ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(executablePath)
                    && !_startupService.SetStartWithWindows(UiStartWithWindows, "HyperTool", executablePath, out var startupError))
                {
                    AddNotification($"Autostart konnte nicht gesetzt werden: {startupError}", "Warning");
                }

                if (_lastAppliedDebugLoggingEnabled != UiDebugLoggingEnabled)
                {
                    try
                    {
                        var logPath = HostLoggingService.Initialize(UiDebugLoggingEnabled);
                        _lastAppliedDebugLoggingEnabled = UiDebugLoggingEnabled;
                        Log.Information("Host logging reconfigured. DebugLoggingEnabled={DebugLoggingEnabled}; LogPath={LogPath}", UiDebugLoggingEnabled, logPath);
                    }
                    catch (Exception logEx)
                    {
                        AddNotification($"Logging konnte nicht neu initialisiert werden: {logEx.Message}", "Warning");
                    }
                }

                AddNotification("Konfiguration gespeichert.", "Success");
                MarkConfigClean();
            }
            else
            {
                AddNotification($"Konfiguration nicht gespeichert: {errorMessage}", "Error");
            }

            return Task.CompletedTask;
        });
    }

    private async Task RestartHnsAsync()
    {
        await ExecuteBusyActionAsync("HNS wird mit UAC neu gestartet...", async token =>
        {
            var result = await _hnsService.RestartHnsElevatedAsync(token);
            if (result.Success)
            {
                AddNotification(result.Message, "Success");
                return;
            }

            AddNotification($"HNS Neustart fehlgeschlagen: {result.Message}", "Error");
        });
    }

    public IReadOnlyList<VmDefinition> GetTrayVms()
    {
        IEnumerable<VmDefinition> trayVms = AvailableVms;

        if (_trayVmNames.Count > 0)
        {
            var allowedVmNames = new HashSet<string>(_trayVmNames, StringComparer.OrdinalIgnoreCase);
            trayVms = trayVms.Where(vm => allowedVmNames.Contains(vm.Name));
        }

        return trayVms
            .OrderByDescending(vm => string.Equals(vm.Name, DefaultVmName, StringComparison.OrdinalIgnoreCase))
            .ThenBy(vm => vm.DisplayLabel, StringComparer.OrdinalIgnoreCase)
            .Select(vm => new VmDefinition
            {
                Name = vm.Name,
                VmId = vm.VmId,
                Label = vm.Label,
                TrayAdapterName = vm.TrayAdapterName,
                OpenConsoleWithSessionEdit = vm.OpenConsoleWithSessionEdit,
                RuntimeState = vm.RuntimeState,
                RuntimeSwitchName = vm.RuntimeSwitchName,
                HasMountedIso = vm.HasMountedIso,
                MountedIsoPath = vm.MountedIsoPath
            })
            .ToList();
    }

    public IReadOnlyList<HostSharedFolderDefinition> GetHostSharedFoldersSnapshot()
    {
        if (!HostSharedFoldersEnabled)
        {
            return [];
        }

        return HostSharedFolders
            .Select(CloneSharedFolderDefinition)
            .ToList();
    }

    public IReadOnlyList<UsbDeviceMetadataEntry> GetUsbDeviceMetadataSnapshot()
    {
        var metadataByKey = new Dictionary<string, UsbDeviceMetadataEntry>(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;

        var expiredAliasKeys = _usbMetadataBusAliasExpiresUtc
            .Where(entry => entry.Value <= now)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var expiredAliasKey in expiredAliasKeys)
        {
            _usbMetadataBusAliasExpiresUtc.Remove(expiredAliasKey);
            _usbMetadataBusAliasByKey.Remove(expiredAliasKey);
        }

        foreach (var entry in _usbDeviceMetadataByKey.Values)
        {
            var normalizedKey = (entry.DeviceKey ?? string.Empty).Trim();
            var normalizedName = (entry.CustomName ?? string.Empty).Trim();
            var normalizedComment = (entry.Comment ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(normalizedKey)
                || (string.IsNullOrWhiteSpace(normalizedName) && string.IsNullOrWhiteSpace(normalizedComment)))
            {
                continue;
            }

            metadataByKey[normalizedKey] = new UsbDeviceMetadataEntry
            {
                DeviceKey = normalizedKey,
                CustomName = normalizedName,
                Comment = normalizedComment
            };
        }

        // Expose runtime bus mapping for guest metadata lookup compatibility.
        foreach (var device in UsbDevices)
        {
            if (device is null || string.IsNullOrWhiteSpace(device.BusId))
            {
                continue;
            }

            UsbDeviceMetadataEntry? metadata = null;
            var identityKey = !string.IsNullOrWhiteSpace(device.DeviceIdentityKey)
                ? device.DeviceIdentityKey.Trim()
                : BuildUsbDeviceIdentityKey(device);

            if (!string.IsNullOrWhiteSpace(identityKey)
                && _usbDeviceMetadataByKey.TryGetValue(identityKey, out var byIdentity))
            {
                metadata = byIdentity;
            }
            else
            {
                foreach (var aliasKey in BuildUsbIdentityAliasKeys(device))
                {
                    if (_usbDeviceMetadataByKey.TryGetValue(aliasKey, out var byAlias))
                    {
                        metadata = byAlias;
                        break;
                    }
                }
            }

            if (metadata is null)
            {
                continue;
            }

            var normalizedName = (metadata.CustomName ?? string.Empty).Trim();
            var normalizedComment = (metadata.Comment ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedName) && string.IsNullOrWhiteSpace(normalizedComment))
            {
                continue;
            }

            var busKey = "busid:" + device.BusId.Trim();
            _usbMetadataBusAliasByKey[busKey] = new UsbDeviceMetadataEntry
            {
                DeviceKey = busKey,
                CustomName = normalizedName,
                Comment = normalizedComment
            };
            _usbMetadataBusAliasExpiresUtc[busKey] = now + UsbMetadataBusAliasTtl;
        }

        foreach (var alias in _usbMetadataBusAliasByKey.Values)
        {
            metadataByKey[alias.DeviceKey] = new UsbDeviceMetadataEntry
            {
                DeviceKey = alias.DeviceKey,
                CustomName = alias.CustomName,
                Comment = alias.Comment
            };
        }

        return metadataByKey.Values
            .OrderBy(entry => entry.DeviceKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<UsbDeviceHostDescriptionEntry> GetUsbDeviceDescriptionSnapshot()
    {
        var descriptionsByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in UsbDevices)
        {
            var description = (device.Description ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            foreach (var aliasKey in BuildUsbIdentityAliasKeys(device))
            {
                var key = (aliasKey ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(key) || descriptionsByKey.ContainsKey(key))
                {
                    continue;
                }

                descriptionsByKey[key] = description;
            }
        }

        return descriptionsByKey
            .Select(pair => new UsbDeviceHostDescriptionEntry
            {
                DeviceKey = pair.Key,
                Description = pair.Value
            })
            .OrderBy(entry => entry.DeviceKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<UsbDeviceAttachmentEntry> GetUsbDeviceAttachmentSnapshot()
    {
        return UsbDevices
            .Where(device => device is not null
                             && device.IsAttached
                             && !string.IsNullOrWhiteSpace(device.BusId))
            .Select(device =>
            {
                var sourceVmId = UsbGuestConnectionRegistry.TryGetFreshGuestVmId(device, GuestAckChannelHealthyWindow, out var vmId)
                    ? vmId
                    : string.Empty;
                var guestVmName = ResolveVmNameByVmId(sourceVmId);

                return new UsbDeviceAttachmentEntry
                {
                    BusId = (device.BusId ?? string.Empty).Trim(),
                    GuestComputerName = (device.AttachedGuestComputerName ?? string.Empty).Trim(),
                    SourceVmId = sourceVmId,
                    GuestVmName = guestVmName,
                    ClientIpAddress = (device.ClientIpAddress ?? string.Empty).Trim()
                };
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.BusId))
            .OrderBy(entry => entry.BusId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool TryUpdateSelectedUsbMetadata(string? customName, string? customComment, out string message)
    {
        if (SelectedUsbDevice is null)
        {
            message = "Kein USB-Gerät ausgewählt.";
            return false;
        }

        return TryUpdateUsbMetadata(SelectedUsbDevice, customName, customComment, out message);
    }

    public bool TryUpdateUsbMetadata(UsbIpDeviceInfo device, string? customName, string? customComment, out string message)
    {
        if (device is null)
        {
            message = "USB-Gerät ist ungültig.";
            return false;
        }

        var key = BuildUsbDeviceIdentityKey(device);
        if (string.IsNullOrWhiteSpace(key))
        {
            message = "Für dieses USB-Gerät konnte kein stabiler Identitätsschlüssel ermittelt werden.";
            return false;
        }

        var normalizedName = (customName ?? string.Empty).Trim();
        var normalizedComment = (customComment ?? string.Empty).Trim();
        var hasMetadata = !string.IsNullOrWhiteSpace(normalizedName) || !string.IsNullOrWhiteSpace(normalizedComment);
        var aliasKeys = BuildUsbIdentityAliasKeys(device)
            .Where(identityKey => !string.IsNullOrWhiteSpace(identityKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!aliasKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            aliasKeys.Add(key);
        }

        var changed = false;
        if (!hasMetadata)
        {
            foreach (var aliasKey in aliasKeys)
            {
                changed = _usbDeviceMetadataByKey.Remove(aliasKey) || changed;
            }
        }
        else
        {
            foreach (var aliasKey in aliasKeys)
            {
                if (string.Equals(aliasKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                changed = _usbDeviceMetadataByKey.Remove(aliasKey) || changed;
            }

            if (_usbDeviceMetadataByKey.TryGetValue(key, out var existing)
                && string.Equals(existing.CustomName, normalizedName, StringComparison.Ordinal)
                && string.Equals(existing.Comment, normalizedComment, StringComparison.Ordinal))
            {
                // no-op
            }
            else
            {
                _usbDeviceMetadataByKey[key] = new UsbDeviceMetadataEntry
                {
                    DeviceKey = key,
                    CustomName = normalizedName,
                    Comment = normalizedComment
                };
                changed = true;
            }
        }

        if (!changed)
        {
            message = "Keine Änderungen an USB-Metadaten.";
            return false;
        }

        device.DeviceIdentityKey = key;
        device.CustomName = normalizedName;
        device.CustomComment = normalizedComment;

        foreach (var runtimeDevice in UsbDevices)
        {
            var runtimeKey = BuildUsbDeviceIdentityKey(runtimeDevice);
            if (!string.IsNullOrWhiteSpace(runtimeKey)
                && string.Equals(runtimeKey, key, StringComparison.OrdinalIgnoreCase))
            {
                runtimeDevice.DeviceIdentityKey = key;
                runtimeDevice.CustomName = normalizedName;
                runtimeDevice.CustomComment = normalizedComment;
            }
        }

        var selectedDevice = SelectedUsbDevice;
        var selectedIdentityKey = selectedDevice is null
            ? string.Empty
            : BuildUsbDeviceIdentityKey(selectedDevice);
        if (selectedDevice is not null
            && (ReferenceEquals(selectedDevice, device)
            || (!string.IsNullOrWhiteSpace(selectedIdentityKey)
                && string.Equals(selectedIdentityKey, key, StringComparison.OrdinalIgnoreCase))))
        {
            selectedDevice.DeviceIdentityKey = key;
            selectedDevice.CustomName = normalizedName;
            selectedDevice.CustomComment = normalizedComment;
        }

        PersistUsbAutoShareConfig();
        MarkConfigDirty();
        NotifyTrayStateChanged();

        message = hasMetadata
            ? "USB-Name/Kommentar gespeichert."
            : "USB-Name/Kommentar entfernt.";
        return true;
    }

    public async Task<bool> MountIsoByVmNameAsync(string? vmName)
    {
        var resolvedVmName = (vmName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(resolvedVmName))
        {
            AddNotification("ISO einbinden abgebrochen: VM-Name fehlt.", "Warning");
            return false;
        }

        var isoPath = _uiInteropService.PickFilePath("ISO auswählen", [".iso"]);
        if (string.IsNullOrWhiteSpace(isoPath))
        {
            return false;
        }

        var mounted = false;
        await ExecuteBusyActionAsync($"ISO wird in '{resolvedVmName}' eingebunden...", async token =>
        {
            await _hyperVService.MountVmIsoAsync(resolvedVmName, isoPath, token);
            AddNotification($"ISO eingebunden: {Path.GetFileName(isoPath)} in '{resolvedVmName}'.", "Success");
            mounted = true;
        }, showNotificationOnErrorOnly: true);

        if (mounted)
        {
            await RefreshVmStatusByNameAsync(resolvedVmName);
        }

        return mounted;
    }

    public async Task<bool> UnmountIsoByVmNameAsync(string? vmName)
    {
        var resolvedVmName = (vmName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(resolvedVmName))
        {
            AddNotification("ISO auswerfen abgebrochen: VM-Name fehlt.", "Warning");
            return false;
        }

        var unmounted = false;
        await ExecuteBusyActionAsync($"ISO wird aus '{resolvedVmName}' ausgeworfen...", async token =>
        {
            await _hyperVService.UnmountVmIsoAsync(resolvedVmName, token);
            AddNotification($"ISO in '{resolvedVmName}' ausgeworfen (kein Medium).", "Success");
            unmounted = true;
        }, showNotificationOnErrorOnly: true);

        if (unmounted)
        {
            await RefreshVmStatusByNameAsync(resolvedVmName);
        }

        return unmounted;
    }

    public void UpsertHostSharedFolderDefinition(HostSharedFolderDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var normalized = NormalizeSharedFolderDefinition(definition);
        if (string.IsNullOrWhiteSpace(normalized.Id))
        {
            normalized.Id = Guid.NewGuid().ToString("N");
        }

        var existingIndex = -1;
        for (var index = 0; index < HostSharedFolders.Count; index++)
        {
            if (!string.Equals(HostSharedFolders[index].Id, normalized.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            existingIndex = index;
            break;
        }

        if (existingIndex >= 0)
        {
            HostSharedFolders[existingIndex] = normalized;
        }
        else
        {
            HostSharedFolders.Add(normalized);
        }

        MarkConfigDirty();
    }

    public bool RemoveHostSharedFolderDefinition(string id)
    {
        var normalizedId = (id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return false;
        }

        for (var index = 0; index < HostSharedFolders.Count; index++)
        {
            if (!string.Equals(HostSharedFolders[index].Id, normalizedId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            HostSharedFolders.RemoveAt(index);
            MarkConfigDirty();
            return true;
        }

        return false;
    }

    public IReadOnlyList<HyperVSwitchInfo> GetTraySwitches()
    {
        return AvailableSwitches
            .Select(vmSwitch => new HyperVSwitchInfo
            {
                Name = vmSwitch.Name,
                SwitchType = vmSwitch.SwitchType
            })
            .ToList();
    }

    public UsbIpDeviceInfo? GetSelectedUsbDeviceForTray()
    {
        if (SelectedUsbDevice is null)
        {
            return null;
        }

        return new UsbIpDeviceInfo
        {
            BusId = SelectedUsbDevice.BusId,
            Description = SelectedUsbDevice.Description,
            HardwareId = SelectedUsbDevice.HardwareId,
            HardwareIdentityKey = SelectedUsbDevice.HardwareIdentityKey,
            InstanceId = SelectedUsbDevice.InstanceId,
            PersistedGuid = SelectedUsbDevice.PersistedGuid,
            ClientIpAddress = SelectedUsbDevice.ClientIpAddress,
            AttachedGuestComputerName = SelectedUsbDevice.AttachedGuestComputerName,
            CustomName = SelectedUsbDevice.CustomName,
            CustomComment = SelectedUsbDevice.CustomComment
        };
    }

    public IReadOnlyList<UsbIpDeviceInfo> GetUsbDevicesForTray()
    {
        if (!HostUsbSharingEnabled)
        {
            return [];
        }

        return UsbDevices
            .Select(device => new UsbIpDeviceInfo
            {
                BusId = device.BusId,
                Description = device.Description,
                HardwareId = device.HardwareId,
                HardwareIdentityKey = device.HardwareIdentityKey,
                InstanceId = device.InstanceId,
                PersistedGuid = device.PersistedGuid,
                ClientIpAddress = device.ClientIpAddress,
                AttachedGuestComputerName = device.AttachedGuestComputerName,
                CustomName = device.CustomName,
                CustomComment = device.CustomComment
            })
            .ToList();
    }

    public Task SelectUsbDeviceForTrayAsync(string selectionKey)
    {
        if (string.IsNullOrWhiteSpace(selectionKey))
        {
            return Task.CompletedTask;
        }

        var key = selectionKey.Trim();

        var target = UsbDevices.FirstOrDefault(device =>
            string.Equals(BuildUsbSelectionKey(device), key, StringComparison.OrdinalIgnoreCase));

        if (target is not null)
        {
            SelectedUsbDevice = target;
        }

        return Task.CompletedTask;
    }

    private static string BuildUsbSelectionKey(UsbIpDeviceInfo device)
    {
        if (!string.IsNullOrWhiteSpace(device.PersistedGuid))
        {
            return "guid:" + device.PersistedGuid.Trim();
        }

        if (!string.IsNullOrWhiteSpace(device.InstanceId))
        {
            return "instance:" + device.InstanceId.Trim();
        }

        var hardwareId = GetUsbHardwareIdentityCandidate(device);
        if (!string.IsNullOrWhiteSpace(hardwareId))
        {
            var key = "hardware:" + hardwareId;
            if (!IsPreciseUsbHardwareIdentity(hardwareId)
                && !string.IsNullOrWhiteSpace(device.BusId))
            {
                key += "|busid:" + device.BusId.Trim();
            }

            return key;
        }

        if (!string.IsNullOrWhiteSpace(device.BusId))
        {
            return "busid:" + device.BusId.Trim();
        }

        return "description:" + (device.Description?.Trim() ?? string.Empty);
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

    private static string GetUsbHardwareIdentityCandidate(UsbIpDeviceInfo device)
    {
        var precise = NormalizeUsbHardwareId(device.HardwareIdentityKey);
        if (!string.IsNullOrWhiteSpace(precise))
        {
            return precise;
        }

        return NormalizeUsbHardwareId(device.HardwareId);
    }

    private static bool IsPreciseUsbHardwareIdentity(string hardwareIdentity)
    {
        return !string.IsNullOrWhiteSpace(hardwareIdentity)
               && hardwareIdentity.Contains("&REV_", StringComparison.OrdinalIgnoreCase);
    }

    public async Task RefreshUsbDevicesFromTrayAsync()
    {
        if (!HostUsbSharingEnabled)
        {
            return;
        }

        var gateEntered = false;
        try
        {
            await _usbTrayRefreshGate.WaitAsync(_lifetimeCancellation.Token);
            gateEntered = true;
            await LoadUsbDevicesAsync(showNotification: false, applyAutoShare: true, useBusyIndicator: false);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (gateEntered)
            {
                _usbTrayRefreshGate.Release();
            }
        }
    }

    private async Task<UsbIpDeviceInfo?> GetHostUsbStateAsync(string targetBusId, TimeSpan timeout)
    {
        using var stateCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        stateCts.CancelAfter(timeout);

        var devices = await _usbIpService.GetDevicesAsync(stateCts.Token);
        return devices.FirstOrDefault(device =>
            string.Equals(device.BusId?.Trim(), targetBusId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> TryHardReleaseAndReshareAsync(string targetBusId)
    {
        // Last-resort recovery: remove share and re-share so stale attachment state is cleared.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var resetCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
                resetCts.CancelAfter(TimeSpan.FromSeconds(18));

                await _usbIpService.UnbindAsync(targetBusId, resetCts.Token);
                await Task.Delay(TimeSpan.FromMilliseconds(380), resetCts.Token);
                await _usbIpService.BindAsync(targetBusId, force: false, resetCts.Token);
                await Task.Delay(TimeSpan.FromMilliseconds(460), resetCts.Token);

                var state = await GetHostUsbStateAsync(targetBusId, TimeSpan.FromSeconds(8));
                if (state is null || !state.IsAttached)
                {
                    Log.Warning(
                        "USB hard release+reshare executed after disconnect detach fallback. BusId={BusId}; Attempt={Attempt}/2",
                        targetBusId,
                        attempt);
                    return true;
                }
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "USB hard release+reshare attempt failed. BusId={BusId}; Attempt={Attempt}/2", targetBusId, attempt);
            }
        }

        return false;
    }

    public async Task HandleUsbClientDisconnectedAsync(string busId, string? hardwareId = null)
    {
        var normalizedBusId = (busId ?? string.Empty).Trim();
        var normalizedHardwareId = NormalizeUsbHardwareId(hardwareId);

        if (string.IsNullOrWhiteSpace(normalizedBusId)
            && string.IsNullOrWhiteSpace(normalizedHardwareId))
        {
            return;
        }

        // BusId from guest event is authoritative. Hardware-based resolution is
        // only used as fallback when BusId is missing.
        if (string.IsNullOrWhiteSpace(normalizedBusId)
            && !string.IsNullOrWhiteSpace(normalizedHardwareId))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
                cts.CancelAfter(TimeSpan.FromSeconds(8));
                var devices = await _usbIpService.GetDevicesAsync(cts.Token);
                var currentMatch = devices.FirstOrDefault(device =>
                    device.IsAttached
                    && !string.IsNullOrWhiteSpace(device.BusId)
                    && (string.Equals(NormalizeUsbHardwareId(device.HardwareId), normalizedHardwareId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(NormalizeUsbHardwareId(device.HardwareIdentityKey), normalizedHardwareId, StringComparison.OrdinalIgnoreCase)));

                if (currentMatch is not null)
                {
                    normalizedBusId = currentMatch.BusId.Trim();
                }
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "USB hardware-id based disconnect resolution failed. HardwareId={HardwareId}; BusId={BusId}", normalizedHardwareId, normalizedBusId);
            }
        }

        if (string.IsNullOrWhiteSpace(normalizedBusId)
            || !HostUsbSharingEnabled)
        {
            return;
        }

        var gateEntered = false;
        try
        {
            await _usbTrayRefreshGate.WaitAsync(_lifetimeCancellation.Token);
            gateEntered = true;

            var detached = await TryDetachBusWithRetryAsync(
                normalizedBusId,
                initialDelay: TimeSpan.Zero,
                _lifetimeCancellation.Token,
                context: "guest-disconnected");

            if (!detached)
            {
                Log.Warning("Automatic USB detach after guest disconnect event failed after retries. Manual detach/unshare may be required. BusId={BusId}", normalizedBusId);
                return;
            }

            _usbVmNotRunningSinceUtc.Remove(normalizedBusId);
            _usbVmOffDetachManualRequiredBusIds.Remove(normalizedBusId);

            Log.Information("USB detach executed after guest disconnect event. BusId={BusId}", normalizedBusId);

            try
            {
                await LoadUsbDevicesAsync(showNotification: false, applyAutoShare: false, useBusyIndicator: false);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "USB refresh after guest disconnect detach failed. BusId={BusId}", normalizedBusId);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Automatic USB detach after guest disconnect event failed. Manual detach/unshare may be required. BusId={BusId}", normalizedBusId);
        }
        finally
        {
            if (gateEntered)
            {
                _usbTrayRefreshGate.Release();
            }
        }
    }

    public async Task HandleUsbStaleExportHintAsync(string busId, string? sourceVmId = null, string? guestComputerName = null)
    {
        await Task.CompletedTask;
    }

    private async Task<bool> WaitForUsbDisconnectRecoveryAsync(string busId, TimeSpan gracePeriod, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(busId))
        {
            return false;
        }

        var remaining = gracePeriod;
        while (remaining > TimeSpan.Zero)
        {
            var delay = remaining <= UsbDisconnectRecoveryProbeInterval
                ? remaining
                : UsbDisconnectRecoveryProbeInterval;

            await Task.Delay(delay, token);

            if (HasFreshUsbGuestActivity(busId))
            {
                return true;
            }

            remaining -= delay;
        }

        return HasFreshUsbGuestActivity(busId);
    }

    private static bool HasFreshUsbGuestActivity(string busId)
    {
        if (string.IsNullOrWhiteSpace(busId))
        {
            return false;
        }

        return UsbGuestConnectionRegistry.TryGetFreshGuestVmId(busId, UsbDisconnectRecoveryFreshnessWindow, out _)
               || UsbGuestConnectionRegistry.TryGetFreshGuestComputerName(busId, UsbDisconnectRecoveryFreshnessWindow, out _);
    }

    private static bool IsUsbDetachNoOpError(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains("Device not found by bus id", StringComparison.OrdinalIgnoreCase)
               || text.Contains("There is no device with busid", StringComparison.OrdinalIgnoreCase)
               || text.Contains("not found", StringComparison.OrdinalIgnoreCase)
               || text.Contains("already detached", StringComparison.OrdinalIgnoreCase)
               || text.Contains("is not attached", StringComparison.OrdinalIgnoreCase)
               || text.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TryDetachBusWithRetryAsync(string busId, TimeSpan initialDelay, CancellationToken token, string context)
    {
        var normalizedBusId = (busId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedBusId))
        {
            return false;
        }

        if (initialDelay > TimeSpan.Zero)
        {
            await Task.Delay(initialDelay, token);
        }

        var attempts = Math.Clamp(_usbAutoDetachRetryAttempts, 1, 10);
        var retryDelay = _usbAutoDetachRetryDelay <= TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(450)
            : _usbAutoDetachRetryDelay;

        Exception? lastException = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                await _usbIpService.DetachAsync(normalizedBusId, token);

                if (attempt > 1)
                {
                    Log.Information(
                        "USB detach succeeded after retry. Context={Context}; BusId={BusId}; Attempt={Attempt}/{Attempts}",
                        context,
                        normalizedBusId,
                        attempt,
                        attempts);
                }

                return true;
            }
            catch (Exception ex) when (IsUsbDetachNoOpError(ex))
            {
                Log.Debug(
                    "USB detach treated as no-op. Context={Context}; BusId={BusId}; Attempt={Attempt}/{Attempts}",
                    context,
                    normalizedBusId,
                    attempt,
                    attempts);
                return true;
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;

                if (attempt >= attempts)
                {
                    break;
                }

                Log.Debug(
                    ex,
                    "USB detach attempt failed. Context={Context}; BusId={BusId}; Attempt={Attempt}/{Attempts}. Retrying in {RetryDelayMs}ms",
                    context,
                    normalizedBusId,
                    attempt,
                    attempts,
                    (int)Math.Round(retryDelay.TotalMilliseconds));

                await Task.Delay(retryDelay, token);
            }
        }

        Log.Warning(
            lastException,
            "USB detach failed after retries. Context={Context}; BusId={BusId}; Attempts={Attempts}",
            context,
            normalizedBusId,
            attempts);

        return false;
    }

    public async Task ShareSelectedUsbFromTrayAsync()
    {
        if (!HostUsbSharingEnabled)
        {
            AddNotification("USB Share ist global im Host deaktiviert.", "Info");
            return;
        }

        if (SelectedUsbDevice is null || string.IsNullOrWhiteSpace(SelectedUsbDevice.BusId))
        {
            AddNotification("Kein USB-Gerät für Share ausgewählt.", "Warning");
            return;
        }

        await BindSelectedUsbDeviceAsync();
    }

    public async Task UnshareSelectedUsbFromTrayAsync()
    {
        if (!HostUsbSharingEnabled)
        {
            AddNotification("USB Share ist global im Host deaktiviert.", "Info");
            return;
        }

        if (SelectedUsbDevice is null || string.IsNullOrWhiteSpace(SelectedUsbDevice.BusId))
        {
            AddNotification("Kein USB-Gerät für Unshare ausgewählt.", "Warning");
            return;
        }

        await UnbindSelectedUsbDeviceAsync();
    }

    public async Task RefreshTrayDataAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            var token = _lifetimeCancellation.Token;
            var runtimeVms = await _hyperVService.GetVmsAsync(token);
            UpdateVmRuntimeStates(runtimeVms);

            var switches = await _hyperVService.GetVmSwitchesAsync(token);
            AvailableSwitches.Clear();
            foreach (var vmSwitch in switches.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                AvailableSwitches.Add(vmSwitch);
            }

            AreSwitchesLoaded = true;
            SyncSelectedSwitchWithCurrentVm(showNotificationOnMissingSwitch: false);
            NotifyTrayStateChanged();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Tray runtime refresh failed.");
        }
    }

    public Task ReloadTrayDataAsync()
    {
        return ReloadConfigAsync();
    }

    public async Task StartVmFromTrayAsync(string vmName)
    {
        await ExecuteBusyActionAsync($"VM '{vmName}' wird gestartet...", async token =>
        {
            await _hyperVService.StartVmAsync(vmName, token);
            AddNotification($"VM '{vmName}' gestartet.", "Success");

            if (UiOpenConsoleAfterVmStart)
            {
                await _hyperVService.OpenVmConnectAsync(vmName, VmConnectComputerName, ShouldOpenConsoleWithSessionEdit(vmName), token);
                AddNotification($"Konsole für '{vmName}' geöffnet.", "Info");
            }
        });
        await RefreshVmStatusByNameAsync(vmName);
    }

    public async Task StopVmFromTrayAsync(string vmName)
    {
        await ExecuteBusyActionAsync($"VM '{vmName}' wird gestoppt...", async token =>
        {
            await _hyperVService.StopVmGracefulAsync(vmName, token);
            AddNotification($"VM '{vmName}' graceful gestoppt.", "Success");
        });
        await RefreshVmStatusByNameAsync(vmName);
    }

    public async Task OpenConsoleFromTrayAsync(string vmName)
    {
        await ExecuteBusyActionAsync($"Konsole für '{vmName}' wird geöffnet...", async token =>
        {
            await _hyperVService.OpenVmConnectAsync(vmName, VmConnectComputerName, ShouldOpenConsoleWithSessionEdit(vmName), token);
            AddNotification($"Konsole für '{vmName}' geöffnet.", "Info");
        });
    }

    public async Task CreateSnapshotFromTrayAsync(string vmName)
    {
        var checkpointName = $"checkpoint-{DateTime.Now:yyyyMMdd-HHmmss}";

        await ExecuteBusyActionAsync($"Checkpoint für '{vmName}' wird erstellt...", async token =>
        {
            await _hyperVService.CreateCheckpointAsync(vmName, checkpointName, null, token);
            AddNotification($"Checkpoint '{checkpointName}' für '{vmName}' erstellt.", "Success");
        });
    }

    public async Task<IReadOnlyList<HyperVVmNetworkAdapterInfo>> GetVmNetworkAdaptersForTrayAsync(string vmName)
    {
        if (string.IsNullOrWhiteSpace(vmName))
        {
            return [];
        }

        try
        {
            var adapters = await _hyperVService.GetVmNetworkAdaptersAsync(vmName, CancellationToken.None);
            return adapters
                .Select(adapter => new HyperVVmNetworkAdapterInfo
                {
                    Name = adapter.Name,
                    SwitchName = adapter.SwitchName,
                    MacAddress = adapter.MacAddress
                })
                .OrderBy(adapter => adapter.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Konnte Netzwerkadapter für Tray-Control-Center nicht laden: {VmName}", vmName);
            return [];
        }
    }

    public Task ConnectVmSwitchFromTrayAsync(string vmName, string switchName)
    {
        return ConnectVmSwitchFromTrayAsync(vmName, switchName, null);
    }

    public async Task ConnectVmSwitchFromTrayAsync(string vmName, string switchName, string? adapterName)
    {
        await ExecuteBusyActionAsync($"'{vmName}' wird mit '{switchName}' verbunden...", async token =>
        {
            string? trayAdapterName = null;
            var vmConfig = AvailableVms.FirstOrDefault(vm => string.Equals(vm.Name, vmName, StringComparison.OrdinalIgnoreCase));
            var configuredAdapterName = vmConfig?.TrayAdapterName?.Trim();
            var requestedAdapterName = adapterName?.Trim();

            IReadOnlyList<HyperVVmNetworkAdapterInfo>? adapters = null;

            if (!string.IsNullOrWhiteSpace(requestedAdapterName)
                || !string.IsNullOrWhiteSpace(configuredAdapterName))
            {
                adapters = await _hyperVService.GetVmNetworkAdaptersAsync(vmName, token);
            }

            if (!string.IsNullOrWhiteSpace(requestedAdapterName))
            {
                var requestedExists = adapters?.Any(adapter =>
                    string.Equals(adapter.Name, requestedAdapterName, StringComparison.OrdinalIgnoreCase)) == true;

                if (requestedExists)
                {
                    trayAdapterName = requestedAdapterName;
                }
                else
                {
                    AddNotification($"Gewählter Adapter '{requestedAdapterName}' für '{vmName}' wurde nicht gefunden. Fallback auf Standard-Verhalten.", "Warning");
                }
            }

            if (string.IsNullOrWhiteSpace(trayAdapterName)
                && !string.IsNullOrWhiteSpace(configuredAdapterName))
            {
                var exists = adapters?.Any(adapter => string.Equals(adapter.Name, configuredAdapterName, StringComparison.OrdinalIgnoreCase)) == true;

                if (exists)
                {
                    trayAdapterName = configuredAdapterName;
                }
                else
                {
                    AddNotification($"Konfigurierter Tray-Adapter '{configuredAdapterName}' für '{vmName}' wurde nicht gefunden. Fallback auf Standard-Verhalten.", "Warning");
                }
            }

            await _hyperVService.ConnectVmNetworkAdapterAsync(vmName, switchName, trayAdapterName, token);

            if (string.IsNullOrWhiteSpace(trayAdapterName))
            {
                AddNotification($"'{vmName}' mit '{switchName}' verbunden.", "Success");
            }
            else
            {
                AddNotification($"'{vmName}' Adapter '{trayAdapterName}' mit '{switchName}' verbunden.", "Success");
            }

            if (ShouldAutoRestartHnsAfterConnect(switchName))
            {
                var hnsResult = await _hnsService.RestartHnsElevatedAsync(token);
                AddNotification(
                    hnsResult.Success ? hnsResult.Message : $"HNS Neustart fehlgeschlagen: {hnsResult.Message}",
                    hnsResult.Success ? "Success" : "Error");
            }
        });

        await RefreshVmStatusByNameAsync(vmName);
    }

    public async Task DisconnectVmSwitchFromTrayAsync(string vmName)
    {
        await ExecuteBusyActionAsync($"'{vmName}' wird vom Switch getrennt...", async token =>
        {
            string? trayAdapterName = null;
            var vmConfig = AvailableVms.FirstOrDefault(vm => string.Equals(vm.Name, vmName, StringComparison.OrdinalIgnoreCase));
            var configuredAdapterName = vmConfig?.TrayAdapterName?.Trim();

            if (!string.IsNullOrWhiteSpace(configuredAdapterName))
            {
                var adapters = await _hyperVService.GetVmNetworkAdaptersAsync(vmName, token);
                var exists = adapters.Any(adapter => string.Equals(adapter.Name, configuredAdapterName, StringComparison.OrdinalIgnoreCase));

                if (exists)
                {
                    trayAdapterName = configuredAdapterName;
                }
                else
                {
                    AddNotification($"Konfigurierter Tray-Adapter '{configuredAdapterName}' für '{vmName}' wurde nicht gefunden. Fallback auf Standard-Verhalten.", "Warning");
                }
            }

            await _hyperVService.DisconnectVmNetworkAdapterAsync(vmName, trayAdapterName, token);

            if (string.IsNullOrWhiteSpace(trayAdapterName))
            {
                AddNotification($"'{vmName}' vom Switch getrennt.", "Success");
            }
            else
            {
                AddNotification($"'{vmName}' Adapter '{trayAdapterName}' vom Switch getrennt.", "Success");
            }
        });

        await RefreshVmStatusByNameAsync(vmName);
    }

    public async Task<IReadOnlyList<HostNetworkAdapterInfo>> GetHostNetworkAdaptersWithUplinkAsync()
    {
        if (IsBusy)
        {
            AddNotification("Bitte warten, ein anderer Vorgang läuft noch.", "Info");
            return [];
        }

        var adapters = Array.Empty<HostNetworkAdapterInfo>();

        await ExecuteBusyActionAsync("Host-Netzwerkdaten werden geladen...", async token =>
        {
            var result = await _hyperVService.GetHostNetworkAdaptersWithUplinkAsync(token);
            adapters = result
                .OrderByDescending(item => !string.IsNullOrWhiteSpace(item.Gateway))
                .ThenBy(item => item.AdapterName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, showNotificationOnErrorOnly: true);

        if (adapters.Length == 0)
        {
            AddNotification("Keine Host-Netzwerkkarten gefunden.", "Warning");
            return adapters;
        }

        AddNotification($"{adapters.Length} Host-Netzwerkkarte(n) geladen.", "Info");
        return adapters;
    }

    public async Task<bool> SetHostNetworkProfileCategoryAsync(string? adapterName, string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            AddNotification("Netzprofil konnte nicht gesetzt werden: Kategorie fehlt.", "Warning");
            return false;
        }

        var normalizedCategory = category.Trim() switch
        {
            "Private" => "Private",
            "Public" => "Public",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(normalizedCategory))
        {
            AddNotification("Netzprofil konnte nicht gesetzt werden: nur Private oder Public sind erlaubt.", "Warning");
            return false;
        }

        var updated = false;
        await ExecuteBusyActionAsync("Host-Netzprofil wird geändert...", async token =>
        {
            await _hyperVService.SetHostNetworkProfileCategoryAsync(adapterName ?? string.Empty, normalizedCategory, token);
            await RefreshHostNetworkProfileAsync();

            var targetText = string.IsNullOrWhiteSpace(adapterName)
                ? "aktive Host-Netzprofile"
                : $"Netzprofil von '{adapterName}'";

            var categoryText = normalizedCategory == "Private" ? "Privat" : "Öffentlich";
            AddNotification($"{targetText} auf '{categoryText}' gesetzt.", "Success");
            updated = true;
        }, showNotificationOnErrorOnly: true);

        return updated;
    }

    private async Task RefreshVmStatusByNameAsync(string vmName)
    {
        var currentSelectedVm = SelectedVm;

        SetSelectedVmInternal(AvailableVms.FirstOrDefault(vm =>
                                string.Equals(vm.Name, vmName, StringComparison.OrdinalIgnoreCase))
                            ?? currentSelectedVm);

        await RefreshVmStatusAsync();
    }

    public void ConfigureResourceMonitoring(bool enabled, int intervalMs, int historyMinutes, int historySize)
    {
        lock (_resourceMonitorSync)
        {
            _monitorEnabled = enabled;
            _monitorIntervalMs = intervalMs switch
            {
                500 => 500,
                1000 => 1000,
                2000 => 2000,
                5000 => 5000,
                _ => 1000
            };
            _monitorHistoryMinutes = Math.Clamp(historyMinutes, 1, 30);
            _monitorHistorySize = Math.Clamp(historySize, 30, 3600);

            TrimHistoryQueue(_hostCpuHistory);
            TrimHistoryQueue(_hostRamPressureHistory);

            foreach (var state in _vmMonitorStates.Values)
            {
                TrimHistoryQueue(state.CpuHistory);
                TrimHistoryQueue(state.RamPressureHistory);
            }
        }

        ResourceMonitorVersion++;
    }

    public void UpdateHostResourceMonitoring(double cpuPercent, double ramUsedGb, double ramTotalGb)
    {
        lock (_resourceMonitorSync)
        {
            _hostCpuPercent = Math.Clamp(cpuPercent, 0d, 100d);
            _hostRamUsedGb = Math.Max(0d, ramUsedGb);
            _hostRamTotalGb = Math.Max(0d, ramTotalGb);
            _lastHostResourceSampleUtc = DateTimeOffset.UtcNow;

            EnqueueHistory(_hostCpuHistory, _hostCpuPercent);
            var pressure = _hostRamTotalGb <= 0 ? 0 : (_hostRamUsedGb / _hostRamTotalGb) * 100d;
            EnqueueHistory(_hostRamPressureHistory, Math.Clamp(pressure, 0d, 100d));
        }

        ResourceMonitorVersion++;
    }

    public void UpdateGuestResourceMonitoring(ResourceMonitorPacket packet)
    {
        if (packet is null || string.IsNullOrWhiteSpace(packet.Vm))
        {
            return;
        }

        lock (_resourceMonitorSync)
        {
            var now = DateTimeOffset.UtcNow;
            var vmName = ResolveVmNameForMonitorPacket(packet.Vm, packet.SourceVmId);
            if (!_vmMonitorStates.TryGetValue(vmName, out var state))
            {
                state = new VmResourceMonitorRuntimeState
                {
                    VmName = vmName
                };
                _vmMonitorStates[vmName] = state;
            }

            state.GuestCpuPercent = Math.Clamp(packet.Cpu, 0d, 100d);
            state.GuestRamUsedGb = Math.Max(0d, packet.RamUsed);
            state.GuestRamTotalGb = Math.Max(0d, packet.RamTotal);
            state.LastGuestSeenUtc = now;

            ApplyPreferredMonitorSource(state, isVmRunning: true, now, enqueueHistory: true);
        }

        ApplyVmMonitorStateToVmDefinitions();
        ResourceMonitorVersion++;
    }

    public async Task RefreshHostVmResourceMonitoringAsync(CancellationToken token)
    {
        IReadOnlyList<VmHostResourcePacket> packets;
        try
        {
            packets = await _hyperVService.GetVmHostResourceMetricsAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Host VM monitor sampling failed.");
            return;
        }

        UpdateHostVmResourceMonitoring(packets);
    }

    public void UpdateHostVmResourceMonitoring(IReadOnlyList<VmHostResourcePacket> packets)
    {
        lock (_resourceMonitorSync)
        {
            var now = DateTimeOffset.UtcNow;
            var packetByVmName = new Dictionary<string, VmHostResourcePacket>(StringComparer.OrdinalIgnoreCase);

            foreach (var packet in packets ?? [])
            {
                if (packet is null)
                {
                    continue;
                }

                var vmName = ResolveVmNameForHostPacket(packet);
                if (string.IsNullOrWhiteSpace(vmName))
                {
                    continue;
                }

                packetByVmName[vmName] = packet;

                if (!_vmMonitorStates.TryGetValue(vmName, out var state))
                {
                    state = new VmResourceMonitorRuntimeState
                    {
                        VmName = vmName
                    };
                    _vmMonitorStates[vmName] = state;
                }

                state.HostCpuPercent = Math.Clamp(packet.CpuPercent, 0d, 100d);
                state.HostRamUsedGb = Math.Max(0d, packet.RamUsedGb);
                state.HostRamTotalGb = Math.Max(state.HostRamUsedGb, Math.Max(0d, packet.RamTotalGb));
                state.LastHostSeenUtc = now;
            }

            foreach (var vm in AvailableVms)
            {
                if (!_vmMonitorStates.TryGetValue(vm.Name, out var state))
                {
                    state = new VmResourceMonitorRuntimeState
                    {
                        VmName = vm.Name
                    };
                    _vmMonitorStates[vm.Name] = state;
                }

                if (packetByVmName.ContainsKey(vm.Name))
                {
                    ApplyPreferredMonitorSource(state, IsRunningState(vm.RuntimeState), now, enqueueHistory: true);
                }
            }
        }

        ApplyVmMonitorStateToVmDefinitions();
        ResourceMonitorVersion++;
    }

    private string ResolveVmNameForHostPacket(VmHostResourcePacket packet)
    {
        var vmNameById = ResolveVmNameByVmId(packet.VmId);
        if (!string.IsNullOrWhiteSpace(vmNameById))
        {
            return vmNameById;
        }

        var vmName = (packet.VmName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(vmName))
        {
            return string.Empty;
        }

        var vmByName = AvailableVms.FirstOrDefault(vm =>
            string.Equals(vm.Name, vmName, StringComparison.OrdinalIgnoreCase));
        if (vmByName is not null)
        {
            return vmByName.Name;
        }

        return vmName;
    }

    private string ResolveVmNameByVmId(string? vmId)
    {
        var normalizedVmId = (vmId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedVmId))
        {
            return string.Empty;
        }

        var vmById = AvailableVms.FirstOrDefault(vm =>
            !string.IsNullOrWhiteSpace(vm.VmId)
            && string.Equals(vm.VmId, normalizedVmId, StringComparison.OrdinalIgnoreCase));

        return vmById?.Name ?? string.Empty;
    }

    private void ApplyPreferredMonitorSource(VmResourceMonitorRuntimeState state, bool isVmRunning, DateTimeOffset now, bool enqueueHistory)
    {
        if (!isVmRunning)
        {
            state.State = "Guest nicht erreichbar";
            state.ActiveSource = "none";
            return;
        }

        var guestFresh = (now - state.LastGuestSeenUtc) <= TimeSpan.FromMilliseconds(Math.Max(_monitorIntervalMs * 3, (int)MonitorAgentTimeout.TotalMilliseconds));
        var hostFresh = (now - state.LastHostSeenUtc) <= TimeSpan.FromMilliseconds(Math.Max(_monitorIntervalMs * 4, (int)HostMonitorTimeout.TotalMilliseconds));

        if (guestFresh)
        {
            state.State = "Connected";
            state.ActiveSource = "guest";
            state.CpuPercent = state.GuestCpuPercent;
            state.RamUsedGb = state.GuestRamUsedGb;
            state.RamTotalGb = state.GuestRamTotalGb;
        }
        else if (hostFresh)
        {
            state.State = "Connected";
            state.ActiveSource = "host";
            state.CpuPercent = state.HostCpuPercent;
            state.RamUsedGb = state.HostRamUsedGb;
            state.RamTotalGb = state.HostRamTotalGb;
        }
        else
        {
            state.State = "Guest nicht erreichbar";
            state.ActiveSource = "none";
            return;
        }

        if (!enqueueHistory)
        {
            return;
        }

        EnqueueHistory(state.CpuHistory, state.CpuPercent);
        var pressure = state.RamTotalGb <= 0 ? 0 : (state.RamUsedGb / state.RamTotalGb) * 100d;
        EnqueueHistory(state.RamPressureHistory, Math.Clamp(pressure, 0d, 100d));
    }

    private string ResolveVmNameForMonitorPacket(string rawPacketVmName, string? sourceVmId)
    {
        var normalizedVmId = (sourceVmId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedVmId))
        {
            var byVmId = AvailableVms.FirstOrDefault(vm =>
                !string.IsNullOrWhiteSpace(vm.VmId)
                && string.Equals(vm.VmId, normalizedVmId, StringComparison.OrdinalIgnoreCase));
            if (byVmId is not null)
            {
                return byVmId.Name;
            }
        }

        var normalized = (rawPacketVmName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var exactByName = AvailableVms.FirstOrDefault(vm =>
            string.Equals(vm.Name, normalized, StringComparison.OrdinalIgnoreCase));
        if (exactByName is not null)
        {
            return exactByName.Name;
        }

        var exactByLabel = AvailableVms.FirstOrDefault(vm =>
            !string.IsNullOrWhiteSpace(vm.Label)
            && string.Equals(vm.Label, normalized, StringComparison.OrdinalIgnoreCase));
        if (exactByLabel is not null)
        {
            return exactByLabel.Name;
        }

        var byDisplayLabel = AvailableVms.FirstOrDefault(vm =>
            !string.IsNullOrWhiteSpace(vm.DisplayLabel)
            && (string.Equals(vm.DisplayLabel, normalized, StringComparison.OrdinalIgnoreCase)
                || vm.DisplayLabel.EndsWith(" - " + normalized, StringComparison.OrdinalIgnoreCase)
                || vm.DisplayLabel.StartsWith(normalized + " - ", StringComparison.OrdinalIgnoreCase)));
        if (byDisplayLabel is not null)
        {
            return byDisplayLabel.Name;
        }

        // Guest monitor packets often carry the guest computer name, not the Hyper-V VM name.
        // If there is exactly one running VM, bind the packet to that VM.
        var runningVms = AvailableVms
            .Where(vm => IsRunningState(vm.RuntimeState))
            .ToList();

        if (runningVms.Count == 1)
        {
            return runningVms[0].Name;
        }

        // If multiple are running, prefer currently selected running VM as best-effort mapping.
        if (SelectedVm is not null
            && IsRunningState(SelectedVm.RuntimeState)
            && runningVms.Any(vm => string.Equals(vm.Name, SelectedVm.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return SelectedVm.Name;
        }

        return normalized;
    }

    public ResourceMonitorSnapshot GetResourceMonitorSnapshot()
    {
        lock (_resourceMonitorSync)
        {
            EnsureFreshHostResourceSampleIfNeeded();

            List<VmResourceMonitorSnapshot> vmStates;
            try
            {
                vmStates = AvailableVms
                    .Select(vm =>
                    {
                        if (!_vmMonitorStates.TryGetValue(vm.Name, out var state))
                        {
                            state = new VmResourceMonitorRuntimeState
                            {
                                VmName = vm.Name,
                                State = "Guest nicht erreichbar"
                            };
                        }

                        var pressure = state.RamTotalGb <= 0 ? 0 : (state.RamUsedGb / state.RamTotalGb) * 100d;

                        return new VmResourceMonitorSnapshot
                        {
                            VmName = vm.DisplayLabel,
                            State = state.State,
                            ActiveSource = state.ActiveSource,
                            CpuPercent = state.CpuPercent,
                            RamUsedGb = state.RamUsedGb,
                            RamTotalGb = state.RamTotalGb,
                            GuestCpuPercent = state.LastGuestSeenUtc == DateTimeOffset.MinValue ? null : state.GuestCpuPercent,
                            GuestRamUsedGb = state.LastGuestSeenUtc == DateTimeOffset.MinValue ? null : state.GuestRamUsedGb,
                            GuestRamTotalGb = state.LastGuestSeenUtc == DateTimeOffset.MinValue ? null : state.GuestRamTotalGb,
                            HostCpuPercent = state.LastHostSeenUtc == DateTimeOffset.MinValue ? null : state.HostCpuPercent,
                            HostRamUsedGb = state.LastHostSeenUtc == DateTimeOffset.MinValue ? null : state.HostRamUsedGb,
                            HostRamTotalGb = state.LastHostSeenUtc == DateTimeOffset.MinValue ? null : state.HostRamTotalGb,
                            RamPressurePercent = Math.Clamp(pressure, 0d, 100d),
                            CpuHistory = state.CpuHistory.ToArray(),
                            RamPressureHistory = state.RamPressureHistory.ToArray()
                        };
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                // Keep host metrics live even if VM collection changes concurrently during a snapshot tick.
                Log.Debug(ex, "Resource monitor snapshot VM projection failed; using VM monitor-state fallback.");

                vmStates = _vmMonitorStates.Values
                    .Select(state =>
                    {
                        var pressure = state.RamTotalGb <= 0 ? 0 : (state.RamUsedGb / state.RamTotalGb) * 100d;
                        return new VmResourceMonitorSnapshot
                        {
                            VmName = state.VmName,
                            State = state.State,
                            ActiveSource = state.ActiveSource,
                            CpuPercent = state.CpuPercent,
                            RamUsedGb = state.RamUsedGb,
                            RamTotalGb = state.RamTotalGb,
                            GuestCpuPercent = state.LastGuestSeenUtc == DateTimeOffset.MinValue ? null : state.GuestCpuPercent,
                            GuestRamUsedGb = state.LastGuestSeenUtc == DateTimeOffset.MinValue ? null : state.GuestRamUsedGb,
                            GuestRamTotalGb = state.LastGuestSeenUtc == DateTimeOffset.MinValue ? null : state.GuestRamTotalGb,
                            HostCpuPercent = state.LastHostSeenUtc == DateTimeOffset.MinValue ? null : state.HostCpuPercent,
                            HostRamUsedGb = state.LastHostSeenUtc == DateTimeOffset.MinValue ? null : state.HostRamUsedGb,
                            HostRamTotalGb = state.LastHostSeenUtc == DateTimeOffset.MinValue ? null : state.HostRamTotalGb,
                            RamPressurePercent = Math.Clamp(pressure, 0d, 100d),
                            CpuHistory = state.CpuHistory.ToArray(),
                            RamPressureHistory = state.RamPressureHistory.ToArray()
                        };
                    })
                    .OrderBy(item => item.VmName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            var hostPressure = _hostRamTotalGb <= 0 ? 0 : (_hostRamUsedGb / _hostRamTotalGb) * 100d;

            return new ResourceMonitorSnapshot
            {
                Enabled = _monitorEnabled,
                IntervalMs = _monitorIntervalMs,
                HistorySize = _monitorHistorySize,
                HostCpuPercent = _hostCpuPercent,
                HostRamUsedGb = _hostRamUsedGb,
                HostRamTotalGb = _hostRamTotalGb,
                HostRamPressurePercent = Math.Clamp(hostPressure, 0d, 100d),
                HostCpuHistory = _hostCpuHistory.ToArray(),
                HostRamPressureHistory = _hostRamPressureHistory.ToArray(),
                VmSnapshots = vmStates
            };
        }
    }

    private void EnsureFreshHostResourceSampleIfNeeded()
    {
        var now = DateTimeOffset.UtcNow;
        var maxAge = TimeSpan.FromMilliseconds(Math.Clamp(_monitorIntervalMs, 500, 5000) * 2);
        if ((now - _lastHostResourceSampleUtc) <= maxAge)
        {
            return;
        }

        try
        {
            var (cpu, ramUsed, ramTotal) = _resourceMonitorHostSampler.Sample();
            _hostCpuPercent = Math.Clamp(cpu, 0d, 100d);
            _hostRamUsedGb = Math.Max(0d, ramUsed);
            _hostRamTotalGb = Math.Max(0d, ramTotal);

            EnqueueHistory(_hostCpuHistory, _hostCpuPercent);
            var pressure = _hostRamTotalGb <= 0 ? 0 : (_hostRamUsedGb / _hostRamTotalGb) * 100d;
            EnqueueHistory(_hostRamPressureHistory, Math.Clamp(pressure, 0d, 100d));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Fallback host resource sampling failed during snapshot retrieval.");
        }
        finally
        {
            _lastHostResourceSampleUtc = now;
        }
    }

    public void ReconcileVmMonitoringRuntimeStates()
    {
        lock (_resourceMonitorSync)
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var vm in AvailableVms)
            {
                if (!_vmMonitorStates.TryGetValue(vm.Name, out var state))
                {
                    state = new VmResourceMonitorRuntimeState
                    {
                        VmName = vm.Name
                    };
                    _vmMonitorStates[vm.Name] = state;
                }

                if (!IsRunningState(vm.RuntimeState))
                {
                    ApplyPreferredMonitorSource(state, isVmRunning: false, now, enqueueHistory: false);
                }
                else
                {
                    ApplyPreferredMonitorSource(state, isVmRunning: true, now, enqueueHistory: false);
                }
            }

            var validNames = AvailableVms.Select(vm => vm.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var staleKeys = _vmMonitorStates.Keys.Where(key => !validNames.Contains(key)).ToList();
            foreach (var staleKey in staleKeys)
            {
                _vmMonitorStates.Remove(staleKey);
            }
        }

        ApplyVmMonitorStateToVmDefinitions();
        ResourceMonitorVersion++;
    }

    private void ApplyVmMonitorStateToVmDefinitions()
    {
        lock (_resourceMonitorSync)
        {
            foreach (var vm in AvailableVms)
            {
                if (!_vmMonitorStates.TryGetValue(vm.Name, out var state))
                {
                    vm.MonitorStateText = "Guest nicht erreichbar";
                    vm.MonitorCpuText = "CPU -";
                    vm.MonitorRamText = "RAM -";
                    continue;
                }

                if (!string.Equals(state.State, "Connected", StringComparison.OrdinalIgnoreCase))
                {
                    vm.MonitorStateText = "Guest nicht erreichbar";
                    vm.MonitorCpuText = "CPU -";
                    vm.MonitorRamText = "RAM -";
                    continue;
                }

                vm.MonitorStateText = "Connected";
                vm.MonitorCpuText = $"CPU {state.CpuPercent:0.#}%";
                vm.MonitorRamText = $"RAM {state.RamUsedGb:0.0}GB";
            }
        }
    }

    private void EnqueueHistory(Queue<double> queue, double value)
    {
        queue.Enqueue(value);
        TrimHistoryQueue(queue);
    }

    private void TrimHistoryQueue(Queue<double> queue)
    {
        while (queue.Count > _monitorHistorySize)
        {
            queue.Dequeue();
        }
    }

    public async Task<bool> RenameVmByNameAsync(string? vmName, string? newVmName)
    {
        var currentName = (vmName ?? string.Empty).Trim();
        var targetName = (newVmName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(currentName) || string.IsNullOrWhiteSpace(targetName))
        {
            AddNotification("VM umbenennen abgebrochen: Name fehlt.", "Warning");
            return false;
        }

        if (string.Equals(currentName, targetName, StringComparison.OrdinalIgnoreCase))
        {
            AddNotification("VM umbenennen abgebrochen: Neuer Name ist identisch.", "Info");
            return false;
        }

        if (AvailableVms.Any(vm => !string.Equals(vm.Name, currentName, StringComparison.OrdinalIgnoreCase)
                                   && string.Equals(vm.Name, targetName, StringComparison.OrdinalIgnoreCase)))
        {
            AddNotification($"VM umbenennen abgebrochen: '{targetName}' existiert bereits.", "Warning");
            return false;
        }

        var renamed = false;
        await ExecuteBusyActionAsync($"VM '{currentName}' wird umbenannt...", async token =>
        {
            await _hyperVService.RenameVmAsync(currentName, targetName, token);
            RenameVmReferencesInState(currentName, targetName);
            AddNotification($"VM '{currentName}' wurde zu '{targetName}' umbenannt.", "Success");
            renamed = true;
        }, showNotificationOnErrorOnly: true);

        if (!renamed)
        {
            return false;
        }

        await LoadVmsFromHyperVAsync();
        await RefreshVmStatusByNameAsync(targetName);
        NotifyTrayStateChanged();
        MarkConfigDirty();
        return true;
    }

    public async Task<VmComputeSettingsInfo?> GetVmComputeSettingsAsync(string? vmName)
    {
        var resolvedVmName = (vmName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(resolvedVmName))
        {
            return null;
        }

        try
        {
            return await _hyperVService.GetVmComputeSettingsAsync(resolvedVmName, _lifetimeCancellation.Token);
        }
        catch (Exception ex)
        {
            AddNotification($"CPU/RAM-Konfiguration konnte nicht geladen werden: {ex.Message}", "Error");
            return null;
        }
    }

    public async Task<bool> UpdateVmComputeResourcesAsync(string? vmName, int cpuCount, int startupMemoryGb)
    {
        var resolvedVmName = (vmName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(resolvedVmName))
        {
            AddNotification("CPU/RAM-Konfiguration abgebrochen: VM-Name fehlt.", "Warning");
            return false;
        }

        var vm = AvailableVms.FirstOrDefault(item => string.Equals(item.Name, resolvedVmName, StringComparison.OrdinalIgnoreCase));
        var runtimeState = vm?.RuntimeState ?? string.Empty;
        if (!IsVmStoppedForComputeSettings(runtimeState))
        {
            AddNotification(
                $"CPU/RAM-Änderung nur bei gestoppter VM möglich. Aktueller Status von '{resolvedVmName}': {runtimeState}.",
                "Warning");
            return false;
        }

        var limits = await GetVmComputeSettingsAsync(resolvedVmName);
        if (limits is null)
        {
            return false;
        }

        var normalizedCpu = Math.Clamp(cpuCount, limits.MinCpuCount, limits.MaxCpuCount);
        var normalizedMemoryGb = Math.Clamp(startupMemoryGb, limits.MinStartupMemoryGb, limits.MaxStartupMemoryGb);
        var updated = false;

        await ExecuteBusyActionAsync($"CPU/RAM für '{resolvedVmName}' wird aktualisiert...", async token =>
        {
            await _hyperVService.SetVmComputeSettingsAsync(resolvedVmName, normalizedCpu, normalizedMemoryGb, token);
            AddNotification($"'{resolvedVmName}' aktualisiert: {normalizedCpu} vCPU, {normalizedMemoryGb} GB RAM.", "Success");
            updated = true;
        }, showNotificationOnErrorOnly: true);

        if (updated)
        {
            await RefreshVmStatusByNameAsync(resolvedVmName);
        }

        return updated;
    }

    private static bool IsVmStoppedForComputeSettings(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        return state.Contains("Off", StringComparison.OrdinalIgnoreCase)
               || state.Contains("Aus", StringComparison.OrdinalIgnoreCase)
               || state.Contains("Stopped", StringComparison.OrdinalIgnoreCase)
               || state.Contains("Heruntergefahren", StringComparison.OrdinalIgnoreCase)
               || state.Contains("Ausgeschaltet", StringComparison.OrdinalIgnoreCase);
    }

    private void RenameVmReferencesInState(string oldName, string newName)
    {
        if (string.Equals(DefaultVmName, oldName, StringComparison.OrdinalIgnoreCase))
        {
            DefaultVmName = newName;
        }

        if (string.Equals(LastSelectedVmName, oldName, StringComparison.OrdinalIgnoreCase))
        {
            LastSelectedVmName = newName;
        }

        for (var i = 0; i < _trayVmNames.Count; i++)
        {
            if (string.Equals(_trayVmNames[i], oldName, StringComparison.OrdinalIgnoreCase))
            {
                _trayVmNames[i] = newName;
            }
        }

        _trayVmNames = NormalizeTrayVmNames(_trayVmNames);

        if (_configuredVmDefinitions.TryGetValue(oldName, out var configured))
        {
            _configuredVmDefinitions.Remove(oldName);
            configured.Name = newName;
            if (string.Equals(configured.Label, oldName, StringComparison.OrdinalIgnoreCase))
            {
                configured.Label = newName;
            }

            _configuredVmDefinitions[newName] = configured;
        }

        foreach (var vm in AvailableVms)
        {
            if (!string.Equals(vm.Name, oldName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            vm.Name = newName;
            if (string.Equals(vm.Label, oldName, StringComparison.OrdinalIgnoreCase))
            {
                vm.Label = newName;
            }
        }

        if (SelectedVm is not null && string.Equals(SelectedVm.Name, oldName, StringComparison.OrdinalIgnoreCase))
        {
            SelectedVm.Name = newName;
        }

        if (SelectedVmForConfig is not null && string.Equals(SelectedVmForConfig.Name, oldName, StringComparison.OrdinalIgnoreCase))
        {
            SelectedVmForConfig.Name = newName;
        }

        if (SelectedDefaultVmForConfig is not null && string.Equals(SelectedDefaultVmForConfig.Name, oldName, StringComparison.OrdinalIgnoreCase))
        {
            SelectedDefaultVmForConfig.Name = newName;
        }
    }

    private void SetSelectedVmInternal(VmDefinition? vm)
    {
        _selectedVmChangeSuppressionDepth++;
        try
        {
            SelectedVm = vm;
        }
        finally
        {
            _selectedVmChangeSuppressionDepth--;
        }
    }

    private async Task ReloadConfigAsync()
    {
        await ExecuteBusyActionAsync("Konfiguration wird neu geladen...", _ =>
        {
            var configResult = _configService.LoadOrCreate(_configPath);
            var config = configResult.Config;
            var previousSelectionName = SelectedVm?.Name;

            _configChangeSuppressionDepth++;
            try
            {
                WindowTitle = "HyperTool";
                ConfigurationNotice = configResult.Notice;
                HnsEnabled = config.Hns.Enabled;
                HnsAutoRestartAfterDefaultSwitch = config.Hns.AutoRestartAfterDefaultSwitch;
                HnsAutoRestartAfterAnyConnect = config.Hns.AutoRestartAfterAnyConnect;
                DefaultVmName = config.DefaultVmName;
                LastSelectedVmName = config.LastSelectedVmName;
                VmConnectComputerName = NormalizeVmConnectComputerName(config.VmConnectComputerName);
                UiEnableTrayIcon = true;
                UiEnableTrayMenu = config.Ui.EnableTrayMenu;
                UiStartMinimized = config.Ui.StartMinimized;
                UiStartWithWindows = config.Ui.StartWithWindows;
                UiOpenConsoleAfterVmStart = config.Ui.OpenConsoleAfterVmStart;
                UiRestoreNumLockAfterVmStart = config.Ui.RestoreNumLockAfterVmStart;
                UiDebugLoggingEnabled = config.Ui.DebugLoggingEnabled;
                _lastAppliedDebugLoggingEnabled = UiDebugLoggingEnabled;
                _uiNumLockWatcherIntervalSeconds = Math.Clamp(config.Ui.NumLockWatcherIntervalSeconds, 5, 600);
                UiOpenVmConnectWithSessionEdit = config.Ui.OpenVmConnectWithSessionEdit;
                UiTheme = NormalizeUiTheme(config.Ui.Theme);
                ApplyConfiguredVmDefinitions(config.Vms);
                _trayVmNames = NormalizeTrayVmNames(config.Ui.TrayVmNames);
                UpdateCheckOnStartup = config.Update.CheckOnStartup;
                GithubOwner = config.Update.GitHubOwner;
                GithubRepo = config.Update.GitHubRepo;
                DefaultVmImportDestinationPath = config.DefaultVmImportDestinationPath?.Trim() ?? string.Empty;
                _monitorEnabled = config.Monitoring.Enabled;
                _monitorIntervalMs = config.Monitoring.IntervalMs;
                _monitorHistoryMinutes = config.Monitoring.GraphHistoryMinutes;
                _monitorHistorySize = config.Monitoring.GraphHistorySize;
                LoadCheckpointDescriptionOverrides(config);
                _usbAutoShareDeviceKeys.Clear();
                foreach (var key in config.Usb.AutoShareDeviceKeys)
                {
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        _usbAutoShareDeviceKeys.Add(key.Trim());
                    }
                }
                _usbHardwareIdentityMigrationCompleted = config.Usb.HardwareIdentityMigrationCompleted;
                _usbConfigResetMigrationApplied = config.Usb.UsbConfigResetMigrationApplied;
                ApplyConfiguredSharedFolders(config.SharedFolders.HostDefinitions);

                if (!_usbConfigResetMigrationApplied)
                {
                    _usbAutoShareDeviceKeys.Clear();
                    _usbDeviceMetadataByKey.Clear();
                    _usbMetadataBusAliasByKey.Clear();
                    _usbMetadataBusAliasExpiresUtc.Clear();
                    _usbHardwareIdentityMigrationCompleted = true;
                    _usbConfigResetMigrationApplied = true;
                    PersistUsbAutoShareConfig();
                    AddNotification("USB-Konfiguration wurde für das neue Mapping einmalig zurückgesetzt. Bitte Auto-Share und Kommentare neu setzen.", "Info");
                    ShowUsbResetMigrationInfoOnce();
                }

                var removedLegacyUsbKeysOnReload = PurgeLegacyUsbBusIdKeysInMemory();
                if (removedLegacyUsbKeysOnReload)
                {
                    PersistUsbAutoShareConfig();
                }

                _suppressUsbAutoShareToggleHandling = true;
                try
                {
                    SelectedUsbDeviceAutoShareEnabled = SelectedUsbDevice is not null
                        && IsUsbAutoShareEnabledForDevice(SelectedUsbDevice);
                }
                finally
                {
                    _suppressUsbAutoShareToggleHandling = false;
                }
            }
            finally
            {
                _configChangeSuppressionDepth--;
            }

            if (string.IsNullOrWhiteSpace(LastSelectedVmName) && !string.IsNullOrWhiteSpace(previousSelectionName))
            {
                LastSelectedVmName = previousSelectionName;
            }

            AddNotification("Konfiguration neu geladen.", "Info");
            return Task.CompletedTask;
        });

        await LoadVmsFromHyperVAsync();
        await RefreshSwitchesAsync();
        NotifyTrayStateChanged();
        MarkConfigClean();
    }

    private void ApplyConfiguredVmDefinitions(IEnumerable<VmDefinition>? configuredVms)
    {
        _configuredVmDefinitions.Clear();

        if (configuredVms is null)
        {
            return;
        }

        foreach (var vm in configuredVms)
        {
            if (vm is null || string.IsNullOrWhiteSpace(vm.Name))
            {
                continue;
            }

            var vmName = vm.Name.Trim();
            _configuredVmDefinitions[vmName] = new VmDefinition
            {
                Name = vmName,
                Label = string.IsNullOrWhiteSpace(vm.Label) ? vmName : vm.Label.Trim(),
                TrayAdapterName = vm.TrayAdapterName?.Trim() ?? string.Empty,
                OpenConsoleWithSessionEdit = vm.OpenConsoleWithSessionEdit
            };
        }
    }

    private void ApplyConfiguredSharedFolders(IEnumerable<HostSharedFolderDefinition>? sharedFolders)
    {
        HostSharedFolders.Clear();

        if (sharedFolders is null)
        {
            return;
        }

        foreach (var folder in sharedFolders)
        {
            if (folder is null)
            {
                continue;
            }

            var normalized = NormalizeSharedFolderDefinition(folder);
            if (string.IsNullOrWhiteSpace(normalized.LocalPath)
                || string.IsNullOrWhiteSpace(normalized.ShareName))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(normalized.Id))
            {
                normalized.Id = Guid.NewGuid().ToString("N");
            }

            HostSharedFolders.Add(normalized);
        }
    }

    private static HostSharedFolderDefinition NormalizeSharedFolderDefinition(HostSharedFolderDefinition definition)
    {
        return new HostSharedFolderDefinition
        {
            Id = (definition.Id ?? string.Empty).Trim(),
            Label = (definition.Label ?? string.Empty).Trim(),
            LocalPath = (definition.LocalPath ?? string.Empty).Trim(),
            ShareName = (definition.ShareName ?? string.Empty).Trim(),
            Enabled = definition.Enabled,
            ReadOnly = definition.ReadOnly
        };
    }

    private static HostSharedFolderDefinition CloneSharedFolderDefinition(HostSharedFolderDefinition definition)
    {
        return new HostSharedFolderDefinition
        {
            Id = definition.Id,
            Label = definition.Label,
            LocalPath = definition.LocalPath,
            ShareName = definition.ShareName,
            Enabled = definition.Enabled,
            ReadOnly = definition.ReadOnly
        };
    }

    private static List<string> NormalizeTrayVmNames(IEnumerable<string>? vmNames)
    {
        if (vmNames is null)
        {
            return [];
        }

        return vmNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task UnshareAllSharedUsbOnShutdownAsync()
    {
        if (IsBusy)
        {
            try
            {
                await _usbIpService.ShutdownElevatedSessionAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Elevated USB session could not be closed during shutdown while busy.");
            }

            return;
        }

        try
        {
            await UnshareAllSharedUsbAsync(
                timeout: TimeSpan.FromSeconds(20),
                successMessage: "Beim Beenden wurden {0} USB-Freigabe(n) entfernt.",
                failedMessage: "Beim Beenden konnten {0} USB-Freigabe(n) nicht entfernt werden.",
                logContext: "shutdown");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "USB-Freigaben konnten beim Beenden nicht vollständig entfernt werden.");
        }
        finally
        {
            try
            {
                await _usbIpService.ShutdownElevatedSessionAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Elevated USB session could not be closed during shutdown cleanup.");
            }
        }
    }

    private async Task UnshareAllSharedUsbAsync(TimeSpan timeout, string successMessage, string failedMessage, string logContext)
    {
        using var cts = new CancellationTokenSource(timeout);
        IReadOnlyList<UsbIpDeviceInfo> devices;
        try
        {
            devices = await _usbIpService.GetDevicesAsync(cts.Token);
        }
        catch (Exception ex) when (IsUsbRuntimeMissing(ex.Message))
        {
            Log.Information("USB-Unshare übersprungen ({Context}): usbipd-win nicht verfügbar.", logContext);
            UsbRuntimeAvailable = false;
            UsbRuntimeHintText = "USB-Funktion deaktiviert: usbipd-win ist nicht installiert. Quelle: https://github.com/dorssel/usbipd-win";
            return;
        }

        var sharedDevices = devices.Where(device => device.IsShared).ToList();

        if (sharedDevices.Count == 0)
        {
            return;
        }

        var released = 0;
        var failed = 0;

        foreach (var device in sharedDevices)
        {
            try
            {
                if (device.IsAttached && !string.IsNullOrWhiteSpace(device.BusId))
                {
                    await _usbIpService.DetachAsync(device.BusId, cts.Token);
                }

                if (!string.IsNullOrWhiteSpace(device.BusId))
                {
                    await _usbIpService.UnbindAsync(device.BusId, cts.Token);
                }
                else if (!string.IsNullOrWhiteSpace(device.PersistedGuid))
                {
                    await _usbIpService.UnbindByPersistedGuidAsync(device.PersistedGuid, cts.Token);
                }
                else
                {
                    failed++;
                    continue;
                }

                released++;
            }
            catch (Exception ex)
            {
                failed++;
                Log.Warning(ex,
                    "Freigeben von USB-Gerät fehlgeschlagen ({Context}) (BusId={BusId}, Guid={Guid}).",
                    logContext,
                    device.BusId,
                    device.PersistedGuid);
            }
        }

        if (released > 0)
        {
            AddNotification(string.Format(successMessage, released), "Info");
        }

        if (failed > 0)
        {
            AddNotification(string.Format(failedMessage, failed), "Warning");
        }
    }

    private static string NormalizeVmConnectComputerName(string? computerName)
    {
        var normalized = computerName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalized)
            || string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return Environment.MachineName;
        }

        return normalized;
    }

    private bool ShouldOpenConsoleWithSessionEdit(string vmName)
    {
        var vm = AvailableVms.FirstOrDefault(item => string.Equals(item.Name, vmName, StringComparison.OrdinalIgnoreCase))
                 ?? _configuredVmDefinitions.Values.FirstOrDefault(item => string.Equals(item.Name, vmName, StringComparison.OrdinalIgnoreCase));

        if (vm is not null)
        {
            return vm.OpenConsoleWithSessionEdit;
        }

        return UiOpenVmConnectWithSessionEdit;
    }

    public bool TryPromptSaveConfigOnClose()
    {
        return TryPromptSaveConfigChanges();
    }

    private bool ShouldPromptSaveWhenLeavingConfig(int newMenuIndex)
    {
        return _lastSelectedMenuIndex == ConfigMenuIndex
               && newMenuIndex != ConfigMenuIndex
               && HasPendingConfigChanges
               && !IsBusy;
    }

    private bool TryPromptSaveConfigChanges()
    {
        if (!CanPromptSaveOnClose)
        {
            return true;
        }

        var result = _uiInteropService.ShowUnsavedConfigPrompt();

        if (result == UnsavedConfigPromptResult.Cancel)
        {
            return false;
        }

        if (result == UnsavedConfigPromptResult.No)
        {
            ReloadConfigSnapshotWithoutRuntimeRefresh();
            return true;
        }

        SaveConfigCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        return !HasPendingConfigChanges;
    }

    private void MarkConfigDirty()
    {
        if (_configChangeSuppressionDepth > 0)
        {
            return;
        }

        HasPendingConfigChanges = true;
    }

    private void ReloadConfigSnapshotWithoutRuntimeRefresh()
    {
        var configResult = _configService.LoadOrCreate(_configPath);
        var config = configResult.Config;
        var previousSelectionName = SelectedVm?.Name;

        _configChangeSuppressionDepth++;
        try
        {
            WindowTitle = "HyperTool";
            ConfigurationNotice = configResult.Notice;
            HnsEnabled = config.Hns.Enabled;
            HnsAutoRestartAfterDefaultSwitch = config.Hns.AutoRestartAfterDefaultSwitch;
            HnsAutoRestartAfterAnyConnect = config.Hns.AutoRestartAfterAnyConnect;
            DefaultVmName = config.DefaultVmName;
            LastSelectedVmName = config.LastSelectedVmName;
            VmConnectComputerName = NormalizeVmConnectComputerName(config.VmConnectComputerName);
            UiEnableTrayIcon = true;
            UiEnableTrayMenu = config.Ui.EnableTrayMenu;
            UiStartMinimized = config.Ui.StartMinimized;
            UiStartWithWindows = config.Ui.StartWithWindows;
            UiOpenConsoleAfterVmStart = config.Ui.OpenConsoleAfterVmStart;
            UiRestoreNumLockAfterVmStart = config.Ui.RestoreNumLockAfterVmStart;
            UiDebugLoggingEnabled = config.Ui.DebugLoggingEnabled;
            _lastAppliedDebugLoggingEnabled = UiDebugLoggingEnabled;
            UiOpenVmConnectWithSessionEdit = config.Ui.OpenVmConnectWithSessionEdit;
            UiTheme = NormalizeUiTheme(config.Ui.Theme);
            ApplyConfiguredVmDefinitions(config.Vms);
            _trayVmNames = NormalizeTrayVmNames(config.Ui.TrayVmNames);
            UpdateCheckOnStartup = config.Update.CheckOnStartup;
            GithubOwner = config.Update.GitHubOwner;
            GithubRepo = config.Update.GitHubRepo;
            HostUsbSharingEnabled = config.Usb.Enabled;
            UsbAutoDetachOnClientDisconnect = config.Usb.AutoDetachOnClientDisconnect;
            _usbAutoDetachRetryAttempts = Math.Clamp(config.Usb.AutoDetachRetryAttempts, 1, 10);
            _usbAutoDetachGracePeriod = TimeSpan.FromSeconds(Math.Clamp(config.Usb.AutoDetachGracePeriodSeconds, 5, 300));
            _usbAutoDetachRetryDelay = TimeSpan.FromMilliseconds(Math.Clamp(config.Usb.AutoDetachRetryDelayMs, 100, 5000));
            UsbUnshareOnExit = config.Usb.UnshareOnExit;
            _monitorEnabled = config.Monitoring.Enabled;
            _monitorIntervalMs = config.Monitoring.IntervalMs;
            _monitorHistoryMinutes = config.Monitoring.GraphHistoryMinutes;
            _monitorHistorySize = config.Monitoring.GraphHistorySize;
            HostSharedFoldersEnabled = config.SharedFolders.Enabled;

            _usbAutoShareDeviceKeys.Clear();
            foreach (var key in config.Usb.AutoShareDeviceKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _usbAutoShareDeviceKeys.Add(key.Trim());
                }
            }
            _usbHardwareIdentityMigrationCompleted = config.Usb.HardwareIdentityMigrationCompleted;
            _usbConfigResetMigrationApplied = config.Usb.UsbConfigResetMigrationApplied;

            _usbDeviceMetadataByKey.Clear();
            _usbMetadataBusAliasByKey.Clear();
            _usbMetadataBusAliasExpiresUtc.Clear();
            LoadCheckpointDescriptionOverrides(config);
            foreach (var metadata in config.Usb.DeviceMetadata)
            {
                if (metadata is null)
                {
                    continue;
                }

                var deviceKey = (metadata.DeviceKey ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(deviceKey))
                {
                    continue;
                }

                var customName = (metadata.CustomName ?? string.Empty).Trim();
                var comment = (metadata.Comment ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(customName) && string.IsNullOrWhiteSpace(comment))
                {
                    continue;
                }

                _usbDeviceMetadataByKey[deviceKey] = new UsbDeviceMetadataEntry
                {
                    DeviceKey = deviceKey,
                    CustomName = customName,
                    Comment = comment
                };
            }

            var removedLegacyUsbKeysOnReload = PurgeLegacyUsbBusIdKeysInMemory();
            if (removedLegacyUsbKeysOnReload)
            {
                PersistUsbAutoShareConfig();
            }

            if (!_usbConfigResetMigrationApplied)
            {
                _usbAutoShareDeviceKeys.Clear();
                _usbDeviceMetadataByKey.Clear();
                _usbMetadataBusAliasByKey.Clear();
                _usbMetadataBusAliasExpiresUtc.Clear();
                _usbHardwareIdentityMigrationCompleted = true;
                _usbConfigResetMigrationApplied = true;
                PersistUsbAutoShareConfig();
                AddNotification("USB-Konfiguration wurde für das neue Mapping einmalig zurückgesetzt. Bitte Auto-Share und Kommentare neu setzen.", "Info");
                ShowUsbResetMigrationInfoOnce();
            }

            ApplyConfiguredSharedFolders(config.SharedFolders.HostDefinitions);

            _suppressUsbAutoShareToggleHandling = true;
            try
            {
                SelectedUsbDeviceAutoShareEnabled = SelectedUsbDevice is not null
                    && IsUsbAutoShareEnabledForDevice(SelectedUsbDevice);
            }
            finally
            {
                _suppressUsbAutoShareToggleHandling = false;
            }
        }
        finally
        {
            _configChangeSuppressionDepth--;
        }

        if (string.IsNullOrWhiteSpace(LastSelectedVmName) && !string.IsNullOrWhiteSpace(previousSelectionName))
        {
            LastSelectedVmName = previousSelectionName;
        }

        MarkConfigClean();
        NotifyTrayStateChanged();
        AddNotification("Konfiguration neu geladen.", "Info");
    }

    private void MarkConfigClean()
    {
        HasPendingConfigChanges = false;
    }

    private void ShowUsbResetMigrationInfoOnce()
    {
        if (_usbResetMigrationInfoShown)
        {
            return;
        }

        _usbResetMigrationInfoShown = true;
        try
        {
            _uiInteropService.ShowInfoMessage(
                "USB-Konfiguration zurückgesetzt",
                "Die alte USB-Zuordnung wurde einmalig bereinigt. Bitte Auto-Share und Kommentare neu setzen.");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "USB reset migration info popup could not be shown.");
        }
    }

    private static string NormalizeUiTheme(string? theme)
    {
        if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            || string.Equals(theme, "Bright", StringComparison.OrdinalIgnoreCase))
        {
            return "Light";
        }

        return "Dark";
    }

    private async Task PersistSelectedVmAsync(string vmName)
    {
        try
        {
            var configResult = _configService.LoadOrCreate(_configPath);
            var config = configResult.Config;
            config.LastSelectedVmName = vmName;

            _ = _configService.TrySave(_configPath, config, out _);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Selected VM could not be persisted.");
        }
    }

    private async Task StartVmByNameAsync(string? vmName)
    {
        if (string.IsNullOrWhiteSpace(vmName))
        {
            return;
        }

        await ExecuteBusyActionAsync($"VM '{vmName}' wird gestartet...", async token =>
        {
            await _hyperVService.StartVmAsync(vmName, token);
            AddNotification($"VM '{vmName}' gestartet.", "Success");

            if (UiOpenConsoleAfterVmStart)
            {
                await _hyperVService.OpenVmConnectAsync(vmName, VmConnectComputerName, ShouldOpenConsoleWithSessionEdit(vmName), token);
                AddNotification($"Konsole für '{vmName}' geöffnet.", "Info");
            }
        });

        await RefreshVmStatusByNameAsync(vmName);
    }

    private async Task RunNumLockWatcherAsync()
    {
        var token = _lifetimeCancellation.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (UiRestoreNumLockAfterVmStart && !IsNumLockEnabled())
                {
                    ToggleNumLockKey();
                }

                var intervalSeconds = Math.Clamp(_uiNumLockWatcherIntervalSeconds, 5, 600);
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "NumLock watcher iteration failed.");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private static bool IsNumLockEnabled()
    {
        return (GetKeyState(VkNumLock) & 0x1) != 0;
    }

    private static void ToggleNumLockKey()
    {
        keybd_event((byte)VkNumLock, 0x45, KeyeventfExtendedKey, UIntPtr.Zero);
        keybd_event((byte)VkNumLock, 0x45, KeyeventfExtendedKey | KeyeventfKeyUp, UIntPtr.Zero);
    }

    private async Task StopVmByNameAsync(string? vmName)
    {
        if (string.IsNullOrWhiteSpace(vmName))
        {
            return;
        }

        await ExecuteBusyActionAsync($"VM '{vmName}' wird gestoppt...", async token =>
        {
            await _hyperVService.StopVmGracefulAsync(vmName, token);
            AddNotification($"VM '{vmName}' gestoppt.", "Success");
        });

        await RefreshVmStatusByNameAsync(vmName);
    }

    private async Task TurnOffVmByNameAsync(string? vmName)
    {
        if (string.IsNullOrWhiteSpace(vmName))
        {
            return;
        }

        await ExecuteBusyActionAsync($"VM '{vmName}' wird hart ausgeschaltet...", async token =>
        {
            await _hyperVService.TurnOffVmAsync(vmName, token);
            AddNotification($"VM '{vmName}' hart ausgeschaltet.", "Warning");
        });

        await RefreshVmStatusByNameAsync(vmName);
    }

    private async Task RestartVmByNameAsync(string? vmName)
    {
        if (string.IsNullOrWhiteSpace(vmName))
        {
            return;
        }

        await ExecuteBusyActionAsync($"VM '{vmName}' wird neu gestartet...", async token =>
        {
            await _hyperVService.RestartVmAsync(vmName, token);
            AddNotification($"VM '{vmName}' neu gestartet.", "Success");
        });

        await RefreshVmStatusByNameAsync(vmName);
    }

    private async Task OpenConsoleByNameAsync(string? vmName)
    {
        if (string.IsNullOrWhiteSpace(vmName))
        {
            return;
        }

        await ExecuteBusyActionAsync($"Konsole für '{vmName}' wird geöffnet...", async token =>
        {
            await _hyperVService.OpenVmConnectAsync(vmName, VmConnectComputerName, ShouldOpenConsoleWithSessionEdit(vmName), token);
            AddNotification($"Konsole für '{vmName}' geöffnet.", "Info");
        });
    }

    private async Task CreateSnapshotByNameAsync(string? vmName)
    {
        if (string.IsNullOrWhiteSpace(vmName))
        {
            return;
        }

        var checkpointName = $"checkpoint-{DateTime.Now:yyyyMMdd-HHmmss}";
        await ExecuteBusyActionAsync($"Snapshot für '{vmName}' wird erstellt...", async token =>
        {
            await _hyperVService.CreateCheckpointAsync(vmName, checkpointName, null, token);
            AddNotification($"Checkpoint '{checkpointName}' für '{vmName}' erstellt.", "Success");
        });
    }

    private void ClearNotifications()
    {
        Notifications.Clear();
    }

    private void CopyNotificationsToClipboard()
    {
        var lines = Notifications
            .Select(entry => $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{entry.Level}] {entry.Message}")
            .ToArray();

        var text = lines.Length == 0 ? "Keine Notifications vorhanden." : string.Join(Environment.NewLine, lines);
        _uiInteropService.SetClipboardText(text);
        AddNotification("Notifications in Zwischenablage kopiert.", "Info");
    }

    private async Task CheckForUpdatesAsync()
    {
        await ExecuteBusyActionAsync("Prüfe GitHub-Version...", async token =>
        {
            var updateOwner = NormalizeGitHubOwnerForUpdate(GithubOwner);
            var result = await _updateService.CheckForUpdateAsync(
                updateOwner,
                GithubRepo,
                AppVersion,
                token,
                "HyperTool-Setup");

            if (!string.Equals(GithubOwner, updateOwner, StringComparison.Ordinal))
            {
                GithubOwner = updateOwner;
            }

            UpdateStatus = result.Message;
            ReleaseUrl = result.ReleaseUrl ?? string.Empty;
            InstallerDownloadUrl = result.InstallerDownloadUrl ?? string.Empty;
            InstallerFileName = result.InstallerFileName ?? string.Empty;
            UpdateInstallAvailable = result.HasUpdate && !string.IsNullOrWhiteSpace(InstallerDownloadUrl);

            if (!result.Success)
            {
                AddNotification(result.Message, "Warning");
                return;
            }

            if (result.HasUpdate && !UpdateInstallAvailable)
            {
                AddNotification("Update gefunden, aber kein Installer-Asset im Release erkannt. Bitte Release-Seite öffnen.", "Warning");
            }

            AddNotification(result.Message, result.HasUpdate ? "Success" : "Info");
        });
    }

    private async Task InstallUpdateAsync()
    {
        if (string.IsNullOrWhiteSpace(InstallerDownloadUrl))
        {
            AddNotification("Kein Installer-Download verfügbar.", "Warning");
            return;
        }

        if (HasPendingConfigChanges)
        {
            AddNotification("Ungespeicherte Einstellungen werden vor dem Update gespeichert...", "Info");
            await SaveConfigAsync();

            if (HasPendingConfigChanges)
            {
                AddNotification("Update abgebrochen: Konfiguration konnte nicht sicher gespeichert werden.", "Error");
                return;
            }
        }

        await ExecuteBusyActionAsync("Update wird heruntergeladen...", async token =>
        {
            var targetDirectory = Path.Combine(Path.GetTempPath(), "HyperTool", "updates");
            Directory.CreateDirectory(targetDirectory);

            var fileName = ResolveInstallerFileName(InstallerDownloadUrl, InstallerFileName);
            var installerPath = Path.Combine(targetDirectory, fileName);

            using var response = await UpdateDownloadClient.GetAsync(InstallerDownloadUrl, token);
            response.EnsureSuccessStatusCode();

            await using (var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fileStream, token);
            }

            AddNotification($"Installer heruntergeladen: {installerPath}", "Success");

            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            });

            AddNotification("Installer gestartet. HyperTool wird beendet.", "Info");
            _uiInteropService.ShutdownApplication();
        });
    }

    private static string ResolveInstallerFileName(string downloadUrl, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return fileName.Trim();
        }

        if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
        {
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return "HyperTool-Setup.exe";
    }

    private static string NormalizeGitHubOwnerForUpdate(string? owner)
    {
        var normalized = (owner ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || string.Equals(normalized, "koerby", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "kaktools", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "KaKTools", StringComparison.OrdinalIgnoreCase))
        {
            return "KaKTools";
        }

        return normalized;
    }

    private void OpenReleasePage()
    {
        if (string.IsNullOrWhiteSpace(ReleaseUrl))
        {
            AddNotification("Keine Release-URL verfügbar.", "Warning");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ReleaseUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AddNotification($"Release-Seite konnte nicht geöffnet werden: {ex.Message}", "Error");
        }
    }

    private void ToggleLog()
    {
        IsLogExpanded = !IsLogExpanded;
    }

    private void OpenLogFile()
    {
        var logDirectoryPath = ResolveLogDirectoryPath();

        try
        {
            Directory.CreateDirectory(logDirectoryPath);

            Process.Start(new ProcessStartInfo
            {
                FileName = logDirectoryPath,
                UseShellExecute = true
            });

            AddNotification($"Log-Ordner geöffnet: {logDirectoryPath}", "Info");
        }
        catch (Exception ex)
        {
            AddNotification($"Log-Ordner konnte nicht geöffnet werden: {ex.Message}", "Error");
        }
    }

    private void SelectVmFromChip(VmDefinition? vm)
    {
        if (vm is null)
        {
            return;
        }

        var selected = AvailableVms.FirstOrDefault(item => string.Equals(item.Name, vm.Name, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            SelectedVm = selected;
        }
    }

    private void OnNotificationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(LatestNotification));
        OnPropertyChanged(nameof(LastNotificationText));
    }

    private void NotifyTrayStateChanged()
    {
        TrayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PublishNotification(string message, string level = "Info")
    {
        AddNotification(message, level);
    }

    private static string ResolveAppVersion()
    {
        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return FormatVersionForDisplay(informationalVersion);
        }

        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        return FormatVersionForDisplay(assemblyVersion);
    }

    private static string FormatVersionForDisplay(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "0.0.0";
        }

        var raw = version.Trim();
        var plusIndex = raw.IndexOf('+');
        if (plusIndex >= 0)
        {
            raw = raw[..plusIndex];
        }

        raw = raw.TrimStart('v', 'V');

        if (Version.TryParse(raw, out var parsed))
        {
            if (parsed.Build > 0)
            {
                if (parsed.Revision > 0)
                {
                    return $"{parsed.Major}.{parsed.Minor}.{parsed.Build}.{parsed.Revision}";
                }

                return $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";
            }

            return $"{parsed.Major}.{parsed.Minor}";
        }

        return raw;
    }

    private static string FormatByteSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private async Task ExecuteBusyActionAsync(string busyText, Func<CancellationToken, Task> action, bool showNotificationOnErrorOnly = false)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyText = busyText;
        BusyProgressPercent = -1;

        try
        {
            await action(_lifetimeCancellation.Token);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Warning(ex, "Aktion fehlgeschlagen (Berechtigung): {BusyText}", busyText);
            AddNotification(ex.Message, "Warning");
            StatusText = "Keine Berechtigung";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Aktion fehlgeschlagen: {BusyText}", busyText);
            AddNotification($"Fehler: {ex.Message}", "Error");
            StatusText = "Fehler";
        }
        finally
        {
            IsBusy = false;
            BusyText = "Bitte warten...";
            BusyProgressPercent = -1;

            if (!showNotificationOnErrorOnly && !StatusText.Equals("Fehler", StringComparison.OrdinalIgnoreCase))
            {
                StatusText = "Bereit";
            }
        }
    }

    private void AddNotification(string message, string level)
    {
        var entry = new UiNotification
        {
            Message = message,
            Level = level,
            Timestamp = DateTime.Now
        };

        Notifications.Insert(0, entry);
        while (Notifications.Count > 200)
        {
            Notifications.RemoveAt(Notifications.Count - 1);
        }

        Log.Information("UI Notification ({Level}): {Message}", level, message);
    }

    private string? PickFolderPath(string description)
    {
        return _uiInteropService.PickFolderPath(description);
    }

    private static string ResolveLogDirectoryPath()
    {
        var logDirectoryCandidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyperTool", "logs"),
            Path.Combine(AppContext.BaseDirectory, "logs"),
            Path.Combine(Path.GetTempPath(), "HyperTool", "logs")
        };

        foreach (var directory in logDirectoryCandidates)
        {
            if (Directory.Exists(directory))
            {
                return directory;
            }
        }

        return logDirectoryCandidates[0];
    }
}