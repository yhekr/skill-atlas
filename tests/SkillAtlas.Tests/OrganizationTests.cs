using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using SkillAtlas.Cli;
using SkillAtlas.Core;
using Spectre.Console;

namespace SkillAtlas.Tests;

public class OrganizationTests
{
    [Theory]
    [InlineData("JetBrains")]
    [InlineData(" JetBrains ")]
    [InlineData("https://github.com/JetBrains")]
    [InlineData("https://github.com/JetBrains/")]
    [InlineData("github.com/JetBrains")]
    public void NormalizesOwner(string input) => Assert.Equal("JetBrains", GitHubOwner.Parse(input).Login);

    [Theory]
    [InlineData("")]
    [InlineData("-JetBrains")]
    [InlineData("JetBrains-")]
    [InlineData("Jet--Brains")]
    [InlineData("Jet_Brains")]
    [InlineData("JetBrains/kotlin")]
    [InlineData("https://github.com/JetBrains/kotlin")]
    [InlineData("https://evil.example/JetBrains")]
    [InlineData("http://github.com/JetBrains")]
    [InlineData("https://user@github.com/JetBrains")]
    [InlineData("https://github.com/JetBrains?tab=repositories")]
    [InlineData("../JetBrains")]
    [InlineData("a234567890123456789012345678901234567890")]
    public void RejectsInvalidOwner(string input) => Assert.Throws<ScanException>(() => GitHubOwner.Parse(input));

