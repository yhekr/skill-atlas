using System.Globalization;
using SkillAtlas.Core;

namespace SkillAtlas.Tests;

public sealed class SimilarityTests
{
    private static Skill Item(string name, string description = "", string? path = null) =>
        new(name, description, path ?? $"{name}/SKILL.md", "https://github.com/o/r/blob/abc/SKILL.md");

    [Fact]
    public void RelatedSkillsRankAboveUnrelatedSkillsAndNeverIncludeTheSelectedSkill()
    {
        Skill[] skills = [
            Item("gradle-upgrade", "Update Gradle dependencies"),
            Item("gradle-upgrade-tests", "Update Gradle dependencies in tests"),
            Item("gradle-report", "Report Gradle configuration"),
            Item("image-colors", "Draw colorful pictures")
        ];
        var matches = SkillSimilarity.FindSimilar(skills, 0);
        Assert.Equal([1, 2], matches.Select(match => match.Index));
        Assert.True(matches[0].Score > matches[1].Score);
        Assert.Contains("gradle", matches[0].SharedTerms);
        Assert.All(matches, match => Assert.InRange(match.Score, SkillSimilarity.MinimumScore, 1));
    }

    [Fact]
    public void NameMatchesOutweighDescriptionOnlyMatches()
    {
        Skill[] skills = [Item("gradle", "wrapper"), Item("gradle", "other"), Item("other", "gradle wrapper")];
        Assert.Equal([1, 2], SkillSimilarity.FindSimilar(skills, 0).Select(match => match.Index));
    }

    [Fact]
    public void RepeatedWordsDoNotInflateTheScoreAndPathsDoNotAffectIt()
    {
        Skill[] skills = [Item("gradle", "wrapper", "shared/one/SKILL.md"),
            Item("gradle", "wrapper wrapper wrapper", "elsewhere/two/SKILL.md"),
            Item("pictures", "colors", "shared/three/SKILL.md")];
        var match = Assert.Single(SkillSimilarity.FindSimilar(skills, 0));
        Assert.Equal(1, match.Score);
        Assert.Equal(["gradle", "wrapper"], match.SharedTerms);
    }

    [Theory]
    [InlineData("gradleUpgrade", "GRADLE_UPGRADE")]
    [InlineData("HTTPParser", "http-parser")]
    [InlineData("сборка-проекта", "СБОРКА ПРОЕКТА")]
    [InlineData("café", "cafe\u0301")]
    [InlineData("Ｇｒａｄｌｅ", "gradle")]
    public void WordBoundariesCaseAndUnicodeAreNormalized(string first, string second)
    {
        Assert.Equal(1, Assert.Single(SkillSimilarity.FindSimilar([Item(first), Item(second)], 0)).Score);
    }

    [Fact]
    public void GenericInstructionsNumbersAndEmptyMetadataDoNotProduceMatches()
    {
        Skill[] skills = [Item("skill", "Use this when the user asks 123 😀"),
            Item("skills", "These instructions are for the user 456"), Item("", "")];
        Assert.Empty(SkillSimilarity.FindSimilar(skills, 0));
        Assert.Empty(SkillSimilarity.FindSimilar(skills, 2));
        Assert.Empty(SkillSimilarity.FindSimilar([Item("single")], 0));
    }

    [Fact]
    public void TiesAreDeterministicAndLimitIsAppliedAfterRanking()
    {
        Skill[] skills = [Item("gradle"), Item("gradle-b", path: "z/SKILL.md"),
            Item("gradle-a", path: "z/SKILL.md"), Item("gradle-a", path: "a/SKILL.md")];
        Assert.Equal([3, 2], SkillSimilarity.FindSimilar(skills, 0, 2).Select(match => match.Index));
    }

    [Fact]
    public void ScoresAndOrderingDoNotDependOnCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal(1, Assert.Single(SkillSimilarity.FindSimilar([Item("BUILD-INDEX"), Item("build-index")], 0)).Score);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void LargeDescriptionsAreBoundedWithoutBreakingUnicode()
    {
        var description = new string(' ', 8191) + "😀" + new string('x', 100_000);
        var match = Assert.Single(SkillSimilarity.FindSimilar([Item("gradle", description), Item("gradle")], 0));
        Assert.Equal(1, match.Score);
    }

    [Fact]
    public void InvalidIndicesAndLimitsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SkillSimilarity.FindSimilar([], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkillSimilarity.FindSimilar([Item("x")], -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkillSimilarity.FindSimilar([Item("x")], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkillSimilarity.FindSimilar([Item("x")], 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkillSimilarity.FindSimilar([Item("x")], 0, 21));
    }

    [Fact]
    public void ExactPathsDisambiguateDuplicateNames()
    {
        Skill[] skills = [Item("build", path: "a/SKILL.md"), Item("build", path: "b/SKILL.md")];
        Assert.Throws<ArgumentException>(() => SkillSimilarity.ResolveIndex(skills, "BUILD"));
        Assert.Equal(1, SkillSimilarity.ResolveIndex(skills, @"b\SKILL.md"));
        Assert.Throws<ArgumentException>(() => SkillSimilarity.ResolveIndex(skills, "missing"));
        Assert.Equal(0, SkillSimilarity.ResolveIndex([Item("Build")], "build"));
    }

    [Fact]
    public void OnlyResultsAtOrAboveMinimumOverlapAreReturned()
    {
        var words = Enumerable.Range(0, 18).Select(number => $"word{number}").ToArray();
        var skills = new[] { Item("gradle"), Item("gradle", string.Join(" ", words.Take(17))),
            Item("gradle", string.Join(" ", words)) };
        var match = Assert.Single(SkillSimilarity.FindSimilar(skills, 0));
        Assert.Equal(1, match.Index);
        Assert.Equal(SkillSimilarity.MinimumScore, match.Score);
    }
}
