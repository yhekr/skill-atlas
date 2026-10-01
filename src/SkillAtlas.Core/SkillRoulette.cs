namespace SkillAtlas.Core;

public sealed record SkillSpin(int Pool, int Index, string Fortune);

/// <summary>Picks a random skill across one or more pools, such as the scans in a collection.</summary>
public static class SkillRoulette
{
    public static IReadOnlyList<string> Fortunes { get; } = [
        "The dice have spoken.",
        "Fortune favours the curious.",
        "A wild skill appears!",
        "Your next superpower awaits.",
        "Today's lucky SKILL.md.",
        "Read it before your agent does.",
        "Hidden gem unlocked.",
        "Not all who wander are lost."
    ];

    /// <summary>
    /// Chooses every skill with equal probability. The excluded skill, usually the one already open,
    /// is skipped whenever another skill exists. Returns <c>null</c> when all pools are empty.
    /// </summary>
    public static SkillSpin? Spin(IReadOnlyList<int> poolSizes, Random random, (int Pool, int Index)? exclude = null)
    {
        ArgumentNullException.ThrowIfNull(poolSizes);
        ArgumentNullException.ThrowIfNull(random);
        if (poolSizes.Any(size => size < 0)) throw new ArgumentOutOfRangeException(nameof(poolSizes));

        var total = poolSizes.Sum(size => (long)size);
        if (total == 0) return null;
        if (total > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(poolSizes));

        var skipped = exclude is (int excludedPool, int excludedIndex) &&
            excludedPool >= 0 && excludedPool < poolSizes.Count &&
            excludedIndex >= 0 && excludedIndex < poolSizes[excludedPool] && total > 1
            ? poolSizes.Take(excludedPool).Sum() + excludedIndex
            : -1;
        var position = random.Next((int)total - (skipped >= 0 ? 1 : 0));
        if (skipped >= 0 && position >= skipped) position++;

        var pool = 0;
        while (position >= poolSizes[pool])
            position -= poolSizes[pool++];
        return new(pool, position, Fortunes[random.Next(Fortunes.Count)]);
    }
}
