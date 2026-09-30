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
}
