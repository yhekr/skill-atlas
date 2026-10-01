using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SkillAtlas.Core;

namespace SkillAtlas.Web.Tests;

public sealed partial class WebApiTests
{
    private static StoredScan RepositoryScan(GitHubRepository repository)
    {
        var source = repository.DisplayName;
        return new(Guid.NewGuid(), new(new(source, repository.Name,
            [new("Build", "Gradle wrapper", "same/SKILL.md", repository.FileUrl(repository.Name, "same/SKILL.md"))], []),
            new Dictionary<string, string> { ["same/SKILL.md"] = $"# {source}" }));
    }

    [Fact]
    public async Task BatchKeepsRepositoriesAndPinnedDocumentsSeparateAndDeduplicatesAliases()
    {
        await using var app = new TestApplication();
        app.Catalog.Factory = RepositoryScan;
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/scan-batches", new
        {
            repositories = new[] { "owner/one", "https://github.com/OWNER/one.git", "owner/two" },
            reference = " release "
        });
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(2, app.Catalog.Calls);
        Assert.Equal("release", app.Catalog.Reference);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var scans = result.GetProperty("scans").EnumerateArray().ToArray();
        Assert.Equal(2, scans.Length);
        Assert.Empty(result.GetProperty("failures").EnumerateArray());
        foreach (var scan in scans)
        {
            var doc = await client.GetFromJsonAsync<JsonElement>($"/api/scans/{scan.GetProperty("scanId").GetGuid()}/skills/0");
            Assert.Equal("# " + scan.GetProperty("source").GetString(), doc.GetProperty("source").GetString());
            Assert.Contains("/blob/" + scan.GetProperty("revision").GetString() + "/", doc.GetProperty("url").GetString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchFailureIsPerRepositoryAndNeverLeaksPrivateDiagnostics(bool allFail)
    {
        await using var app = new TestApplication();
        app.Catalog.Factory = repository => allFail || repository.Name == "bad"
            ? throw new ScanException("credential-bearing-private-temp-path") : RepositoryScan(repository);
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/scan-batches", new { repositories = new[] { "owner/good", "owner/bad" } });
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-temp", text);
        using var json = JsonDocument.Parse(text);
        Assert.Equal(allFail ? 0 : 1, json.RootElement.GetProperty("scans").GetArrayLength());
        Assert.Equal(allFail ? 2 : 1, json.RootElement.GetProperty("failures").GetArrayLength());
        Assert.Equal(2, app.Catalog.Calls);
    }

    [Theory]
    [InlineData("{\"repositories\":null}")]
    [InlineData("{\"repositories\":[]}")]
    [InlineData("{\"repositories\":[\"o/r\",null]}")]
    [InlineData("{\"repositories\":[\"o/r\",\"https://evil.example/o/r\"]}")]
    [InlineData("{\"repositories\":[\"o/r\",\"o/r\",\"o/r\",\"o/r\",\"o/r\",\"o/r\"]}")]
    [InlineData("{\"repositories\":[\"o/r\"],\"reference\":\"--option\"}")]
    public async Task InvalidBatchIsRejectedBeforeAnyScan(string body)
    {
        await using var app = new TestApplication();
        using var client = app.CreateClient();
        var response = await client.PostAsync("/api/scan-batches", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, app.Catalog.Calls);
    }

    [Theory]
    [InlineData("busy", 429)]
    [InlineData("cancelled", 408)]
    public async Task BatchPropagatesCapacityAndCancellation(string failure, int expected)
    {
        await using var app = new TestApplication();
        app.Catalog.Failure = failure;
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/scan-batches", new { repositories = new[] { "owner/one", "owner/two" } });
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(1, app.Catalog.Calls);
    }

    [Fact]
    public async Task BatchRejectsCrossOriginAndRetainsSuccessfulEmptyRepositories()
    {
        await using var app = new TestApplication();
        app.Catalog.Scan = new(Guid.NewGuid(), new(new("owner/empty", null, [], []), new Dictionary<string, string>()));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://unrelated.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/scan-batches", new { repositories = new[] { "owner/empty" } })).StatusCode);
        Assert.Equal(0, app.Catalog.Calls);
        client.DefaultRequestHeaders.Remove("Origin");
        var response = await client.PostAsJsonAsync("/api/scan-batches", new { repositories = new[] { "owner/empty" } });
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(Assert.Single(result.GetProperty("scans").EnumerateArray()).GetProperty("skills").EnumerateArray());
    }
}
