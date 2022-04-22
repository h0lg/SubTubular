using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SubTubular
{
    internal static class ShellCommands
    {
        internal static void OpenFile(string path) // from https://stackoverflow.com/a/53245993
            => Process.Start(new ProcessStartInfo(new Uri(path).AbsoluteUri) { UseShellExecute = true });

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

        internal static bool ExploreFolder(string folder)
        {
            if (!folder.IsDirectoryPath()) folder = Path.GetDirectoryName(folder);

            var info = new SHELLEXECUTEINFO();
            info.cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>();
            info.lpVerb = "explore";
            info.nShow = SW_SHOW;
            info.lpFile = folder;
            return ShellExecuteEx(ref info);
        }
        #endregion
    }
}