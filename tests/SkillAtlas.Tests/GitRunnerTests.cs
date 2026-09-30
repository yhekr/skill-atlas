using System.Diagnostics;
using SkillAtlas.Core;

namespace SkillAtlas.Tests;

public class GitRunnerTests
{
    [Fact]
    public async Task NonzeroExitIncludesDiagnosticsAndExitCode()
    {
        using var workspace = new TestWorkspace();
        var error = await Assert.ThrowsAsync<GitCommandException>(() =>
            new GitRunner().RunAsync(workspace.Root, ["not-a-real-git-command"], CancellationToken.None));
        Assert.NotEqual(0, error.ExitCode);
        Assert.Contains("not-a-real-git-command", error.Message);
    }

    [Fact]
    public async Task CancellationTerminatesRunningGitAndItsChild()
    {
        using var workspace = new TestWorkspace();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GitRunner().RunAsync(workspace.Root, ["-c", "alias.slow-test=!sleep 30", "slow-test"], cancellation.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task TimeoutIsReportedAsScanFailure()
    {
        using var workspace = new TestWorkspace();
        var watch = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<ScanException>(() =>
            new GitRunner(TimeSpan.FromMilliseconds(350)).RunAsync(workspace.Root,
                ["-c", "alias.slow-test=!sleep 30", "slow-test"], CancellationToken.None));
        Assert.Contains("timed out", error.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task OutputLimitFailsExplicitlyInsteadOfReturningTruncatedData()
    {
        using var workspace = new TestWorkspace();
        var error = await Assert.ThrowsAsync<ScanException>(() =>
            new GitRunner(maxOutputCharacters: 4).RunAsync(workspace.Root, ["--version"], CancellationToken.None));
        Assert.Contains("exceeded", error.Message);
    }

    [Fact]
    public async Task AlreadyCancelledTokenDoesNotRunGit()
    {
        using var workspace = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GitRunner().RunAsync(workspace.Root, ["init"], cancellation.Token));
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".git")));
    }
}
