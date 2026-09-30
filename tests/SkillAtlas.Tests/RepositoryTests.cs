using SkillAtlas.Core;

namespace SkillAtlas.Tests;

public class RepositoryTests
{
    [Theory]
    [InlineData("https://github.com/JetBrains/kotlin")]
    [InlineData("https://github.com/JetBrains/kotlin.git/")]
    [InlineData("git@github.com:JetBrains/kotlin.git")]
    [InlineData("JetBrains/kotlin")]
    [InlineData("github.com/JetBrains/kotlin")]
    public void NormalizesRepository(string input)
    {
        var repository = GitHubRepository.Parse(input);
        Assert.Equal("JetBrains/kotlin", repository.DisplayName);
        Assert.Equal("https://github.com/JetBrains/kotlin.git", repository.CloneUrl);
    }

    [Theory]
    [InlineData("https://evil.example/owner/repo")]
    [InlineData("https://github.com/owner/repo/tree/main")]
    [InlineData("https://user:password@github.com/owner/repo")]
    [InlineData("https://github.com/owner/repo?token=abc")]
    [InlineData("file:///tmp/repo")]
    [InlineData("owner/..")]
    [InlineData("--upload-pack=command")]
    public void RejectsInvalidSources(string input) => Assert.Throws<ScanException>(() => GitHubRepository.Parse(input));

    [Fact]
    public void EncodesFileLinks() => Assert.Equal("https://github.com/o/r/blob/abc/skills/a%20%23b/SKILL.md",
        new GitHubRepository("o", "r").FileUrl("abc", "skills/a #b/SKILL.md"));

    [Fact]
    public void ParsesNullDelimitedTreeAndSkipsSymlinksAndSubmodules()
    {
        var files = GitHubScanner.SkillFiles("100644 blob abc\tskills/space and\ttab/SKILL.md\0" +
            "120000 blob def\tsymlink/SKILL.md\0" + "160000 commit ghi\tsubmodule\0" +
            "100644 blob jkl\tREADME.md\0" + "100755 blob mno\tnested/skill.md\0").ToArray();
        Assert.Equal(2, files.Length);
        Assert.Equal("skills/space and\ttab/SKILL.md", files[0].Path);
        Assert.Equal("mno", files[1].ObjectId);
    }
}
