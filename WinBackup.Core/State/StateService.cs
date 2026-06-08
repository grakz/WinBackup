using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinBackup.Core.State;

/// <summary>
/// Reads and writes <see cref="BackupState"/> (backup history) to a JSON file and provides
/// the history queries the engines rely on (e.g. the last successful SSD backup, which drives
/// incremental cutoffs). A missing or malformed file yields empty state rather than throwing.
/// </summary>
public sealed class StateService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public BackupState Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new BackupState();
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BackupState>(json, Options) ?? new BackupState();
        }
        catch (JsonException)
        {
            return new BackupState();
        }
    }

    public void Save(string path, BackupState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(state);

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(state, Options);
        File.WriteAllText(path, json);
    }

    /// <summary>Appends <paramref name="record"/> to the state at <paramref name="path"/> and persists it.</summary>
    public void AddRecord(string path, BackupRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        BackupState state = Load(path);
        state.Records.Add(record);
        Save(path, state);
    }

    /// <summary>
    /// Returns the most recent successful (or partially successful) record for <paramref name="target"/>,
    /// or <c>null</c> if none exists. "Most recent" is by <see cref="BackupRecord.CompletedAt"/>.
    /// </summary>
    public BackupRecord? GetLastSuccessful(BackupState state, BackupTarget target)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.Records
            .Where(r => r.Target == target && IsSuccessful(r.ResultCode))
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefault();
    }

    private static bool IsSuccessful(BackupResultCode code) =>
        code is BackupResultCode.Success or BackupResultCode.PartialSuccess;
}
