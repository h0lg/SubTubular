using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SubTubular
{
    internal static class FileHelper
    {
        internal static IEnumerable<string> DeleteFiles(string directory, string searchPattern = "*",
            ushort? notAccessedForDays = null, Func<string, bool> isFileNameDeletable = null)
        {
            foreach (var file in GetFiles(directory, searchPattern, notAccessedForDays, isFileNameDeletable))
            {
                file.Delete();
                yield return file.Name;
            }
        }

        internal static IEnumerable<FileInfo> GetFiles(string directory, string searchPattern,
            ushort? notAccessedForDays = null, Func<string, bool> includeFileName = null)
        {
            var earliestAccess = notAccessedForDays.HasValue ? DateTime.Today.AddDays(-notAccessedForDays.Value) : null as DateTime?;

            return new DirectoryInfo(directory).EnumerateFiles(searchPattern, SearchOption.TopDirectoryOnly)
                .Where(file => (includeFileName == null ? true : includeFileName(file.Name))
                    && (earliestAccess == null || file.LastAccessTime < earliestAccess));
        }
    }
}