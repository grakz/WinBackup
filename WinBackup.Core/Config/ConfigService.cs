using System.Text.Json;

namespace WinBackup.Core.Config;

/// <summary>
/// Reads and writes <see cref="BackupConfig"/> to a JSON file. Loading a missing or
/// malformed file returns a fresh default config rather than throwing, so the app can
/// always start and surface a first-run setup flow.
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Loads config from <paramref name="path"/>. Returns a default <see cref="BackupConfig"/>
    /// when the file does not exist or cannot be parsed.
    /// </summary>
    public BackupConfig Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new BackupConfig();
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BackupConfig>(json, Options) ?? new BackupConfig();
        }
        catch (JsonException)
        {
            // Malformed config must never crash the app; fall back to defaults.
            return new BackupConfig();
        }
    }

    /// <summary>Serializes <paramref name="config"/> to <paramref name="path"/>, creating the directory if needed.</summary>
    public void Save(string path, BackupConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(config);

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(config, Options);
        File.WriteAllText(path, json);
    }
}
