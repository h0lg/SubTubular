using System.Runtime.InteropServices;
using SubTubular.Extensions;

namespace SubTubular;

public static class ErrorLog
{
    /*  Non-constant fields should not be visible
     *  This is a user preference that should be kept public and writable
     *  to enable informed configuration at app start-up. */
#pragma warning disable CA2211
    /// <summary>New lines inserted in between error messages and different parts of them.
    /// Configure this once when starting the app - changing this during runtime is not thread-safe.</summary>
    public static string OutputSpacing = Environment.NewLine + Environment.NewLine;
#pragma warning restore CA2211

    /// <summary>Prefixes the <paramref name="errors"/> with the optional <paramref name="header"/> and some environment info,
    /// creating a report. It then tries to save that report to a file path. If it succeeds, the path and the report are returned.
    /// Otherwise, only the report is returned and the path is null.</summary>
    /// <param name="fileNameDescription">An optional human-readable part of the error log file name.</param>
    public static async Task<(string? path, string report)> WriteAsync(string errors, string? header = null, string? fileNameDescription = null)
    {
        var environmentInfo = new[] { "on", Environment.OSVersion.VersionString, RuntimeInformation.FrameworkDescription,
            AssemblyInfo.Name, AssemblyInfo.GetProductVersion() }.Join(" ");

        var preamble = header.IsNonWhiteSpace() ? header + OutputSpacing : string.Empty;
        var report = preamble + environmentInfo + OutputSpacing + errors;

        try
        {
            var fileSafeName = fileNameDescription == null ? null : (" " + fileNameDescription.ToFileSafe());
            var path = Path.Combine(Folder.GetPath(Folders.errors), $"error {DateTime.Now:yyyy-MM-dd HHmmss}{fileSafeName}.txt");

            /* don't continueOnCapturedContext to enable safely waiting for this synchronously on the UI thread
             * using Write() below - without dead-locking the UI thread by awaiting on it */
            await FileHelper.WriteTextAsync(report, path).ConfigureAwait(continueOnCapturedContext: false);

            return (path, report);
        }
        catch (Exception ex)
        {
            report += OutputSpacing + "Error writing error log: " + ex;
            return (null, report);
        }
    }

    public static (string? path, string report) Write(string errors, string? header = null, string? fileNameDescription = null)
        => WriteAsync(errors, header, fileNameDescription).Result;
}