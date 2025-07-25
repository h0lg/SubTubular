using System.Text.RegularExpressions;
using Lifti;
using YoutubeExplode.Exceptions;

/*  Namespace does not match folder structure.
 *  It was deliberately chosen to avoid including maybe conflicting extensions accidentally
 *  with the reference to other public types in the top-level namespace. */
#pragma warning disable IDE0130
namespace SubTubular.Extensions;
#pragma warning restore IDE0130

public static class TimeSpanExtensions
{
    private const string minSec = "mm':'ss";

    //inspired by https://stackoverflow.com/a/4709641
    public static string FormatWithOptionalHours(this TimeSpan timeSpan)
        => timeSpan.ToString(timeSpan.TotalHours >= 1 ? "h':'" + minSec : minSec);
}

/// <summary>Extension methods for <see cref="string"/>s.</summary>
public static partial class StringExtensions
{
    /// <summary>Determines whether <paramref name="input"/> <see cref="string.IsNullOrEmpty(string?)"/>.</summary>
    internal static bool IsNullOrEmpty(this string? input) => string.IsNullOrEmpty(input);

    /// <summary>Determines whether <paramref name="input"/> NOT <see cref="string.IsNullOrEmpty(string?)"/>.</summary>
    public static bool IsNonEmpty(this string? input) => !string.IsNullOrEmpty(input);

    /// <summary>Determines whether <paramref name="input"/> <see cref="string.IsNullOrWhiteSpace(string?)"/>.</summary>
    public static bool IsNullOrWhiteSpace(this string? input) => string.IsNullOrWhiteSpace(input);

    /// <summary>Determines whether <paramref name="input"/> NOT <see cref="string.IsNullOrWhiteSpace(string?)"/>.</summary>
    public static bool IsNonWhiteSpace(this string? input) => !string.IsNullOrWhiteSpace(input);

    /// <summary>Replaces all <see cref="ConsecutiveWhitespace"/> characters in
    /// <paramref name="input"/> with <paramref name="normalizeTo"/>.</summary>
    internal static string NormalizeWhiteSpace(this string input, string normalizeTo = " ")
        => ConsecutiveWhitespace().Replace(input, normalizeTo);

    [GeneratedRegex(@"\s+")] private static partial Regex ConsecutiveWhitespace();

    /// <summary>Concatenates the <paramref name="pieces"/> into a single string
    /// putting <paramref name="glue"/> in between them.</summary>
    public static string Join(this IEnumerable<string> pieces, string glue) => string.Join(glue, pieces);

    /// <summary>Indicates whether <paramref name="path"/> points to a directory rather than a file.
    /// Inspired by https://stackoverflow.com/a/19596821 .</summary>
    public static bool IsDirectoryPath(this string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        path = path.Trim();

        // neither file nor directory exists. guess intention

        // if has trailing slash then it's a directory
        char lastChar = path[^1];
        if (lastChar == Path.DirectorySeparatorChar || lastChar == Path.AltDirectorySeparatorChar)
            return true;

        // if has extension then its a file; directory otherwise
        return !Path.HasExtension(path);
    }

    /// <summary>Replaces all characters unsafe for file or directory names in <paramref name="value"/>
    /// with <paramref name="replacement"/>.</summary>
    public static string ToFileSafe(this string value, string replacement = "_")
        => Regex.Replace(value, "[" + Regex.Escape(new string(Path.GetInvalidFileNameChars())) + "]", replacement);

    /// <summary>Removes the <paramref name="prefix"/> from the start and the <paramref name="suffix"/>
    /// from the end of the <paramref name="value"/> and returns the rest.</summary>
    public static string StripAffixes(this string value, string prefix, string suffix)
    {
        int afterPrefix = prefix.Length;
        int beforeSuffix = value.Length - suffix.Length;
        return value[afterPrefix..beforeSuffix];
    }

    internal static IEnumerable<string> Wrap(this string input, int columnWidth)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Split(' ').Wrap(columnWidth);
    }

    internal static IEnumerable<string> Wrap(this IEnumerable<string> phrases, int columnWidth)
    {
        ArgumentNullException.ThrowIfNull(phrases);
        if (phrases.Count() <= 1) return phrases;
        if (columnWidth <= 0) throw new ArgumentException("Column width must be greater than 0.", nameof(columnWidth));

        // from https://stackoverflow.com/a/29689349
        return phrases.Skip(1).Aggregate(seed: phrases.Take(1).ToList(), (lines, phrase) =>
        {
            // if length of last line gets up to or over columnWidth, add the phrase on a new line
            if (lines[^1].Length + phrase.Length >= columnWidth) lines.Add(phrase);
            else lines[^1] += " " + phrase; // otherwise add phrase to last line

            return lines;
        });
    }

    internal static IEnumerable<string> Indent(this IEnumerable<string> lines, int indentLevel)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (indentLevel < 0) throw new ArgumentException("Only positive non-zero indents are supported.", nameof(indentLevel));
        if (indentLevel == 0) return lines;
        return lines.Select(l => new string(' ', indentLevel) + l);
    }
}

