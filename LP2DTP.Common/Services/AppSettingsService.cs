using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LP2DTP.Common.Models;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// Service for managing application settings
    /// </summary>
    public class AppSettingsService
    {
        private const string SettingsFileName = "appsettings.json";

        private static readonly string SettingsDirectory = ApplicationDataPath.DirectoryPath;

        private static readonly string SettingsFilePath = ApplicationDataPath.GetFilePath(SettingsFileName);

        private AppSettings? _currentSettings;

        /// <summary>
        /// Get current settings (cached, may be null if not loaded yet)
        /// </summary>
        public AppSettings? CurrentSettings => _currentSettings;

        /// <summary>
        /// Load settings from file
        /// </summary>
        public async Task<AppSettings> LoadSettingsAsync()
        {
            try
            {
                ApplicationDataPath.EnsureDirectoryExists();
                ApplicationDataPath.MigrateLegacyFileIfNeeded(SettingsFileName);

                if (File.Exists(SettingsFilePath))
                {
                    var json = await File.ReadAllTextAsync(SettingsFilePath);
                    _currentSettings = JsonSerializer.Deserialize<AppSettings>(json);
                }

                if (_currentSettings == null)
                {
                    _currentSettings = AppSettings.Default;
                }

                _currentSettings.Normalize();
                return _currentSettings;
            }
            catch
            {
                _currentSettings = AppSettings.Default;
                _currentSettings.Normalize();
                return _currentSettings;
            }
        }

        /// <summary>
        /// Save settings to file
        /// </summary>
        public async Task SaveSettingsAsync(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            try
            {
                settings.Normalize();
                ApplicationDataPath.EnsureDirectoryExists();

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(settings, options);
                await File.WriteAllTextAsync(SettingsFilePath, json);

                _currentSettings = settings;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save settings: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Reset settings to default
        /// </summary>
        public async Task ResetToDefaultAsync()
        {
            await SaveSettingsAsync(AppSettings.Default);
        }
    }
}
