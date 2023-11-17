using System.Reflection;

namespace SubTubular;

public static class Folder
{
    public static string GetPath(Folders folder)
    {
        string path;

        switch (folder)
        {
            case Folders.app: path = Environment.CurrentDirectory; break;
            case Folders.cache: path = GetStoragePath("cache"); break;
            case Folders.errors: path = GetStoragePath("errors"); break;
            case Folders.output: path = GetStoragePath("out"); break;
            case Folders.storage: path = GetStoragePath(); break;
            default: throw new NotImplementedException($"Opening {folder} is not implemented.");
        }

        return path;
    }

    private static string GetStoragePath(string subFolder = "") => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Assembly.GetExecutingAssembly().GetName().Name ?? nameof(SubTubular), subFolder);
}

/// <summary>App-related folders.</summary>
public enum Folders
{
    /// <summary>The directory the app is running from.</summary>
    app,

    /// <summary>The directory used for caching channel, playlist and video info.</summary>
    cache,

    /// <summary>The directory error logs are written to.</summary>
    errors,

    /// <summary>The directory output files are written to by default
    /// (unless explicitly specified using <see cref="SearchCommand.FileOutputPath"/>).</summary>
    output,

    /// <summary>The directory that hosts the <see cref="cache"/>,
    /// <see cref="errors"/> and <see cref="output"/> folders.</summary>
    storage
}