/// <summary>Extension methods for <see cref="IEnumerable{T}"/> types.</summary>
public static class EnumerableExtensions
{
    /// <summary>Indicates whether <paramref name="collection"/>
    /// contains any of the supplied <paramref name="values"/>.</summary>
    internal static bool ContainsAny<T>(this IEnumerable<T> collection, IEnumerable<T> values)
        => values.Intersect(collection).Any();

    /// <summary>Indicates whether <paramref name="collection"/> is not null and contains any items.</summary>
    public static bool HasAny<T>(this IEnumerable<T>? collection) => collection?.Any() == true;

    public static IEnumerable<T> WithValue<T>(this IEnumerable<T?> nullables)
        => nullables.Where(v => v != null).Select(v => v!);

    public static IEnumerable<T> WithValue<T>(this IEnumerable<T?> nullables) where T : struct
        => nullables.Where(v => v.HasValue).Select(v => v!.Value);
}

internal static class HashCodeExtensions
{
    // Convert a collection to a HashSet of hash codes
    internal static HashSet<int> AsHashCodeSet<T>(this IEnumerable<T>? collection)
        => collection == null ? [] : [.. collection.Select(item => item?.GetHashCode() ?? 0)];

    // Adds the elements of a collection to the HashCode in a combined and ordered way
    internal static HashCode AddOrdered<T>(this HashCode hashCode, IEnumerable<T> collection)
    {
        // Sort the hash codes before combining them to ensure order-independence
        foreach (var item in collection.Order()) hashCode.Add(item);
        return hashCode;
    }
}

/// <summary>Helpers for <see cref="ValueTask"/>s. Inspired by https://stackoverflow.com/a/63141544 .</summary>
internal static class ValueTasks
{
    internal static async ValueTask<(T[], Exception[])> WhenAll<T>(IReadOnlyList<ValueTask<T>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var exceptions = new Exception[tasks.Count];
        var results = new T[tasks.Count];

        for (var i = 0; i < tasks.Count; i++)
            try { results[i] = await tasks[i].ConfigureAwait(false); }
            catch (Exception ex) { exceptions[i] = ex; }

        return (results, exceptions);
    }

    internal static ValueTask<(T[], Exception[])> WhenAll<T>(IEnumerable<ValueTask<T>> tasks) => WhenAll(tasks!.ToArray());
}

internal static class TaskExtensions
{
    /// <summary>Use with the <paramref name="task"/> returned by <see cref="Task.WhenAll(IEnumerable{Task})"/>
    /// to throw all exceptions as an <see cref="AggregateException"/> instead of only the first one (as is the default).
    /// From https://github.com/dotnet/runtime/issues/47605#issuecomment-778930734</summary>
    internal static async Task WithAggregateException(this Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) when (task.IsCanceled) { throw; }
        catch { task.Wait(); }
    }
}

public static class ExceptionExtensions
{
    public static IEnumerable<Exception> GetRootCauses(this IEnumerable<Exception> exns)
        => exns.SelectMany(ex => ex.GetRootCauses());

    public static IEnumerable<Exception> GetRootCauses(this Exception ex) => ex switch
    {
        AggregateException aggex => aggex.Flatten().InnerExceptions.SelectMany(inner => inner.GetRootCauses()),
        _ => [ex]
    };

    public static bool IsInputError(this Exception ex) => ex is InputException || ex is LiftiException
        || ex is VideoUnavailableException || ex is PlaylistUnavailableException;

    public static bool AnyNeedReporting(this IEnumerable<Exception> exns)
        => exns.Any(e => e is not OperationCanceledException && !e.IsInputError());

    public static bool HaveInputError(this IEnumerable<Exception> exns) => exns.Any(IsInputError);
    public static bool AreAll<T>(this IEnumerable<Exception> exns) => exns.All(e => e is T);
    public static bool AreAllCancelations(this IEnumerable<Exception> exns) => exns.AreAll<OperationCanceledException>();

    internal static bool IsNotFound(this HttpRequestException exception)
        => exception.StatusCode == System.Net.HttpStatusCode.NotFound || exception.Message.Contains("404 (NotFound)");
}
