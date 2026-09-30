using System.Text.RegularExpressions;

namespace SkillAtlas.Core;

public sealed partial record GitHubRepository(string Owner, string Name)
{
    public string DisplayName => $"{Owner}/{Name}";
    public string CloneUrl => $"https://github.com/{Owner}/{Name}.git";

    public string FileUrl(string revision, string path) =>
        $"https://github.com/{Owner}/{Name}/blob/{Uri.EscapeDataString(revision)}/" +
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    public static GitHubRepository Parse(string input)
    {
        var value = input.Trim().TrimEnd('/');
        if (value.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase)) value = "https://" + value;
        if (value.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            value = value[15..];
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme != "https" || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new ScanException("Use a GitHub HTTPS URL, git@github.com:owner/repo.git, or owner/repo.");
            value = uri.AbsolutePath.Trim('/');
        }

        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        var parts = value.Split('/');
        if (parts.Length != 2 || parts.Any(p => !RepositoryPart().IsMatch(p) || p is "." or ".."))
            throw new ScanException("Expected a local directory or GitHub repository: https://github.com/owner/repo. Use --ref for a branch or tag.");
        return new GitHubRepository(parts[0], parts[1]);
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+$")]
    private static partial Regex RepositoryPart();
}
