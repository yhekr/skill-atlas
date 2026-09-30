namespace SkillAtlas.Core;

public sealed record Skill(string Name, string Description, string Path, string Url)
{
    internal string? ContentHash { get; init; }
}

public sealed record ScanResult(
    string Source,
    string? Revision,
    IReadOnlyList<Skill> Skills,
    IReadOnlyList<string> Warnings)
{
    public ScanResult Filter(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return this;
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return this with
        {
            Skills = Skills.Where(s => terms.All(term =>
                s.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                s.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                s.Path.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray()
        };
    }
}

public class ScanException(string message) : Exception(message);

public sealed class GitCommandException(int exitCode, string message) : ScanException(message)
{
    public int ExitCode { get; } = exitCode;
}

public sealed record RepositorySnapshot(ScanResult Catalog, IReadOnlyDictionary<string, string> Documents);
