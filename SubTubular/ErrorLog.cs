using System.Runtime.InteropServices;
using SubTubular.Extensions;

namespace SubTubular;

public static class ErrorLog
{
    public static string OutputSpacing = Environment.NewLine + Environment.NewLine;

    /// <summary>Prefixes the <paramref name="errors"/> with the optional <paramref name="header"/> and some environment info,
    /// creating a report. It then tries to save that report to a file path. If it succeeds, the path and the report are returned.
    /// Otherwise, only the report is returned and the path is null.</summary>
    /// <param name="fileNameDescription">An optional human-readable part of the error log file name.</param>
    public static async Task<(string? path, string report)> WriteAsync(string errors, string? header = null, string? fileNameDescription = null)
    {
        var environmentInfo = new[] { "on", Environment.OSVersion.VersionString, RuntimeInformation.FrameworkDescription,
            AssemblyInfo.Name, AssemblyInfo.GetProductVersion() }.Join(" ");

        var preamble = header.IsNonWhiteSpace() ? string.Empty : header + OutputSpacing;
        var report = preamble + environmentInfo + OutputSpacing + errors;

        try
        {
            var fileSafeName = fileNameDescription == null ? null : (" " + fileNameDescription.ToFileSafe());
            var path = Path.Combine(Folder.GetPath(Folders.errors), $"error {DateTime.Now:yyyy-MM-dd HHmmss}{fileSafeName}.txt");
            await FileHelper.WriteTextAsync(report, path);
            return (path, report);
        }
        catch (Exception ex)
        {
            report += OutputSpacing + "Error writing error log: " + ex.ToString();
            return (null, report);
        }
    }
}