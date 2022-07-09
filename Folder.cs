using System;
using System.IO;
using System.Reflection;

namespace SubTubular
{
    internal static class Folder
    {
        internal static string GetPath(Folders folder)
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
            Assembly.GetEntryAssembly().GetName().Name, subFolder);
    }

    public enum Folders { app, cache, errors, output, storage }
}