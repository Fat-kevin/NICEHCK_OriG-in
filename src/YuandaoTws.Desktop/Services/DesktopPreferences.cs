using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace YuandaoTws.Desktop.Services;

public enum BatteryColorMode
{
    Automatic,
}

public sealed class DesktopPreferences
{
    public bool TaskbarWidgetEnabled { get; set; } = true;
    public BatteryColorMode BatteryColorMode { get; set; } = BatteryColorMode.Automatic;
    public string BatteryAccentColor { get; set; } = "#46A8EC";
    public string ChargingColor { get; set; } = "#50E5A0";
}

/// <summary>桌面显示设置的唯一持久化入口。坏配置只会回退到默认值，不阻断应用启动。</summary>
public sealed class DesktopPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _sync = new();
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YuandaoTws",
        "settings.json");
    private DesktopPreferences _current;

    public DesktopPreferencesService()
    {
        _current = LoadFromDisk();
    }

    public DesktopPreferences Current
    {
        get
        {
            lock (_sync)
            {
                return Clone(_current);
            }
        }
    }

    public event EventHandler? PreferencesChanged;

    public void Update(Action<DesktopPreferences> update)
    {
        lock (_sync)
        {
            update(_current);
            Normalize(_current);
            SaveToDisk(_current);
        }

        PreferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetColors()
    {
        Update(preferences =>
        {
            preferences.BatteryColorMode = BatteryColorMode.Automatic;
            preferences.BatteryAccentColor = "#46A8EC";
            preferences.ChargingColor = "#50E5A0";
        });
    }

    private DesktopPreferences LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new DesktopPreferences();
            }

            var json = File.ReadAllText(_path);
            var value = JsonSerializer.Deserialize<DesktopPreferences>(json, JsonOptions) ?? new DesktopPreferences();
            Normalize(value);
            return value;
        }
        catch (Exception)
        {
            return new DesktopPreferences();
        }
    }

    private void SaveToDisk(DesktopPreferences value)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporaryPath, _path, true);
    }

    private static DesktopPreferences Clone(DesktopPreferences value) => new()
    {
        TaskbarWidgetEnabled = value.TaskbarWidgetEnabled,
        BatteryColorMode = value.BatteryColorMode,
        BatteryAccentColor = value.BatteryAccentColor,
        ChargingColor = value.ChargingColor,
    };

    private static void Normalize(DesktopPreferences value)
    {
        if (!Enum.IsDefined(value.BatteryColorMode))
        {
            value.BatteryColorMode = BatteryColorMode.Automatic;
        }

        value.BatteryAccentColor = BatteryColorResolver.NormalizeHex(value.BatteryAccentColor, "#46A8EC");
        value.ChargingColor = BatteryColorResolver.NormalizeHex(value.ChargingColor, "#50E5A0");
    }
}
