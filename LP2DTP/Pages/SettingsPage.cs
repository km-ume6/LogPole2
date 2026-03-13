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
        private AppSettings _settings;
        private NumberBox _pollingIntervalBox = null!;
        private NumberBox _healthCheckIntervalBox = null!;
        private TextBox _serviceExecutablePathBox = null!;
        private TextBox _serviceNameBox = null!;
        private TextBox _serviceDescriptionBox = null!;
        private TextBlock _serviceStateText = null!;
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
            _settings = AppSettings.Default;

            InitializeUI();
            Loaded += SettingsPage_Loaded;
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

            serviceButtonPanel.Children.Add(_registerServiceButton);
            serviceButtonPanel.Children.Add(_startServiceButton);
            serviceButtonPanel.Children.Add(_stopServiceButton);
            serviceButtonPanel.Children.Add(_restartServiceButton);
            serviceButtonPanel.Children.Add(_deleteServiceButton);
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

            contentPanel.Children.Add(servicePanel);

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
    }
}
