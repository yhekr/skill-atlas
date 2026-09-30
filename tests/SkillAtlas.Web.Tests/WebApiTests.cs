using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkillAtlas.Core;

namespace SkillAtlas.Web.Tests;

public sealed class WebApiTests
{
    [Fact]
    public async Task HomePageServesRepositoryFormAndSecurityHeaders()
    {
        await using var app = new TestApplication();
        using var client = app.CreateClient();
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"repository\"", await response.Content.ReadAsStringAsync());
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
        foreach (var index in new[] { -1, 1, 9999 })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/scans/{app.Catalog.Scan.Id}/skills/{index}")).StatusCode);
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
        public StoredScan Scan { get; } = new(Guid.NewGuid(), new RepositorySnapshot(
            new ScanResult("owner/repo", "abc", [new Skill("example", "Example skill", "skills/example/SKILL.md", "https://github.com/owner/repo/blob/abc/skills/example/SKILL.md")], []),
            new Dictionary<string, string> { ["skills/example/SKILL.md"] = Source }));
        public int Calls { get; private set; }
        public string? Reference { get; private set; }
        public string? Failure { get; set; }

        public Task<StoredScan> ScanAsync(GitHubRepository repository, string? reference, CancellationToken cancellationToken)
        {
            Calls++;
            Reference = reference;
            return Failure switch
            {
                "busy" => throw new ScanBusyException(),
                "cancelled" => throw new OperationCanceledException(),
                "git" => throw new ScanException("private-temp-path"),
                _ => Task.FromResult(Scan)
            };
        }

        public StoredScan? Find(Guid id) => id == Scan.Id ? Scan : null;
    }
}
