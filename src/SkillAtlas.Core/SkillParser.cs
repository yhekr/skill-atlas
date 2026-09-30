using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace SkillAtlas.Core;

public static partial class SkillParser
{
    public const int MaxFileBytes = 1024 * 1024;

    public static Skill Parse(string content, string path, string url, ICollection<string> warnings)
    {
        var normalizedPath = path;
        var parts = normalizedPath.Split('/');
        var name = parts.Length > 1 ? parts[^2] : "unnamed-skill";
        string? description = null;
        var lines = content.TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n');
        var bodyStart = 0;

        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            var end = Array.FindIndex(lines, 1, line => line.Trim() is "---" or "...");
            if (end < 0)
            {
                warnings.Add($"{path}: front matter has no closing delimiter; using Markdown fallback.");
                bodyStart = lines.Length;
            }
            else
            {
                bodyStart = end + 1;
                try
                {
                    var yaml = new YamlStream();
                    yaml.Load(new StringReader(string.Join('\n', lines[1..end])));
                    if (yaml.Documents.Count > 0)
                    {
                        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode map)
                            throw new YamlException("Expected a YAML mapping.");
                        name = Scalar(map, "name") ?? name;
                        description = Scalar(map, "description");
                    }
                }
                catch (YamlException)
                {
                    warnings.Add($"{path}: invalid YAML front matter; using available metadata or Markdown fallback.");
                }
            }
        }

        var body = lines[bodyStart..];
        if (name == "unnamed-skill")
            name = body.FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim() ?? name;

        description ??= FirstParagraph(body);
        return new Skill(Clean(name), Clean(description ?? "No description provided."), normalizedPath, url)
        {
            ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))))
        };
    }

    private static string? Scalar(YamlMappingNode map, string key)
    {
        if (!map.Children.TryGetValue(new YamlScalarNode(key), out var node))
            return null;
        if (node is not YamlScalarNode scalar)
            throw new YamlException($"'{key}' must be a scalar.");
        if (scalar.Tag == "tag:yaml.org,2002:null" ||
            (scalar.Style == ScalarStyle.Plain && scalar.Tag != "tag:yaml.org,2002:str" &&
             (scalar.Value == "~" || string.Equals(scalar.Value, "null", StringComparison.OrdinalIgnoreCase))))
            return null;
        return string.IsNullOrWhiteSpace(scalar.Value) ? null : scalar.Value.Trim();
    }

    private static string? FirstParagraph(IEnumerable<string> lines)
    {
        var paragraph = new List<string>();
        var inCode = false;
        foreach (var line in lines)
        {
            var value = line.Trim();
            if (value.StartsWith("```", StringComparison.Ordinal) || value.StartsWith("~~~", StringComparison.Ordinal))
            {
                if (paragraph.Count > 0) break;
                inCode = !inCode;
                continue;
            }
            if (inCode) continue;
            if (value.Length == 0 || value.StartsWith('#') || value is "---" or "***")
            {
                if (paragraph.Count > 0) break;
                continue;
            }
            paragraph.Add(value);
        }
        return paragraph.Count == 0 ? null : string.Join(' ', paragraph);
    }

    private static string Clean(string value) => Whitespace().Replace(value, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
