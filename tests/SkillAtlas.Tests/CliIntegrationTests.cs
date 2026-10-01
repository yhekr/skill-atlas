using System.Text.Json;
using SkillAtlas.Cli;

namespace SkillAtlas.Tests;

public class CliIntegrationTests
{
    private static readonly string CliAssembly = typeof(CliOptions).Assembly.Location;

    [Theory]
    [InlineData(" WRAPPER  GRADLE ", "gradle-build")]
    [InlineData("gradle absent", null)]
    [InlineData("build/SKILL.md wrapper", "gradle-build")]
    public async Task QueryMatchesAllWordsAcrossFieldsThroughActualCli(string query, string? expected)
    {
        using var workspace = new TestWorkspace();
        workspace.Write("build/SKILL.md", "---\nname: gradle-build\ndescription: Update the wrapper\n---");
        workspace.Write("docs/SKILL.md", "---\nname: gradle-docs\ndescription: Write documentation\n---");
        var result = await workspace.RunAsync("dotnet", CliAssembly, "scan", workspace.Root, "--query", query, "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        var skills = json.RootElement.GetProperty("skills").EnumerateArray();
        if (expected is null) Assert.Empty(skills);
        else Assert.Equal(expected, Assert.Single(skills).GetProperty("name").GetString());
    }

    [Fact]
    public async Task SimilarityJsonUsesActualScannerAndReportsSharedTerms()
    {
        using var workspace = new TestWorkspace();
        workspace.Write("one/SKILL.md", "---\nname: gradle-upgrade\ndescription: Update Gradle dependencies\n---");
        workspace.Write("two/SKILL.md", "---\nname: gradle-upgrade-tests\ndescription: Update Gradle dependencies in tests\n---");
        workspace.Write("three/SKILL.md", "---\nname: image-colors\ndescription: Draw colorful pictures\n---");
        var result = await workspace.RunAsync("dotnet", CliAssembly, "scan", workspace.Root, "--similar", "gradle-upgrade", "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("gradle-upgrade", json.RootElement.GetProperty("selected").GetProperty("name").GetString());
        var match = Assert.Single(json.RootElement.GetProperty("matches").EnumerateArray());
        Assert.Equal("gradle-upgrade-tests", match.GetProperty("skill").GetProperty("name").GetString());
        Assert.InRange(match.GetProperty("score").GetDouble(), 0.15, 1);
        Assert.Contains("gradle", match.GetProperty("sharedTerms").EnumerateArray().Select(term => term.GetString()));
        Assert.False(json.RootElement.TryGetProperty("skills", out _));
    }

    [Fact]
    public async Task SimilarityRequiresAnUnambiguousSkillAndAcceptsItsExactPath()
    {
        using var workspace = new TestWorkspace();
        workspace.Write("one/SKILL.md", "---\nname: build\ndescription: Gradle dependencies\n---");
        workspace.Write("two/SKILL.md", "---\nname: build\ndescription: Gradle tests\n---");
        foreach (var selector in new[] { "build", "missing" })
        {
            var result = await workspace.RunAsync("dotnet", CliAssembly, "scan", workspace.Root, "--similar", selector, "--json");
            Assert.Equal(2, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains("Error:", result.Error);
        }
        var byPath = await workspace.RunAsync("dotnet", CliAssembly, "scan", workspace.Root, "--similar", "one/SKILL.md", "--no-color");
        Assert.Equal(0, byPath.ExitCode);
        Assert.Contains("overlap", byPath.Output);
        Assert.Contains("two/SKILL.md", byPath.Output);
        Assert.DoesNotContain("\u001b", byPath.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoSimilarSkillsIsSuccessfulAndDoesNotSuggestTheSelectedSkill()
    {
        using var workspace = new TestWorkspace();
        workspace.Write("one/SKILL.md", "---\nname: Unique\ndescription: Only one skill.\n---");
        var result = await workspace.RunAsync("dotnet", CliAssembly, "scan", workspace.Root, "--similar", "Unique", "--no-color");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No similar skills found.", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task CheckedInFixtureMatchesTheSlideThroughActualCli()
    {
        using var workspace = new TestWorkspace();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SkillAtlas.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var fixture = Path.Combine(directory.FullName, "fixtures", "scan");
        var result = await workspace.RunAsync("dotnet", CliAssembly, "scan", fixture, "--json");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(new[] { "review-change", "build-project" },
            json.RootElement.GetProperty("skills").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task EndOfOptionsAllowsDirectoryNameStartingWithHyphen()
    {
        using var workspace = new TestWorkspace();
        workspace.Write("-folder/SKILL.md", "# Hyphen\n\nDescription");
        var result = await workspace.RunAsync("dotnet", CliAssembly, "scan", "--json", "--", "-folder");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("Hyphen", Assert.Single(json.RootElement.GetProperty("skills").EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task JsonOutputIsParseableAndWarningsStayOnStderr()
    {
        using var workspace = new TestWorkspace();
        workspace.Write("valid/SKILL.md", "---\nname: Русский [red]\ndescription: Find Gradle 😀\n---");
        workspace.Write("broken/SKILL.md", "---\nname: [broken\n---\nFallback.");
        var result = await workspace.RunAsync("dotnet", CliAssembly, "scan", workspace.Root, "--json", "--query", "gradle");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("Русский [red]", Assert.Single(json.RootElement.GetProperty("skills").EnumerateArray()).GetProperty("name").GetString());
        Assert.Single(json.RootElement.GetProperty("warnings").EnumerateArray());
        Assert.Contains("Warning:", result.Error);
        Assert.DoesNotContain("\u001b", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlainOutputEscapesControlsAndPreservesLongDescriptions()
    {
        using var workspace = new TestWorkspace();
        var description = new string('x', 400);
        workspace.Write("[red]skill/SKILL.md", $"---\nname: 'literal [red] name'\ndescription: '{description}'\n---");
        var result = await workspace.RunAsync("dotnet", CliAssembly, "scan", workspace.Root, "--no-color");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("literal [red] name", result.Output);
        Assert.Contains(description, result.Output);
        Assert.DoesNotContain("\u001b", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task EmptyResultIsSuccessAndNeverPollutesJson()
    {
        using var workspace = new TestWorkspace();
        var result = await workspace.RunAsync("dotnet", CliAssembly, "scan", workspace.Root, "--json");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Empty(json.RootElement.GetProperty("skills").EnumerateArray());
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("scan", "--json")]
    [InlineData("scan", "--unknown")]
    [InlineData("unknown", "command")]
    public async Task InvalidUsageReturnsTwoAndWritesOnlyStderr(string first, string second)
    {
        using var workspace = new TestWorkspace();
        var result = await workspace.RunAsync("dotnet", CliAssembly, first, second);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Error:", result.Error);
    }

    [Theory]
    [InlineData("org")]
    [InlineData("org bad--name")]
    [InlineData("org JetBrains --ref main")]
    [InlineData("org JetBrains --concurrency 99")]
    public async Task InvalidOrganizationUsageFailsBeforeAnyNetworkAccess(string command)
    {
        using var workspace = new TestWorkspace();
        var result = await workspace.RunAsync("dotnet", new[] { CliAssembly }.Concat(command.Split(' ')).ToArray());
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Error:", result.Error);
        Assert.DoesNotContain(" at ", result.Error);
    }

    [Fact]
    public async Task OrganizationHelpIsPrintedWithoutScanning()
    {
        using var workspace = new TestWorkspace();
        var result = await workspace.RunAsync("dotnet", CliAssembly, "org", "--help");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--include-archived", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task MissingPathAndLocalRefReturnOneWithoutStackTrace()
    {
        using var workspace = new TestWorkspace();
        foreach (var args in new[]
        {
            new[] { "scan", Path.Combine(workspace.Root, "missing"), "--json" },
            new[] { "scan", workspace.Root, "--ref", "main" }
        })
        {
            var result = await workspace.RunAsync("dotnet", new[] { CliAssembly }.Concat(args).ToArray());
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains("Error:", result.Error);
            Assert.DoesNotContain(" at ", result.Error);
        }
    }
}
