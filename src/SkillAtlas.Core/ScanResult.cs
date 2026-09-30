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
    public ScanResult Filter(string? query) => string.IsNullOrWhiteSpace(query)
        ? this
        : this with
        {
            Skills = Skills.Where(s =>
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Path.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray()
        };
}

public class ScanException(string message) : Exception(message);

public sealed class GitCommandException(int exitCode, string message) : ScanException(message)
{
    public int ExitCode { get; } = exitCode;
}

public sealed record RepositorySnapshot(ScanResult Catalog, IReadOnlyDictionary<string, string> Documents);
