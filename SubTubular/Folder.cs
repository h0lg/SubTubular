using System.Reflection;

namespace SubTubular;

public static class Folder
{
    public static string GetPath(Folders folder) => folder switch
    {
        Folders.app => Environment.CurrentDirectory,
        Folders.cache => GetStoragePath("cache"),
        Folders.thumbnails => GetStoragePath("cache/thumbnails"),
        Folders.errors => GetStoragePath("errors"),
        Folders.output => GetStoragePath("out"),
        Folders.storage => GetStoragePath(),
        _ => throw new NotImplementedException($"Opening {folder} is not implemented."),
    };

    private static string GetStoragePath(string subFolder = "") => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Assembly.GetExecutingAssembly().GetName().Name ?? nameof(SubTubular), subFolder);

    public static View[] DisplayList()
    {
        return Enum.GetValues<Folders>().Select(f => (Label(f), GetPath(f)))
            .Append(("other releases", ReleaseManager.GetArchivePath(GetPath(Folders.app))))
            .Where(pair => Directory.Exists(pair.Item2))
            .OrderBy(pair => (pair.Item1 != nameof(Folders.app), pair.Item2)) // sort app first, then by path
            .Aggregate(Array.Empty<View>(), MapFolderToView) // relies on prior sorting by path
            .Reverse().ToArray();

        string Label(Folders folder) => folder == Folders.storage ? "user data" : folder.ToString();

        // maps a label/path tuple to a FolderView relative to already mapped ancestor FolderViews
        View[] MapFolderToView(View[] mapped, (string, string) pair)
        {
            var (label, path) = pair;

            // first ancestor with matching Path
            var ancestor = mapped.Length == 0 ? null : mapped.FirstOrDefault(prev => path.Contains(prev.Path));

            var (level, pathDiff) = ancestor == null ? (0, path)
                : (ancestor.IndentLevel + 1,
                    path[ancestor.Path.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            var folderView = new View(label, path, level, pathDiff);
            return mapped.Prepend(folderView).ToArray();
        }
    }

    public sealed record View(string Label, string Path,
        int IndentLevel, // to root
        string PathDiff); // diff of Path to that of the parent FolderView if any, otherwise the full Path
}

/// <summary>App-related folders.</summary>
public enum Folders
{
    /// <summary>The directory the app is running from.</summary>
    app,

    /// <summary>The directory used for caching channel, playlist and video info
    /// as well as full-text indexes to search them.</summary>
    cache,

    /// <summary>The directory used for caching channel, playlist and video thumbnails.</summary>
    thumbnails,

    /// <summary>The directory error logs are written to.</summary>
    errors,

    /// <summary>The directory output files are written to by default
    /// (unless explicitly specified using <see cref="SearchCommand.FileOutputPath"/>).</summary>
    output,

    /// <summary>The user-specific directory that holds app data as well as hosting the <see cref="cache"/>,
    /// <see cref="errors"/> and <see cref="output"/> folders.</summary>
    storage
}
