using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SubTubular
{
    internal static class FileHelper
    {
        /// <summary>Deletes the file at <paramref name="filePath"/> if it exists
        /// and was <paramref name="notAccessedForDays"/> (if specified).</summary>
        /// <returns>Whether a file existed and was kept.</returns>
        internal static bool DeleteFile(string filePath, ushort? notAccessedForDays = null)
        {
            var file = new FileInfo(filePath);
            if (!file.Exists) return false;

            var earliestAccess = GetEarliestAccess(notAccessedForDays);
            if (earliestAccess.HasValue && file.LastAccessTime >= earliestAccess) return true;

            file.Delete();
            return false;
        }

        internal static void DeleteFiles(string directory, string searchPattern,
            ushort? notAccessedForDays = null, Func<string, bool> isFileNameDeletable = null)
        {
            var deletable = GetFiles(directory, searchPattern, notAccessedForDays, isFileNameDeletable);
            foreach (var file in deletable) file.Delete();
        }

        internal static IEnumerable<FileInfo> GetFiles(string directory, string searchPattern,
            ushort? notAccessedForDays = null, Func<string, bool> includeFileName = null)
        {
            var earliestAccess = GetEarliestAccess(notAccessedForDays);

            return new DirectoryInfo(directory).EnumerateFiles(searchPattern, SearchOption.TopDirectoryOnly)
                .Where(file => includeFileName == null ? true : includeFileName(file.Name)
                    && earliestAccess.HasValue && file.LastAccessTime < earliestAccess);
        }

        private static DateTime? GetEarliestAccess(ushort? daysAgo)
            => daysAgo.HasValue ? DateTime.Today.AddDays(-daysAgo.Value) : null as DateTime?;
    }
}