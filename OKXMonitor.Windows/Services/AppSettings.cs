using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OKXMonitor.Services;

public enum LayoutKind { Portrait, Landscape }

public sealed class WindowGeometry
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
}

/// Non-secret settings persisted to %AppData%\OKXMonitor\settings.json.
/// Credentials NEVER live here — see CredentialStore.
public sealed class AppSettings
{
    public string Host { get; set; } = OkxClient.DefaultHost;
    public double RefreshInterval { get; set; } = 5;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LayoutKind LastLayout { get; set; } = LayoutKind.Portrait;

    public WindowGeometry Portrait { get; set; } = new();
    public WindowGeometry Landscape { get; set; } = new();

    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OKXMonitor");
    static string FilePath => Path.Combine(Dir, "settings.json");

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Opts) ?? new();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
        }
        catch { }
    }

    public WindowGeometry GeometryFor(LayoutKind k) => k == LayoutKind.Portrait ? Portrait : Landscape;
}
