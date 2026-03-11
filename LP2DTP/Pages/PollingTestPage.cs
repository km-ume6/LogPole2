using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using LP2DTP.Common.Services;
using LP2DTP.ViewModels;

namespace LP2DTP.Pages
{
    public sealed class PollingTestPage : Page
    {
        private enum SortColumn
        {
            MachineName,
            UnitName,
            IpAddress,
            Ping,
            Type,
            Data,
            UpdatedAt
        }

        private sealed class RowVisual
        {
            public TextBlock MachineName { get; init; } = null!;
            public TextBlock UnitName { get; init; } = null!;
            public TextBlock IpAddress { get; init; } = null!;
            public TextBlock Ping { get; init; } = null!;
            public TextBlock Type { get; init; } = null!;
            public TextBlock Data { get; init; } = null!;
            public TextBlock UpdatedAt { get; init; } = null!;
        }

        private sealed class VisaDisplayValues
        {
            public string Current { get; set; } = "-";
            public string Voltage { get; set; } = "-";
        }

        private sealed class PendingRowUpdate
        {
            public string MachineName { get; init; } = "-";
            public string UnitName { get; init; } = "-";
            public string IpAddress { get; init; } = "-";
            public string Command { get; init; } = "-";
            public string Response { get; init; } = "-";
            public DateTime Timestamp { get; init; }
            public bool IsError { get; init; }
        }

        private readonly VisaItemListViewModel _visaViewModel;
        private readonly ModbusItemListViewModel _modbusViewModel;
        private readonly PollingWorkerManager _pollingManager;
        private readonly AppSettingsService _settingsService;
        private readonly Dictionary<string, RowVisual> _rowVisuals = new();
        private readonly List<string> _rowOrder = new();
        private readonly Dictionary<string, string> _deviceTypeByDeviceKey = new();
        private readonly Dictionary<string, string> _visaCurrentCommandByDeviceKey = new();
        private readonly Dictionary<string, string> _visaVoltageCommandByDeviceKey = new();
        private readonly Dictionary<string, VisaDisplayValues> _visaDisplayValuesByRowKey = new();
        private readonly Dictionary<string, PendingRowUpdate> _pendingRowUpdates = new();
        private readonly object _pendingRowUpdatesLock = new();

        private Button _loadButton = null!;
        private Button _startButton = null!;
        private Button _stopButton = null!;
        private TextBlock _statusText = null!;
        private ScrollViewer _scrollViewer = null!;
        private Grid _communicationTableGrid = null!;
        private DispatcherTimer _rowUpdateTimer = null!;
        private DispatcherTimer _logRenderTimer = null!;
        private TextBlock? _logTextBlock;
        private ScrollViewer? _logScrollViewer;
        private CheckBox? _logAutoScrollToggle;
        private bool _isLogRenderPending;
        private SortColumn? _currentSortColumn;
        private bool _isSortAscending = true;

        public PollingTestPage()
        {
            _visaViewModel = new VisaItemListViewModel();
            _modbusViewModel = new ModbusItemListViewModel();
            _pollingManager = new PollingWorkerManager();
            _settingsService = new AppSettingsService();

            _pollingManager.DataReceived += PollingManager_DataReceived;
            _pollingManager.ErrorOccurred += PollingManager_ErrorOccurred;

            PollingLogService.Instance.DispatchToUI = (action) =>
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => action());
            };

            _rowUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _rowUpdateTimer.Tick += RowUpdateTimer_Tick;

            _logRenderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _logRenderTimer.Tick += LogRenderTimer_Tick;

