namespace SkillAtlas.Core;

internal static class DiscoveryPolicy
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "bin", "obj", ".venv", "venv",
        "product", "products", "resources", "test", "tests", "testdata", "test-data",
        "test_data", "testresources", "test-resources", "test_resources", "fixtures", "__fixtures__"
    };

    public static bool ExcludesDirectory(string name) => Excluded.Contains(name);

    public static bool IncludesFile(string path) => !path.Split('/').SkipLast(1).Any(ExcludesDirectory);

    public static IReadOnlyList<Skill> OrderAndDeduplicate(IEnumerable<Skill> skills)
    {
        var seen = new HashSet<(string Scope, string RelativePath, string Hash)>();
        var result = new List<Skill>();
        foreach (var skill in skills.OrderBy(s => s.Path, StringComparer.Ordinal))
        {
            var parts = skill.Path.Split('/');
            var agentRoot = Array.FindIndex(parts, p => p is ".agents" or ".claude");
            if (agentRoot >= 0 && parts.Length > agentRoot + 2 && parts[agentRoot + 1] == "skills" && skill.ContentHash is not null)
            {
                var identity = (string.Join('/', parts[..agentRoot]), string.Join('/', parts[(agentRoot + 2)..]), skill.ContentHash);
                if (!seen.Add(identity)) continue;
            }
            result.Add(skill);
        }
        return result;
    }
}
