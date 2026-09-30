using System.Text.Json;
using SkillAtlas.Cli;

namespace SkillAtlas.Tests;

public class CliIntegrationTests
{
    private static readonly string CliAssembly = typeof(CliOptions).Assembly.Location;

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
