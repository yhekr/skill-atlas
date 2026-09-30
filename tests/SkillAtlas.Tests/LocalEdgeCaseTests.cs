using SkillAtlas.Core;

namespace SkillAtlas.Tests;

public class LocalEdgeCaseTests
{
    [Fact]
    public async Task EmptyDirectoryReturnsNoSkills()
    {
        using var workspace = new TestWorkspace();
        var result = await new LocalScanner().ScanAsync(workspace.Root);
        Assert.Empty(result.Skills);
        Assert.Empty(result.Warnings);
        Assert.Null(result.Revision);
    }

    [Fact]
    public async Task MissingDirectoryFailsClearly()
    {
        using var workspace = new TestWorkspace();
        var error = await Assert.ThrowsAsync<ScanException>(() => new LocalScanner().ScanAsync(Path.Combine(workspace.Root, "missing")));
        Assert.Contains("does not exist", error.Message);
    }

    [Fact]
    public async Task FindsRootUnicodeSpacesBracketsAndDuplicateNamesInStableOrder()
    {
        using var workspace = new TestWorkspace();
        workspace.Write("SKILL.md", "# Root skill\n\nRoot description.");
        workspace.Write("z/skill.md", "---\nname: same\ndescription: last\n---");
        workspace.Write(".agents/skills/навык [a] #1/SKILL.md", "---\nname: same\ndescription: Поиск 😀\n---");
        workspace.Write("not-a-skill.md", "Ignored");
        workspace.Write("SKILL.md.backup", "Ignored");
        var result = await new LocalScanner().ScanAsync(workspace.Root);
        Assert.Equal(3, result.Skills.Count);
        Assert.Equal(2, result.Skills.Count(s => s.Name == "same"));
        Assert.Equal("Поиск 😀", result.Skills[0].Description);
        Assert.Equal("Root skill", result.Skills[1].Name);
        Assert.Contains("%23", result.Skills[0].Url);
        Assert.Equal(result.Skills.Select(s => s.Path).Order(StringComparer.Ordinal), result.Skills.Select(s => s.Path));
    }

    [Fact]
    public async Task ByteLimitIsInclusiveAndUsesUtf8Bytes()
    {
        using var workspace = new TestWorkspace();
        workspace.Write("boundary/SKILL.md", new string('x', SkillParser.MaxFileBytes));
        workspace.Write("too-big/SKILL.md", new string('я', SkillParser.MaxFileBytes / 2 + 1));
        var result = await new LocalScanner().ScanAsync(workspace.Root);
        Assert.Equal("boundary", Assert.Single(result.Skills).Name);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task DirectoryLinksDoNotEscapeRootOrCauseCycles()
    {
        using var workspace = new TestWorkspace();
        using var outside = new TestWorkspace();
        outside.Write("secret/SKILL.md", "Do not scan outside the requested root.");
        var link = Path.Combine(workspace.Root, "linked");
        if (OperatingSystem.IsWindows())
        {
            var result = await workspace.RunAsync("cmd.exe", "/c", "mklink", "/J", link, outside.Root);
            Assert.True(result.ExitCode == 0, result.Error);
        }
        else Directory.CreateSymbolicLink(link, outside.Root);
        try
        {
            var scan = await new LocalScanner().ScanAsync(workspace.Root);
            Assert.Empty(scan.Skills);
            await Assert.ThrowsAsync<ScanException>(() => new LocalScanner().ScanAsync(link));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task UnreadableFileWarnsWithoutLosingOtherSkills()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows sharing locks deny reads; Unix advisory locks do not.
        using var workspace = new TestWorkspace();
        var path = workspace.Write("locked/SKILL.md", "Locked");
        workspace.Write("readable/SKILL.md", "Readable");
        await using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await new LocalScanner().ScanAsync(workspace.Root);
        Assert.Equal("readable", Assert.Single(result.Skills).Name);
        Assert.Single(result.Warnings);
    }
}
