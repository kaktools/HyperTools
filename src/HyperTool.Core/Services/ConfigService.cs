using HyperTool.Models;
using Serilog;
using System.IO;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class ConfigService : IConfigService
{
    private const int CurrentConfigSchemaVersion = 2;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ConfigLoadResult LoadOrCreate(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                var defaultConfig = HyperToolConfig.CreateDefault();

                var couldWriteDefault = TryWriteConfig(configPath, defaultConfig);

                Log.Warning("Config file not found. Created default config at {ConfigPath}", configPath);
                return new ConfigLoadResult
                {
                    Config = defaultConfig,
                    ConfigPath = configPath,
                    IsGenerated = true,
                    Notice = couldWriteDefault
                        ? "Konfiguration fehlte und wurde als Beispiel erzeugt. Bitte HyperTool.config.json prüfen."
                        : "Konfiguration fehlte und konnte wegen fehlender Schreibrechte nicht gespeichert werden. HyperTool läuft mit In-Memory-Defaults."
                };
            }

            var raw = File.ReadAllText(configPath);
            var loaded = JsonSerializer.Deserialize<HyperToolConfig>(raw, SerializerOptions) ?? HyperToolConfig.CreateDefault();
            var (validated, wasUpdated, notice) = ValidateAndNormalize(loaded);

            if (wasUpdated)
            {
                var couldWriteValidated = TryWriteConfig(configPath, validated);
                if (couldWriteValidated)
                {
                    Log.Warning("Config was normalized and rewritten at {ConfigPath}", configPath);
                }
                else
                {
                    notice = string.IsNullOrWhiteSpace(notice)
                        ? "Konfiguration wurde korrigiert, konnte aber nicht gespeichert werden (fehlende Schreibrechte)."
                        : $"{notice} Konfiguration konnte nicht gespeichert werden (fehlende Schreibrechte).";
                }
            }

            return new ConfigLoadResult
            {
                Config = validated,
                ConfigPath = configPath,
                HasValidationFixes = wasUpdated,
                Notice = notice
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load config. Recreating default config at {ConfigPath}", configPath);

            var fallbackConfig = HyperToolConfig.CreateDefault();
            var couldWriteFallback = TryWriteConfig(configPath, fallbackConfig);

            return new ConfigLoadResult
            {
                Config = fallbackConfig,
                ConfigPath = configPath,
                IsGenerated = true,
                Notice = couldWriteFallback
                    ? "Konfiguration war ungültig und wurde auf ein Beispiel zurückgesetzt."
                    : "Konfiguration war ungültig und konnte wegen fehlender Schreibrechte nicht gespeichert werden. HyperTool läuft mit In-Memory-Defaults."
            };
        }
    }

    public bool TrySave(string configPath, HyperToolConfig config, out string? errorMessage)
    {
        try
        {
            var (validated, _, _) = ValidateAndNormalize(config);
            var success = TryWriteConfig(configPath, validated);

            if (success)
            {
                errorMessage = null;
                Log.Information("Config saved to {ConfigPath}", configPath);
                return true;
            }

            errorMessage = "Konfiguration konnte nicht gespeichert werden (Schreibrechte prüfen).";
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Config save failed for {ConfigPath}", configPath);
            errorMessage = ex.Message;
            return false;
        }
    }

    private static bool TryWriteConfig(string configPath, HyperToolConfig config)
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var json = JsonSerializer.Serialize(config, SerializerOptions);
            File.WriteAllText(configPath, json);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Config write failed for {ConfigPath}", configPath);
            return false;
        }
    }

    private static (HyperToolConfig Config, bool WasUpdated, string? Notice) ValidateAndNormalize(HyperToolConfig config)
    {
        var wasUpdated = false;
        var notices = new List<string>();

        if (config.ConfigSchemaVersion != CurrentConfigSchemaVersion)
        {
            config.ConfigSchemaVersion = CurrentConfigSchemaVersion;
            wasUpdated = true;
            notices.Add("Konfigurationsschema wurde aktualisiert und veraltete Artefakte wurden bereinigt.");
        }

        if (config.Vms is null)
        {
            config.Vms = [];
            wasUpdated = true;
        }
        else
        {
            var normalizedVms = config.Vms
                .Where(vm => vm is not null)
                .Select(vm => new VmDefinition
                {
                    Name = vm.Name?.Trim() ?? string.Empty,
                    Label = vm.Label?.Trim() ?? string.Empty,
                    TrayAdapterName = vm.TrayAdapterName?.Trim() ?? string.Empty,
                    OpenConsoleWithSessionEdit = vm.OpenConsoleWithSessionEdit
                })
                .Where(vm => !string.IsNullOrWhiteSpace(vm.Name))
                .ToList();

            if (normalizedVms.Count != config.Vms.Count)
            {
                wasUpdated = true;
            }

            config.Vms = normalizedVms;
        }

        foreach (var vm in config.Vms)
        {
            vm.Name = vm.Name?.Trim() ?? string.Empty;
            vm.TrayAdapterName = vm.TrayAdapterName?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(vm.Label))
            {
                vm.Label = vm.Name;
                wasUpdated = true;
            }
        }

        config.DefaultVmName = config.DefaultVmName?.Trim() ?? string.Empty;
        config.LastSelectedVmName = config.LastSelectedVmName?.Trim() ?? string.Empty;

        if (config.Vms.Count > 0)
        {
            var vmExists = config.Vms.Any(vm => string.Equals(vm.Name, config.DefaultVmName, StringComparison.OrdinalIgnoreCase));
            if (!vmExists)
            {
                config.DefaultVmName = config.Vms.FirstOrDefault()?.Name ?? string.Empty;
                wasUpdated = true;
                if (!string.IsNullOrWhiteSpace(config.DefaultVmName))
                {
                    notices.Add("DefaultVmName war ungültig und wurde auf die erste VM gesetzt.");
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(config.DefaultVmName))
        {
            config.DefaultVmName = string.Empty;
            wasUpdated = true;
        }

        if (string.IsNullOrWhiteSpace(config.DefaultSwitchName))
        {
            config.DefaultSwitchName = "Default Switch";
            wasUpdated = true;
            notices.Add("DefaultSwitchName war leer und wurde auf 'Default Switch' gesetzt.");
        }

        if (string.IsNullOrWhiteSpace(config.VmConnectComputerName))
        {
            config.VmConnectComputerName = Environment.MachineName;
            wasUpdated = true;
        }
        else if (string.Equals(config.VmConnectComputerName.Trim(), "localhost", StringComparison.OrdinalIgnoreCase))
        {
            config.VmConnectComputerName = Environment.MachineName;
            wasUpdated = true;
        }

        config.Hns ??= new HnsSettings();
        config.Ui ??= new UiSettings();
        config.Update ??= new UpdateSettings();
        config.Usb ??= new UsbSettings();
        config.SharedFolders ??= new SharedFolderSettings();
        config.Monitoring ??= new MonitoringSettings();
        config.Checkpoints ??= new CheckpointSettings();

        if (config.Checkpoints.DescriptionOverrides is null)
        {
            config.Checkpoints.DescriptionOverrides = [];
            wasUpdated = true;
        }
        else
        {
            var normalizedCheckpointOverrides = config.Checkpoints.DescriptionOverrides
                .Where(entry => entry is not null)
                .Select(entry => new CheckpointDescriptionOverrideEntry
                {
                    Key = entry.Key?.Trim() ?? string.Empty,
                    Description = entry.Description?.Trim() ?? string.Empty
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key)
                                && !string.IsNullOrWhiteSpace(entry.Description))
                .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedCheckpointOverrides.Count != config.Checkpoints.DescriptionOverrides.Count)
            {
                wasUpdated = true;
            }

            config.Checkpoints.DescriptionOverrides = normalizedCheckpointOverrides;
        }

        if (config.Ui.TrayVmNames is null)
        {
            config.Ui.TrayVmNames = [];
            wasUpdated = true;
        }
        else
        {
            var normalizedTrayVmNames = config.Ui.TrayVmNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedTrayVmNames.Count != config.Ui.TrayVmNames.Count)
            {
                wasUpdated = true;
            }

            config.Ui.TrayVmNames = normalizedTrayVmNames;
        }

        if (config.Usb.AutoShareDeviceKeys is null)
        {
            config.Usb.AutoShareDeviceKeys = [];
            wasUpdated = true;
        }
        else
        {
            var normalizedAutoShareKeys = config.Usb.AutoShareDeviceKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sanitizedAutoShareKeys = normalizedAutoShareKeys
                .Where(IsSupportedUsbDeviceKey)
                .ToList();

            if (normalizedAutoShareKeys.Count != config.Usb.AutoShareDeviceKeys.Count
                || sanitizedAutoShareKeys.Count != normalizedAutoShareKeys.Count)
            {
                wasUpdated = true;
            }

            config.Usb.AutoShareDeviceKeys = sanitizedAutoShareKeys;
        }

        if (config.Usb.DeviceMetadata is null)
        {
            config.Usb.DeviceMetadata = [];
            wasUpdated = true;
        }
        else
        {
            var normalizedMetadata = config.Usb.DeviceMetadata
                .Where(entry => entry is not null)
                .Select(entry => new UsbDeviceMetadataEntry
                {
                    DeviceKey = entry.DeviceKey?.Trim() ?? string.Empty,
                    CustomName = entry.CustomName?.Trim() ?? string.Empty,
                    Comment = entry.Comment?.Trim() ?? string.Empty
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.DeviceKey)
                                && (!string.IsNullOrWhiteSpace(entry.CustomName)
                                    || !string.IsNullOrWhiteSpace(entry.Comment)))
                .Where(entry => IsSupportedUsbDeviceKey(entry.DeviceKey))
                .GroupBy(entry => entry.DeviceKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(entry => entry.DeviceKey, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedMetadata.Count != config.Usb.DeviceMetadata.Count)
            {
                wasUpdated = true;
            }

            config.Usb.DeviceMetadata = normalizedMetadata;
        }

        var normalizedAutoDetachRetryAttempts = Math.Clamp(config.Usb.AutoDetachRetryAttempts, 1, 10);
        if (config.Usb.AutoDetachRetryAttempts != normalizedAutoDetachRetryAttempts)
        {
            config.Usb.AutoDetachRetryAttempts = normalizedAutoDetachRetryAttempts;
            wasUpdated = true;
            notices.Add("Usb.AutoDetachRetryAttempts war außerhalb des gültigen Bereichs und wurde korrigiert (1-10).");
        }

        var normalizedAutoDetachGracePeriodSeconds = Math.Clamp(config.Usb.AutoDetachGracePeriodSeconds, 5, 300);
        if (config.Usb.AutoDetachGracePeriodSeconds != normalizedAutoDetachGracePeriodSeconds)
        {
            config.Usb.AutoDetachGracePeriodSeconds = normalizedAutoDetachGracePeriodSeconds;
            wasUpdated = true;
            notices.Add("Usb.AutoDetachGracePeriodSeconds war außerhalb des gültigen Bereichs und wurde korrigiert (5-300).");
        }

        var normalizedAutoDetachRetryDelayMs = Math.Clamp(config.Usb.AutoDetachRetryDelayMs, 100, 5000);
        if (config.Usb.AutoDetachRetryDelayMs != normalizedAutoDetachRetryDelayMs)
        {
            config.Usb.AutoDetachRetryDelayMs = normalizedAutoDetachRetryDelayMs;
            wasUpdated = true;
            notices.Add("Usb.AutoDetachRetryDelayMs war außerhalb des gültigen Bereichs und wurde korrigiert (100-5000).");
        }

        if (config.SharedFolders.HostDefinitions is null)
        {
            config.SharedFolders.HostDefinitions = [];
            wasUpdated = true;
        }
        else
        {
            var normalizedSharedFolderDefinitions = config.SharedFolders.HostDefinitions
                .Where(definition => definition is not null)
                .Select(definition => new HostSharedFolderDefinition
                {
                    Id = string.IsNullOrWhiteSpace(definition.Id)
                        ? Guid.NewGuid().ToString("N")
                        : definition.Id.Trim(),
                    Label = definition.Label?.Trim() ?? string.Empty,
                    LocalPath = definition.LocalPath?.Trim() ?? string.Empty,
                    ShareName = definition.ShareName?.Trim() ?? string.Empty,
                    Enabled = definition.Enabled,
                    ReadOnly = definition.ReadOnly
                })
                .Where(definition => !string.IsNullOrWhiteSpace(definition.LocalPath)
                                     && !string.IsNullOrWhiteSpace(definition.ShareName))
                .GroupBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (normalizedSharedFolderDefinitions.Count != config.SharedFolders.HostDefinitions.Count)
            {
                wasUpdated = true;
            }

            config.SharedFolders.HostDefinitions = normalizedSharedFolderDefinitions;
        }

        if (string.IsNullOrWhiteSpace(config.Ui.WindowTitle))
        {
            config.Ui.WindowTitle = "HyperTool";
            wasUpdated = true;
        }

        var normalizedTheme = NormalizeTheme(config.Ui.Theme);
        if (!string.Equals(config.Ui.Theme, normalizedTheme, StringComparison.Ordinal))
        {
            config.Ui.Theme = normalizedTheme;
            wasUpdated = true;
        }

        var normalizedNumLockWatcherIntervalSeconds = Math.Clamp(config.Ui.NumLockWatcherIntervalSeconds, 5, 600);
        if (config.Ui.NumLockWatcherIntervalSeconds != normalizedNumLockWatcherIntervalSeconds)
        {
            config.Ui.NumLockWatcherIntervalSeconds = normalizedNumLockWatcherIntervalSeconds;
            wasUpdated = true;
            notices.Add("Ui.NumLockWatcherIntervalSeconds war außerhalb des gültigen Bereichs und wurde korrigiert (5-600).");
        }

        if (!config.Ui.EnableTrayIcon)
        {
            config.Ui.EnableTrayIcon = true;
            wasUpdated = true;
        }

        var normalizedMonitoringIntervalMs = config.Monitoring.IntervalMs switch
        {
            500 => 500,
            1000 => 1000,
            2000 => 2000,
            5000 => 5000,
            _ => 1000
        };

        if (config.Monitoring.IntervalMs != normalizedMonitoringIntervalMs)
        {
            config.Monitoring.IntervalMs = normalizedMonitoringIntervalMs;
            wasUpdated = true;
            notices.Add("Monitoring.IntervalMs war ungültig und wurde auf 1000 ms gesetzt (erlaubt: 500/1000/2000/5000). ");
        }

        var normalizedMonitoringHistoryMinutes = Math.Clamp(config.Monitoring.GraphHistoryMinutes, 1, 30);
        if (config.Monitoring.GraphHistoryMinutes != normalizedMonitoringHistoryMinutes)
        {
            config.Monitoring.GraphHistoryMinutes = normalizedMonitoringHistoryMinutes;
            wasUpdated = true;
        }

        var expectedHistorySize = (int)Math.Max(1, Math.Round((config.Monitoring.GraphHistoryMinutes * 60_000d) / config.Monitoring.IntervalMs));
        var normalizedMonitoringHistorySize = Math.Clamp(config.Monitoring.GraphHistorySize, 30, 3600);

        if (normalizedMonitoringHistorySize != expectedHistorySize)
        {
            config.Monitoring.GraphHistorySize = expectedHistorySize;
            wasUpdated = true;
        }

        var normalizedGitHubOwner = NormalizeGitHubOwner(config.Update.GitHubOwner);
        if (!string.Equals(config.Update.GitHubOwner, normalizedGitHubOwner, StringComparison.Ordinal))
        {
            config.Update.GitHubOwner = normalizedGitHubOwner;
            wasUpdated = true;
        }

        if (string.IsNullOrWhiteSpace(config.Update.GitHubRepo))
        {
            config.Update.GitHubRepo = "HyperTool";
            wasUpdated = true;
        }

        var notice = notices.Count == 0 ? null : string.Join(" ", notices);
        return (config, wasUpdated, notice);
    }

    private static bool IsSupportedUsbDeviceKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalized = key.Trim();
        return normalized.StartsWith("hardware:", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("instance:", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("guid:", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTheme(string? theme)
    {
        if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            || string.Equals(theme, "Bright", StringComparison.OrdinalIgnoreCase))
        {
            return "Light";
        }

        return "Dark";
    }

    private static string NormalizeGitHubOwner(string? owner)
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
}