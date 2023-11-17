using SubTubular.Extensions;

namespace SubTubular;

/// <summary>A non-generic base type for <see cref="Range{T}"/>.</summary>
public abstract class BaseRange
{
    internal IComparable Start { get; }
    internal IComparable End { get; }

    /// <summary>Whether <see cref="End"/> is included in the range.
    /// Mathematically speaking, true represents a closed and false an open interval.</summary>
    internal readonly bool IsEndIncluded;

    internal BaseRange(IComparable start, IComparable end, bool endIncluded = true)
    {
        var startBeforeEnd = start.IsLessThan(end);
        Start = startBeforeEnd ? start : end;
        End = startBeforeEnd ? end : start;
        IsEndIncluded = endIncluded;
    }

    private bool IsGreaterThanEnd(IComparable value) => value.IsGreaterThan(End, orEqualTo: !IsEndIncluded);

    /// <summary>Indicates wheter this range overlaps the <paramref name="other"/> range.
    /// See https://stackoverflow.com/a/7325268 .</summary>
    public bool Intersects(BaseRange other) => !(IsGreaterThanEnd(other.Start) || other.IsGreaterThanEnd(Start));

    public override int GetHashCode() => HashCode.Combine(Start, End);
}

/// <summary>A generic implementation of <see cref="BaseRange"/> similar to <see cref="Range"/>
/// representing a span or period of elements of type <typeparamref name="T"/>
/// with a <see cref="Start"/> and an <see cref="End"/>.
/// Inspired by https://stackoverflow.com/a/16103156 and https://stackoverflow.com/a/10174234 .</summary>
public abstract class Range<T> : BaseRange where T : IComparable
{
    public new T Start => (T)base.Start;
    public new T End => (T)base.End;

    public Range(T start, T end, bool endIncluded = true) : base(start, end, endIncluded) { }
}

/// <summary>Extension methods for <see cref="BaseRange"/> and <see cref="Range{T}"/>.</summary>
public static class RangeExtensions
{
    /// <summary>Indicates whether the range <paramref name="self"/> <see cref="BaseRange.Intersects(BaseRange)"/>
    /// <paramref name="orTouches"/> the <paramref name="other"/> range.</summary>
    public static bool Intersects(this Range<int> self, Range<int> other, bool orTouches = false)
        => self.Intersects(other) || orTouches && (self.Start == other.End + 1 || other.Start == self.End + 1);

    /// <summary>Groups the incoming <paramref name="ranges"/> by overlap/intersection <paramref name="orTouching"/>/butting
    /// returning ranges that don't overlap with or touch any other as the only range within their group
    /// and all overlapping ranges together in the same.
    /// Inspired by union-find algorithm, see https://stackoverflow.com/a/9919203 .</summary>
    public static IEnumerable<IEnumerable<T>> GroupOverlapping<T>(this IEnumerable<T> ranges, bool orTouching = false) where T : Range<int>
    {
        var groups = ranges.Select(i => ranges.Where(o => i == o || i.Intersects(o, orTouching)).ToList()).ToList();
        while (MergeGroupsWithOverlaps()) { } // call repeatedly until it returns false indicating it's done
        return groups;

        bool MergeGroupsWithOverlaps()
        {
            var groupsWithOverlaps = groups.Where(i => i.Count > 1).ToArray();

            foreach (var group in groupsWithOverlaps)
            {
                // other groups with overlaps containing any of the ranges in group
                var intersectingGroups = groupsWithOverlaps.Where(other => group != other && group.ContainsAny(other)).ToArray();

                if (intersectingGroups.Length <= 0) continue; // nothing to merge

                foreach (var intersectingGroup in intersectingGroups)
                {
                    group.AddRange(intersectingGroup.Except(group)); // figure out diff and merge it into the first group
                    groups.Remove(intersectingGroup); // now contains only duplicates
                }

                return true; /* indicating we merged and are not done;
                    returning because we're iterating over groupsWithOverlaps
                    which has been modified by groups.Remove() */
            }

            return false; // indicating no merges were made and we're done
        }
    }
}