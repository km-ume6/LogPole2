using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using LP2DTP.Common.Models;
using LP2DTP.Common.Services;
using LP2DTP.ViewModels;

namespace LP2DTP.Pages
{
    public sealed class PollingTestPage : Page
    {
        private readonly VisaItemListViewModel _visaViewModel;
        private readonly ModbusItemListViewModel _modbusViewModel;
        private readonly PollingWorkerManager _pollingManager;
        private readonly AppSettingsService _settingsService;
        private StackPanel _dataDisplayPanel = null!;
        private Button _loadButton = null!;
        private Button _startButton = null!;
        private Button _stopButton = null!;
        private TextBlock _statusText = null!;
        private ScrollViewer _scrollViewer = null!;

        public PollingTestPage()
        {
            _visaViewModel = new VisaItemListViewModel();
            _modbusViewModel = new ModbusItemListViewModel();
            _pollingManager = new PollingWorkerManager();
            _settingsService = new AppSettingsService();

            // Subscribe to polling events
            _pollingManager.DataReceived += PollingManager_DataReceived;
            _pollingManager.ErrorOccurred += PollingManager_ErrorOccurred;

            InitializeUI();
            Loaded += PollingTestPage_Loaded;
            Unloaded += PollingTestPage_Unloaded;
        }

