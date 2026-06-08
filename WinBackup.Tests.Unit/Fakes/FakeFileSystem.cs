using WinBackup.Core.Abstractions;

namespace WinBackup.Tests.Unit.Fakes;

/// <summary>In-memory <see cref="IFileSystem"/> for deterministic engine/copy tests.</summary>
public sealed class FakeFileSystem : IFileSystem
{
    private sealed class Entry
    {
        public byte[] Content = Array.Empty<byte>();
        public DateTimeOffset LastWriteUtc;
        public FileAttributes Attributes = FileAttributes.Normal;
    }

    private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Paths that throw a sharing violation (locked file) when opened for read.</summary>
    public HashSet<string> LockedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Path → number of remaining transient (non-sharing) IOExceptions to throw on read.</summary>
    public Dictionary<string, int> TransientReadFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Paths whose stored bytes are corrupted right after write, to force a hash mismatch.</summary>
    public HashSet<string> CorruptOnWrite { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Read-open count per path (lets tests assert that no retry occurred).</summary>
    public Dictionary<string, int> ReadOpenCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public long FreeSpace { get; set; } = long.MaxValue;

    public void AddFile(string path, byte[] content, DateTimeOffset? lastWriteUtc = null, FileAttributes attributes = FileAttributes.Normal)
    {
        _files[path] = new Entry
        {
            Content = content,
            LastWriteUtc = lastWriteUtc ?? DateTimeOffset.UtcNow,
            Attributes = attributes,
        };
        AddDirChain(Path.GetDirectoryName(path));
    }

    public void AddFile(string path, string content, DateTimeOffset? lastWriteUtc = null, FileAttributes attributes = FileAttributes.Normal)
        => AddFile(path, System.Text.Encoding.UTF8.GetBytes(content), lastWriteUtc, attributes);

    public byte[]? GetContent(string path) => _files.TryGetValue(path, out Entry? e) ? e.Content : null;

    private void AddDirChain(string? dir)
    {
        while (!string.IsNullOrEmpty(dir))
        {
            _dirs.Add(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    public bool FileExists(string path) => _files.ContainsKey(path);

    public bool DirectoryExists(string path) => _dirs.Contains(path);

    public void CreateDirectory(string path) => AddDirChain(path);

    public IEnumerable<FileItem> EnumerateFiles(string root)
    {
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return _files
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(kv => ToItem(kv.Key, kv.Value))
            .OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase);
    }

    public FileItem GetItem(string path) => ToItem(path, _files[path]);

    private static FileItem ToItem(string path, Entry e) =>
        new(path, e.Content.Length, e.LastWriteUtc, e.Attributes);

    public Stream OpenRead(string path, FileShare share)
    {
        ReadOpenCounts[path] = ReadOpenCounts.GetValueOrDefault(path) + 1;

        if (LockedPaths.Contains(path))
        {
            throw new IOException("The process cannot access the file because it is being used by another process.")
            {
                HResult = unchecked((int)0x80070020), // sharing violation
            };
        }

        if (TransientReadFailures.TryGetValue(path, out int remaining) && remaining > 0)
        {
            TransientReadFailures[path] = remaining - 1;
            throw new IOException("Transient I/O error.") { HResult = unchecked((int)0x80070021) }; // lock violation (non-sharing)
        }

        if (!_files.TryGetValue(path, out Entry? entry))
        {
            throw new FileNotFoundException("Not found", path);
        }

        return new MemoryStream(entry.Content, writable: false);
    }

    public Stream OpenWrite(string path)
    {
        AddDirChain(Path.GetDirectoryName(path));
        return new CapturingStream(bytes =>
        {
            if (CorruptOnWrite.Contains(path) && bytes.Length > 0)
            {
                bytes[0] ^= 0xFF;
            }

            _files[path] = new Entry { Content = bytes, LastWriteUtc = DateTimeOffset.UtcNow };
        });
    }

    public long GetAvailableFreeSpace(string path) => FreeSpace;

    private sealed class CapturingStream : MemoryStream
    {
        private readonly Action<byte[]> _onClose;
        private bool _flushed;

        public CapturingStream(Action<byte[]> onClose) => _onClose = onClose;

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_flushed)
            {
                _flushed = true;
                _onClose(ToArray());
            }

            base.Dispose(disposing);
        }
    }
}
