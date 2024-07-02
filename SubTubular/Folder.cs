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

    /// <summary>The directory that holds app data as well as hosting the <see cref="cache"/>,
    /// <see cref="errors"/> and <see cref="output"/> folders.</summary>
    storage
}