            InitializeUI();
            Loaded += PollingTestPage_Loaded;
            Unloaded += PollingTestPage_Unloaded;
        }

        private void InitializeUI()
        {
            var rootGrid = new Grid { Padding = new Thickness(24) };
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleText = new TextBlock
            {
                Text = "Polling Test Page",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(titleText, 0);
            rootGrid.Children.Add(titleText);

            var toolbar = CreateToolbar();
            Grid.SetRow(toolbar, 1);
            rootGrid.Children.Add(toolbar);

            _communicationTableGrid = CreateCommunicationTableGrid();

            _scrollViewer = new ScrollViewer
            {
                Margin = new Thickness(0, 16, 0, 8),
                MinHeight = 360,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 200, G = 200, B = 200 }),
                Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 250, G = 250, B = 250 }),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _communicationTableGrid
            };
            Grid.SetRow(_scrollViewer, 2);
            rootGrid.Children.Add(_scrollViewer);

            var statusPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
            _statusText = new TextBlock { Text = "Ready", FontSize = 14 };
            statusPanel.Children.Add(_statusText);

            var statsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
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

            var logGrid = CreateLogGrid();
            Grid.SetRow(logGrid, 4);
            rootGrid.Children.Add(logGrid);

            Content = rootGrid;
        }

        private Grid CreateCommunicationTableGrid()
        {
            var table = new Grid();
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });

            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddHeaderCell(table, 0, 0, "Machine Name", SortColumn.MachineName);
            AddHeaderCell(table, 0, 1, "Unit Name", SortColumn.UnitName);
            AddHeaderCell(table, 0, 2, "IP Address", SortColumn.IpAddress);
            AddHeaderCell(table, 0, 3, "Ping", SortColumn.Ping);
            AddHeaderCell(table, 0, 4, "Type", SortColumn.Type);
            AddHeaderCell(table, 0, 5, "Data", SortColumn.Data);
            AddHeaderCell(table, 0, 6, "Updated At", SortColumn.UpdatedAt);

            return table;
        }

        private void AddHeaderCell(Grid table, int row, int col, string text, SortColumn sortColumn)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 80, G = 80, B = 80 }),
                BorderThickness = new Thickness(0.5),
                Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 225, G = 225, B = 225 }),
                Padding = new Thickness(8, 4, 8, 4)
            };
            var tb = new TextBlock
            {
                Text = text,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.NoWrap,
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 20, G = 20, B = 20 })
            };

            border.Tapped += (s, e) => OnHeaderTapped(sortColumn);
            border.Child = tb;
            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            table.Children.Add(border);
        }

        private static TextBlock AddDataCell(Grid table, int row, int col)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 120, G = 120, B = 120 }),
                BorderThickness = new Thickness(0.5),
                Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 245, G = 245, B = 245 }),
                Padding = new Thickness(8, 4, 8, 4)
            };
            var tb = new TextBlock
            {
                Text = "-",
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 20, G = 20, B = 20 })
            };
            border.Child = tb;
            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            table.Children.Add(border);
            return tb;
        }

        private RowVisual AddRowVisual(string rowKey)
        {
            _rowOrder.Add(rowKey);
            var rowIndex = _rowOrder.Count;
            _communicationTableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var visual = new RowVisual
            {
                MachineName = AddDataCell(_communicationTableGrid, rowIndex, 0),
                UnitName = AddDataCell(_communicationTableGrid, rowIndex, 1),
                IpAddress = AddDataCell(_communicationTableGrid, rowIndex, 2),
                Ping = AddDataCell(_communicationTableGrid, rowIndex, 3),
                Type = AddDataCell(_communicationTableGrid, rowIndex, 4),
                Data = AddDataCell(_communicationTableGrid, rowIndex, 5),
                UpdatedAt = AddDataCell(_communicationTableGrid, rowIndex, 6)
            };

            _rowVisuals[rowKey] = visual;

            if (_currentSortColumn.HasValue)
            {
                ApplySorting();
            }

            return visual;
        }

        private void OnHeaderTapped(SortColumn sortColumn)
        {
            if (_currentSortColumn == sortColumn)
            {
                _isSortAscending = !_isSortAscending;
            }
            else
            {
                _currentSortColumn = sortColumn;
                _isSortAscending = true;
            }

            ApplySorting();
        }

        private void ApplySorting()
        {
            if (!_currentSortColumn.HasValue || _rowOrder.Count <= 1)
            {
                return;
            }

            var column = _currentSortColumn.Value;
            _rowOrder.Sort((a, b) => CompareRowKey(a, b, column, _isSortAscending));
            RepositionRows();
        }

        private int CompareRowKey(string aKey, string bKey, SortColumn column, bool ascending)
        {
            var a = _rowVisuals[aKey];
            var b = _rowVisuals[bKey];

            int result = column switch
            {
                SortColumn.MachineName => string.Compare(a.MachineName.Text, b.MachineName.Text, StringComparison.OrdinalIgnoreCase),
                SortColumn.UnitName => string.Compare(a.UnitName.Text, b.UnitName.Text, StringComparison.OrdinalIgnoreCase),
                SortColumn.IpAddress => CompareIp(a.IpAddress.Text, b.IpAddress.Text),
                SortColumn.Ping => string.Compare(a.Ping.Text, b.Ping.Text, StringComparison.OrdinalIgnoreCase),
                SortColumn.Type => string.Compare(a.Type.Text, b.Type.Text, StringComparison.OrdinalIgnoreCase),
                SortColumn.Data => string.Compare(a.Data.Text, b.Data.Text, StringComparison.OrdinalIgnoreCase),
                SortColumn.UpdatedAt => CompareDateTime(a.UpdatedAt.Text, b.UpdatedAt.Text),
                _ => 0
            };

            return ascending ? result : -result;
        }

        private void RepositionRows()
        {
            for (var i = 0; i < _rowOrder.Count; i++)
            {
                var rowVisual = _rowVisuals[_rowOrder[i]];
                var row = i + 1;
                SetDataCellRow(rowVisual.MachineName, row);
                SetDataCellRow(rowVisual.UnitName, row);
                SetDataCellRow(rowVisual.IpAddress, row);
                SetDataCellRow(rowVisual.Ping, row);
                SetDataCellRow(rowVisual.Type, row);
                SetDataCellRow(rowVisual.Data, row);
                SetDataCellRow(rowVisual.UpdatedAt, row);
            }
        }

        private static void SetDataCellRow(TextBlock cell, int row)
        {
            if (cell.Parent is FrameworkElement parent)
            {
                Grid.SetRow(parent, row);
            }
        }

        private static int CompareIp(string a, string b)
        {
            if (!IPAddress.TryParse(a, out var ipa) || !IPAddress.TryParse(b, out var ipb))
            {
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            }

            var ab = ipa.GetAddressBytes();
            var bb = ipb.GetAddressBytes();
            for (var i = 0; i < Math.Min(ab.Length, bb.Length); i++)
            {
                if (ab[i] != bb[i])
                {
                    return ab[i].CompareTo(bb[i]);
                }
            }

            return ab.Length.CompareTo(bb.Length);
        }

        private static int CompareDateTime(string a, string b)
        {
            if (!DateTime.TryParse(a, out var da)) da = DateTime.MinValue;
            if (!DateTime.TryParse(b, out var db)) db = DateTime.MinValue;
            return da.CompareTo(db);
        }

        private void LogRenderTimer_Tick(object? sender, object e)
        {
            _logRenderTimer.Stop();
            if (!_isLogRenderPending)
            {
                return;
            }

            _isLogRenderPending = false;
            RenderLogs();
        }

        private void RenderLogs()
        {
            if (_logTextBlock == null || _logScrollViewer == null)
            {
                return;
            }

            var logService = PollingLogService.Instance;
            var logText = string.Join(Environment.NewLine, logService.Logs.Where(l => l != null).Select(l => l.FormattedLog));
            _logTextBlock.Text = logText;

            if (_logAutoScrollToggle?.IsChecked == true)
            {
                _logScrollViewer.ScrollToVerticalOffset(_logScrollViewer.ScrollableHeight);
            }
        }

        private async void PollingTestPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = await _settingsService.LoadSettingsAsync();
                _pollingManager.PollingIntervalMs = settings.PollingIntervalMs;
                _pollingManager.HealthCheckIntervalMs = settings.HealthCheckIntervalMs;
                UpdateStatus($"Page loaded. Polling: {settings.PollingIntervalMs}ms / HealthCheck: {settings.HealthCheckIntervalMs}ms. Click 'Load Data' to begin.", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading settings: {ex.Message}. Using defaults.", true);
                _pollingManager.PollingIntervalMs = 1000;
                _pollingManager.HealthCheckIntervalMs = 5000;
            }
        }

        private void PollingTestPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _rowUpdateTimer.Stop();
            _logRenderTimer.Stop();
            _pollingManager.Dispose();
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _loadButton.IsEnabled = false;
                UpdateStatus("Loading items...", false);

                await _visaViewModel.LoadItemsAsync();
                foreach (var item in _visaViewModel.Items)
                {
                    _pollingManager.AddVisaItem(item);
                }

                await _modbusViewModel.LoadItemsAsync();
                foreach (var item in _modbusViewModel.Items)
                {
                    _pollingManager.AddModbusItem(item);
                }

                BuildDeviceTypeIndex();
                BuildInitialRows();

                var totalItems = _visaViewModel.Items.Count + _modbusViewModel.Items.Count;
                _startButton.IsEnabled = totalItems > 0;
                UpdateStatus($"Loaded {_visaViewModel.Items.Count} VISA items and {_modbusViewModel.Items.Count} Modbus items", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading data: {ex.Message}", true);
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
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error starting polling: {ex.Message}", true);
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
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error stopping polling: {ex.Message}", true);
                _stopButton.IsEnabled = true;
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _rowVisuals.Clear();
            _rowOrder.Clear();
            _visaDisplayValuesByRowKey.Clear();
            _communicationTableGrid.Children.Clear();
            _communicationTableGrid.RowDefinitions.Clear();
            _communicationTableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddHeaderCell(_communicationTableGrid, 0, 0, "Machine Name", SortColumn.MachineName);
            AddHeaderCell(_communicationTableGrid, 0, 1, "Unit Name", SortColumn.UnitName);
            AddHeaderCell(_communicationTableGrid, 0, 2, "IP Address", SortColumn.IpAddress);
            AddHeaderCell(_communicationTableGrid, 0, 3, "Ping", SortColumn.Ping);
            AddHeaderCell(_communicationTableGrid, 0, 4, "Type", SortColumn.Type);
            AddHeaderCell(_communicationTableGrid, 0, 5, "Data", SortColumn.Data);
            AddHeaderCell(_communicationTableGrid, 0, 6, "Updated At", SortColumn.UpdatedAt);
            UpdateStatus("Display cleared", false);
        }

        private void PollingManager_DataReceived(object? sender, PollingDataReceivedEventArgs e)
        {
            EnqueueRowUpdate(new PendingRowUpdate
            {
                MachineName = e.MachineName,
                UnitName = e.UnitName,
                IpAddress = e.IpAddress,
                Command = e.Command,
                Response = e.Response,
                Timestamp = e.Timestamp,
                IsError = false
            });
        }

        private void PollingManager_ErrorOccurred(object? sender, PollingErrorEventArgs e)
        {
            EnqueueRowUpdate(new PendingRowUpdate
            {
                MachineName = e.MachineName,
                UnitName = e.UnitName,
                IpAddress = e.IpAddress,
                Command = e.Command,
                Response = "-",
                Timestamp = e.Timestamp,
                IsError = true
            });
        }

        private void EnqueueRowUpdate(PendingRowUpdate update)
        {
            var pendingKey = BuildPendingKey(update.MachineName, update.UnitName, update.IpAddress, update.Command);
            lock (_pendingRowUpdatesLock)
            {
                _pendingRowUpdates[pendingKey] = update;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_rowUpdateTimer.IsEnabled)
                {
                    _rowUpdateTimer.Start();
                }
            });
        }

        private void RowUpdateTimer_Tick(object? sender, object e)
        {
            _rowUpdateTimer.Stop();

            List<PendingRowUpdate> updates;
            lock (_pendingRowUpdatesLock)
            {
                if (_pendingRowUpdates.Count == 0)
                {
                    return;
                }

                updates = _pendingRowUpdates.Values.ToList();
                _pendingRowUpdates.Clear();
            }

            foreach (var update in updates)
            {
                var type = ResolveType(update.MachineName, update.UnitName, update.IpAddress, update.Command);
                var rowKey = BuildRowKey(update.MachineName, update.UnitName, update.IpAddress, type);
                var dataText = update.IsError
                    ? "-"
                    : BuildDataDisplay(type, rowKey, update.MachineName, update.UnitName, update.IpAddress, update.Command, update.Response);

                UpsertCommunicationRow(
                    rowKey,
                    update.MachineName,
                    update.UnitName,
                    update.IpAddress,
                    update.IsError ? "NG" : "OK",
                    type,
                    dataText,
                    update.Timestamp);
            }
        }

        private void BuildDeviceTypeIndex()
        {
            _deviceTypeByDeviceKey.Clear();
            _visaCurrentCommandByDeviceKey.Clear();
            _visaVoltageCommandByDeviceKey.Clear();

            foreach (var item in _visaViewModel.Items)
            {
                var key = BuildDeviceKey(item.Device.MachineName, item.Device.UnitName, item.Device.IpAddress);
                _deviceTypeByDeviceKey[key] = "VISA";
                _visaCurrentCommandByDeviceKey[key] = item.CommandCurr;
                _visaVoltageCommandByDeviceKey[key] = item.CommandVolt;
            }

            foreach (var item in _modbusViewModel.Items)
            {
                var key = BuildDeviceKey(item.Device.MachineName, item.Device.UnitName, item.Device.IpAddress);
                _deviceTypeByDeviceKey[key] = "Modbus";
            }
        }

        private void BuildInitialRows()
        {
            ClearButton_Click(this, new RoutedEventArgs());

            foreach (var item in _visaViewModel.Items)
            {
                var rowKey = BuildRowKey(item.Device.MachineName, item.Device.UnitName, item.Device.IpAddress, "VISA");
                _visaDisplayValuesByRowKey[rowKey] = new VisaDisplayValues();
                UpsertCommunicationRow(rowKey, item.Device.MachineName, item.Device.UnitName, item.Device.IpAddress, "-", "VISA", " - A /  - V", null);
            }

            foreach (var item in _modbusViewModel.Items)
            {
                var rowKey = BuildRowKey(item.Device.MachineName, item.Device.UnitName, item.Device.IpAddress, "Modbus");
                UpsertCommunicationRow(rowKey, item.Device.MachineName, item.Device.UnitName, item.Device.IpAddress, "-", "Modbus", "-", null);
            }
        }

        private string BuildDataDisplay(string type, string rowKey, string? machine, string? unit, string? ip, string? command, string? data)
        {
            if (!string.Equals(type, "VISA", StringComparison.OrdinalIgnoreCase))
            {
                return data ?? "-";
            }

            var deviceKey = BuildDeviceKey(machine, unit, ip);
            if (!_visaDisplayValuesByRowKey.TryGetValue(rowKey, out var display))
            {
                display = new VisaDisplayValues();
                _visaDisplayValuesByRowKey[rowKey] = display;
            }

            var formatted = FormatDisplayData(type, data);
            if (_visaCurrentCommandByDeviceKey.TryGetValue(deviceKey, out var currentCmd) && string.Equals(command, currentCmd, StringComparison.OrdinalIgnoreCase))
            {
                display.Current = formatted;
            }
            else if (_visaVoltageCommandByDeviceKey.TryGetValue(deviceKey, out var voltageCmd) && string.Equals(command, voltageCmd, StringComparison.OrdinalIgnoreCase))
            {
                display.Voltage = formatted;
            }
            else
            {
                display.Current = formatted;
            }

            return $"{FormatAlignedValue(display.Current)} A / {FormatAlignedValue(display.Voltage)} V";
        }

        private void UpsertCommunicationRow(string rowKey, string? machine, string? unit, string? ip, string ping, string type, string? data, DateTime? updatedAt)
        {
            if (!_rowVisuals.TryGetValue(rowKey, out var row))
            {
                row = AddRowVisual(rowKey);
            }

            row.MachineName.Text = EmptyToDash(machine);
            row.UnitName.Text = EmptyToDash(unit);
            row.IpAddress.Text = EmptyToDash(ip);
            row.Ping.Text = EmptyToDash(ping);
            row.Type.Text = EmptyToDash(type);
            row.Data.Text = string.Equals(type, "VISA", StringComparison.OrdinalIgnoreCase)
                ? EmptyToDash(data)
                : FormatDisplayData(type, data);
            row.UpdatedAt.Text = updatedAt.HasValue ? updatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "-";
        }

        private static string FormatDisplayData(string type, string? data)
        {
            var value = EmptyToDash(data);
            if (value == "-")
            {
                return value;
            }

            if (!string.Equals(type, "VISA", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedInvariant))
            {
                return parsedInvariant.ToString("0.0000", CultureInfo.InvariantCulture);
            }

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsedCurrent))
            {
                return parsedCurrent.ToString("0.0000", CultureInfo.CurrentCulture);
            }

            return value;
        }

        private static string FormatAlignedValue(string value)
        {
            var normalized = EmptyToDash(value);
            if (normalized == "-")
            {
                return "-".PadLeft(10);
            }

            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedInvariant))
            {
                return parsedInvariant.ToString("0.0000", CultureInfo.InvariantCulture).PadLeft(10);
            }

            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsedCurrent))
            {
                return parsedCurrent.ToString("0.0000", CultureInfo.CurrentCulture).PadLeft(10);
            }

            return normalized.PadLeft(10);
        }

        private string ResolveType(string? machine, string? unit, string? ip, string? command)
        {
            var key = BuildDeviceKey(machine, unit, ip);
            if (_deviceTypeByDeviceKey.TryGetValue(key, out var itemType))
            {
                return itemType;
            }

            if (!string.IsNullOrWhiteSpace(command) && command.Contains("MODBUS", StringComparison.OrdinalIgnoreCase))
            {
                return "Modbus";
            }

            return "VISA";
        }

        private static string BuildDeviceKey(string? machine, string? unit, string? ip)
            => $"{EmptyToDash(machine)}|{EmptyToDash(unit)}|{EmptyToDash(ip)}";

        private static string BuildRowKey(string? machine, string? unit, string? ip, string? type)
            => $"{BuildDeviceKey(machine, unit, ip)}|{EmptyToDash(type)}";

        private static string BuildPendingKey(string? machine, string? unit, string? ip, string? command)
            => $"{BuildDeviceKey(machine, unit, ip)}|{EmptyToDash(command)}";

        private static string EmptyToDash(string? value)
            => string.IsNullOrWhiteSpace(value) ? "-" : value;

        private void UpdateStatus(string message, bool isError)
        {
            _statusText.Text = message;
            _statusText.Foreground = new SolidColorBrush(isError
                ? new Windows.UI.Color { A = 255, R = 255, G = 0, B = 0 }
                : new Windows.UI.Color { A = 255, R = 0, G = 128, B = 0 });
        }

        private string WorkerStatsText => $"Workers: {_pollingManager.TotalWorkerCount} (Active: {_pollingManager.ActiveWorkerCount})";

        private StackPanel CreateToolbar()
        {
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

            _loadButton = new Button { Content = "Load Data", Padding = new Thickness(16, 8, 16, 8) };
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

            var clearButton = new Button { Content = "Clear Display", Padding = new Thickness(16, 8, 16, 8) };
            clearButton.Click += ClearButton_Click;
            toolbar.Children.Add(clearButton);

            return toolbar;
        }

        private Grid CreateLogGrid()
        {
            var logGrid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            logGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            logGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var logTitlePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 12) };
            var logTitle = new TextBlock { Text = "Polling Logs", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
            logTitlePanel.Children.Add(logTitle);

            var clearButton = new Button { Content = "Clear", Padding = new Thickness(12, 4, 12, 4), FontSize = 12 };
            clearButton.Click += (s, e) => PollingLogService.Instance.Clear();
            logTitlePanel.Children.Add(clearButton);

            var autoScrollToggle = new CheckBox { Content = "Auto Scroll", Padding = new Thickness(12, 4, 12, 4), IsChecked = true };
            logTitlePanel.Children.Add(autoScrollToggle);

            Grid.SetRow(logTitlePanel, 0);
            logGrid.Children.Add(logTitlePanel);

            var logScrollViewer = new ScrollViewer
            {
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 200, G = 200, B = 200 }),
                Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 }),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var logTextBlock = new TextBlock
            {
                Padding = new Thickness(8),
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 12,
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 0, G = 0, B = 0 }),
                TextWrapping = TextWrapping.Wrap
            };

            logScrollViewer.Content = logTextBlock;
            _logTextBlock = logTextBlock;
            _logScrollViewer = logScrollViewer;
            _logAutoScrollToggle = autoScrollToggle;

            var logService = PollingLogService.Instance;
            logService.Logs.CollectionChanged += (s, e) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _isLogRenderPending = true;
                    if (!_logRenderTimer.IsEnabled)
                    {
                        _logRenderTimer.Start();
                    }
                });
            };

            Grid.SetRow(logScrollViewer, 1);
            logGrid.Children.Add(logScrollViewer);
            return logGrid;
        }
    }
}
