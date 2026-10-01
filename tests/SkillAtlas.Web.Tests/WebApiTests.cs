using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkillAtlas.Core;

namespace SkillAtlas.Web.Tests;

public sealed partial class WebApiTests
{
    [Fact]
    public async Task SimilarSkillsComeFromTheSameSnapshotAndCanBeOpenedByReturnedIndex()
    {
        await using var app = new TestApplication();
        Skill[] skills = [
            new("gradle-upgrade", "Update Gradle dependencies", "one/SKILL.md", "https://github.com/owner/repo/blob/abc/one/SKILL.md"),
            new("gradle-upgrade-tests", "Update Gradle dependencies in tests", "two/SKILL.md", "https://github.com/owner/repo/blob/abc/two/SKILL.md"),
            new("image-colors", "Draw colorful pictures", "three/SKILL.md", "https://github.com/owner/repo/blob/abc/three/SKILL.md")
        ];
        app.Catalog.Scan = new(Guid.NewGuid(), new(new ScanResult("owner/repo", "abc", skills, []),
            skills.ToDictionary(skill => skill.Path, skill => $"# {skill.Name}")));
        using var client = app.CreateClient();
        var response = await client.GetAsync($"/api/scans/{app.Catalog.Scan.Id}/skills/0/similar");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var match = Assert.Single(json.GetProperty("matches").EnumerateArray());
        Assert.Equal(1, match.GetProperty("index").GetInt32());
        Assert.Equal("gradle-upgrade-tests", match.GetProperty("skill").GetProperty("name").GetString());
        Assert.Contains("gradle", match.GetProperty("sharedTerms").EnumerateArray().Select(term => term.GetString()));
        Assert.InRange(match.GetProperty("score").GetDouble(), SkillSimilarity.MinimumScore, 1);
        Assert.Equal(0, app.Catalog.Calls);
        var document = await client.GetFromJsonAsync<JsonElement>($"/api/scans/{app.Catalog.Scan.Id}/skills/{match.GetProperty("index").GetInt32()}");
        Assert.Equal("# gradle-upgrade-tests", document.GetProperty("source").GetString());
        Assert.Contains("/blob/abc/", document.GetProperty("url").GetString());
    }

    [Fact]
    public async Task SimilarityForASingleSkillReturnsAnEmptyArray()
    {
        await using var app = new TestApplication();
        using var client = app.CreateClient();
        var json = await client.GetFromJsonAsync<JsonElement>($"/api/scans/{app.Catalog.Scan.Id}/skills/0/similar");
        Assert.Empty(json.GetProperty("matches").EnumerateArray());
    }

    [Fact]
    public async Task SimilarityIsLimitedToFiveResults()
    {
        await using var app = new TestApplication();
        var skills = Enumerable.Range(0, 9).Select(index =>
            new Skill($"gradle-upgrade-{index}", "Gradle dependencies", $"{index}/SKILL.md", "https://github.com/o/r/blob/abc/SKILL.md")).ToArray();
        app.Catalog.Scan = new(Guid.NewGuid(), new(new("o/r", "abc", skills, []), new Dictionary<string, string>()));
        using var client = app.CreateClient();
        var json = await client.GetFromJsonAsync<JsonElement>($"/api/scans/{app.Catalog.Scan.Id}/skills/0/similar");
        Assert.Equal(5, json.GetProperty("matches").GetArrayLength());
        Assert.All(json.GetProperty("matches").EnumerateArray(), match => Assert.NotEqual(0, match.GetProperty("index").GetInt32()));
    }

    [Theory]
    [InlineData("/skill-filter.mjs?v=stars-1")]
    [InlineData("/skill-stars.mjs?v=stars-1")]
    public async Task BrowserModulesAreServedWithJavaScriptContentType(string path)
    {
        await using var app = new TestApplication();
        using var client = app.CreateClient();
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        Assert.Contains(response.Content.Headers.ContentType?.MediaType,
            new[] { "text/javascript", "application/javascript" });
    }

