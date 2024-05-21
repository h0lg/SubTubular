using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace SubTubular.Extensions;

public static class TimeSpanExtensions
{
    private const string minSec = "mm':'ss";

    //inspired by https://stackoverflow.com/a/4709641
    public static string FormatWithOptionalHours(this TimeSpan timeSpan)
        => timeSpan.ToString(timeSpan.TotalHours >= 1 ? "h':'" + minSec : minSec);
}

/// <summary>Extension methods for <see cref="string"/>s.</summary>
public static class StringExtensions
{
    /// <summary>Determines whether <paramref name="input"/> <see cref="string.IsNullOrEmpty(string?)"/>.</summary>
    internal static bool IsNullOrEmpty(this string? input) => string.IsNullOrEmpty(input);

    /// <summary>Determines whether <paramref name="input"/> NOT <see cref="string.IsNullOrEmpty(string?)"/>.</summary>
    internal static bool IsNonEmpty(this string? input) => !string.IsNullOrEmpty(input);

    /// <summary>Determines whether <paramref name="input"/> <see cref="string.IsNullOrWhiteSpace(string?)"/>.</summary>
    internal static bool IsNullOrWhiteSpace(this string? input) => string.IsNullOrWhiteSpace(input);

    /// <summary>Determines whether <paramref name="input"/> NOT <see cref="string.IsNullOrWhiteSpace(string?)"/>.</summary>
    public static bool IsNonWhiteSpace(this string? input) => !string.IsNullOrWhiteSpace(input);

    /// <summary>Replaces all consecutive white space characters in
    /// <paramref name="input"/> with <paramref name="normalizeTo"/>.</summary>
    internal static string NormalizeWhiteSpace(this string input, string normalizeTo = " ")
        => Regex.Replace(input, @"\s+", normalizeTo);

    /// <summary>Concatenates the <paramref name="pieces"/> into a single string
    /// putting <paramref name="glue"/> in between them.</summary>
    public static string Join(this IEnumerable<string> pieces, string glue) => string.Join(glue, pieces);

    /// <summary>Indicates whether <paramref name="path"/> points to a directory rather than a file.
    /// From https://stackoverflow.com/a/19596821 .</summary>
    internal static bool IsDirectoryPath(this string path)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        path = path.Trim();

        if (Directory.Exists(path)) return true;
        if (File.Exists(path)) return false;

        // neither file nor directory exists. guess intention

        // if has trailing slash then it's a directory
        if (new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }.Any(x => path.EndsWith(x)))
            return true;

        // if has extension then its a file; directory otherwise
        return Path.GetExtension(path).IsNullOrWhiteSpace();
    }

    /// <summary>Replaces all characters unsafe for file or directory names in <paramref name="value"/>
    /// with <paramref name="replacement"/>.</summary>
    public static string ToFileSafe(this string value, string replacement = "_")
        => Regex.Replace(value, "[" + Regex.Escape(new string(Path.GetInvalidFileNameChars())) + "]", replacement);

    internal static IEnumerable<string> Wrap(this string input, int columnWidth)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        return input.Split(' ').Wrap(columnWidth);
    }

    internal static IEnumerable<string> Wrap(this IEnumerable<string> phrases, int columnWidth)
    {
        if (phrases == null) throw new ArgumentNullException(nameof(phrases));
        if (phrases.Count() <= 1) return phrases;
        if (columnWidth <= 0) throw new ArgumentException("Column width must be greater than 0.", nameof(columnWidth));

        // from https://stackoverflow.com/a/29689349
        return phrases.Skip(1).Aggregate(seed: phrases.Take(1).ToList(), (lines, phrase) =>
        {
            // if length of last line gets up to or over columnWidth, add the phrase on a new line
            if (lines.Last().Length + phrase.Length >= columnWidth) lines.Add(phrase);
            else lines[lines.Count - 1] += " " + phrase; // otherwise add phrase to last line

            return lines;
        });
    }

    internal static IEnumerable<string> Indent(this IEnumerable<string> lines, int indentLevel)
    {
        if (lines == null) throw new ArgumentNullException(nameof(lines));
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
}

internal static class HashCodeExtensions
{
    // Convert a collection to a HashSet of hash codes
    internal static HashSet<int> AsHashCodeSet<T>(this IEnumerable<T>? collection)
        => collection == null ? [] : collection.Select(item => item?.GetHashCode() ?? 0).ToHashSet();

    // Adds the elements of a collection to the HashCode in a combined and ordered way
    internal static HashCode AddOrdered<T>(this HashCode hashCode, IEnumerable<T> collection)
    {
        // Sort the hash codes before combining them to ensure order-independence
        foreach (var item in collection.OrderBy(i => i)) hashCode.Add(item);
        return hashCode;
    }
}

public static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> Parallelize<T>(this IEnumerable<IAsyncEnumerable<T>> asyncProducers,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        var products = Channel.CreateUnbounded<T>(new UnboundedChannelOptions() { SingleReader = true });

        var productionLines = asyncProducers.Select(async asyncProducer =>
        {
            try
            {
                await foreach (var product in asyncProducer.WithCancellation(cancellation))
                    await products.Writer.WriteAsync(product, cancellation);
            }
            catch (OperationCanceledException) { } // Catch cancellation from outer loop
        }).ToList();

        // hook up writer completion before starting to read to ensure the reader knows when it's done
        var completion = Task.WhenAll(productionLines).ContinueWith(_ => products.Writer.Complete(), cancellation).WithAggregateException();

        // Read from product channel and yield each product
        await foreach (var product in products.Reader.ReadAllAsync(cancellation)) yield return product;

        await completion; // to ensure any exceptions are propagated
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

internal static class HttpRequestExceptionExtensions
{
    internal static bool IsNotFound(this HttpRequestException exception)
        => exception.StatusCode == System.Net.HttpStatusCode.NotFound || exception.Message.Contains("404 (NotFound)");
}

internal static class ChannelAliasMapExtensions
{
    internal static ChannelAliasMap? ForAlias(this ISet<ChannelAliasMap> maps, object alias)
    {
        var (type, value) = ChannelAliasMap.GetTypeAndValue(alias);

        return maps.SingleOrDefault(known => known.Type == type
            && known.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
    }
}