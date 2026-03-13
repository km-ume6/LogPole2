using System;
using System.IO;

namespace LP2DTP.Common.Services
{
    internal static class ApplicationDataPath
    {
        public const string FolderName = "LogPole2";
        public const string LegacyFolderName = "LP2DTP";

        public static string DirectoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            FolderName);

        public static string LegacyDirectoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            LegacyFolderName);

        public static string GetFilePath(string fileName)
        {
            return Path.Combine(DirectoryPath, fileName);
        }

        public static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(DirectoryPath))
            {
                Directory.CreateDirectory(DirectoryPath);
            }
        }

        public static void MigrateLegacyFileIfNeeded(string fileName)
        {
            var currentFilePath = GetFilePath(fileName);
            if (File.Exists(currentFilePath))
            {
                return;
            }

            var legacyFilePath = Path.Combine(LegacyDirectoryPath, fileName);
            if (!File.Exists(legacyFilePath))
            {
                return;
            }

            EnsureDirectoryExists();
            File.Copy(legacyFilePath, currentFilePath, overwrite: false);
        }
    }
}