    [Fact]
    public async Task HomePageServesRepositoryFormAndSecurityHeaders()
    {
        await using var app = new TestApplication();
        using var client = app.CreateClient();
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("id=\"repository\"", html);
        Assert.Contains("id=\"starred-toggle\"", html);
        Assert.Contains("id=\"star-button\"", html);
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task ScanAndReadReturnsMetadataThenPinnedDocument()
    {
        await using var app = new TestApplication();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/scans", new { repository = "github.com/owner/repo", reference = " feature/skills " });
        response.EnsureSuccessStatusCode();
        var scan = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("owner/repo", scan.GetProperty("source").GetString());
        Assert.Equal("feature/skills", app.Catalog.Reference);
        var item = Assert.Single(scan.GetProperty("skills").EnumerateArray());
        Assert.Equal("example", item.GetProperty("name").GetString());
        Assert.False(item.TryGetProperty("contentHash", out _));
        Assert.False(item.TryGetProperty("source", out _));
        var documentResponse = await client.GetAsync($"/api/scans/{scan.GetProperty("scanId").GetGuid()}/skills/0");
        var document = await documentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(FakeCatalog.Source, document.GetProperty("source").GetString());
        Assert.Contains("<h1>Example</h1>", document.GetProperty("html").GetString());
        Assert.Contains("/blob/abc/", document.GetProperty("url").GetString());
        Assert.True(documentResponse.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("https://example.com/owner/repo")]
    [InlineData("C:\\private\\folder")]
    [InlineData("file:///tmp/secret")]
    [InlineData("https://github.com/owner/repo/tree/main")]
    public async Task InvalidRepositoryIsRejectedBeforeScannerRuns(string repository)
    {
        await using var app = new TestApplication();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/scans", new { repository });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, app.Catalog.Calls);
    }

    [Theory]
    [InlineData("--upload-pack=command")]
    [InlineData("main\nother")]
    public async Task InvalidRefIsRejected(string reference)
    {
        await using var app = new TestApplication();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/scans", new { repository = "owner/repo", reference });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, app.Catalog.Calls);
    }

    [Fact]
    public async Task CrossOriginRequestsCannotStartScan()
    {
        await using var app = new TestApplication();
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://unrelated.example");
        var response = await client.PostAsJsonAsync("/api/scans", new { repository = "owner/repo" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, app.Catalog.Calls);
    }

    [Fact]
    public async Task UnknownScanAndInvalidIndexHaveClearStatusCodes()
    {
        await using var app = new TestApplication();
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync($"/api/scans/{Guid.NewGuid()}/skills/0")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync($"/api/scans/{Guid.NewGuid()}/skills/0/similar")).StatusCode);
        foreach (var index in new[] { -1, 1, 9999 })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/scans/{app.Catalog.Scan.Id}/skills/{index}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/scans/{app.Catalog.Scan.Id}/skills/{index}/similar")).StatusCode);
        }
    }

    [Theory]
    [InlineData("busy", 429)]
    [InlineData("cancelled", 408)]
    [InlineData("git", 422)]
    public async Task ScannerFailuresAreControlledAndDoNotLeakDetails(string failure, int expectedStatus)
    {
        await using var app = new TestApplication();
        app.Catalog.Failure = failure;
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/scans", new { repository = "owner/repo" });
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("error", text);
        Assert.DoesNotContain("private-temp-path", text);
    }

    private sealed class TestApplication : WebApplicationFactory<Program>
    {
        public FakeCatalog Catalog { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IScanCatalog>();
            services.AddSingleton<IScanCatalog>(Catalog);
        });
    }

    private sealed class FakeCatalog : IScanCatalog
    {
        public const string Source = "---\nname: example\ndescription: Example skill\n---\n# Example\n\nRead the **instructions**.";
        public StoredScan Scan { get; set; } = new(Guid.NewGuid(), new RepositorySnapshot(
            new ScanResult("owner/repo", "abc", [new Skill("example", "Example skill", "skills/example/SKILL.md", "https://github.com/owner/repo/blob/abc/skills/example/SKILL.md")], []),
            new Dictionary<string, string> { ["skills/example/SKILL.md"] = Source }));
        public int Calls { get; private set; }
        public string? Reference { get; private set; }
        public string? Failure { get; set; }
        public Func<GitHubRepository, StoredScan>? Factory { get; set; }
        private readonly Dictionary<Guid, StoredScan> _scans = [];

        public Task<StoredScan> ScanAsync(GitHubRepository repository, string? reference, CancellationToken cancellationToken)
        {
            Calls++;
            Reference = reference;
            if (Factory is not null)
            {
                var scan = Factory(repository);
                _scans[scan.Id] = scan;
                return Task.FromResult(scan);
            }
            return Failure switch
            {
                "busy" => throw new ScanBusyException(),
                "cancelled" => throw new OperationCanceledException(),
                "git" => throw new ScanException("private-temp-path"),
                _ => Task.FromResult(Scan)
            };
        }

        public StoredScan? Find(Guid id) => id == Scan.Id ? Scan : _scans.GetValueOrDefault(id);
    }
}
