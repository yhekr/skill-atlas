using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;

namespace SkillAtlas.Core;

public sealed record SimilarSkill(int Index, Skill Skill, double Score, IReadOnlyList<string> SharedTerms);

/// <summary>Deterministic weighted Jaccard overlap of names and descriptions. No model or network calls.</summary>
public static partial class SkillSimilarity
{
    public const double MinimumScore = 0.15;
    private const int MaximumMetadataLength = 8192;
    private static readonly FrozenSet<string> StopWords = (
        "a an and are as at be been being by can could do does for from had has have how " +
        "if in into is it its of on or should that the their them then these they this those " +
        "to use used using user users when whenever where which who will with would you your " +
        "skill skills instruction instructions request requests asked asks " +
        "и в во не на для из по с со к от это этот эта при как или что чтобы").Split(' ').ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlyList<SimilarSkill> FindSimilar(IReadOnlyList<Skill> skills, int index, int limit = 5)
    {
        ArgumentNullException.ThrowIfNull(skills);
        if (index < 0 || index >= skills.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (limit is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(limit));

        var selected = Terms(skills[index]);
        if (selected.Count == 0) return [];
        var matches = new List<SimilarSkill>();
        for (var candidateIndex = 0; candidateIndex < skills.Count; candidateIndex++)
        {
            if (candidateIndex == index) continue;
            var candidate = Terms(skills[candidateIndex]);
            var shared = selected.Keys.Where(candidate.ContainsKey).Order(StringComparer.Ordinal).ToArray();
            if (shared.Length == 0) continue;
            var intersection = shared.Sum(term => Math.Min(selected[term], candidate[term]));
            var union = selected.Values.Sum() + candidate.Values.Sum() - intersection;
            var score = (double)intersection / union;
            if (score >= MinimumScore)
                matches.Add(new(candidateIndex, skills[candidateIndex], score, shared));
        }
        return matches.OrderByDescending(match => match.Score)
            .ThenBy(match => match.Skill.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(match => match.Skill.Path, StringComparer.Ordinal)
            .ThenBy(match => match.Index)
            .Take(limit).ToArray();
    }

    public static int ResolveIndex(IReadOnlyList<Skill> skills, string selector)
    {
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        // Exact paths take precedence over names, and keep Git's case-sensitive path semantics.
        var path = selector.Replace('\\', '/');
        var pathMatches = skills.Select((skill, index) => (skill, index)).Where(item => item.skill.Path == path).ToArray();
        if (pathMatches.Length == 1) return pathMatches[0].index;
        var names = skills.Select((skill, index) => (skill, index))
            .Where(item => item.skill.Name.Equals(selector, StringComparison.OrdinalIgnoreCase)).ToArray();
        return names.Length switch
        {
            1 => names[0].index,
            0 => throw new ArgumentException($"No skill matches '{selector}'. Use an exact skill name or path."),
            _ => throw new ArgumentException($"More than one skill is named '{selector}'. Use its full SKILL.md path.")
        };
    }

    private static Dictionary<string, int> Terms(Skill skill)
    {
        var terms = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var term in Tokenize(skill.Description)) terms[term] = 1;
        foreach (var term in Tokenize(skill.Name)) terms[term] = 3;
        return terms;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        // Bound work on repository-supplied metadata without cutting a surrogate pair in half.
        var length = Math.Min(text.Length, MaximumMetadataLength);
        if (length < text.Length && char.IsHighSurrogate(text[length - 1])) length--;
        var normalized = text[..length].Normalize(NormalizationForm.FormKC);
        normalized = AcronymBoundary().Replace(normalized, "$1 $2");
        normalized = CamelBoundary().Replace(normalized, "$1 $2").ToLowerInvariant();
        return Words().Matches(normalized).Select(match => match.Value)
            .Where(term => term.Length > 1 && term.Any(char.IsLetter) && !StopWords.Contains(term))
            .Distinct(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"(\p{Lu})(\p{Lu}\p{Ll})", RegexOptions.CultureInvariant)]
    private static partial Regex AcronymBoundary();
    [GeneratedRegex(@"([\p{Ll}\p{Nd}])(\p{Lu})", RegexOptions.CultureInvariant)]
    private static partial Regex CamelBoundary();
    [GeneratedRegex(@"[\p{L}\p{M}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex Words();
}
