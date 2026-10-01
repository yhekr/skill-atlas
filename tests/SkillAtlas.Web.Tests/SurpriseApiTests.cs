using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SkillAtlas.Core;

namespace SkillAtlas.Web.Tests;

public sealed partial class WebApiTests
{
    private static StoredScan ScanOf(string source, params string[] names)
    {
        var skills = names.Select(name => new Skill(name, $"About {name}", $"{name}/SKILL.md", $"https://github.com/{source}/blob/abc/{name}/SKILL.md")).ToArray();
        return new(Guid.NewGuid(), new(new(source, "abc", skills, []), skills.ToDictionary(skill => skill.Path, skill => $"# {skill.Name}")));
    }

    [Fact]
    public async Task SurprisePicksAcrossScansSkipsTheOpenSkillAndReturnsAnOpenableIndex()
    {
        await using var app = new TestApplication();
        var first = ScanOf("owner/one", "alpha", "beta");
        var second = ScanOf("owner/two", "gamma", "delta");
        app.Catalog.Add(first);
        app.Catalog.Add(second);
        var random = new FixedRandom(2, 1);
        app.RandomSource = random;
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/surprise", new
        {
            scanIds = new[] { first.Id, second.Id },
            exclude = new { scanId = second.Id, index = 0 }
        });
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(second.Id, json.GetProperty("scanId").GetGuid());
        Assert.Equal(1, json.GetProperty("index").GetInt32());
        Assert.Equal("delta", json.GetProperty("skill").GetProperty("name").GetString());
        Assert.Equal(SkillRoulette.Fortunes[1], json.GetProperty("fortune").GetString());
        Assert.Equal([3, SkillRoulette.Fortunes.Count], random.Bounds);
        Assert.Equal(0, app.Catalog.Calls);

        var document = await client.GetFromJsonAsync<JsonElement>($"/api/scans/{second.Id}/skills/{json.GetProperty("index").GetInt32()}");
        Assert.Equal("# delta", document.GetProperty("source").GetString());
    }

    [Fact]
    public async Task SurpriseWithTheDefaultRandomStaysInsideTheRequestedScans()
    {
        await using var app = new TestApplication();
        var scan = ScanOf("owner/one", "alpha", "beta", "gamma");
        app.Catalog.Add(scan);
        using var client = app.CreateClient();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var json = await (await client.PostAsJsonAsync("/api/surprise", new
            {
                scanIds = new[] { scan.Id },
                exclude = new { scanId = scan.Id, index = 1 }
            })).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(scan.Id, json.GetProperty("scanId").GetGuid());
            Assert.Contains(json.GetProperty("index").GetInt32(), new[] { 0, 2 });
            Assert.Contains(json.GetProperty("fortune").GetString(), SkillRoulette.Fortunes);
        }
    }

    [Fact]
    public async Task SurpriseRejectsInvalidScanLists()
    {
        await using var app = new TestApplication();
        using var client = app.CreateClient();
        var id = app.Catalog.Scan.Id;
        object[] bodies = [
            new { },
            new { scanIds = Array.Empty<Guid>() },
            new { scanIds = new[] { id, id } },
            new { scanIds = Enumerable.Range(0, ScanBatch.MaximumSources + 1).Select(_ => Guid.NewGuid()).ToArray() },
            new { scanIds = new[] { "not-a-guid" } }
        ];
        foreach (var body in bodies)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/surprise", body)).StatusCode);
    }

    [Fact]
    public async Task SurpriseReportsExpiredScansAndEmptyCollections()
    {
        await using var app = new TestApplication();
        var empty = ScanOf("owner/empty");
        app.Catalog.Add(empty);
        using var client = app.CreateClient();

        var expired = await client.PostAsJsonAsync("/api/surprise", new { scanIds = new[] { app.Catalog.Scan.Id, Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        Assert.Contains("expired", (await expired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var nothing = await client.PostAsJsonAsync("/api/surprise", new { scanIds = new[] { empty.Id } });
        Assert.Equal(HttpStatusCode.NotFound, nothing.StatusCode);
        Assert.Contains("no skills", (await nothing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task SurpriseIgnoresAnExclusionFromAnotherScan()
    {
        await using var app = new TestApplication();
        app.RandomSource = new FixedRandom(0, 0);
        using var client = app.CreateClient();
        var json = await (await client.PostAsJsonAsync("/api/surprise", new
        {
            scanIds = new[] { app.Catalog.Scan.Id },
            exclude = new { scanId = Guid.NewGuid(), index = 0 }
        })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("example", json.GetProperty("skill").GetProperty("name").GetString());
    }

    [Fact]
    public async Task CrossOriginRequestsCannotSpin()
    {
        await using var app = new TestApplication();
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://unrelated.example");
        var response = await client.PostAsJsonAsync("/api/surprise", new { scanIds = new[] { app.Catalog.Scan.Id } });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed class FixedRandom(params int[] values) : Random
    {
        private int _next;
        public List<int> Bounds { get; } = [];

        public override int Next(int maxValue)
        {
            Bounds.Add(maxValue);
            return values[_next++];
        }
    }
}
