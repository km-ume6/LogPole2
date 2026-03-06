using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using LP2DTP.Common.Models;
using LP2DTP.Common.Services;

namespace LP2DTP.ViewModels
{
    /// <summary>
    /// ViewModel for Modbus item list management
    /// </summary>
    public class ModbusItemListViewModel
    {
        private readonly ModbusItemService _service;

        public ObservableCollection<ModbusItem> Items { get; }

        public ModbusItemListViewModel()
        {
            _service = new ModbusItemService();
            Items = new ObservableCollection<ModbusItem>();
        }

        /// <summary>
        /// Load items from file
        /// </summary>
        public async Task LoadItemsAsync()
        {
            try
            {
                var items = await _service.LoadAsync();
                Items.Clear();
                foreach (var item in items)
                {
                    Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading items: {ex.Message}");
            }
        }

        /// <summary>
        /// Save items to file
        /// </summary>
        public async Task SaveItemsAsync()
        {
            try
            {
                await _service.SaveAsync(Items.ToList());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving items: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Add new item
        /// </summary>
        public void AddItem()
        {
            Items.Add(new ModbusItem());
        }

        /// <summary>
        /// Add specific item
        /// </summary>
        public void AddItem(ModbusItem item)
        {
            Items.Add(item);
        }

        /// <summary>
        /// Remove item
        /// </summary>
        public void RemoveItem(ModbusItem item)
        {
            Items.Remove(item);
        }

        /// <summary>
        /// Export items to file
        /// </summary>
        public async Task ExportAsync(string filePath)
        {
            try
            {
                await _service.ExportAsync(Items.ToList(), filePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error exporting items: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Import items from file
        /// </summary>
        public async Task ImportAsync(string filePath)
        {
            try
            {
                var items = await _service.ImportAsync(filePath);
                Items.Clear();
                foreach (var item in items)
                {
                    Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error importing items: {ex.Message}");
                throw;
            }
        }
    }
}
