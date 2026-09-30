using System.Globalization;
using SkillAtlas.Core;

namespace SkillAtlas.Tests;

public class FilterTests
{
    private static readonly ScanResult Catalog = new("owner/repo", "abc",
    [
        new("gradle-build", "Update the wrapper and run tests", ".agents/build/SKILL.md", "https://example.com/a"),
        new("gradle-docs", "Write documentation", "docs/SKILL.md", "https://example.com/b"),
        new("Сборка", "Проверяет проект", "unicode/SKILL.md", "https://example.com/c"),
        new("C++ [draft]", "Literal .* examples for .NET", "literal/SKILL.md", "https://example.com/d")
    ], ["A warning"]);

    [Theory]
    [InlineData("gradle wrapper", "gradle-build")]
    [InlineData("WRAPPER GRADLE", "gradle-build")]
    [InlineData("  gradle \t wrapper\n", "gradle-build")]
    [InlineData("gradle\u00a0wrapper", "gradle-build")]
    [InlineData("gradle gradle wrapper", "gradle-build")]
    [InlineData("сБорКа ПРОЕКТ", "Сборка")]
    [InlineData("grad WRAP", "gradle-build")]
    [InlineData("[draft] C++", "C++ [draft]")]
    [InlineData(".* .net", "C++ [draft]")]
    [InlineData(".agents/ wrapper", "gradle-build")]
    public void EveryTermCanMatchADifferentFieldInAnyOrder(string query, string expected)
    {
        Assert.Equal(expected, Assert.Single(Catalog.Filter(query).Skills).Name);
    }

    [Theory]
    [InlineData("gradle missing")]
    [InlineData("gradle проект")]
    [InlineData("documentationwrapper")]
    [InlineData("example.com")]
    [InlineData("^gradle")]
    [InlineData("<script>alert(1)</script>")]
    public void TermsAreLiteralAndMustAllMatchTheSameSkill(string query)
    {
        Assert.Empty(Catalog.Filter(query).Skills);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n\u00a0")]
    public void EmptyQueryReturnsOriginalCatalog(string? query)
    {
        Assert.Same(Catalog, Catalog.Filter(query));
    }

    [Fact]
    public void FilteringPreservesOrderMetadataWarningsAndOriginalCatalog()
    {
        var filtered = Catalog.Filter("gradle");
        Assert.Equal(["gradle-build", "gradle-docs"], filtered.Skills.Select(s => s.Name));
        Assert.Same(Catalog.Skills[0], filtered.Skills[0]);
        Assert.Equal(Catalog.Source, filtered.Source);
        Assert.Equal(Catalog.Revision, filtered.Revision);
        Assert.Same(Catalog.Warnings, filtered.Warnings);
        Assert.Equal(4, Catalog.Skills.Count);
        Assert.Empty((Catalog with { Skills = [] }).Filter("gradle").Skills);
    }

    [Fact]
    public void MatchingDoesNotDependOnTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("gradle-build", Assert.Single(Catalog.Filter("BUILD").Skills).Name);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
