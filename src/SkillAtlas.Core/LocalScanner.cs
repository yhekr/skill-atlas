using System.Text;

namespace SkillAtlas.Core;

public sealed class LocalScanner
{
    public async Task<ScanResult> ScanAsync(string directory, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root)) throw new ScanException($"Directory does not exist: {root}");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new ScanException("Choose the real directory instead of a symbolic link or junction.");

        var skills = new List<Skill>();
        var warnings = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(current); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Cannot read directory {current}: {ex.Message}");
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (!DiscoveryPolicy.ExcludesDirectory(Path.GetFileName(entry))) pending.Push(entry);
                        continue;
                    }
                    if (!Path.GetFileName(entry).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)) continue;
                    var relativePath = Path.GetRelativePath(root, entry).Replace('\\', '/');
                    await using var stream = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (stream.Length > SkillParser.MaxFileBytes)
                    {
                        warnings.Add($"{relativePath}: exceeds 1 MiB; skipped.");
                        continue;
                    }
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    var content = await reader.ReadToEndAsync(cancellationToken);
                    skills.Add(SkillParser.Parse(content, relativePath, new Uri(entry).AbsoluteUri, warnings));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Cannot read {entry}: {ex.Message}");
                }
            }
        }

        return new ScanResult(root, null, DiscoveryPolicy.OrderAndDeduplicate(skills), warnings);
    }
}