        private void InitializeUI()
        {
            var rootGrid = new Grid
            {
                Padding = new Thickness(24)
            };

            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Title
            var titleText = new TextBlock
            {
                Text = "Polling Test Page",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(titleText, 0);
            rootGrid.Children.Add(titleText);

            // Toolbar
            var toolbar = CreateToolbar();
            Grid.SetRow(toolbar, 1);
            rootGrid.Children.Add(toolbar);

            // Data Display Area
            _scrollViewer = new ScrollViewer
            {
                Margin = new Thickness(0, 16, 0, 16),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 200, G = 200, B = 200 }),
                Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 250, G = 250, B = 250 })
            };

            _dataDisplayPanel = new StackPanel
            {
                Spacing = 4,
                Padding = new Thickness(12)
            };

            _scrollViewer.Content = _dataDisplayPanel;
            Grid.SetRow(_scrollViewer, 2);
            rootGrid.Children.Add(_scrollViewer);

            // Status Area
            var statusPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8
            };

            _statusText = new TextBlock
            {
                Text = "Ready",
                FontSize = 14
            };
            statusPanel.Children.Add(_statusText);

            var statsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 24
            };

            var workerCountText = new TextBlock
            {
                Text = "Workers: 0",
                FontSize = 12,
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 100, G = 100, B = 100 })
            };
            workerCountText.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding
            {
                Source = this,
                Path = new PropertyPath("WorkerStatsText"),
                Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay
            });
            statsPanel.Children.Add(workerCountText);

            statusPanel.Children.Add(statsPanel);
            Grid.SetRow(statusPanel, 3);
            rootGrid.Children.Add(statusPanel);

            Content = rootGrid;
        }

        private StackPanel CreateToolbar()
        {
            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12
            };

            _loadButton = new Button
            {
                Content = "Load Data",
                Padding = new Thickness(16, 8, 16, 8)
            };
            _loadButton.Click += LoadButton_Click;
            toolbar.Children.Add(_loadButton);

            _startButton = new Button
            {
                Content = "Start Polling",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 0, G = 128, B = 0 }),
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 }),
                IsEnabled = false
            };
            _startButton.Click += StartButton_Click;
            toolbar.Children.Add(_startButton);

            _stopButton = new Button
            {
                Content = "Stop Polling",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 244, G = 67, B = 54 }),
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 }),
                IsEnabled = false
            };
            _stopButton.Click += StopButton_Click;
            toolbar.Children.Add(_stopButton);

            var clearButton = new Button
            {
                Content = "Clear Display",
                Padding = new Thickness(16, 8, 16, 8)
            };
            clearButton.Click += ClearButton_Click;
            toolbar.Children.Add(clearButton);

            return toolbar;
        }

        private async void PollingTestPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Load settings and apply polling interval
                var settings = await _settingsService.LoadSettingsAsync();
                _pollingManager.PollingIntervalMs = settings.PollingIntervalMs;

                UpdateStatus($"Page loaded. Polling interval: {settings.PollingIntervalMs}ms. Click 'Load Data' to begin.", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading settings: {ex.Message}. Using default: 1000ms", true);
                _pollingManager.PollingIntervalMs = 1000; // Fallback to default
            }
        }

        private void PollingTestPage_Unloaded(object sender, RoutedEventArgs e)
        {
            // Clean up
            _pollingManager.Dispose();
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _loadButton.IsEnabled = false;
                UpdateStatus("Loading items...", false);

                // Load VISA items
                await _visaViewModel.LoadItemsAsync();
                foreach (var item in _visaViewModel.Items)
                {
                    _pollingManager.AddVisaItem(item);
                }

                // Load Modbus items
                await _modbusViewModel.LoadItemsAsync();
                foreach (var item in _modbusViewModel.Items)
                {
                    _pollingManager.AddModbusItem(item);
                }

                var totalItems = _visaViewModel.Items.Count + _modbusViewModel.Items.Count;
                _startButton.IsEnabled = totalItems > 0;
                UpdateStatus($"Loaded {_visaViewModel.Items.Count} VISA items and {_modbusViewModel.Items.Count} Modbus items", false);

                AddDisplayMessage($"[INFO] Loaded {_visaViewModel.Items.Count} VISA items and {_modbusViewModel.Items.Count} Modbus items", new Windows.UI.Color { A = 255, R = 0, G = 102, B = 204 });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading data: {ex.Message}", true);
                AddDisplayMessage($"[ERROR] {ex.Message}", new Windows.UI.Color { A = 255, R = 255, G = 0, B = 0 });
            }
            finally
            {
                _loadButton.IsEnabled = true;
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _startButton.IsEnabled = false;
                _stopButton.IsEnabled = true;
                UpdateStatus("Starting polling...", false);

                await _pollingManager.StartAllAsync();

                UpdateStatus($"Polling started. Active workers: {_pollingManager.ActiveWorkerCount}", false);
                AddDisplayMessage($"[INFO] Polling started - {_pollingManager.ActiveWorkerCount} workers active", new Windows.UI.Color { A = 255, R = 0, G = 128, B = 0 });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error starting polling: {ex.Message}", true);
                AddDisplayMessage($"[ERROR] {ex.Message}", new Windows.UI.Color { A = 255, R = 255, G = 0, B = 0 });
                _startButton.IsEnabled = true;
                _stopButton.IsEnabled = false;
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _stopButton.IsEnabled = false;
                UpdateStatus("Stopping polling...", false);

                await _pollingManager.StopAllAsync();

                _startButton.IsEnabled = true;
                UpdateStatus("Polling stopped", false);
                AddDisplayMessage("[INFO] Polling stopped", new Windows.UI.Color { A = 255, R = 255, G = 140, B = 0 });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error stopping polling: {ex.Message}", true);
                AddDisplayMessage($"[ERROR] {ex.Message}", new Windows.UI.Color { A = 255, R = 255, G = 0, B = 0 });
                _stopButton.IsEnabled = true;
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _dataDisplayPanel.Children.Clear();
            UpdateStatus("Display cleared", false);
        }

        private void PollingManager_DataReceived(object? sender, PollingDataReceivedEventArgs e)
        {
            // UI update must be on UI thread
            DispatcherQueue.TryEnqueue(() =>
            {
                var message = $"[{e.Timestamp:HH:mm:ss.fff}] {e.MachineName} / {e.UnitName} / {e.IpAddress} | {e.Command} → {e.Response}";
                AddDisplayMessage(message, new Windows.UI.Color { A = 255, R = 0, G = 0, B = 0 });

                // Keep only last 100 messages
                while (_dataDisplayPanel.Children.Count > 100)
                {
                    _dataDisplayPanel.Children.RemoveAt(0);
                }

                // Auto-scroll to bottom
                _scrollViewer.ChangeView(null, _scrollViewer.ScrollableHeight, null);
            });
        }

        private void PollingManager_ErrorOccurred(object? sender, PollingErrorEventArgs e)
        {
            // UI update must be on UI thread
            DispatcherQueue.TryEnqueue(() =>
            {
                var message = $"[{e.Timestamp:HH:mm:ss.fff}] ERROR - {e.MachineName} / {e.UnitName} | {e.ErrorMessage}";
                AddDisplayMessage(message, new Windows.UI.Color { A = 255, R = 255, G = 0, B = 0 });
            });
        }

        private void AddDisplayMessage(string message, Windows.UI.Color color)
        {
            var textBlock = new TextBlock
            {
                Text = message,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 12,
                Foreground = new SolidColorBrush(color),
                TextWrapping = TextWrapping.Wrap
            };

            _dataDisplayPanel.Children.Add(textBlock);
        }

        private void UpdateStatus(string message, bool isError)
        {
            _statusText.Text = message;
            _statusText.Foreground = new SolidColorBrush(isError
                ? new Windows.UI.Color { A = 255, R = 255, G = 0, B = 0 }
                : new Windows.UI.Color { A = 255, R = 0, G = 128, B = 0 });
        }

        private string WorkerStatsText => $"Workers: {_pollingManager.TotalWorkerCount} (Active: {_pollingManager.ActiveWorkerCount})";
    }
}
