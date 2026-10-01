using System.Text.RegularExpressions;

namespace SkillAtlas.Core;

/// <summary>A GitHub organization or user account whose repositories are scanned together.</summary>
public sealed partial record GitHubOwner(string Login)
{
    public override string ToString() => Login;

    public static GitHubOwner Parse(string input)
    {
        var value = input.Trim().TrimEnd('/');
        if (value.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase)) value = "https://" + value;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme != "https" || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new ScanException("Use a GitHub organization name, github.com/owner, or https://github.com/owner.");
            value = uri.AbsolutePath.Trim('/');
        }

        if (!LoginPattern().IsMatch(value))
            throw new ScanException("Expected a GitHub organization or user name, for example JetBrains or https://github.com/JetBrains.");
        return new GitHubOwner(value);
    }

    // GitHub logins: up to 39 letters, digits or single inner hyphens.
    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38}$")]
    private static partial Regex LoginPattern();
}
