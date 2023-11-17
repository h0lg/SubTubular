using System.Diagnostics;
using System.Reflection;

namespace SubTubular;

public static class AssemblyInfo
{
    public const string Name = nameof(SubTubular),
        IssuesUrl = $"{RepoUrl}/issues", ReleasesUrl = $"{RepoUrl}/releases", RepoUrl = $"https://github.com/{RepoOwner}/{RepoName}";

    internal const string RepoOwner = "h0lg", RepoName = Name;

    public static string OutputSpacing = Environment.NewLine + Environment.NewLine;
    public static readonly string Title, Copyright, InformationalVersion;

    internal static readonly string Location, Version;

    static AssemblyInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        Location = assembly.Location;
        Title = assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title;
        Copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;
        InformationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = assembly.GetName().Version.ToString();
        Version = version.Remove(version.LastIndexOf('.'));
    }

    public static string GetProductVersion()
    {
        try { return FileVersionInfo.GetVersionInfo(Location).ProductVersion; }
        catch { return Version; }
    }
}