    [Fact]
    public async Task ListsEveryPageWithHeadersAndSortsRepositories()
    {
        var handler = new FakeGitHub(request => request.RequestUri!.Query.EndsWith("&page=1", StringComparison.Ordinal)
            ? Page(Enumerable.Range(0, 100).Select(i => Repo($"Owner/repo-{i:000}")).Reverse())
            : Page([Repo("Owner/zeta", fork: true), Repo("Owner/Alpha", archived: true), Repo("Owner/off", disabled: true)]));
        var repositories = await new GitHubRepositoryLister(handler.Client).ListAsync(new GitHubOwner("Owner"));

        Assert.Equal(102, repositories.Count);
        Assert.Equal("Owner/Alpha", repositories[0].Repository.DisplayName);
        Assert.True(repositories[0].IsArchived);
        Assert.True(repositories[^1].IsFork);
        Assert.DoesNotContain(repositories, r => r.Repository.Name == "off");
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("api.github.com", request.Uri.Host);
            Assert.StartsWith("/orgs/Owner/repos", request.Uri.AbsolutePath);
            Assert.Contains("per_page=100", request.Uri.Query);
            Assert.Equal("skill-atlas", request.UserAgent);
            Assert.Null(request.Authorization);
        });
    }

    [Fact]
    public async Task ExactlyFullPagesRequestOneMorePage()
    {
        var handler = new FakeGitHub(request => request.RequestUri!.Query.EndsWith("&page=1", StringComparison.Ordinal)
            ? Page(Enumerable.Range(0, 100).Select(i => Repo($"o/r{i}"))) : Page([]));
        Assert.Equal(100, (await new GitHubRepositoryLister(handler.Client).ListAsync(new GitHubOwner("o"))).Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FallsBackToUserAccountWhenOrganizationDoesNotExist()
    {
        var handler = new FakeGitHub(request => request.RequestUri!.AbsolutePath.StartsWith("/orgs/")
            ? new HttpResponseMessage(HttpStatusCode.NotFound) : Page([Repo("someone/tools")]));
        var repositories = await new GitHubRepositoryLister(handler.Client, " secret-token ").ListAsync(new GitHubOwner("someone"));
        Assert.Equal("someone/tools", Assert.Single(repositories).Repository.DisplayName);
        Assert.Equal(new[] { "/orgs/someone/repos", "/users/someone/repos" }, handler.Requests.Select(r => r.Uri.AbsolutePath));
        Assert.Contains("type=owner", handler.Requests[1].Uri.Query);
        Assert.All(handler.Requests, request => Assert.Equal("Bearer secret-token", request.Authorization));
    }

    [Fact]
    public async Task MissingAccountExplainsPrivateOrganizations()
    {
        var handler = new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var error = await Assert.ThrowsAsync<ScanException>(() => new GitHubRepositoryLister(handler.Client).ListAsync(new GitHubOwner("ghost")));
        Assert.Contains("'ghost' was not found", error.Message);
        Assert.Contains("GITHUB_TOKEN", error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "0", null, "rate limit", "Set GITHUB_TOKEN")]
    [InlineData(HttpStatusCode.TooManyRequests, null, null, "rate limit", "Set GITHUB_TOKEN")]
    [InlineData(HttpStatusCode.Forbidden, "0", "token", "rate limit", "Retry later")]
    [InlineData(HttpStatusCode.Unauthorized, null, "token", "rejected the token", "GITHUB_TOKEN")]
    [InlineData(HttpStatusCode.Forbidden, "42", null, "HTTP 403", "listing")]
    [InlineData(HttpStatusCode.InternalServerError, null, null, "HTTP 500", "listing")]
    public async Task ApiFailuresBecomeActionableErrorsWithoutLeakingTheToken(HttpStatusCode status, string? remaining,
        string? token, string expected, string advice)
    {
        var handler = new FakeGitHub(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("{\"message\":\"secret-token\"}") };
            if (remaining is not null) response.Headers.Add("X-RateLimit-Remaining", remaining);
            return response;
        });
        var error = await Assert.ThrowsAsync<ScanException>(() =>
            new GitHubRepositoryLister(handler.Client, token is null ? null : "secret-token").ListAsync(new GitHubOwner("o")));
        Assert.Contains(expected, error.Message);
        Assert.Contains(advice, error.Message);
        Assert.DoesNotContain("secret-token", error.Message);
    }

    [Theory]
    [InlineData("{\"message\":\"not a list\"}")]
    [InlineData("[{\"name\":\"missing-full-name\"}]")]
    [InlineData("[{\"full_name\":42}]")]
    [InlineData("[{\"full_name\":\"o/--upload-pack=x/y\"}]")]
    [InlineData("[{\"full_name\":\"o/..\"}]")]
    [InlineData("[{\"full_name\":\"https://evil.example/o/r\"}]")]
    [InlineData("not json")]
    public async Task MalformedOrUnsafeApiDataIsRejected(string body)
    {
        var handler = new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        var error = await Assert.ThrowsAsync<ScanException>(() => new GitHubRepositoryLister(handler.Client).ListAsync(new GitHubOwner("o")));
        Assert.StartsWith("GitHub API returned", error.Message);
    }

    [Fact]
    public async Task NetworkFailureIsAScanError()
    {
        var handler = new FakeGitHub(_ => throw new HttpRequestException("proxy refused"));
        var error = await Assert.ThrowsAsync<ScanException>(() => new GitHubRepositoryLister(handler.Client).ListAsync(new GitHubOwner("o")));
        Assert.Contains("Could not reach the GitHub API", error.Message);
    }

    [Fact]
    public async Task CancellationIsNotReportedAsANetworkError()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new FakeGitHub(_ => Page([]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GitHubRepositoryLister(handler.Client).ListAsync(new GitHubOwner("o"), cancellation.Token));
    }

    [Fact]
    public async Task SkipsForksAndArchivedByDefaultAndKeepsListingOrder()
    {
        var scanned = new ConcurrentBag<string>();
        var scanner = Scanner([Repo("o/fork", fork: true), Repo("o/old", archived: true), Repo("o/old-fork", fork: true, archived: true),
            Repo("o/b"), Repo("o/a")], async (repository, _) =>
            {
                scanned.Add(repository.DisplayName);
                // Finish out of order to prove results keep the listing order.
                await Task.Delay(repository.Name == "a" ? 50 : 0);
                return new ScanResult(repository.DisplayName, "abc", [], []);
            });
        var result = await scanner.ScanAsync(new GitHubOwner("o"), new OrganizationScanOptions());

        Assert.Equal(new[] { "o/a", "o/b" }, result.Repositories.Select(r => r.Source));
        Assert.Equal(new[] { "o/a", "o/b" }, scanned.Order());
        Assert.Equal(new[] { ("o/fork", "fork"), ("o/old", "archived"), ("o/old-fork", "fork") },
            result.Skipped.Select(s => (s.Source, s.Reason)));
        Assert.All(result.Repositories, r => Assert.Null(r.Reference));
    }

    [Theory]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 4)]
    public async Task ArchivedForksNeedBothIncludeFlags(bool forks, bool archived, int expected)
    {
        var scanner = Scanner([Repo("o/fork", fork: true), Repo("o/old", archived: true), Repo("o/old-fork", fork: true, archived: true), Repo("o/a")],
            (repository, _) => Task.FromResult(new ScanResult(repository.DisplayName, "abc", [], [])));
        var result = await scanner.ScanAsync(new GitHubOwner("o"), new OrganizationScanOptions { IncludeForks = forks, IncludeArchived = archived });
        Assert.Equal(expected, result.Repositories.Count);
        Assert.Equal(4 - expected, result.Skipped.Count);
    }

    [Fact]
    public async Task FailedRepositoryIsReportedWithoutDiscardingOthers()
    {
        var progress = new ConcurrentQueue<(int Done, int Total)>();
        var scanner = Scanner([Repo("o/good"), Repo("o/broken"), Repo("o/io")], (repository, _) => repository.Name switch
        {
            "broken" => throw new GitCommandException(128, "Git failed: repository not found"),
            "io" => throw new IOException("disk full"),
            _ => Task.FromResult(new ScanResult(repository.DisplayName, "abc", [new Skill("s", "d", "SKILL.md", "u")], []))
        });
        var result = await scanner.ScanAsync(new GitHubOwner("o"), new OrganizationScanOptions(), (done, total) => progress.Enqueue((done, total)));

        Assert.Equal(new[] { "o/broken", "o/good", "o/io" }, result.Repositories.Select(r => r.Source));
        Assert.Equal("Git failed: repository not found", result.Repositories[0].Error);
        Assert.Null(result.Repositories[0].Result);
        Assert.Single(result.Repositories[1].Result!.Skills);
        Assert.Equal("disk full", result.Repositories[2].Error);
        Assert.Equal(new[] { 0, 1, 2, 3 }, progress.Select(p => p.Done).Order());
        Assert.All(progress, p => Assert.Equal(3, p.Total));
    }

    [Fact]
    public async Task UnexpectedExceptionsAreNotHiddenAsRepositoryFailures()
    {
        var scanner = Scanner([Repo("o/a")], (_, _) => throw new InvalidOperationException("bug"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync(new GitHubOwner("o"), new OrganizationScanOptions()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ConcurrencyLimitIsRespected(int limit)
    {
        var active = 0;
        var peak = 0;
        var scanner = Scanner(Enumerable.Range(0, 12).Select(i => Repo($"o/r{i}")).ToArray(), async (repository, token) =>
        {
            var now = Interlocked.Increment(ref active);
            InterlockedMax(ref peak, now);
            await Task.Delay(20, token);
            Interlocked.Decrement(ref active);
            return new ScanResult(repository.DisplayName, "abc", [], []);
        });
        var result = await scanner.ScanAsync(new GitHubOwner("o"), new OrganizationScanOptions { Concurrency = limit });
        Assert.Equal(12, result.Repositories.Count);
        Assert.InRange(peak, 1, limit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task InvalidConcurrencyIsRejectedBeforeListing(int concurrency)
    {
        var handler = new FakeGitHub(_ => Page([]));
        var scanner = new OrganizationScanner(new GitHubRepositoryLister(handler.Client), (_, _) => throw new InvalidOperationException());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            scanner.ScanAsync(new GitHubOwner("o"), new OrganizationScanOptions { Concurrency = concurrency }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CancellationStopsTheScanInsteadOfRecordingFailures()
    {
        using var cancellation = new CancellationTokenSource();
        var scanner = Scanner(Enumerable.Range(0, 20).Select(i => Repo($"o/r{i}")).ToArray(), async (repository, token) =>
        {
            if (repository.Name == "r1") await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            throw new ScanException("unreachable");
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scanner.ScanAsync(new GitHubOwner("o"), new OrganizationScanOptions { Concurrency = 2 }, null, cancellation.Token));
    }

    [Fact]
    public void QueryFiltersEveryRepositoryAndKeepsFailures()
    {
        var result = new OrganizationScanResult("o", [
            new("o/a", null, new ScanResult("o/a", "1", [new("gradle-build", "Update wrapper", "a/SKILL.md", "u"), new("docs", "Write", "b/SKILL.md", "u")], []), null),
            new("o/b", null, null, "failed")
        ], []);
        var filtered = result.Filter("WRAPPER gradle");
        Assert.Equal("gradle-build", Assert.Single(filtered.Repositories[0].Result!.Skills).Name);
        Assert.Equal("failed", filtered.Repositories[1].Error);
        Assert.Same(result, result.Filter("  "));
    }

    [Fact]
    public async Task RealGitScansEveryListedRepositoryThroughTheProductionPipeline()
    {
        using var first = new TestWorkspace();
        using var second = new TestWorkspace();
        first.Write(".claude/skills/one/SKILL.md", "---\nname: one\ndescription: First repository\n---");
        first.Write(".agents/skills/one/SKILL.md", "---\nname: one\ndescription: First repository\n---");
        first.Write("tests/fixtures/ignored/SKILL.md", "---\nname: ignored\n---");
        second.Write("README.md", "No skills here.");
        foreach (var workspace in new[] { first, second }) await CommitAsync(workspace.Root);
        var sources = new Dictionary<string, string> { ["one"] = first.Root, ["two"] = second.Root };
        var handler = new FakeGitHub(_ => Page([Repo("owner/two"), Repo("owner/one"), Repo("owner/missing")]));
        var scanner = new OrganizationScanner(new GitHubRepositoryLister(handler.Client),
            new GitHubScanner(new RedirectCloneRunner(name => sources.GetValueOrDefault(name))));

        var result = await scanner.ScanAsync(new GitHubOwner("owner"), new OrganizationScanOptions { Concurrency = 2 });

        Assert.Equal(new[] { "owner/missing", "owner/one", "owner/two" }, result.Repositories.Select(r => r.Source));
        Assert.Contains("Git failed", result.Repositories[0].Error);
        var one = result.Repositories[1].Result!;
        Assert.Equal(".agents/skills/one/SKILL.md", Assert.Single(one.Skills).Path);
        Assert.StartsWith($"https://github.com/owner/one/blob/{one.Revision}/", one.Skills[0].Url);
        Assert.Empty(result.Repositories[2].Result!.Skills);
        Assert.Matches("^[0-9a-f]{40}$", result.Repositories[2].Result!.Revision!);
    }

    [Fact]
    public async Task CommandWritesJsonWithRepositoriesSkippedAndExitOneOnFailure()
    {
        var scanner = Scanner([Repo("o/a"), Repo("o/b"), Repo("o/fork", fork: true)], (repository, _) => repository.Name == "b"
            ? throw new ScanException("Git failed: \u001b[31mboom")
            : Task.FromResult(new ScanResult(repository.DisplayName, "abc",
                [new Skill("gradle", "Update", "SKILL.md", "u"), new Skill("docs", "Write", "docs/SKILL.md", "u")], ["SKILL.md: odd \u001b[2J"])));
        using var output = new StringWriter();
        using var error = new StringWriter();
        var options = CliOptions.Parse(["org", "o", "--json", "--query", "gradle"]);

        var exitCode = await OrganizationCommand.RunAsync(options, scanner, false, output, error, null, CancellationToken.None);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("o", json.RootElement.GetProperty("owner").GetString());
        var repositories = json.RootElement.GetProperty("repositories").EnumerateArray().ToArray();
        Assert.Equal(new[] { "o/a", "o/b" }, repositories.Select(r => r.GetProperty("source").GetString()));
        Assert.Equal(JsonValueKind.Null, repositories[0].GetProperty("reference").ValueKind);
        Assert.Equal("gradle", Assert.Single(repositories[0].GetProperty("result").GetProperty("skills").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, repositories[1].GetProperty("result").ValueKind);
        Assert.Contains("boom", repositories[1].GetProperty("error").GetString());
        var skipped = Assert.Single(json.RootElement.GetProperty("skipped").EnumerateArray());
        Assert.Equal("fork", skipped.GetProperty("reason").GetString());
        Assert.Contains("Error (o/b): Git failed:  [31mboom", error.ToString());
        Assert.Contains("Warning (o/a): SKILL.md: odd  [2J", error.ToString());
        Assert.DoesNotContain("\u001b", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandPlainOutputListsOnlyRepositoriesWithSkillsAndSucceeds()
    {
        var scanner = Scanner([Repo("o/empty"), Repo("o/full"), Repo("o/fork", fork: true), Repo("o/old", archived: true), Repo("o/old2", archived: true)],
            (repository, _) => Task.FromResult(new ScanResult(repository.DisplayName, "abc",
                repository.Name == "full" ? [new Skill("[red]build\u001b", "Build things", "SKILL.md", "https://github.com/o/full/blob/abc/SKILL.md")] : [], [])));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await OrganizationCommand.RunAsync(CliOptions.Parse(["org", "o", "--no-color"]), scanner, false, output, error, null, CancellationToken.None);

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.StartsWith("o  1 skill in 1 of 2 repositories", text);
        Assert.Contains("Skipped 1 fork (--include-forks) and 2 archived repositories (--include-archived).", text);
        Assert.Contains("o/full  1 skill", text);
        Assert.Contains("1. [red]build ", text);
        Assert.DoesNotContain("o/empty", text);
        Assert.DoesNotContain("\u001b", text, StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Theory]
    [InlineData(null, "No SKILL.md files found.")]
    [InlineData("absent", "No skills match 'absent'.")]
    public async Task CommandReportsWhenNothingWasFound(string? query, string message)
    {
        var scanner = Scanner([Repo("o/a")], (repository, _) => Task.FromResult(new ScanResult(repository.DisplayName, null, [], [])));
        using var output = new StringWriter();
        var args = query is null ? new[] { "org", "o", "--no-color" } : new[] { "org", "o", "--no-color", "-q", query };
        Assert.Equal(0, await OrganizationCommand.RunAsync(CliOptions.Parse(args), scanner, false, output, TextWriter.Null, null, CancellationToken.None));
        Assert.Contains("o  0 skills in 0 of 1 repository", output.ToString());
        Assert.Contains(message, output.ToString());
    }

    [Fact]
    public async Task StyledOutputShowsProgressSummaryAndTreatsMarkupAsData()
    {
        var scanner = Scanner([Repo("o/a"), Repo("o/b")], (repository, _) => Task.FromResult(new ScanResult(repository.DisplayName, new string('f', 40),
            [new Skill("[blue]skill", "Description [link]", ".agents/skills/x/SKILL.md", "https://github.com/o/a/blob/f/SKILL.md")], [])));
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.No,
            Interactive = InteractionSupport.No,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false }
        });
        console.Profile.Width = 120;
        console.Profile.Encoding = Encoding.UTF8;

        var exitCode = await OrganizationCommand.RunAsync(CliOptions.Parse(["org", "o"]), scanner, true, TextWriter.Null, TextWriter.Null, console, CancellationToken.None);

        Assert.Equal(0, exitCode);
        var text = writer.ToString();
        Assert.Contains("2 skills in 2 of 2 repositories", text);
        Assert.Contains("o/a", text);
        Assert.Contains("o/b", text);
        Assert.Contains("[blue]skill", text);
        Assert.Contains("Commit ffffffffffff", text);
    }

    [Fact]
    public void TokenIsReadFromGitHubTokenThenGhToken()
    {
        var original = (Environment.GetEnvironmentVariable("GITHUB_TOKEN"), Environment.GetEnvironmentVariable("GH_TOKEN"));
        try
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", " ");
            Environment.SetEnvironmentVariable("GH_TOKEN", "gh");
            Assert.Equal("gh", OrganizationCommand.TokenFromEnvironment());
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "github");
            Assert.Equal("github", OrganizationCommand.TokenFromEnvironment());
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
            Environment.SetEnvironmentVariable("GH_TOKEN", null);
            Assert.Null(OrganizationCommand.TokenFromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", original.Item1);
            Environment.SetEnvironmentVariable("GH_TOKEN", original.Item2);
        }
    }

    private static OrganizationScanner Scanner(IEnumerable<string> repositories,
        Func<GitHubRepository, CancellationToken, Task<ScanResult>> scan) =>
        new(new GitHubRepositoryLister(new FakeGitHub(_ => Page(repositories)).Client), scan);

    private static string Repo(string fullName, bool fork = false, bool archived = false, bool disabled = false) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["full_name"] = fullName, ["fork"] = fork, ["archived"] = archived, ["disabled"] = disabled });

    private static HttpResponseMessage Page(IEnumerable<string> repositories) =>
        new(HttpStatusCode.OK) { Content = new StringContent($"[{string.Join(',', repositories)}]", Encoding.UTF8, "application/json") };

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current) { }
    }

    private static async Task CommitAsync(string root)
    {
        Task Git(params string[] args) => new GitRunner().RunAsync(root,
            new[] { "-c", $"core.hooksPath={Path.Combine(root, "no-hooks")}" }.Concat(args).ToArray(), CancellationToken.None);
        await Git("init", "--initial-branch=main");
        await Git("config", "uploadpack.allowFilter", "true");
        await Git("add", ".");
        await Git("-c", "user.name=Skill Atlas Tests", "-c", "user.email=tests@example.invalid", "-c", "commit.gpgsign=false", "commit", "-m", "Fixture");
    }

    private sealed record RecordedRequest(Uri Uri, string? UserAgent, string? Authorization);

    private sealed class FakeGitHub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];
        public HttpClient Client => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (Requests)
                Requests.Add(new(request.RequestUri!, request.Headers.UserAgent.ToString(), request.Headers.Authorization?.ToString()));
            return Task.FromResult(respond(request));
        }
    }

    // Exercises the production Git pipeline offline: only the clone source changes.
    private sealed class RedirectCloneRunner(Func<string, string?> localSource) : IGitRunner
    {
        public Task<string> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            var adapted = arguments.ToArray();
            if (adapted.Contains("clone"))
            {
                var name = adapted[^2].Split('/')[^1].Replace(".git", "", StringComparison.Ordinal);
                adapted[^2] = localSource(name) is { } path ? new Uri(path).AbsoluteUri : new Uri(Path.Combine(Path.GetTempPath(), "skill-atlas-missing", Guid.NewGuid().ToString("N"))).AbsoluteUri;
            }
            return new GitRunner().RunAsync(workingDirectory,
                new[] { "-c", "protocol.file.allow=always" }.Concat(adapted).ToArray(), cancellationToken);
        }
    }
}
