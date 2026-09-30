using SkillAtlas.Core;

namespace SkillAtlas.Tests;

public class DiscoveryBehaviorTests
{
    [Fact]
    public async Task MirroredAgentsAndClaudeCopiesProduceOneEntry()
    {
        using var workspace = new TestWorkspace();
        const string content = "---\nname: shared\ndescription: Shared skill\n---\nActual instructions.\n";
        workspace.Write(".claude/skills/shared/SKILL.md", content.Replace("\n", "\r\n"));
        workspace.Write(".agents/skills/shared/SKILL.md", content);
        var result = await new LocalScanner().ScanAsync(workspace.Root);
        var skill = Assert.Single(result.Skills);
        Assert.Equal(".agents/skills/shared/SKILL.md", skill.Path);
    }

    [Fact]
    public async Task SameMetadataWithDifferentInstructionsRemainsTwoEntries()
    {
        using var workspace = new TestWorkspace();
        const string metadata = "---\nname: shared\ndescription: Shared skill\n---\n";
        workspace.Write(".claude/skills/shared/SKILL.md", metadata + "Claude instructions.");
        workspace.Write(".agents/skills/shared/SKILL.md", metadata + "Other instructions.");
        Assert.Equal(2, (await new LocalScanner().ScanAsync(workspace.Root)).Skills.Count);
    }

    [Fact]
    public async Task CopiesInDifferentProjectsRemainSeparate()
    {
        using var workspace = new TestWorkspace();
        workspace.Write("first/.agents/skills/shared/SKILL.md", "Same content");
        workspace.Write("second/.claude/skills/shared/SKILL.md", "Same content");
        Assert.Equal(2, (await new LocalScanner().ScanAsync(workspace.Root)).Skills.Count);
    }

    [Fact]
    public async Task AgentSkillsDirectoryIsDiscovered()
    {
        using var workspace = new TestWorkspace();
        workspace.Write("agent/skills/build/SKILL.md", "# Build\n\nAgent skill.");
        Assert.Equal("agent/skills/build/SKILL.md", Assert.Single((await new LocalScanner().ScanAsync(workspace.Root)).Skills).Path);
    }

    [Theory]
    [InlineData("product/skills/game/SKILL.md")]
    [InlineData("products/game/skills/SKILL.md")]
    [InlineData("tests/resources/sample/SKILL.md")]
    [InlineData("src/test/resources/skills/SKILL.md")]
    [InlineData("testData/.claude/skills/sample/SKILL.md")]
    [InlineData("test-resources/skills/SKILL.md")]
    [InlineData("__fixtures__/skills/SKILL.md")]
    [InlineData("plugins/mcp-tools/resources/jetbrains/mps/agents/mcp/skills/product/SKILL.md")]
    public async Task ProductSkillsAndTestResourcesAreExcluded(string path)
    {
        using var workspace = new TestWorkspace();
        workspace.Write(path, "This is product/test data, not a repository agent skill.");
        Assert.Empty((await new LocalScanner().ScanAsync(workspace.Root)).Skills);
    }
}
