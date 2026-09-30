using SkillAtlas.Core;

namespace SkillAtlas.Tests;

public sealed class ScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "skill-atlas-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LocalScanFindsHiddenSkillsExcludesDependenciesAndWarnsForOversizedFiles()
    {
        Write(".claude/skills/build/SKILL.md", "---\nname: build\ndescription: Build Gradle\n---\n");
        Write("nested/skill.md", "# Nested\n\nA local skill.");
        Write("node_modules/ignored/SKILL.md", "Ignored");
        Write(".git/ignored/SKILL.md", "Ignored");
        Write("large/SKILL.md", new string('x', SkillParser.MaxFileBytes + 1));
        var result = await new LocalScanner().ScanAsync(_root);
        Assert.Equal(2, result.Skills.Count);
        Assert.Single(result.Warnings);
        Assert.Single(result.Filter("GRADLE").Skills);
        Assert.Single(result.Filter("nested/").Skills);
        Assert.All(result.Skills, s => Assert.StartsWith("file:", s.Url));
    }

    [Fact]
    public async Task LocalScanHonorsCancellation()
    {
        Directory.CreateDirectory(_root);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LocalScanner().ScanAsync(_root, cancelled.Token));
    }

    [Fact]
    public async Task RemoteScanPinsLinksPassesRefAndCleansTemporaryRepository()
    {
        var git = new FakeGit();
        var result = await new GitHubScanner(git).ScanAsync(new GitHubRepository("owner", "repo"), "feature/skills");
        var skill = Assert.Single(result.Skills);
        Assert.Equal("remote-skill", skill.Name);
        Assert.Contains("/blob/0123456789abcdef/", skill.Url);
        Assert.Contains("--filter=blob:none", git.CloneArguments!);
        Assert.Contains("feature/skills", git.CloneArguments!);
        Assert.False(Directory.Exists(git.Workspace));
    }

    [Fact]
    public async Task FailedRemoteScanCleansTemporaryRepository()
    {
        var git = new FakeGit { Fail = true };
        await Assert.ThrowsAsync<ScanException>(() => new GitHubScanner(git).ScanAsync(new GitHubRepository("o", "r")));
        Assert.False(Directory.Exists(git.Workspace));
    }

    private void Write(string path, string content)
    {
        var fullPath = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeGit : IGitRunner
    {
        public string? Workspace { get; private set; }
        public IReadOnlyList<string>? CloneArguments { get; private set; }
        public bool Fail { get; init; }

        public Task<string> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Assert.Equal("-c", arguments[0]);
            Assert.StartsWith("core.hooksPath=", arguments[1]);
            arguments = arguments.Skip(2).ToArray();
            if (arguments[0] == "clone")
            {
                Workspace = workingDirectory;
                CloneArguments = arguments;
                if (Fail) throw new ScanException("Simulated network failure.");
                return Task.FromResult("");
            }
            return Task.FromResult(arguments[0] switch
            {
                "rev-parse" => "0123456789abcdef\n",
                "ls-tree" => "100644 blob abc\t.claude/skills/example/SKILL.md\0",
                "cat-file" when arguments[1] == "-s" => "80\n",
                "cat-file" => "---\nname: remote-skill\ndescription: Remote description\n---\n",
                _ => throw new InvalidOperationException()
            });
        }
    }
}
