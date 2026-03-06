using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using LP2DTP.ViewModels;
using Windows.Storage.Pickers;
using LP2DTP.Common.Models;
using WinRT.Interop;

namespace LP2DTP
{
    public sealed class VisaItemListContent : Grid
    {
        private StackPanel _itemsStackPanel;
        private TextBlock _statusTextBlock;
        public VisaItemListViewModel ViewModel { get; }

        public VisaItemListContent()
        {
            ViewModel = new VisaItemListViewModel();
            this.Padding = new Thickness(24);

            this.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            this.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            this.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Toolbar
            var toolbar = CreateToolbar();
            Grid.SetRow(toolbar, 0);
            this.Children.Add(toolbar);

            // Items Container
            var scrollViewer = new ScrollViewer
            {
                Margin = new Thickness(0, 12, 0, 12)
            };

            _itemsStackPanel = new StackPanel { Spacing = 8 };
            
            scrollViewer.Content = _itemsStackPanel;
            Grid.SetRow(scrollViewer, 1);
            this.Children.Add(scrollViewer);

            // Status
            _statusTextBlock = new TextBlock
            {
                Text = "Ready",
                Padding = new Thickness(0, 12, 0, 0)
            };
            Grid.SetRow(_statusTextBlock, 2);
            this.Children.Add(_statusTextBlock);

            Loaded += VisaItemListContent_Loaded;
        }

