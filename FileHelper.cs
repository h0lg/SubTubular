using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SubTubular
{
    internal static class FileHelper
    {
        internal static IEnumerable<string> DeleteFiles(string directory, string searchPattern = "*",
            ushort? notAccessedForDays = null)
        {
            foreach (var file in GetFiles(directory, searchPattern, notAccessedForDays))
            {
                file.Delete();
                yield return file.Name;
            }
        }

        internal static IEnumerable<FileInfo> GetFiles(string directory, string searchPattern, ushort? notAccessedForDays = null)
        {
            var earliestAccess = notAccessedForDays.HasValue ? DateTime.Today.AddDays(-notAccessedForDays.Value) : null as DateTime?;

            return new DirectoryInfo(directory).EnumerateFiles(searchPattern, SearchOption.TopDirectoryOnly)
                .Where(file => earliestAccess == null || file.LastAccessTime < earliestAccess);
        }
    }
}