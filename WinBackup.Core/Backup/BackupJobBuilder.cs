using WinBackup.Core.Abstractions;

namespace WinBackup.Core.Backup;

/// <summary>
/// Builds the set of <see cref="CopyJob"/>s for a backup: enumerates each source folder and maps
/// every file to a destination under <c>{targetFolder}\{sourceLeafName}\{relativePath}</c>, honouring
/// an optional modified-since cutoff for incremental runs.
/// </summary>
public static class BackupJobBuilder
{
    public static IReadOnlyList<CopyJob> Build(
        IFileSystem fs,
        IEnumerable<string> sourceFolders,
        string targetFolder,
        DateTimeOffset? modifiedSince)
    {
        var jobs = new List<CopyJob>();

        foreach (string source in sourceFolders)
        {
            if (string.IsNullOrWhiteSpace(source) || !fs.DirectoryExists(source))
            {
                continue;
            }

            string leaf = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(leaf))
            {
                leaf = "root";
            }

            foreach (FileItem item in fs.EnumerateFiles(source))
            {
                if (modifiedSince is { } cutoff && item.LastWriteTimeUtc <= cutoff)
                {
                    continue;
                }

                string relative = Path.GetRelativePath(source, item.Path);
                string dest = Path.Combine(targetFolder, leaf, relative);
                jobs.Add(new CopyJob(item, dest));
            }
        }

        return jobs;
    }
}
