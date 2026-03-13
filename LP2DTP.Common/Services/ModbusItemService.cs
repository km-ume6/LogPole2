using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LP2DTP.Common.Models;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// Shared service for managing Modbus items - save, load, import, export
    /// </summary>
    public class ModbusItemService
    {
        private readonly string _dataDirectory;
        private readonly string _filePath;
        private const string FileName = "modbusitems.json";

        public ModbusItemService()
        {
            _dataDirectory = ApplicationDataPath.DirectoryPath;
            _filePath = ApplicationDataPath.GetFilePath(FileName);

            ApplicationDataPath.EnsureDirectoryExists();
            ApplicationDataPath.MigrateLegacyFileIfNeeded(FileName);
        }

        public async Task<List<ModbusItem>> LoadAsync()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return new List<ModbusItem>();
                }

                var json = await File.ReadAllTextAsync(_filePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<List<ModbusItem>>(json, options) ?? new List<ModbusItem>();
            }
            catch
            {
                return new List<ModbusItem>();
            }
        }

        public async Task SaveAsync(List<ModbusItem> items)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(items, options);
                await File.WriteAllTextAsync(_filePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save Modbus items: {ex.Message}", ex);
            }
        }

        public async Task ExportAsync(List<ModbusItem> items, string filePath)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(items, options);
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to export Modbus items: {ex.Message}", ex);
            }
        }

        public async Task<List<ModbusItem>> ImportAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"File not found: {filePath}");
                }

                var json = await File.ReadAllTextAsync(filePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<List<ModbusItem>>(json, options) ?? new List<ModbusItem>();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to import Modbus items: {ex.Message}", ex);
            }
        }
    }
}
