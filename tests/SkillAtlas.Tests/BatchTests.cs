using System.Text.Json;
using SkillAtlas.Cli;
using SkillAtlas.Core;

namespace SkillAtlas.Tests;

public sealed class BatchTests
{
    [Fact]
    public void RepositoryAliasesDeduplicateInFirstSeenOrder()
    {
        var targets = ScanBatch.Normalize([" JetBrains/kotlin ", "https://github.com/JETBRAINS/Kotlin.git/",
            "git@github.com:JetBrains/kotlin.git", "JetBrains/MPS"], " release/one ", false);
        Assert.Equal(["JetBrains/kotlin", "JetBrains/MPS"], targets.Select(target => target.Source));
        Assert.All(targets, target => Assert.Equal("release/one", target.Reference));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("https://example.com/o/r")]
    [InlineData("https://github.com/o/r/tree/main")]
    [InlineData("o/r\nother/repo")]
    [InlineData("C:\\private\\folder")]
    public void InvalidWebSourcesFailBeforeAnyScanning(string? source) =>
        Assert.Throws<ArgumentException>(() => ScanBatch.Normalize(["owner/good", source], null, false));

    [Fact]
    public void SourceAndReferenceBoundsAreEnforced()
    {
        Assert.Throws<ArgumentException>(() => ScanBatch.Normalize(null, null, false));
        Assert.Throws<ArgumentException>(() => ScanBatch.Normalize([], null, false));
        Assert.Throws<ArgumentException>(() => ScanBatch.Normalize(Enumerable.Repeat<string?>("o/r", 6).ToArray(), null, false));
        Assert.Throws<ArgumentException>(() => ScanBatch.Normalize(["o/" + new string('r', 299)], null, false));
        foreach (var reference in new[] { "--option", "main\nnext", new string('x', 201) })
            Assert.Throws<ArgumentException>(() => ScanBatch.Normalize(["o/r"], reference, false));
        Assert.Equal(5, ScanBatch.Normalize(Enumerable.Range(1, 5).Select(i => $"o/r{i}").ToArray(), null, false).Count);
    }

    [Fact]
    public void LocalAliasesCollapseButDifferentDirectoriesDoNot()
    {
        using var first = new TestWorkspace();
        using var second = new TestWorkspace();
        var targets = ScanBatch.Normalize([first.Root, Path.Combine(first.Root, "."), first.Root + Path.DirectorySeparatorChar, second.Root], null, true);
        Assert.Equal(2, targets.Count);
    }

    [Fact]
    public async Task PartialFailurePreservesOrderAndEmptySuccess()
    {
        var targets = ScanBatch.Normalize(["o/good", "o/bad", "o/empty"], null, false);
        var calls = new List<string>();
        var results = await ScanBatch.RunAsync(targets, (target, _) =>
        {
            calls.Add(target.Source);
            if (target.Source == "o/bad") throw new ScanException("Cannot read");
            return Task.FromResult(new ScanResult(target.Source, null, [], []));
        });
        Assert.Equal(targets.Select(target => target.Source), calls);
        Assert.NotNull(results[0].Result);
        Assert.Null(results[1].Result);
        Assert.Equal("Cannot read", results[1].Error);
        Assert.NotNull(results[2].Result);
    }

    [Fact]
    public async Task CancellationStopsTheBatchInsteadOfBecomingAPartialFailure()
    {
        using var cancel = new CancellationTokenSource();
        var calls = 0;
        var targets = ScanBatch.Normalize(["o/one", "o/two"], null, false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ScanBatch.RunAsync(targets, (_, _) =>
        {
            calls++;
            cancel.Cancel();
            return Task.FromResult("finished just after cancellation");
        }, cancel.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task UnexpectedErrorsAreNotHidden()
    {
        var targets = ScanBatch.Normalize(["o/r"], null, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ScanBatch.RunAsync<string>(targets,
            (_, _) => throw new InvalidOperationException("bug")));
    }

    [Fact]
    public void CliAcceptsMultipleSourcesButRejectsAmbiguousSimilarity()
    {
        var options = CliOptions.Parse(["scan", "o/one", "o/two", "--query", "build tests", "--json"]);
        Assert.Equal(["o/one", "o/two"], options.Sources);
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["scan", "o/one", "o/two", "--similar", "build"]));
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["scan", "a/r", "b/r", "c/r", "d/r", "e/r", "f/r"]));
    }

    [Fact]
    public async Task ActualCliSearchesBothDirectoriesAndKeepsIdenticalPathsSeparate()
    {
        using var one = new TestWorkspace();
        using var two = new TestWorkspace();
        one.Write("same/SKILL.md", "---\nname: Build\ndescription: Run Gradle tests\n---\nFirst");
        two.Write("same/SKILL.md", "---\nname: Build\ndescription: Run Gradle wrapper\n---\nSecond");
        var result = await one.RunAsync("dotnet", typeof(CliOptions).Assembly.Location, "scan", one.Root, two.Root, "--query", "gradle", "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        var repos = json.RootElement.GetProperty("repositories").EnumerateArray().ToArray();
        Assert.Equal(2, repos.Length);
        Assert.NotEqual(repos[0].GetProperty("source").GetString(), repos[1].GetProperty("source").GetString());
        Assert.All(repos, repo => Assert.Single(repo.GetProperty("result").GetProperty("skills").EnumerateArray()));
    }

    [Fact]
    public async Task ActualCliReturnsPartialJsonAndExitOneWithoutLosingSuccessfulResult()
    {
        using var one = new TestWorkspace();
        one.Write("SKILL.md", "# Build\n\nBuild tests");
        var result = await one.RunAsync("dotnet", typeof(CliOptions).Assembly.Location, "scan", one.Root,
            Path.Combine(one.Root, "missing"), "--json");
        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var repos = json.RootElement.GetProperty("repositories").EnumerateArray().ToArray();
        Assert.Single(repos[0].GetProperty("result").GetProperty("skills").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, repos[1].GetProperty("result").ValueKind);
        Assert.Contains("Local directory", repos[1].GetProperty("error").GetString());
        Assert.Contains("Error", result.Error);
    }
}
