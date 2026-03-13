namespace HyperTool.Models;

public sealed class HyperToolConfig
{
    public string DefaultVmName { get; set; } = string.Empty;

    public string LastSelectedVmName { get; set; } = string.Empty;

    public List<VmDefinition> Vms { get; set; } = [];

    public string DefaultSwitchName { get; set; } = "Default Switch";

    public string VmConnectComputerName { get; set; } = Environment.MachineName;

    public HnsSettings Hns { get; set; } = new();

    public UiSettings Ui { get; set; } = new();

    public UpdateSettings Update { get; set; } = new();

    public UsbSettings Usb { get; set; } = new();

    public SharedFolderSettings SharedFolders { get; set; } = new();

    public MonitoringSettings Monitoring { get; set; } = new();

    public string DefaultVmImportDestinationPath { get; set; } = string.Empty;

    public static HyperToolConfig CreateDefault() => new();
}

public sealed class HnsSettings
{
    public bool Enabled { get; set; }

    public bool AutoRestartAfterDefaultSwitch { get; set; }

    public bool AutoRestartAfterAnyConnect { get; set; }
}

public sealed class UiSettings
{
    public string WindowTitle { get; set; } = "HyperTool";

    public string Theme { get; set; } = "Dark";

    public bool DebugLoggingEnabled { get; set; }

    public bool StartMinimized { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    public bool EnableTrayIcon { get; set; } = true;

    public bool EnableTrayMenu { get; set; } = true;

    public bool StartWithWindows { get; set; } = true;

    public bool OpenConsoleAfterVmStart { get; set; } = true;

    public bool RestoreNumLockAfterVmStart { get; set; }

    public int NumLockWatcherIntervalSeconds { get; set; } = 30;

    public bool OpenVmConnectWithSessionEdit { get; set; }

    public List<string> TrayVmNames { get; set; } = [];
}

public sealed class UpdateSettings
{
    public bool CheckOnStartup { get; set; } = true;

    public string GitHubOwner { get; set; } = "KaKTools";

    public string GitHubRepo { get; set; } = "HyperTool";
}

public sealed class UsbSettings
{
    public bool Enabled { get; set; } = true;

    public bool AutoDetachOnClientDisconnect { get; set; } = true;

    public int AutoDetachRetryAttempts { get; set; } = 3;

    public int AutoDetachGracePeriodSeconds { get; set; } = 90;

    public int AutoDetachRetryDelayMs { get; set; } = 450;

    public bool UnshareOnExit { get; set; } = true;

    public List<string> AutoShareDeviceKeys { get; set; } = [];

    public List<UsbDeviceMetadataEntry> DeviceMetadata { get; set; } = [];

    public bool HardwareIdentityMigrationCompleted { get; set; }

    public bool UsbConfigResetMigrationApplied { get; set; }
}

public sealed class SharedFolderSettings
{
    public bool Enabled { get; set; } = true;

    public List<HostSharedFolderDefinition> HostDefinitions { get; set; } = [];
}

public sealed class MonitoringSettings
{
    public bool Enabled { get; set; } = true;

    public int IntervalMs { get; set; } = 1000;

    public int GraphHistoryMinutes { get; set; } = 5;

    public int GraphHistorySize { get; set; } = 300;
}