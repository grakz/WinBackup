using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WinBackup.Core.Pipes;

namespace WinBackup.Core.Volume;

/// <summary>
/// Provides a VSS shadow-copy root for a volume, creating at most one snapshot per volume per backup
/// session and reusing it for every locked file on that volume. Snapshots are deleted at session end.
/// </summary>
public interface IVssCoordinator
{
    /// <summary>Shadow device root for the volume containing <paramref name="volume"/> (e.g. "C:\"), or null if unavailable.</summary>
    Task<string?> GetShadowRootAsync(string volume, CancellationToken ct = default);

    /// <summary>Deletes all snapshots created during this session.</summary>
    Task DeleteAllAsync(CancellationToken ct = default);
}

/// <summary>
/// <see cref="IVssCoordinator"/> backed by the elevated helper. Caches the snapshot device path per
/// volume so repeated locked files do not each trigger a new (expensive) snapshot.
/// </summary>
public sealed class VssCoordinator : IVssCoordinator
{
    private readonly IElevatedHelperClient _client;
    private readonly ILogger _log;
    private readonly Dictionary<string, string> _shadowRootByVolume = new(StringComparer.OrdinalIgnoreCase);

    public VssCoordinator(IElevatedHelperClient client, ILogger? log = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _log = log ?? NullLogger.Instance;
    }

    public async Task<string?> GetShadowRootAsync(string volume, CancellationToken ct = default)
    {
        string key = NormalizeVolume(volume);
        if (_shadowRootByVolume.TryGetValue(key, out string? cached))
        {
            return cached;
        }

        HelperResponse response = await _client
            .SendCommandAsync(new HelperRequest { Command = HelperCommand.VssSnapshot, Volume = key }, ct)
            .ConfigureAwait(false);

        if (!response.Success || string.IsNullOrEmpty(response.Data))
        {
            _log.LogWarning("VSS snapshot for {Volume} failed: {Error}", key, response.Error);
            return null;
        }

        _shadowRootByVolume[key] = response.Data;
        return response.Data;
    }

    public async Task DeleteAllAsync(CancellationToken ct = default)
    {
        foreach (string shadowRoot in _shadowRootByVolume.Values)
        {
            await _client.SendCommandAsync(
                new HelperRequest { Command = HelperCommand.VssDeleteSnapshot, SnapshotId = shadowRoot }, ct)
                .ConfigureAwait(false);
        }

        _shadowRootByVolume.Clear();
    }

    private static string NormalizeVolume(string volume)
    {
        string? root = Path.GetPathRoot(volume);
        return string.IsNullOrEmpty(root) ? volume : root;
    }
}
