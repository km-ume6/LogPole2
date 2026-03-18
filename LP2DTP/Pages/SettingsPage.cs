using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using LP2DTP.Common.Models;
using LP2DTP.Common.Services;
using LP2DTP.Services;
using WinAppSdkPickers = Microsoft.Windows.Storage.Pickers;
using WinRT.Interop;

namespace LP2DTP.Pages
{
    public sealed class SettingsPage : Page
    {
        private readonly AppSettingsService _settingsService;
        private readonly WindowsServiceManager _serviceManager;
        private readonly ServiceHealthMonitor _serviceHealthMonitor;
        private readonly DispatcherTimer _serviceMonitorTimer;
        private AppSettings _settings;
        private NumberBox _pollingIntervalBox = null!;
        private NumberBox _healthCheckIntervalBox = null!;
        private TextBox _serviceExecutablePathBox = null!;
        private TextBox _serviceNameBox = null!;
        private TextBox _serviceDescriptionBox = null!;
        private TextBlock _serviceStateText = null!;
        private TextBlock _serviceHealthSummaryText = null!;
        private TextBlock _monitorSnapshotStateText = null!;
        private TextBlock _monitorStartedAtText = null!;
        private TextBlock _monitorHeartbeatText = null!;
        private TextBlock _monitorInitialCycleText = null!;
        private TextBlock _monitorLastSuccessText = null!;
        private TextBlock _monitorLastSuccessDeviceText = null!;
        private TextBlock _monitorWorkersText = null!;
        private TextBlock _monitorIntervalsText = null!;
        private TextBlock _monitorSqlErrorText = null!;
        private TextBlock _monitorSelfRecoveryText = null!;
        private TextBlock _monitorLastErrorText = null!;
        private TextBlock _statusText = null!;
        private Button _registerServiceButton = null!;
        private Button _startServiceButton = null!;
        private Button _stopServiceButton = null!;
        private Button _restartServiceButton = null!;
        private Button _deleteServiceButton = null!;

        public SettingsPage()
        {
            _settingsService = new AppSettingsService();
            _serviceManager = new WindowsServiceManager();
            _serviceHealthMonitor = new ServiceHealthMonitor();
            _serviceMonitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _serviceMonitorTimer.Tick += ServiceMonitorTimer_Tick;
            _settings = AppSettings.Default;

            InitializeUI();
            Loaded += SettingsPage_Loaded;
            Unloaded += SettingsPage_Unloaded;
        }

        private void InitializeUI()
        {
            var rootGrid = new Grid
            {
                Padding = new Thickness(24)
            };

            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Title
            var titleText = new TextBlock
            {
                Text = "Settings",
                FontSize = 32,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 24)
            };
            Grid.SetRow(titleText, 0);
            rootGrid.Children.Add(titleText);

            var contentScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollMode = ScrollMode.Auto,
                HorizontalScrollMode = ScrollMode.Disabled
            };

