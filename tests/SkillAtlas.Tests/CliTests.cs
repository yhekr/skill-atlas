using SkillAtlas.Cli;

namespace SkillAtlas.Tests;

public class CliTests
{
    [Fact]
    public void ParsesScanOptions()
    {
        var options = CliOptions.Parse(["scan", "owner/repo", "--ref", "feature/skills", "-q", "gradle", "--json", "--no-color"]);
        Assert.Equal("owner/repo", options.Source);
        Assert.Equal("feature/skills", options.Reference);
        Assert.Equal("gradle", options.Query);
        Assert.True(options.Json);
        Assert.True(options.NoColor);
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("scan owner/repo --ref")]
    [InlineData("scan owner/repo --query --json")]
    [InlineData("scan owner/repo --similar")]
    [InlineData("scan owner/repo --similar --json")]
    [InlineData("scan owner/repo --query gradle --similar gradle")]
    [InlineData("scan owner/repo --unknown")]
    [InlineData("unknown")]
    public void RejectsInvalidArguments(string command) =>
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(command.Split(' ')));

    [Fact]
    public void StripsTerminalControlCharacters() =>
        Assert.Equal("hello [31mworld ", ResultRenderer.SafeText("hello\u001b[31mworld\u202e"));

    [Fact]
    public void ParsesSimilaritySelectorAlongsideRefAndJson()
    {
        var options = CliOptions.Parse(["scan", "owner/repo", "--similar", ".claude/skills/build/SKILL.md", "--ref", "main", "--json"]);
        Assert.Equal(".claude/skills/build/SKILL.md", options.SimilarTo);
        Assert.Equal("main", options.Reference);
        Assert.True(options.Json);
        Assert.Null(options.Query);
    }

    [Fact]
    public void ParsesOrganizationOptions()
    {
        var options = CliOptions.Parse(["org", "https://github.com/JetBrains", "--include-forks", "--include-archived",
            "--concurrency", "8", "-q", "gradle", "--json", "--no-color"]);
        Assert.Equal("JetBrains", options.Organization);
        Assert.Null(options.Source);
        Assert.True(options.IncludeForks);
        Assert.True(options.IncludeArchived);
        Assert.Equal(8, options.Concurrency);
        Assert.Equal("gradle", options.Query);
        Assert.True(options.Json);
        Assert.True(options.NoColor);
    }

    [Fact]
    public void OrganizationDefaultsSkipForksAndArchivedWithFourParallelScans()
    {
        var options = CliOptions.Parse(["org", "JetBrains"]);
        Assert.Equal("JetBrains", options.Organization);
        Assert.False(options.IncludeForks);
        Assert.False(options.IncludeArchived);
        Assert.Equal(4, options.Concurrency);
        Assert.True(CliOptions.Parse(["org", "--help"]).Help);
        Assert.Equal("JetBrains", CliOptions.Parse(["org", "--", "JetBrains"]).Organization);
    }

    [Theory]
    [InlineData("org")]
    [InlineData("org JetBrains Kotlin")]
    [InlineData("org JetBrains/kotlin")]
    [InlineData("org bad--name")]
    [InlineData("org JetBrains --ref main")]
    [InlineData("org JetBrains --similar build")]
    [InlineData("org JetBrains --concurrency")]
    [InlineData("org JetBrains --concurrency 0")]
    [InlineData("org JetBrains --concurrency 17")]
    [InlineData("org JetBrains --concurrency -1")]
    [InlineData("org JetBrains --concurrency +4")]
    [InlineData("org JetBrains --concurrency four")]
    [InlineData("org JetBrains --unknown")]
    public void RejectsInvalidOrganizationArguments(string command) =>
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(command.Split(' ')));

    [Fact]
    public void HelpDocumentsOrganizationScans()
    {
        Assert.Contains("skill-atlas org <organization", CliOptions.HelpText);
        Assert.Contains("--include-forks", CliOptions.HelpText);
        Assert.Contains("GITHUB_TOKEN", CliOptions.HelpText);
    }
}
