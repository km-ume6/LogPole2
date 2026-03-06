using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LP2DTP.Common.Models;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// Shared service for managing VISA items - save, load, import, export
    /// </summary>
    public class VisaItemService
    {
        private readonly string _dataDirectory;
        private readonly string _filePath;
        private const string FileName = "visaitems.json";

        public VisaItemService()
        {
            _dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "LP2DTP");
            _filePath = Path.Combine(_dataDirectory, FileName);

            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
            }
        }

        public async Task<List<VisaItem>> LoadAsync()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return new List<VisaItem>();
                }

                var json = await File.ReadAllTextAsync(_filePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<List<VisaItem>>(json, options) ?? new List<VisaItem>();
            }
            catch
            {
                return new List<VisaItem>();
            }
        }

        public async Task SaveAsync(List<VisaItem> items)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(items, options);
                await File.WriteAllTextAsync(_filePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save VISA items: {ex.Message}", ex);
            }
        }

        public async Task ExportAsync(List<VisaItem> items, string filePath)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(items, options);
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to export VISA items: {ex.Message}", ex);
            }
        }

        public async Task<List<VisaItem>> ImportAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"File not found: {filePath}");
                }

                var json = await File.ReadAllTextAsync(filePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<List<VisaItem>>(json, options) ?? new List<VisaItem>();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to import VISA items: {ex.Message}", ex);
            }
        }
    }
}
