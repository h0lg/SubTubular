using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SubTubular
{
    internal static class TimeSpanExtensions
    {
        private const string minSec = "mm':'ss";

        //inspired by https://stackoverflow.com/a/4709641
        internal static string FormatWithOptionalHours(this TimeSpan timeSpan)
            => timeSpan.ToString(timeSpan.TotalHours >= 1 ? "h':'" + minSec : minSec);
    }

    /// <summary>Extension methods for <see cref="string"/>s.</summary>
    internal static class StringExtensions
    {
        /// <summary>Replaces all consecutive white space characters in
        /// <paramref name="input"/> with <paramref name="normalizeTo"/>.</summary>
        internal static string NormalizeWhiteSpace(this string input, string normalizeTo = " ")
            => System.Text.RegularExpressions.Regex.Replace(input, @"\s+", normalizeTo);

        /// <summary>Concatenates the <paramref name="pieces"/> into a single string
        /// putting <paramref name="glue"/> in between them.</summary>
        internal static string Join(this IEnumerable<string> pieces, string glue) => string.Join(glue, pieces);

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
            return string.IsNullOrWhiteSpace(Path.GetExtension(path));
        }

        /// <summary>Replaces all characters unsafe for file or directory names in <paramref name="value"/>
        /// with <paramref name="replacement"/>.</summary>
        internal static string ToFileSafe(this string value, string replacement = "_")
            => Regex.Replace(value, "[" + Regex.Escape(new string(Path.GetInvalidFileNameChars())) + "]", replacement);
    }

    /// <summary>Extension methods for <see cref="IEnumerable{T}"/> types.</summary>
    internal static class EnumerableExtenions
    {
        /// <summary>Indicates whether <paramref name="collection"/>
        /// contains any of the supplied <paramref name="values"/>.</summary>
        internal static bool ContainsAny<T>(this IEnumerable<T> collection, IEnumerable<T> values)
            => values.Intersect(collection).Any();

        /// <summary>Indicates whether <paramref name="collection"/> is not null and contains any items.</summary>
        internal static bool HasAny<T>(this IEnumerable<T> collection) => collection != null && collection.Any();
    }

    /// <summary>Extension methods for <see cref="IComparable"/> types.</summary>
    internal static class ComparableExtensions
    {
        /// <summary>Determines whether <paramref name="other"/> is greater than
        /// <paramref name="orEqualTo"/> the <paramref name="other"/>.</summary>
        internal static bool IsGreaterThan(this IComparable comparable, IComparable other, bool orEqualTo = false)
        {
            var position = comparable.CompareTo(other);
            return orEqualTo ? position >= 0 : position > 0;
        }

        /// <summary>Determines whether <paramref name="other"/> is less than
        /// <paramref name="orEqualTo"/> the <paramref name="other"/>.</summary>
        internal static bool IsLessThan(this IComparable comparable, IComparable other, bool orEqualTo = false)
        {
            var position = comparable.CompareTo(other);
            return orEqualTo ? position <= 0 : position < 0;
        }
    }

    internal static class TaskExtensions
    {
        /// <summary>
        /// Returns the input tasks in the order they complete
        /// From https://devblogs.microsoft.com/pfxteam/processing-tasks-as-they-complete/ .
        /// </summary>
        /// <param name="tasks"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        internal static Task<Task<T>>[] Interleaved<T>(this IEnumerable<Task<T>> tasks)
        {
            var inputTasks = tasks.ToList();
            var buckets = new TaskCompletionSource<Task<T>>[inputTasks.Count];
            var results = new Task<Task<T>>[buckets.Length];

            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = new TaskCompletionSource<Task<T>>();
                results[i] = buckets[i].Task;
            }

            int nextTaskIndex = -1;

            Action<Task<T>> continuation = completed =>
            {
                var bucket = buckets[Interlocked.Increment(ref nextTaskIndex)];
                bucket.TrySetResult(completed);
            };

            foreach (var inputTask in inputTasks)
                inputTask.ContinueWith(continuation, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            return results;
        }
    }
}