namespace SkillAtlas.Core;

public sealed record ScanTarget(string Source, string? Reference);
public sealed record BatchResult<T>(string Source, string? Reference, T? Result, string? Error) where T : class;

/// <summary>Shared bounded, ordered multi-source scanning; cancellation is never a partial failure.</summary>
public static class ScanBatch
{
    public const int MaximumSources = 5;

    public static IReadOnlyList<ScanTarget> Normalize(IReadOnlyList<string?>? sources, string? reference, bool allowLocal)
    {
        if (sources is null || sources.Count is < 1 or > MaximumSources)
            throw new ArgumentException($"Enter between 1 and {MaximumSources} repositories.");
        reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (reference?.Length > 200 || reference?.Any(char.IsControl) == true || reference?.StartsWith('-') == true)
            throw new ArgumentException("Enter a valid branch or tag.");
        var targets = new List<ScanTarget>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in sources)
        {
            if (string.IsNullOrWhiteSpace(input) || input.Length > (allowLocal ? 4096 : 300) || input.Any(char.IsControl))
                throw new ArgumentException("Each entry must contain one repository URL or path.");
            var source = input.Trim();
            string key;
            if (allowLocal && (Directory.Exists(source) || Path.IsPathRooted(source) || source.StartsWith('.') || source.Contains('\\')))
            {
                source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
                key = "local:" + (OperatingSystem.IsWindows() ? source.ToUpperInvariant() : source);
            }
            else
            {
                try { source = GitHubRepository.Parse(source).DisplayName; }
                catch (ScanException ex) { throw new ArgumentException(ex.Message); }
                key = "github:" + source.ToLowerInvariant();
            }
            if (seen.Add(key)) targets.Add(new(source, reference));
        }
        return targets;
    }

    public static async Task<IReadOnlyList<BatchResult<T>>> RunAsync<T>(IReadOnlyList<ScanTarget> targets,
        Func<ScanTarget, CancellationToken, Task<T>> scan, CancellationToken cancellationToken = default) where T : class
    {
        var results = new List<BatchResult<T>>();
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await scan(target, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(new(target.Source, target.Reference, result, null));
            }
            catch (Exception ex) when (ex is ScanException or IOException or UnauthorizedAccessException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(new(target.Source, target.Reference, null, ex.Message));
            }
        }
        return results;
    }
}
