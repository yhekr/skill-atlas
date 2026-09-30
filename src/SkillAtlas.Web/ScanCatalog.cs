using Microsoft.Extensions.Caching.Memory;
using SkillAtlas.Core;

namespace SkillAtlas.Web;

public interface IScanCatalog
{
    Task<StoredScan> ScanAsync(GitHubRepository repository, string? reference, CancellationToken cancellationToken);
    StoredScan? Find(Guid id);
}

public sealed record StoredScan(Guid Id, RepositorySnapshot Snapshot);
public sealed class ScanBusyException : Exception;

public sealed class ScanCatalog : IScanCatalog, IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 64 * 1024 * 1024 });
    private readonly SemaphoreSlim _slots = new(2);

    public async Task<StoredScan> ScanAsync(GitHubRepository repository, string? reference, CancellationToken cancellationToken)
    {
        if (!await _slots.WaitAsync(0, cancellationToken)) throw new ScanBusyException();
        try
        {
            var snapshot = await new GitHubScanner().ScanWithContentAsync(repository, reference, cancellationToken);
            var scan = new StoredScan(Guid.NewGuid(), snapshot);
            var size = Math.Max(1, snapshot.Documents.Sum(d => (long)d.Value.Length * sizeof(char)) + snapshot.Catalog.Skills.Count * 4096L);
            _cache.Set(scan.Id, scan, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20),
                Size = size
            });
            if (Find(scan.Id) is null) throw new ScanException("This scan is too large to keep in the web reader. Use the CLI to list its skills.");
            return scan;
        }
        finally { _slots.Release(); }
    }

    public StoredScan? Find(Guid id) => _cache.TryGetValue(id, out StoredScan? scan) ? scan : null;
    public void Dispose() { _cache.Dispose(); _slots.Dispose(); }
}
