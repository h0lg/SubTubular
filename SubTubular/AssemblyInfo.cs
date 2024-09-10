using System.Diagnostics;
using System.Reflection;

namespace SubTubular;

public static class AssemblyInfo
{
    public const string Name = nameof(SubTubular),
        IssuesUrl = $"{RepoUrl}/issues", ReleasesUrl = $"{RepoUrl}/releases", RepoUrl = $"https://github.com/{RepoOwner}/{RepoName}";

    internal const string RepoOwner = "h0lg", RepoName = Name;

    public static readonly string Title, Copyright, InformationalVersion;

    internal static readonly string Location, Version;

    static AssemblyInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        Location = assembly.Location;
        Title = assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? string.Empty;
        Copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;
        InformationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
        var version = assembly.GetName().Version?.ToString();
        Version = version == null ? string.Empty : version.Remove(version.LastIndexOf('.'));
    }

    public static string GetProductVersion()
    {
        try { return FileVersionInfo.GetVersionInfo(Location).ProductVersion ?? Version; }
        catch { return Version; }
    }
}