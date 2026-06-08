namespace WinBackup.Core.Backup;

/// <summary>Progress for a single in-flight file copy.</summary>
public sealed record FileCopyProgress(string FileName, long BytesCopied, long TotalBytes);

/// <summary>How a copy attempt resolved.</summary>
public enum FileCopyOutcome
{
    /// <summary>File copied and SHA-256 verified.</summary>
    Copied,

    /// <summary>File matched an exclusion pattern and was not copied.</summary>
    Excluded,

    /// <summary>File is locked (sharing violation); caller should retry via the VSS snapshot path.</summary>
    RequiresVssFallback,
}

/// <summary>Result of <see cref="FileCopyService.CopyAsync"/>.</summary>
public sealed record FileCopyResult(FileCopyOutcome Outcome, long BytesCopied);

/// <summary>Thrown when a copied file's SHA-256 does not match its source.</summary>
public sealed class HashMismatchException : Exception
{
    public HashMismatchException(string path)
        : base($"SHA-256 verification failed for '{path}': destination does not match source.")
    {
        Path = path;
    }

    public string Path { get; }
}
