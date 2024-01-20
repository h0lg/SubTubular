using System.Diagnostics;
using System.Runtime.InteropServices;
using SubTubular.Extensions;

namespace SubTubular;

public static class ShellCommands
{
    public static void OpenUri(string uri) // from https://stackoverflow.com/a/61035650
        => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    // from https://stackoverflow.com/a/53245993
    public static void OpenFile(string path) => OpenUri(new Uri(path).AbsoluteUri);

    #region explore folder in file browser, from https://stackoverflow.com/a/65886646
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpVerb;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpFile;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpParameters;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    private const int SW_SHOW = 5;

    public static bool ExploreFolder(string folder)
    {
        if (!folder.IsDirectoryPath()) folder = Path.GetDirectoryName(folder)!;

        var info = new SHELLEXECUTEINFO();
        info.cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>();
        info.lpVerb = "explore";
        info.nShow = SW_SHOW;
        info.lpFile = folder;
        return ShellExecuteEx(ref info);
    }
    #endregion
}