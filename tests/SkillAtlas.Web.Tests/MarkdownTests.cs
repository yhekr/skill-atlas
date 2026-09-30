namespace SkillAtlas.Web.Tests;

public class MarkdownTests
{
    [Fact]
    public void FrontMatterIsNotRenderedAndRelativeLinksPointToPinnedRevision()
    {
        var html = SkillMarkdown.Render("---\nname: hidden-metadata\n---\n# Heading\n\n[Guide](../guide.md)", "https://github.com/o/r/blob/abc/skills/example/SKILL.md");
        Assert.DoesNotContain("hidden-metadata", html);
        Assert.Contains("<h1>Heading</h1>", html);
        Assert.Contains("https://github.com/o/r/blob/abc/skills/guide.md", html);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("[click](javascript:alert%281%29)")]
    [InlineData("![tracking](https://example.com/tracker.png)")]
    [InlineData("[click](data:text/html,script)")]
    public void UntrustedMarkdownCannotProduceExecutableHtmlOrRemoteImages(string source)
    {
        var html = SkillMarkdown.Render(source, "https://github.com/o/r/blob/abc/SKILL.md");
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"data:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FencedHtmlIsDisplayedAsCode()
    {
        var html = SkillMarkdown.Render("```html\n<script>example</script>\n```", "https://github.com/o/r/blob/abc/SKILL.md");
        Assert.Contains("<pre><code>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }
}
