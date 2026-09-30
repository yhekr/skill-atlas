using System.Text;
using SkillAtlas.Cli;
using SkillAtlas.Core;
using Spectre.Console;

namespace SkillAtlas.Tests;

public class RendererTests
{
    [Theory]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(160)]
    public void StyledOutputHandlesBracketsUnicodeAndNarrowTerminals(int width)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.No,
            Interactive = InteractionSupport.No
        });
        console.Profile.Width = width;
        console.Profile.Encoding = Encoding.UTF8;
        var result = new ScanResult("owner/[red]repo", new string('a', 40),
            [new Skill("[red] skill 😀", "Description with [link] markup", ".agents/skills/[red]/SKILL.md", "https://github.com/o/r/blob/a/%5Bred%5D/SKILL.md")], []);
        ResultRenderer.Render(result, true, null, console);
        var output = writer.ToString();
        Assert.Contains("1 skill", output);
        Assert.Contains("[red]", output);
        Assert.Contains("SKILL.md", output);
    }

    [Fact]
    public void TruncatingDescriptionDoesNotSplitEmojiSurrogatePair()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No });
        console.Profile.Width = 240;
        console.Profile.Encoding = Encoding.UTF8;
        var description = new string('x', 176) + "😀" + new string('y', 20);
        ResultRenderer.Render(new ScanResult("repo", null, [new Skill("skill", description, "SKILL.md", "https://example.com")], []), true, null, console);
        Assert.Contains("😀…", writer.ToString());
        Assert.DoesNotContain("\ufffd", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("yyyy", writer.ToString());
    }
}
