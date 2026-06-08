using System.ComponentModel;
using System.Security.Cryptography;
using WinBackup.Core.Abstractions;

namespace WinBackup.Core.Backup;

/// <summary>
/// Copies a single file with progress reporting, retry-on-transient-lock, and mandatory
/// SHA-256 verification. Locked files (sharing violations) are not retried here — they are
/// reported via <see cref="FileCopyOutcome.RequiresVssFallback"/> so the caller can route
/// them through the elevated VSS snapshot path instead.
/// </summary>
public sealed class FileCopyService
{
    private const int BufferSize = 1024 * 1024;
    private const int SharingViolationErrorCode = 32; // ERROR_SHARING_VIOLATION

    private readonly IFileSystem _fs;
    private readonly FileFilterService _filter;
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;

    public FileCopyService(
        IFileSystem fileSystem,
        FileFilterService filter,
        int maxRetries = 3,
        TimeSpan? retryDelay = null)
    {
        _fs = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _maxRetries = Math.Max(0, maxRetries);
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="dest"/>. Returns an outcome describing
    /// what happened. Throws <see cref="HashMismatchException"/> on verification failure and
    /// rethrows the last <see cref="IOException"/> if a non-sharing error persists past all retries.
    /// </summary>
    public async Task<FileCopyResult> CopyAsync(
        string source,
        string dest,
        IProgress<FileCopyProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (_filter.ShouldExclude(source))
        {
            return new FileCopyResult(FileCopyOutcome.Excluded, 0);
        }

        IOException? lastError = null;
        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                long bytes = await CopyAndVerifyAsync(source, dest, progress, ct).ConfigureAwait(false);
                return new FileCopyResult(FileCopyOutcome.Copied, bytes);
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                // Locked file — do not retry here; hand off to the VSS fallback path.
                return new FileCopyResult(FileCopyOutcome.RequiresVssFallback, 0);
            }
            catch (IOException ex) when (attempt < _maxRetries)
            {
                lastError = ex;
                await Task.Delay(_retryDelay, ct).ConfigureAwait(false);
            }
        }

        // Exhausted retries on a non-sharing IOException.
        throw lastError ?? new IOException($"Failed to copy '{source}'.");
    }

    private async Task<long> CopyAndVerifyAsync(
        string source,
        string dest,
        IProgress<FileCopyProgress>? progress,
        CancellationToken ct)
    {
        string fileName = Path.GetFileName(source);
        byte[] sourceHash;
        long total;

        using (Stream src = _fs.OpenRead(source, FileShare.ReadWrite))
        using (Stream dst = _fs.OpenWrite(dest))
        using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            total = src.CanSeek ? src.Length : 0;
            byte[] buffer = new byte[BufferSize];
            long copied = 0;
            int read;
            while ((read = await src.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                hasher.AppendData(buffer, 0, read);
                copied += read;
                progress?.Report(new FileCopyProgress(fileName, copied, total));
            }

            sourceHash = hasher.GetHashAndReset();
            total = copied;
        }

        // Re-read the written file and confirm it matches what we streamed.
        byte[] destHash;
        using (Stream verify = _fs.OpenRead(dest, FileShare.Read))
        {
            destHash = await SHA256.HashDataAsync(verify, ct).ConfigureAwait(false);
        }

        if (!sourceHash.AsSpan().SequenceEqual(destHash))
        {
            throw new HashMismatchException(source);
        }

        return total;
    }

    private static bool IsSharingViolation(IOException ex)
    {
        int code = ex.HResult & 0xFFFF;
        if (code == SharingViolationErrorCode)
        {
            return true;
        }

        // Some providers surface the raw Win32 code via the inner exception.
        return ex.InnerException is Win32Exception win32 && win32.NativeErrorCode == SharingViolationErrorCode;
    }
}
