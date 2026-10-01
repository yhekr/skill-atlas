namespace SkillAtlas.Core;

public sealed record OrganizationScanOptions
{
    public const int DefaultConcurrency = 4;
    public const int MaximumConcurrency = 16;

    public bool IncludeForks { get; init; }
    public bool IncludeArchived { get; init; }
    public int Concurrency { get; init; } = DefaultConcurrency;
}

public sealed record SkippedRepository(string Source, string Reason);

public sealed record OrganizationScanResult(
    string Owner,
    IReadOnlyList<BatchResult<ScanResult>> Repositories,
    IReadOnlyList<SkippedRepository> Skipped)
{
    public OrganizationScanResult Filter(string? query) => string.IsNullOrWhiteSpace(query) ? this : this with
    {
        Repositories = Repositories.Select(r => r with { Result = r.Result?.Filter(query) }).ToArray()
    };
}

/// <summary>
/// Scans every repository of a GitHub organization or user at its default branch with bounded parallelism.
/// Results use the multi-source <see cref="BatchResult{T}"/> shape and listing order; a failed repository
/// is reported without discarding the others, and cancellation is never a partial failure.
/// </summary>
public sealed class OrganizationScanner(
    GitHubRepositoryLister lister,
    Func<GitHubRepository, CancellationToken, Task<ScanResult>> scan)
{
    public OrganizationScanner(GitHubRepositoryLister lister, GitHubScanner scanner)
        : this(lister, (repository, token) => scanner.ScanAsync(repository, null, token)) { }

    public async Task<OrganizationScanResult> ScanAsync(GitHubOwner owner, OrganizationScanOptions options,
        Action<int, int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Concurrency, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Concurrency, OrganizationScanOptions.MaximumConcurrency);

        var listed = await lister.ListAsync(owner, cancellationToken);
        var selected = new List<GitHubRepository>();
        var skipped = new List<SkippedRepository>();
        foreach (var item in listed)
        {
            if (item.IsFork && !options.IncludeForks) skipped.Add(new(item.Repository.DisplayName, "fork"));
            else if (item.IsArchived && !options.IncludeArchived) skipped.Add(new(item.Repository.DisplayName, "archived"));
            else selected.Add(item.Repository);
        }

        var results = new BatchResult<ScanResult>[selected.Count];
        var completed = 0;
        progress?.Invoke(0, selected.Count);
        await Parallel.ForEachAsync(Enumerable.Range(0, selected.Count),
            new ParallelOptions { MaxDegreeOfParallelism = options.Concurrency, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                var repository = selected[index];
                try
                {
                    results[index] = new(repository.DisplayName, null, await scan(repository, token), null);
                }
                catch (Exception ex) when (ex is ScanException or IOException or UnauthorizedAccessException)
                {
                    token.ThrowIfCancellationRequested();
                    results[index] = new(repository.DisplayName, null, null, ex.Message);
                }
                progress?.Invoke(Interlocked.Increment(ref completed), selected.Count);
            });
        return new OrganizationScanResult(owner.Login, results, skipped);
    }
}
