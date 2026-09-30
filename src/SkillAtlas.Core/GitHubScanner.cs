using System.Globalization;

namespace SkillAtlas.Core;

public sealed class GitHubScanner(IGitRunner? git = null)
{
    private readonly IGitRunner _git = git ?? new GitRunner();

    public Task<ScanResult> ScanAsync(GitHubRepository repository, string? reference = null, CancellationToken cancellationToken = default) =>
        ScanInternalAsync(repository, reference, cancellationToken, null);

    public async Task<RepositorySnapshot> ScanWithContentAsync(GitHubRepository repository, string? reference = null, CancellationToken cancellationToken = default)
    {
        var documents = new Dictionary<string, string>(StringComparer.Ordinal);
        var totalCharacters = 0;
        var result = await ScanInternalAsync(repository, reference, cancellationToken, (path, content) =>
        {
            totalCharacters += content.Length;
            if (totalCharacters > 16 * 1024 * 1024)
                throw new ScanException("Skill documents exceed the 16 Mi character limit for a web scan. Use the CLI to list this repository.");
            documents[path] = content;
        });
        return new RepositorySnapshot(result, result.Skills.ToDictionary(s => s.Path, s => documents[s.Path], StringComparer.Ordinal));
    }

    private async Task<ScanResult> ScanInternalAsync(GitHubRepository repository, string? reference, CancellationToken cancellationToken, Action<string, string>? documentRead)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workspace = Path.Combine(Path.GetTempPath(), "skill-atlas", Guid.NewGuid().ToString("N"));
        var checkout = Path.Combine(workspace, "repository");
        var emptyTemplate = Path.Combine(workspace, "empty-template");
        Directory.CreateDirectory(emptyTemplate);
        var warnings = new List<string>();
        Task<string> Run(string directory, IReadOnlyList<string> arguments) =>
            _git.RunAsync(directory, new[] { "-c", $"core.hooksPath={emptyTemplate}" }.Concat(arguments).ToArray(), cancellationToken);
        try
        {
            var clone = new List<string>
            {
                "clone", "--quiet", "--depth=1", "--filter=blob:none", "--no-checkout", "--no-tags",
                "--template", emptyTemplate
            };
            if (reference is not null) clone.AddRange(["--branch", reference]);
            clone.AddRange(["--", repository.CloneUrl, checkout]);
            await Run(workspace, clone);
            string revision;
            try { revision = (await Run(checkout, ["rev-parse", "--verify", "--quiet", "HEAD"])).Trim(); }
            catch (GitCommandException ex) when (ex.ExitCode == 1)
            {
                return new ScanResult(repository.DisplayName, null, [], warnings);
            }
            var tree = await Run(checkout, ["ls-tree", "-r", "-z", "--full-tree", "HEAD"]);
            var skills = new List<Skill>();
            foreach (var (path, objectId) in SkillFiles(tree))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sizeText = await Run(checkout, ["cat-file", "-s", objectId]);
                if (!long.TryParse(sizeText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var size))
                    throw new ScanException($"Git returned an invalid file size for {path}.");
                if (size > SkillParser.MaxFileBytes)
                {
                    warnings.Add($"{path}: exceeds 1 MiB; skipped.");
                    continue;
                }
                var content = await Run(checkout, ["cat-file", "blob", objectId]);
                documentRead?.Invoke(path, content);
                skills.Add(SkillParser.Parse(content, path, repository.FileUrl(revision, path), warnings));
            }
            return new ScanResult(repository.DisplayName, revision,
                DiscoveryPolicy.OrderAndDeduplicate(skills), warnings);
        }
        finally
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    // Git pack files can be read-only on Windows. This is our own no-checkout temp repository.
                    if (!Directory.Exists(workspace)) break;
                    foreach (var file in Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(workspace, recursive: true);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt == 5) warnings.Add($"Could not remove temporary repository {workspace}: {ex.Message}");
                    else await Task.Delay(100, CancellationToken.None);
                }
            }
        }
    }

    public static IEnumerable<(string Path, string ObjectId)> SkillFiles(string tree)
    {
        foreach (var entry in tree.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t');
            if (tab < 0) continue;
            var metadata = entry[..tab].Split(' ');
            var path = entry[(tab + 1)..];
            if (metadata.Length != 3 || metadata[1] != "blob" || metadata[0] is not ("100644" or "100755")) continue;
            if (path.Split('/')[^1].Equals("SKILL.md", StringComparison.OrdinalIgnoreCase) && DiscoveryPolicy.IncludesFile(path))
                yield return (path, metadata[2]);
        }
    }
}