            var contentPanel = new StackPanel
            {
                Spacing = 24,
                MaxWidth = 720,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Polling Settings Section
            var pollingSectionHeader = new TextBlock
            {
                Text = "Polling Settings",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            };
            contentPanel.Children.Add(pollingSectionHeader);

            // Polling Interval Setting
            var pollingIntervalPanel = CreateSettingRow(
                "Polling Interval (sec)",
                "Time interval between polling cycles"
            );

            _pollingIntervalBox = new NumberBox
            {
                Minimum = 1,
                Maximum = 3600,
                Value = _settings.PollingIntervalSeconds,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                SmallChange = 1,
                LargeChange = 10,
                Width = 200,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _pollingIntervalBox.ValueChanged += (s, e) =>
            {
                if (_pollingIntervalBox.Value >= _pollingIntervalBox.Minimum &&
                    _pollingIntervalBox.Value <= _pollingIntervalBox.Maximum)
                {
                    var seconds = Math.Clamp((int)Math.Round(_pollingIntervalBox.Value), 1, 3600);
                    if (Math.Abs(_pollingIntervalBox.Value - seconds) > double.Epsilon)
                    {
                        _pollingIntervalBox.Value = seconds;
                        return;
                    }

                    _settings.PollingIntervalSeconds = seconds;
                }
            };

            pollingIntervalPanel.Children.Add(_pollingIntervalBox);
            contentPanel.Children.Add(pollingIntervalPanel);

            var healthCheckIntervalPanel = CreateSettingRow(
                "Health Check Interval (sec)",
                "Time interval for endpoint health checks"
            );

            _healthCheckIntervalBox = new NumberBox
            {
                Minimum = 1,
                Maximum = 3600,
                Value = _settings.HealthCheckIntervalSeconds,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                SmallChange = 1,
                LargeChange = 10,
                Width = 200,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _healthCheckIntervalBox.ValueChanged += (s, e) =>
            {
                if (_healthCheckIntervalBox.Value >= _healthCheckIntervalBox.Minimum &&
                    _healthCheckIntervalBox.Value <= _healthCheckIntervalBox.Maximum)
                {
                    var seconds = Math.Clamp((int)Math.Round(_healthCheckIntervalBox.Value), 1, 3600);
                    if (Math.Abs(_healthCheckIntervalBox.Value - seconds) > double.Epsilon)
                    {
                        _healthCheckIntervalBox.Value = seconds;
                        return;
                    }

                    _settings.HealthCheckIntervalSeconds = seconds;
                }
            };

            healthCheckIntervalPanel.Children.Add(_healthCheckIntervalBox);
            contentPanel.Children.Add(healthCheckIntervalPanel);

            // Action Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Margin = new Thickness(0, 24, 0, 0)
            };

            var saveButton = new Button
            {
                Content = "Save Settings",
                Padding = new Thickness(24, 12, 24, 12),
                Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 0, G = 128, B = 0 }),
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 })
            };
            saveButton.Click += SaveButton_Click;
            buttonPanel.Children.Add(saveButton);

            var resetButton = new Button
            {
                Content = "Reset to Default",
                Padding = new Thickness(24, 12, 24, 12)
            };
            resetButton.Click += ResetButton_Click;
            buttonPanel.Children.Add(resetButton);

            contentPanel.Children.Add(buttonPanel);

            // Service Management Section
            var serviceSectionHeader = new TextBlock
            {
                Text = "LP2SVR Service",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 16, 0, 12)
            };
            contentPanel.Children.Add(serviceSectionHeader);

            // Service Executable Path
            var servicePanel = CreateSettingRow(
                "Service",
                "Register, delete, start, stop, and restart LP2SVR. Run LP2DTP as Administrator to use these actions."
            );

            var serviceNamePanel = CreateSettingRow(
                "Service Name",
                "Windows service name used for register/start/stop/delete"
            );

            _serviceNameBox = new TextBox
            {
                PlaceholderText = "Service name",
                Text = WindowsServiceManager.DefaultServiceName,
                Width = 260,
                MaxWidth = 260,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _serviceNameBox.LostFocus += ServiceNameBox_LostFocus;
            serviceNamePanel.Children.Add(_serviceNameBox);
            servicePanel.Children.Add(serviceNamePanel);

            var serviceDescriptionPanel = CreateSettingRow(
                "Service Description",
                "Description shown in Windows Services"
            );

            _serviceDescriptionBox = new TextBox
            {
                PlaceholderText = "Service description",
                Text = WindowsServiceManager.DefaultDescription,
                Width = 460,
                MaxWidth = 460,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            serviceDescriptionPanel.Children.Add(_serviceDescriptionBox);
            servicePanel.Children.Add(serviceDescriptionPanel);

            var servicePathPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 8, 0, 0)
            };

            _serviceExecutablePathBox = new TextBox
            {
                PlaceholderText = "Path to LP2SVR.exe",
                Width = 460,
                MaxWidth = 460
            };
            servicePathPanel.Children.Add(_serviceExecutablePathBox);

            var browseButton = new Button
            {
                Content = "Browse...",
                Padding = new Thickness(16, 10, 16, 10)
            };
            browseButton.Click += BrowseServiceExecutableButton_Click;
            servicePathPanel.Children.Add(browseButton);

            servicePanel.Children.Add(servicePathPanel);

            // Service Action Buttons
            var serviceButtonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 12, 0, 0)
            };

            _registerServiceButton = CreateServiceButton("Register", RegisterServiceButton_Click);
            _startServiceButton = CreateServiceButton("Start", StartServiceButton_Click);
            _stopServiceButton = CreateServiceButton("Stop", StopServiceButton_Click);
            _restartServiceButton = CreateServiceButton("Restart", RestartServiceButton_Click);
            _deleteServiceButton = CreateServiceButton("Delete", DeleteServiceButton_Click);
            var refreshMonitorButton = CreateServiceButton("Refresh", RefreshMonitorButton_Click);

            serviceButtonPanel.Children.Add(_registerServiceButton);
            serviceButtonPanel.Children.Add(_startServiceButton);
            serviceButtonPanel.Children.Add(_stopServiceButton);
            serviceButtonPanel.Children.Add(_restartServiceButton);
            serviceButtonPanel.Children.Add(_deleteServiceButton);
            serviceButtonPanel.Children.Add(refreshMonitorButton);
            servicePanel.Children.Add(serviceButtonPanel);

            // Service Status Text
            _serviceStateText = new TextBlock
            {
                Text = "Service status: Unknown",
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 100, G = 100, B = 100 })
            };
            servicePanel.Children.Add(_serviceStateText);

            var serviceMonitorPanel = CreateSettingRow(
                "Service Monitor",
                "Monitor LP2SVR heartbeat and the latest successful polling status. Refreshes every 5 seconds."
            );

            _serviceHealthSummaryText = CreateMonitorValueTextBlock("Monitor health: No data");
            _monitorSnapshotStateText = CreateMonitorValueTextBlock("Snapshot state: -");
            _monitorStartedAtText = CreateMonitorValueTextBlock("Started at: -");
            _monitorHeartbeatText = CreateMonitorValueTextBlock("Last heartbeat: -");
            _monitorInitialCycleText = CreateMonitorValueTextBlock("Initial cycle: -");
            _monitorLastSuccessText = CreateMonitorValueTextBlock("Last success: -");
            _monitorLastSuccessDeviceText = CreateMonitorValueTextBlock("Last success device: -");
            _monitorWorkersText = CreateMonitorValueTextBlock("Workers: -");
            _monitorIntervalsText = CreateMonitorValueTextBlock("Intervals: -");
            _monitorSqlErrorText = CreateMonitorValueTextBlock("SQL write errors: -");
            _monitorSelfRecoveryText = CreateMonitorValueTextBlock("Self-recovery: -");
            _monitorLastErrorText = CreateMonitorValueTextBlock("Last error: -");

            serviceMonitorPanel.Children.Add(_serviceHealthSummaryText);
            serviceMonitorPanel.Children.Add(_monitorSnapshotStateText);
            serviceMonitorPanel.Children.Add(_monitorStartedAtText);
            serviceMonitorPanel.Children.Add(_monitorHeartbeatText);
            serviceMonitorPanel.Children.Add(_monitorInitialCycleText);
            serviceMonitorPanel.Children.Add(_monitorLastSuccessText);
            serviceMonitorPanel.Children.Add(_monitorLastSuccessDeviceText);
            serviceMonitorPanel.Children.Add(_monitorWorkersText);
            serviceMonitorPanel.Children.Add(_monitorIntervalsText);
            serviceMonitorPanel.Children.Add(_monitorSqlErrorText);
            serviceMonitorPanel.Children.Add(_monitorSelfRecoveryText);
            serviceMonitorPanel.Children.Add(_monitorLastErrorText);
            servicePanel.Children.Add(serviceMonitorPanel);

            contentPanel.Children.Add(servicePanel);

            // Service Log and Control Buttons
            var logControlPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var viewLogButton = new Button
            {
                Content = "View Log",
                Padding = new Thickness(16, 10, 16, 10)
            };
            viewLogButton.Click += ViewLogButton_Click;
            logControlPanel.Children.Add(viewLogButton);

            var clearLogButton = new Button
            {
                Content = "Clear Log",
                Padding = new Thickness(16, 10, 16, 10)
            };
            clearLogButton.Click += ClearLogButton_Click;
            logControlPanel.Children.Add(clearLogButton);

            contentPanel.Children.Add(logControlPanel);

            contentScrollViewer.Content = contentPanel;
            Grid.SetRow(contentScrollViewer, 1);
            rootGrid.Children.Add(contentScrollViewer);

            // Status Area
            _statusText = new TextBlock
            {
                Text = "",
                FontSize = 14,
                Margin = new Thickness(0, 12, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(_statusText, 2);
            rootGrid.Children.Add(_statusText);

            Content = rootGrid;
        }

        private StackPanel CreateSettingRow(string label, string description)
        {
            var panel = new StackPanel
            {
                Spacing = 4
            };

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            panel.Children.Add(labelText);

            var descText = new TextBlock
            {
                Text = description,
                FontSize = 12,
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 100, G = 100, B = 100 }),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(descText);

            return panel;
        }

        private TextBlock CreateMonitorDetailTextBlock(string label, string defaultValue)
        {
            return new TextBlock
            {
                Text = $"{label}: {defaultValue}",
                FontSize = 12,
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 0, G = 128, B = 128 }),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private static Button CreateServiceButton(string label, RoutedEventHandler clickHandler)
        {
            var button = new Button
            {
                Content = label,
                Padding = new Thickness(16, 10, 16, 10)
            };
            button.Click += clickHandler;
            return button;
        }

        private static TextBlock CreateMonitorValueTextBlock(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
        }

        private async void ServiceNameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                await RefreshServiceStatusAsync();
            }
            catch
            {
            }
        }

        private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _settings = await _settingsService.LoadSettingsAsync();
                _pollingIntervalBox.Value = _settings.PollingIntervalSeconds;
                _healthCheckIntervalBox.Value = _settings.HealthCheckIntervalSeconds;
                _serviceExecutablePathBox.Text = _serviceManager.TryResolveExecutablePath() ?? string.Empty;

                var isAdmin = _serviceManager.IsAdministrator();
                _registerServiceButton.IsEnabled = isAdmin;
                _startServiceButton.IsEnabled = isAdmin;
                _stopServiceButton.IsEnabled = isAdmin;
                _restartServiceButton.IsEnabled = isAdmin;
                _deleteServiceButton.IsEnabled = isAdmin;

                await RefreshServiceStatusAsync();
                _serviceMonitorTimer.Start();

                if (!isAdmin)
                {
                    UpdateStatus("管理者権限ではないため、サービスの登録/開始/停止/再起動/削除は無効です。LP2DTPを管理者として実行してください。", true);
                }
                else
                {
                    UpdateStatus("Settings loaded", false);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading settings: {ex.Message}", true);
            }
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _serviceMonitorTimer.Stop();
        }

        private async void ServiceMonitorTimer_Tick(object sender, object e)
        {
            try
            {
                await RefreshServiceStatusAsync();
            }
            catch
            {
            }
        }

        private async void RefreshMonitorButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await RefreshServiceStatusAsync();
                UpdateStatus("Service monitor refreshed", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error refreshing service monitor: {ex.Message}", true);
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _settingsService.SaveSettingsAsync(_settings);
                UpdateStatus("Settings saved successfully! Changes will take effect on next polling start.", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error saving settings: {ex.Message}", true);
            }
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _settingsService.ResetToDefaultAsync();
                var currentSettings = _settingsService.CurrentSettings;
                if (currentSettings != null)
                {
                    _settings = currentSettings;
                    _pollingIntervalBox.Value = _settings.PollingIntervalSeconds;
                    _healthCheckIntervalBox.Value = _settings.HealthCheckIntervalSeconds;
                }
                UpdateStatus("Settings reset to default values", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error resetting settings: {ex.Message}", true);
            }
        }

        private async void BrowseServiceExecutableButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedPath = await TryPickExecutablePathAsync();
                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    return;
                }

                _serviceExecutablePathBox.Text = selectedPath;
                UpdateStatus("LP2SVR executable selected", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error selecting executable: {ex.Message}", true);
            }
        }

        private async Task<string?> TryPickExecutablePathAsync()
        {
            var window = (Application.Current as App)?.Window;
            if (window == null)
            {
                return null;
            }

            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);

            var picker = new WinAppSdkPickers.FileOpenPicker(windowId)
            {
                SuggestedStartLocation = WinAppSdkPickers.PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add(".exe");

            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }

        private async void RegisterServiceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var executablePath = _serviceManager.TryResolveExecutablePath(_serviceExecutablePathBox.Text);
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    throw new InvalidOperationException("LP2SVR.exe path could not be resolved.");
                }

                _serviceExecutablePathBox.Text = executablePath;
                var serviceName = GetSelectedServiceName();
                var serviceDescription = GetSelectedServiceDescription();

                await _serviceManager.RegisterAsync(
                    executablePath,
                    serviceName: serviceName,
                    displayName: serviceName,
                    description: serviceDescription);

                await RefreshServiceStatusAsync();
                UpdateStatus($"Service '{serviceName}' registered", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error registering service: {ex.Message}", true);
            }
        }

        private async void StartServiceButton_Click(object sender, RoutedEventArgs e)
        {
            var serviceName = GetSelectedServiceName();
            await ExecuteServiceActionAsync(
                () => _serviceManager.StartAsync(serviceName: serviceName),
                $"Service '{serviceName}' started",
                "starting service");
        }

        private async void StopServiceButton_Click(object sender, RoutedEventArgs e)
        {
            var serviceName = GetSelectedServiceName();
            await ExecuteServiceActionAsync(
                () => _serviceManager.StopAsync(serviceName: serviceName),
                $"Service '{serviceName}' stopped",
                "stopping service");
        }

        private async void RestartServiceButton_Click(object sender, RoutedEventArgs e)
        {
            var serviceName = GetSelectedServiceName();
            await ExecuteServiceActionAsync(
                () => _serviceManager.RestartAsync(serviceName: serviceName),
                $"Service '{serviceName}' restarted",
                "restarting service");
        }

        private async void DeleteServiceButton_Click(object sender, RoutedEventArgs e)
        {
            var serviceName = GetSelectedServiceName();
            await ExecuteServiceActionAsync(
                () => _serviceManager.DeleteAsync(serviceName: serviceName),
                $"Service '{serviceName}' deleted",
                "deleting service");
        }

        private async System.Threading.Tasks.Task ExecuteServiceActionAsync(Func<System.Threading.Tasks.Task> action, string successMessage, string actionName)
        {
            try
            {
                await action();
                await RefreshServiceStatusAsync();
                UpdateStatus(successMessage, false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error {actionName}: {ex.Message}", true);
            }
        }

        private async System.Threading.Tasks.Task RefreshServiceStatusAsync()
        {
            var serviceName = GetSelectedServiceName();
            var statusText = await _serviceManager.GetStatusTextAsync(serviceName: serviceName);
            _serviceStateText.Text = $"Service '{serviceName}' status: {statusText}";

            var description = await _serviceManager.GetDescriptionAsync(serviceName: serviceName);
            if (description != null)
            {
                _serviceDescriptionBox.Text = description;
            }

            await RefreshServiceMonitorAsync(serviceName, statusText);
        }

        private async Task RefreshServiceMonitorAsync(string serviceName, string windowsServiceStatus)
        {
            var snapshot = await _serviceHealthMonitor.LoadAsync();
            var hasSnapshot = !string.IsNullOrWhiteSpace(snapshot.ServiceName);
            var isServiceMatch = hasSnapshot && string.Equals(snapshot.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase);

            if (!hasSnapshot)
            {
                ApplySnapshotDisplay("No monitor data", new SolidColorBrush(new Windows.UI.Color { A = 255, R = 120, G = 120, B = 120 }));
                _monitorSnapshotStateText.Text = "Snapshot state: -";
                _monitorStartedAtText.Text = "Started at: -";
                _monitorHeartbeatText.Text = "Last heartbeat: -";
                _monitorInitialCycleText.Text = "Initial cycle: -";
                _monitorLastSuccessText.Text = "Last success: -";
                _monitorLastSuccessDeviceText.Text = "Last success device: -";
                _monitorWorkersText.Text = "Workers: -";
                _monitorIntervalsText.Text = "Intervals: -";
                _monitorSqlErrorText.Text = "SQL write errors: -";
                _monitorSelfRecoveryText.Text = "Self-recovery: -";
                _monitorLastErrorText.Text = "Last error: -";
                return;
            }

            if (!isServiceMatch)
            {
                ApplySnapshotDisplay($"Monitor data exists for service '{snapshot.ServiceName}'", new SolidColorBrush(new Windows.UI.Color { A = 255, R = 180, G = 120, B = 0 }));
                ApplySnapshotDetails(snapshot);
                return;
            }

            var (summary, brush) = EvaluateMonitorHealth(snapshot, windowsServiceStatus);
            ApplySnapshotDisplay(summary, brush);
            ApplySnapshotDetails(snapshot);
        }

        private void ApplySnapshotDetails(ServiceHealthSnapshot snapshot)
        {
            _monitorSnapshotStateText.Text = $"Snapshot state: {snapshot.State}";
            _monitorStartedAtText.Text = $"Started at: {FormatDateTime(snapshot.StartedAtUtc)}";
            _monitorHeartbeatText.Text = $"Last heartbeat: {FormatDateTime(snapshot.LastHeartbeatUtc)}";
            _monitorInitialCycleText.Text = $"Initial cycle: {FormatInitialCycle(snapshot)}";
            _monitorLastSuccessText.Text = $"Last success: {FormatDateTime(snapshot.LastSuccessfulPollUtc)}";
            _monitorLastSuccessDeviceText.Text = $"Last success device: {FormatDevice(snapshot.LastSuccessfulMachineName, snapshot.LastSuccessfulUnitName, snapshot.LastSuccessfulIpAddress)}";
            _monitorWorkersText.Text = $"Workers: {snapshot.ActiveWorkerCount}/{snapshot.TotalWorkerCount} (VISA {snapshot.VisaItemCount}, Modbus {snapshot.ModbusItemCount})";
            _monitorIntervalsText.Text = $"Intervals: Polling {snapshot.PollingIntervalSeconds}s / HealthCheck {snapshot.HealthCheckIntervalSeconds}s / Heartbeat {snapshot.HeartbeatIntervalSeconds}s";
            _monitorSqlErrorText.Text = $"SQL write errors: {FormatSqlErrors(snapshot)}";
            _monitorSelfRecoveryText.Text = $"Self-recovery: {FormatSelfRecovery(snapshot)}";
            _monitorLastErrorText.Text = $"Last error: {FormatLastError(snapshot)}";
        }

        private void ApplySnapshotDisplay(string summary, Brush brush)
        {
            _serviceHealthSummaryText.Text = $"Monitor health: {summary}";
            _serviceHealthSummaryText.Foreground = brush;
            _serviceStateText.Foreground = brush;
        }

        private static (string Summary, Brush Brush) EvaluateMonitorHealth(ServiceHealthSnapshot snapshot, string windowsServiceStatus)
        {
            if (!string.Equals(windowsServiceStatus, "Running", StringComparison.OrdinalIgnoreCase))
            {
                return ($"Windows service is {windowsServiceStatus}", new SolidColorBrush(new Windows.UI.Color { A = 255, R = 180, G = 80, B = 0 }));
            }

            var nowUtc = DateTime.UtcNow;
            var heartbeatIntervalSeconds = snapshot.HeartbeatIntervalSeconds > 0
                ? snapshot.HeartbeatIntervalSeconds
                : ServiceHealthMonitor.DefaultHeartbeatIntervalSeconds;
            var heartbeatTimeout = TimeSpan.FromSeconds(Math.Max(heartbeatIntervalSeconds * 2, 30));
            if (!snapshot.LastHeartbeatUtc.HasValue)
            {
                return ("No heartbeat received yet", new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 140, B = 0 }));
            }

            var heartbeatAge = nowUtc - snapshot.LastHeartbeatUtc.Value;
            if (heartbeatAge > heartbeatTimeout)
            {
                return ($"Heartbeat stale ({Math.Floor(heartbeatAge.TotalSeconds)}s ago)", new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 0, B = 0 }));
            }

            if (snapshot.SelfRecoverySuppressedUntilUtc.HasValue && snapshot.SelfRecoverySuppressedUntilUtc.Value > nowUtc)
            {
                return ($"Self-recovery suppressed until {snapshot.SelfRecoverySuppressedUntilUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}", new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 0, B = 0 }));
            }

            if (!snapshot.InitialCycleCompletedAtUtc.HasValue)
            {
                return ("Initial polling cycle in progress", new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 140, B = 0 }));
            }

            var pollingIntervalSeconds = snapshot.PollingIntervalSeconds > 0 ? snapshot.PollingIntervalSeconds : 1;
            var successTimeout = TimeSpan.FromSeconds(Math.Max(pollingIntervalSeconds * 5, 60));
            if (!snapshot.LastSuccessfulPollUtc.HasValue)
            {
                if (snapshot.ConsecutiveSqlWriteErrorCount > 0)
                {
                    return ($"No successful polling yet / SQL errors={snapshot.ConsecutiveSqlWriteErrorCount}", new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 140, B = 0 }));
                }

                return ("No successful polling yet", new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 140, B = 0 }));
            }

            var successAge = nowUtc - snapshot.LastSuccessfulPollUtc.Value;
            if (successAge > successTimeout)
            {
                return ($"No recent successful polling ({Math.Floor(successAge.TotalSeconds)}s ago)", new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 0, B = 0 }));
            }

            if (snapshot.ConsecutiveSqlWriteErrorCount > 0)
            {
                return ($"Polling healthy / SQL errors={snapshot.ConsecutiveSqlWriteErrorCount}", new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 140, B = 0 }));
            }

            return ($"Healthy (last success {Math.Floor(successAge.TotalSeconds)}s ago)", new SolidColorBrush(new Windows.UI.Color { A = 255, R = 0, G = 128, B = 0 }));
        }

        private static string FormatDateTime(DateTime? dateTimeUtc)
        {
            return dateTimeUtc.HasValue
                ? dateTimeUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : "-";
        }

        private static string FormatDevice(string? machineName, string? unitName, string? ipAddress)
        {
            var machine = string.IsNullOrWhiteSpace(machineName) ? "-" : machineName.Trim();
            var unit = string.IsNullOrWhiteSpace(unitName) ? "-" : unitName.Trim();
            var ip = string.IsNullOrWhiteSpace(ipAddress) ? "-" : ipAddress.Trim();
            return $"{machine} / {unit} / {ip}";
        }

        private static string FormatLastError(ServiceHealthSnapshot snapshot)
        {
            if (!snapshot.LastErrorUtc.HasValue && string.IsNullOrWhiteSpace(snapshot.LastErrorMessage))
            {
                return "-";
            }

            return $"{FormatDateTime(snapshot.LastErrorUtc)} | Count={snapshot.ConsecutiveErrorCount} | {snapshot.LastErrorMessage ?? "-"}";
        }

        private static string FormatInitialCycle(ServiceHealthSnapshot snapshot)
        {
            return snapshot.InitialCycleCompletedAtUtc.HasValue
                ? $"Completed at {FormatDateTime(snapshot.InitialCycleCompletedAtUtc)}"
                : "Waiting for first scheduled cycle completion";
        }

        private static string FormatSqlErrors(ServiceHealthSnapshot snapshot)
        {
            if (snapshot.ConsecutiveSqlWriteErrorCount <= 0 && !snapshot.LastSqlWriteErrorUtc.HasValue)
            {
                return "0";
            }

            return $"Count={snapshot.ConsecutiveSqlWriteErrorCount} | Last={FormatDateTime(snapshot.LastSqlWriteErrorUtc)}";
        }

        private static string FormatSelfRecovery(ServiceHealthSnapshot snapshot)
        {
            if (snapshot.SelfRecoverySuppressedUntilUtc.HasValue && snapshot.SelfRecoverySuppressedUntilUtc.Value > DateTime.UtcNow)
            {
                return $"Suppressed until {FormatDateTime(snapshot.SelfRecoverySuppressedUntilUtc)} | Attempts={snapshot.SelfRecoveryAttemptCount}";
            }

            if (snapshot.LastSelfRecoveryTriggeredAtUtc.HasValue)
            {
                return $"Last restart trigger {FormatDateTime(snapshot.LastSelfRecoveryTriggeredAtUtc)} | Attempts={snapshot.SelfRecoveryAttemptCount}";
            }

            if (snapshot.SelfRecoveryAttemptCount > 0)
            {
                return $"Attempts={snapshot.SelfRecoveryAttemptCount}";
            }

            return "No self-recovery activity";
        }

        private string GetSelectedServiceName()
        {
            return string.IsNullOrWhiteSpace(_serviceNameBox.Text)
                ? WindowsServiceManager.DefaultServiceName
                : _serviceNameBox.Text.Trim();
        }

        private string GetSelectedServiceDescription()
        {
            return string.IsNullOrWhiteSpace(_serviceDescriptionBox.Text)
                ? WindowsServiceManager.DefaultDescription
                : _serviceDescriptionBox.Text.Trim();
        }

        private void UpdateStatus(string message, bool isError)
        {
            _statusText.Text = message;
            _statusText.Foreground = new SolidColorBrush(isError
                ? new Windows.UI.Color { A = 255, R = 255, G = 0, B = 0 }
                : new Windows.UI.Color { A = 255, R = 0, G = 128, B = 0 });
        }

        private async void ViewLogButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement log viewing functionality
            UpdateStatus("Log viewing not implemented yet.", true);
        }

        private async void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement log clearing functionality
            UpdateStatus("Log clearing not implemented yet.", true);
        }
    }
}
