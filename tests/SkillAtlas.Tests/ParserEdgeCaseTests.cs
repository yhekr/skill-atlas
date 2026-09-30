using SkillAtlas.Core;

namespace SkillAtlas.Tests;

public class ParserEdgeCaseTests
{
    [Theory]
    [InlineData("")]
    [InlineData("# Title only")]
    [InlineData("---\n---")]
    [InlineData("---\n# Just a YAML comment\n---")]
    public void EmptyMetadataAndMissingDescriptionsAreValid(string content)
    {
        var warnings = new List<string>();
        var skill = SkillParser.Parse(content, "example/SKILL.md", "", warnings);
        Assert.Equal("example", skill.Name);
        Assert.Equal("No description provided.", skill.Description);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("~")]
    [InlineData("NULL")]
    [InlineData("")]
    public void NullYamlScalarsUseFallback(string value)
    {
        var warnings = new List<string>();
        var skill = SkillParser.Parse($"---\nname: {value}\ndescription: {value}\n---\n\nFallback description.", "folder/SKILL.md", "", warnings);
        Assert.Equal("folder", skill.Name);
        Assert.Equal("Fallback description.", skill.Description);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("name: [one, two]")]
    [InlineData("description: {unexpected: mapping}")]
    [InlineData("name: a\nname: duplicate")]
    [InlineData("name: *undefined")]
    [InlineData("- root sequence")]
    public void InvalidMetadataReportsWarningAndKeepsReadableResult(string yaml)
    {
        var warnings = new List<string>();
        var skill = SkillParser.Parse($"---\n{yaml}\n---\n# Heading\n\nFallback body.", "folder/SKILL.md", "", warnings);
        Assert.Equal("Fallback body.", skill.Description);
        Assert.Single(warnings);
    }

    [Fact]
    public void QuotedNullsAreLiteralAndAliasesAreResolved()
    {
        var warnings = new List<string>();
        var skill = SkillParser.Parse("---\nname: 'null'\ndescription: &text 'Русский текст: [bold] 😀'\nother: *text\n...\n", "folder/SKILL.md", "", warnings);
        Assert.Equal("null", skill.Name);
        Assert.Equal("Русский текст: [bold] 😀", skill.Description);
        Assert.Empty(warnings);
    }

    [Fact]
    public void MarkdownSeparatorsDoNotBecomeFrontMatter()
    {
        var skill = SkillParser.Parse("# Heading\n\nFirst paragraph.\n\n---\n\nLater text.", "SKILL.md", "", new List<string>());
        Assert.Equal("Heading", skill.Name);
        Assert.Equal("First paragraph.", skill.Description);
    }
}