        private Grid CreateHeaderRow()
        {
            var headerGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(150) },
                    new ColumnDefinition { Width = new GridLength(150) },
                    new ColumnDefinition { Width = new GridLength(150) },
                    new ColumnDefinition { Width = new GridLength(200) },
                    new ColumnDefinition { Width = new GridLength(200) },
                    new ColumnDefinition { Width = new GridLength(120) },
                    new ColumnDefinition { Width = new GridLength(80) },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                Padding = new Thickness(8),
                Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 0, G = 102, B = 204 })
            };

            var headers = new[] { "Machine Name", "Unit Name", "IP Address", "Command Curr", "Command Volt", "Item Type", "Enabled", "Action" };
            for (int i = 0; i < headers.Length; i++)
            {
                var headerText = new TextBlock
                {
                    Text = headers[i],
                    Margin = new Thickness(4),
                    Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 })
                };
                Grid.SetColumn(headerText, i);
                headerGrid.Children.Add(headerText);
            }

            return headerGrid;
        }

        private Grid CreateItemRow(VisaItem item)
        {
            var itemGrid = new Grid
            {
                DataContext = item,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(150) },
                    new ColumnDefinition { Width = new GridLength(150) },
                    new ColumnDefinition { Width = new GridLength(150) },
                    new ColumnDefinition { Width = new GridLength(200) },
                    new ColumnDefinition { Width = new GridLength(200) },
                    new ColumnDefinition { Width = new GridLength(120) },
                    new ColumnDefinition { Width = new GridLength(80) },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                Padding = new Thickness(4),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(new Windows.UI.Color { A = 128, R = 200, G = 200, B = 200 })
            };

            // Machine Name
            var machineNameBox = new TextBox { Margin = new Thickness(4), Text = item.Device.MachineName };
            machineNameBox.TextChanged += (s, e) => item.Device.MachineName = machineNameBox.Text;
            Grid.SetColumn(machineNameBox, 0);
            itemGrid.Children.Add(machineNameBox);

            // Unit Name
            var unitNameBox = new TextBox { Margin = new Thickness(4), Text = item.Device.UnitName };
            unitNameBox.TextChanged += (s, e) => item.Device.UnitName = unitNameBox.Text;
            Grid.SetColumn(unitNameBox, 1);
            itemGrid.Children.Add(unitNameBox);

            // IP Address
            var ipAddressBox = new TextBox { Margin = new Thickness(4), Text = item.Device.IpAddress };
            ipAddressBox.TextChanged += (s, e) => item.Device.IpAddress = ipAddressBox.Text;
            Grid.SetColumn(ipAddressBox, 2);
            itemGrid.Children.Add(ipAddressBox);

            // Command A
            var commandABox = new TextBox { Margin = new Thickness(4), Text = item.CommandCurr };
            commandABox.TextChanged += (s, e) => item.CommandCurr = commandABox.Text;
            Grid.SetColumn(commandABox, 3);
            itemGrid.Children.Add(commandABox);

            // Command V
            var commandVBox = new TextBox { Margin = new Thickness(4), Text = item.CommandVolt };
            commandVBox.TextChanged += (s, e) => item.CommandVolt = commandVBox.Text;
            Grid.SetColumn(commandVBox, 4);
            itemGrid.Children.Add(commandVBox);

            // Item Type ComboBox
            var itemTypeCombo = new ComboBox
            {
                Margin = new Thickness(4),
                ItemsSource = Enum.GetValues(typeof(VisaItemType)),
                SelectedItem = item.ItemType
            };
            itemTypeCombo.SelectionChanged += (s, e) => item.ItemType = (VisaItemType)itemTypeCombo.SelectedItem;
            Grid.SetColumn(itemTypeCombo, 5);
            itemGrid.Children.Add(itemTypeCombo);

            // Is Enabled CheckBox
            var isEnabledCheck = new CheckBox { Margin = new Thickness(4, 8, 4, 4), IsChecked = item.IsEnabled };
            isEnabledCheck.Checked += (s, e) => item.IsEnabled = isEnabledCheck.IsChecked == true;
            isEnabledCheck.Unchecked += (s, e) => item.IsEnabled = false;
            Grid.SetColumn(isEnabledCheck, 6);
            itemGrid.Children.Add(isEnabledCheck);

            // Action Buttons Container
            var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            
            // Duplicate Button
            var duplicateButton = new Button
            {
                Content = "Copy",
                Margin = new Thickness(2),
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 33, G = 150, B = 243 })
            };
            duplicateButton.Click += (s, e) =>
            {
                var newItem = new VisaItem
                {
                    Device = new PollingItem
                    {
                        MachineName = item.Device.MachineName,
                        UnitName = item.Device.UnitName,
                        IpAddress = item.Device.IpAddress
                    },
                    CommandCurr = item.CommandCurr,
                    CommandVolt = item.CommandVolt,
                    ItemType = item.ItemType,
                    IsEnabled = item.IsEnabled
                };
                ViewModel.AddItem(newItem);
                RefreshItemsList();
                UpdateStatus("Item duplicated");
            };
            actionPanel.Children.Add(duplicateButton);

            // Delete Button
            var deleteButton = new Button
            {
                Content = "Delete",
                Margin = new Thickness(2),
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 244, G = 67, B = 54 })
            };
            deleteButton.Click += (s, e) =>
            {
                ViewModel.RemoveItem(item);
                RefreshItemsList();
                UpdateStatus("Item removed");
            };
            actionPanel.Children.Add(deleteButton);

            Grid.SetColumn(actionPanel, 7);
            itemGrid.Children.Add(actionPanel);

            return itemGrid;
        }

        private StackPanel CreateToolbar()
        {
            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12
            };

            toolbar.Children.Add(CreateButton("Add", AddButton_Click));
            toolbar.Children.Add(CreateButton("Save", SaveButton_Click, isGreen: true));
            toolbar.Children.Add(CreateButton("Export", ExportButton_Click));
            toolbar.Children.Add(CreateButton("Import", ImportButton_Click));

            return toolbar;
        }

        private Button CreateButton(string content, RoutedEventHandler click, bool isGreen = false)
        {
            var button = new Button { Content = content };
            button.Click += click;
            if (isGreen)
            {
                button.Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 0, G = 128, B = 0 });
            }
            return button;
        }

        private async void VisaItemListContent_Loaded(object sender, RoutedEventArgs e)
        {
            await ViewModel.LoadItemsAsync();
            RefreshItemsList();
            UpdateStatus("Items loaded successfully");
        }

        private void RefreshItemsList()
        {
            _itemsStackPanel.Children.Clear();
            _itemsStackPanel.Children.Add(CreateHeaderRow());

            foreach (var item in ViewModel.Items)
            {
                _itemsStackPanel.Children.Add(CreateItemRow(item));
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.AddItem();
            RefreshItemsList();
            UpdateStatus("New item added");
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ViewModel.SaveItemsAsync();
                UpdateStatus("Items saved successfully");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error saving items: {ex.Message}", isError: true);
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileSavePicker();
                picker.FileTypeChoices.Add("JSON", new[] { ".json" });
                picker.SuggestedFileName = "visaitems.json";

                var window = (Application.Current as App)?.Window;
                if (window != null)
                {
                    InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
                }

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    await ViewModel.ExportAsync(file.Path);
                    UpdateStatus($"Items exported to {file.Name}");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error exporting items: {ex.Message}", isError: true);
            }
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".json");

                var window = (Application.Current as App)?.Window;
                if (window != null)
                {
                    InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
                }

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    await ViewModel.ImportAsync(file.Path);
                    RefreshItemsList();
                    UpdateStatus($"Items imported from {file.Name}");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error importing items: {ex.Message}", isError: true);
            }
        }

        private void UpdateStatus(string message, bool isError = false)
        {
            _statusTextBlock.Text = message;
            var color = isError 
                ? new Windows.UI.Color { A = 255, R = 255, G = 0, B = 0 }
                : new Windows.UI.Color { A = 255, R = 0, G = 128, B = 0 };
            _statusTextBlock.Foreground = new SolidColorBrush(color);
        }
    }
}
