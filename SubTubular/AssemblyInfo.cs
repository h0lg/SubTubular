using System.Diagnostics;
using System.Reflection;

namespace SubTubular;

internal static class AssemblyInfo
{
    internal const string Name = nameof(SubTubular),
        RepoOwner = "h0lg", RepoName = Name, RepoUrl = $"https://github.com/{RepoOwner}/{RepoName}",
        IssuesUrl = $"{RepoUrl}/issues", ReleasesUrl = $"{RepoUrl}/releases";

    internal static string OutputSpacing = Environment.NewLine + Environment.NewLine;
    internal static readonly string Location, Version;

    static AssemblyInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        Location = assembly.Location;
        var version = assembly.GetName().Version.ToString();
        Version = version.Remove(version.LastIndexOf('.'));
    }

    internal static string GetProductVersion()
    {
        try { return FileVersionInfo.GetVersionInfo(Location).ProductVersion; }
        catch { return Version; }
    }
}