using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinBackup.Core.Pipes;

/// <summary>Operations the elevated helper can perform on behalf of the (non-elevated) main app.</summary>
public enum HelperCommand
{
    Lock,
    Dismount,
    Eject,
    RemoveMountPoint,
    Remount,
    VssSnapshot,
    VssDeleteSnapshot,
    Exit,
}

/// <summary>A command sent to the elevated helper over the named pipe.</summary>
public sealed class HelperRequest
{
    public HelperCommand Command { get; set; }

    /// <summary>Target volume (e.g. drive letter "X:" or volume path), where applicable.</summary>
    public string? Volume { get; set; }

    /// <summary>Existing snapshot id for <see cref="HelperCommand.VssDeleteSnapshot"/>.</summary>
    public string? SnapshotId { get; set; }
}

/// <summary>The helper's reply to a <see cref="HelperRequest"/>.</summary>
public sealed class HelperResponse
{
    public bool Success { get; set; }

    public string? Error { get; set; }

    /// <summary>Command-specific payload (e.g. the shadow-copy device path for a VSS snapshot).</summary>
    public string? Data { get; set; }

    public static HelperResponse Ok(string? data = null) => new() { Success = true, Data = data };

    public static HelperResponse Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Newline-delimited JSON framing for helper messages, shared by the client (main app) and the
/// server (elevated helper). Kept transport-agnostic (works over any <see cref="Stream"/>) so it
/// can be unit-tested without a real named pipe.
/// </summary>
public static class ElevatedProtocol
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task WriteMessageAsync<T>(Stream stream, T message, CancellationToken ct = default)
    {
        string json = JsonSerializer.Serialize(message, Options);
        byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<T?> ReadMessageAsync<T>(Stream stream, CancellationToken ct = default)
    {
        var buffer = new List<byte>(256);
        var one = new byte[1];
        while (true)
        {
            int read = await stream.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (read == 0)
            {
                if (buffer.Count == 0)
                {
                    return default; // clean end of stream
                }

                break;
            }

            if (one[0] == (byte)'\n')
            {
                break;
            }

            buffer.Add(one[0]);
        }

        string json = Encoding.UTF8.GetString(buffer.ToArray());
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}

/// <summary>
/// Client side of the helper protocol used by the main app. Writes a request and awaits the
/// response over the supplied streams (a duplex named-pipe stream in production).
/// </summary>
public sealed class ElevatedHelperClient
{
    private readonly Stream _read;
    private readonly Stream _write;

    public ElevatedHelperClient(Stream read, Stream write)
    {
        _read = read;
        _write = write;
    }

    /// <summary>Convenience ctor for a single duplex stream (e.g. a named pipe).</summary>
    public ElevatedHelperClient(Stream duplex) : this(duplex, duplex) { }

    public async Task<HelperResponse> SendCommandAsync(HelperRequest request, CancellationToken ct = default)
    {
        await ElevatedProtocol.WriteMessageAsync(_write, request, ct).ConfigureAwait(false);
        HelperResponse? response = await ElevatedProtocol.ReadMessageAsync<HelperResponse>(_read, ct).ConfigureAwait(false);
        return response ?? HelperResponse.Fail("No response from elevated helper.");
    }
}
