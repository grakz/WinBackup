using System.Text.RegularExpressions;

namespace WinBackup.Core.Backup;

/// <summary>
/// Decides whether a file should be excluded from backup before any copy is attempted.
/// Built-in patterns cover Office lock files and common OS/editor temp files; callers can
/// supply extra glob patterns (from <c>BackupConfig.ExcludePatterns</c>). Matching is
/// case-insensitive and applied to the file name only.
/// </summary>
public sealed class FileFilterService
{
    /// <summary>Built-in exclusion globs, matched against the file name.</summary>
    public static readonly IReadOnlyList<string> DefaultPatterns = new[]
    {
        "~$*",        // Office lock files: ~$document.docx
        "*.tmp",      // generic temp
        "*.~*",       // editor temp/backup variants: file.~wbk
        "desktop.ini",
        "thumbs.db",
        "ehthumbs.db",
    };

    private readonly Regex[] _matchers;

    public FileFilterService(IEnumerable<string>? userPatterns = null)
    {
        IEnumerable<string> all = DefaultPatterns;
        if (userPatterns is not null)
        {
            all = all.Concat(userPatterns.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        _matchers = all.Select(GlobToRegex).ToArray();
    }

    /// <summary>True when the file at <paramref name="path"/> matches any exclusion pattern.</summary>
    public bool ShouldExclude(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        string name = Path.GetFileName(path);
        foreach (Regex matcher in _matchers)
        {
            if (matcher.IsMatch(name))
            {
                return true;
            }
        }

        return false;
    }

    private static Regex GlobToRegex(string glob)
    {
        // Translate a filename glob (* and ?) into an anchored, case-insensitive regex.
        var sb = new System.Text.StringBuilder("^");
        foreach (char c in glob)
        {
            sb.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString()),
            });
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
