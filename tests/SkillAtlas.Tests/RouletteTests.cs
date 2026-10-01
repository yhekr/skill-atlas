using SkillAtlas.Core;

namespace SkillAtlas.Tests;

public sealed class RouletteTests
{
    [Fact]
    public void EveryPositionMapsToItsPoolAndIndexAcrossEmptyPools()
    {
        int[] pools = [2, 0, 3];
        var picks = Enumerable.Range(0, 5)
            .Select(position => SkillRoulette.Spin(pools, new ScriptedRandom(position, 0))!)
            .Select(spin => (spin.Pool, spin.Index));
        Assert.Equal([(0, 0), (0, 1), (2, 0), (2, 1), (2, 2)], picks);
    }

    [Fact]
    public void TheExcludedSkillIsSkippedAndEveryOtherSkillRemainsReachable()
    {
        int[] pools = [2, 3];
        var random = new ScriptedRandom(0, 0, 1, 0, 2, 0, 3, 0);
        var picks = Enumerable.Range(0, 4).Select(_ => SkillRoulette.Spin(pools, random, (1, 0))!).Select(spin => (spin.Pool, spin.Index));
        Assert.Equal([(0, 0), (0, 1), (1, 1), (1, 2)], picks);
        Assert.Equal([4, SkillRoulette.Fortunes.Count, 4, SkillRoulette.Fortunes.Count], random.Bounds.Take(4));
    }

    [Fact]
    public void ASingleSkillIsReturnedEvenWhenItIsExcluded()
    {
        var spin = SkillRoulette.Spin([0, 1], new ScriptedRandom(0, 0), (1, 0));
        Assert.Equal((1, 0), (spin!.Pool, spin.Index));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    [InlineData(0, -1)]
    public void ExclusionsOutsideThePoolsAreIgnored(int pool, int index)
    {
        var random = new ScriptedRandom(3, 0);
        var spin = SkillRoulette.Spin([2, 2], random, (pool, index));
        Assert.Equal((1, 1), (spin!.Pool, spin.Index));
        Assert.Equal(4, random.Bounds[0]);
    }

    [Fact]
    public void FortunesComeFromTheFixedListAndAreNonEmpty()
    {
        var spin = SkillRoulette.Spin([1], new ScriptedRandom(0, SkillRoulette.Fortunes.Count - 1));
        Assert.Equal(SkillRoulette.Fortunes[^1], spin!.Fortune);
        Assert.All(SkillRoulette.Fortunes, fortune => Assert.False(string.IsNullOrWhiteSpace(fortune)));
        Assert.Equal(SkillRoulette.Fortunes.Count, SkillRoulette.Fortunes.Distinct().Count());
    }

    [Fact]
    public void RealRandomPicksStayInRangeAndCoverEverySkill()
    {
        var random = new Random(42);
        var seen = new HashSet<(int, int)>();
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var spin = SkillRoulette.Spin([3, 2], random, (0, 1))!;
            Assert.NotEqual((0, 1), (spin.Pool, spin.Index));
            Assert.Contains(spin.Fortune, SkillRoulette.Fortunes);
            seen.Add((spin.Pool, spin.Index));
        }
        Assert.Equal(4, seen.Count);
    }

    [Fact]
    public void EmptyAndInvalidPoolsAreHandled()
    {
        Assert.Null(SkillRoulette.Spin([], new ScriptedRandom()));
        Assert.Null(SkillRoulette.Spin([0, 0], new ScriptedRandom(), (0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkillRoulette.Spin([1, -1], new ScriptedRandom(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkillRoulette.Spin([int.MaxValue, 1], new ScriptedRandom(0, 0)));
        Assert.Throws<ArgumentNullException>(() => SkillRoulette.Spin(null!, new ScriptedRandom()));
        Assert.Throws<ArgumentNullException>(() => SkillRoulette.Spin([1], null!));
    }

    private sealed class ScriptedRandom(params int[] values) : Random
    {
        private int _next;
        public List<int> Bounds { get; } = [];

        public override int Next(int maxValue)
        {
            Bounds.Add(maxValue);
            var value = values[_next++];
            Assert.InRange(value, 0, maxValue - 1);
            return value;
        }
    }
}
