using Ganss.Xss;
using Markdig;

namespace SkillAtlas.Web;

public static class SkillMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseEmphasisExtras().DisableHtml().Build();

    public static string Render(string source, string sourceUrl)
    {
        var lines = source.TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n');
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            var end = Array.FindIndex(lines, 1, line => line.Trim() is "---" or "...");
            if (end >= 0) source = string.Join('\n', lines[(end + 1)..]);
        }
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "h1", "h2", "h3", "h4", "h5", "h6", "a", "pre", "code", "ul", "ol", "li", "blockquote", "strong", "em", "del", "hr", "br", "table", "thead", "tbody", "tr", "td", "th" })
            sanitizer.AllowedTags.Add(tag);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedAttributes.Add("title");
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("http");
        return sanitizer.Sanitize(Markdown.ToHtml(source, Pipeline), sourceUrl);
    }
}
