using System.IO.Compression;
using SubTubular.Extensions;

namespace SubTubular;

public static class FileHelper
{
    internal static IEnumerable<string> DeleteFiles(string directory, string searchPattern = "*",
        ushort? notAccessedForDays = null, bool simulate = false)
    {
        foreach (var file in GetFiles(directory, searchPattern, notAccessedForDays))
        {
            if (!simulate) file.Delete();
            yield return file.Name;
        }
    }

    internal static IEnumerable<FileInfo> GetFiles(string directory, string searchPattern, ushort? notAccessedForDays = null)
    {
        var earliestAccess = notAccessedForDays.HasValue ? DateTime.Today.AddDays(-notAccessedForDays.Value) : null as DateTime?;

        return new DirectoryInfo(directory).EnumerateFiles(searchPattern, SearchOption.TopDirectoryOnly)
            .Where(file => earliestAccess == null || file.LastAccessTime < earliestAccess);
    }

    /// <summary>Returns all file paths in <paramref name="folder"/> recursively
    /// except for paths in <paramref name="excludedFolder"/>.</summary>
    internal static IEnumerable<string> GetFilesExcluding(string folder, string excludedFolder)
        => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(excludedFolder));

    /// <summary>Creates the parent directory/ies for <paramref name="filePath"/> if they don't exist already.</summary>
    internal static void CreateFolder(string filePath)
    {
        var targetFolder = Path.GetDirectoryName(filePath);
        if (targetFolder != null && !Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);
    }

    /// <summary>Asynchronously downloads a file from <paramref name="downloadUrl"/>
    /// and saves it at <paramref name="targetPath"/>.</summary>
    internal static async Task DownloadAsync(string downloadUrl, string targetPath)
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(downloadUrl);

        if (response.IsSuccessStatusCode) await File.WriteAllBytesAsync(targetPath,
            await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>Unpacks the <paramref name="zipFile"/> into the <paramref name="targetFolder"/>
    /// while flattening a top-level folder that wraps all zip contents.</summary>
    internal static void Unzip(string zipFile, string targetFolder)
    {
        using var archive = ZipFile.OpenRead(zipFile);
        var topLevelFolders = archive.Entries.Where(e => e.Name.IsNullOrEmpty() && Path.EndsInDirectorySeparator(e.FullName)
            && e.FullName.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar) == 1).ToArray();

        var wrappingFolder = topLevelFolders.Length == 1 ? topLevelFolders[0].FullName : null;
        var hasWrappingFolder = wrappingFolder != null && archive.Entries.All(e => e.FullName.StartsWith(wrappingFolder));

        foreach (var entry in archive.Entries.Where(e => e.Name.IsNonEmpty()))
        {
            var relativePath = hasWrappingFolder ? entry.FullName.Remove(0, wrappingFolder!.Length) : entry.FullName;
            var targetFilePath = Path.Combine(targetFolder, relativePath);
            CreateFolder(targetFilePath);
            entry.ExtractToFile(targetFilePath);
        }
    }

    public static Task WriteTextAsync(string text, string path)
    {
        CreateFolder(path);
        return File.WriteAllTextAsync(path, text);
    }
}