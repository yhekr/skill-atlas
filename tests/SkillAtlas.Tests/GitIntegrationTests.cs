using SkillAtlas.Core;

namespace SkillAtlas.Tests;

public class GitIntegrationTests
{
    [Fact]
    public async Task RemoteAndLocalDiscoveryUseSameExclusionsAndDeduplication()
    {
        using var fixture = new GitFixture();
        await fixture.InitializeAsync();
        const string skill = "---\nname: mirrored\ndescription: One skill\n---\nInstructions.";
        fixture.Files.Write(".claude/skills/mirrored/SKILL.md", skill);
        fixture.Files.Write(".agents/skills/mirrored/SKILL.md", skill);
        fixture.Files.Write("agent/skills/other/SKILL.md", "Other skill");
        fixture.Files.Write("tests/resources/ignored/SKILL.md", "Test resource");
        fixture.Files.Write("plugins/tools/resources/skills/ignored/SKILL.md", "Product resource");
        await fixture.CommitAsync();
        var remote = await fixture.ScanAsync();
        var local = await new LocalScanner().ScanAsync(fixture.Files.Root);
        Assert.Equal(2, remote.Skills.Count);
        Assert.Equal(local.Skills.Select(s => s.Path), remote.Skills.Select(s => s.Path));
        Assert.Empty(remote.Warnings);
    }

    [Fact]
    public async Task EmptyGitRepositoryReturnsNoSkills()
    {
        using var fixture = new GitFixture();
        await fixture.InitializeAsync();
        var result = await fixture.ScanAsync();
        Assert.Empty(result.Skills);
        Assert.Null(result.Revision);
        Assert.Empty(result.Warnings);
        Assert.False(Directory.Exists(fixture.Runner.LastWorkspace));
    }

    [Fact]
    public async Task RepositoryWithoutSkillsReturnsEmptyResultWithRevision()
    {
        using var fixture = new GitFixture();
        await fixture.InitializeAsync();
        fixture.Files.Write("README.md", "No skills.");
        await fixture.CommitAsync();
        var result = await fixture.ScanAsync();
        Assert.Empty(result.Skills);
        Assert.Matches("^[0-9a-f]{40}$", result.Revision!);
    }

    [Fact]
    public async Task RealGitScansDefaultBranchSlashBranchAndTag()
    {
        using var fixture = new GitFixture();
        await fixture.InitializeAsync();
        fixture.Files.Write(".claude/skills/first/SKILL.md", "---\nname: first\ndescription: On main\n---");
        await fixture.CommitAsync();
        await fixture.GitAsync("tag", "v1.0");
        await fixture.GitAsync("checkout", "-b", "feature/skills");
        fixture.Files.Write("навык [a] #1/SKILL.md", "---\nname: второй\ndescription: Unicode 😀\n---");
        await fixture.CommitAsync();
        await fixture.GitAsync("checkout", "main");

        var main = await fixture.ScanAsync();
        var branch = await fixture.ScanAsync("feature/skills");
        var tag = await fixture.ScanAsync("v1.0");
        Assert.Single(main.Skills);
        Assert.Equal(2, branch.Skills.Count);
        Assert.Equal(main.Revision, tag.Revision);
        Assert.NotEqual(main.Revision, branch.Revision);
        Assert.Equal("Unicode 😀", branch.Skills[1].Description);
        Assert.Contains("%23", branch.Skills[1].Url);
        Assert.All(branch.Skills, s => Assert.Contains($"/blob/{branch.Revision}/", s.Url));
        Assert.False(Directory.Exists(fixture.Runner.LastWorkspace));
    }

    [Fact]
    public async Task RealGitSkipsOversizedBlobAndSymbolicLink()
    {
        using var fixture = new GitFixture();
        await fixture.InitializeAsync();
        fixture.Files.Write("normal/SKILL.md", "# Normal\n\nUseful skill.");
        fixture.Files.Write("large/SKILL.md", new string('x', SkillParser.MaxFileBytes + 1));
        await fixture.CommitAsync();
        var blob = (await fixture.GitAsync("rev-parse", "HEAD:normal/SKILL.md")).Trim();
        await fixture.GitAsync("update-index", "--add", "--cacheinfo", $"120000,{blob},symlink/SKILL.md");
        await fixture.GitAsync("-c", "user.name=Skill Atlas Tests", "-c", "user.email=tests@example.invalid", "-c", "commit.gpgsign=false", "commit", "-m", "Add symlink index entry");
        var result = await fixture.ScanAsync();
        Assert.Equal("normal", Assert.Single(result.Skills).Name);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task MissingBranchReportsGitErrorAndCleansClone()
    {
        using var fixture = new GitFixture();
        await fixture.InitializeAsync();
        fixture.Files.Write("README.md", "Hello");
        await fixture.CommitAsync();
        var error = await Assert.ThrowsAnyAsync<ScanException>(() => fixture.ScanAsync("missing-branch"));
        Assert.Contains("missing-branch", error.Message);
        Assert.False(Directory.Exists(fixture.Runner.LastWorkspace));
    }

    private sealed class GitFixture : IDisposable
    {
        public TestWorkspace Files { get; } = new();
        public RedirectCloneRunner Runner { get; }
        public GitFixture() => Runner = new RedirectCloneRunner(Files.Root);
        public Task<string> GitAsync(params string[] args) => new GitRunner().RunAsync(Files.Root,
            new[] { "-c", $"core.hooksPath={Path.Combine(Files.Root, "no-hooks")}" }.Concat(args).ToArray(), CancellationToken.None);

        public async Task InitializeAsync()
        {
            await GitAsync("init", "--initial-branch=main");
            await GitAsync("config", "uploadpack.allowFilter", "true");
        }

        public async Task CommitAsync()
        {
            await GitAsync("add", ".");
            await GitAsync("-c", "user.name=Skill Atlas Tests", "-c", "user.email=tests@example.invalid", "-c", "commit.gpgsign=false", "commit", "-m", "Fixture commit");
        }

        public Task<ScanResult> ScanAsync(string? reference = null) =>
            new GitHubScanner(Runner).ScanAsync(new GitHubRepository("fixture", "repository"), reference);
        public void Dispose() => Files.Dispose();
    }

    private sealed class RedirectCloneRunner(string localSource) : IGitRunner
    {
        public string? LastWorkspace { get; private set; }

        public Task<string> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            var adapted = arguments.ToArray();
            if (adapted.Contains("clone"))
            {
                LastWorkspace = workingDirectory;
                // Exercise the production Git pipeline offline. Only the source URL changes.
                adapted[^2] = new Uri(localSource).AbsoluteUri;
            }
            return new GitRunner().RunAsync(workingDirectory,
                new[] { "-c", "protocol.file.allow=always" }.Concat(adapted).ToArray(), cancellationToken);
        }
    }
}
