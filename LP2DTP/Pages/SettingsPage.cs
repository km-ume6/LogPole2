using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using LP2DTP.Common.Models;
using LP2DTP.Common.Services;

namespace LP2DTP.Pages
{
    public sealed class SettingsPage : Page
    {
        private readonly AppSettingsService _settingsService;
        private AppSettings _settings;
        private NumberBox _pollingIntervalBox = null!;
        private TextBlock _statusText = null!;

        public SettingsPage()
        {
            _settingsService = new AppSettingsService();
            _settings = AppSettings.Default; // Start with default values

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

            // Settings Content
            var contentPanel = new StackPanel
            {
                Spacing = 24,
                MaxWidth = 600,
                HorizontalAlignment = HorizontalAlignment.Left
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
                "Polling Interval (ms)",
                "Time interval between polling cycles (1000ms = 1 second)"
            );

            _pollingIntervalBox = new NumberBox
            {
                Minimum = 100,
                Maximum = 60000,
                Value = _settings.PollingIntervalMs,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                SmallChange = 100,
                LargeChange = 1000,
                Width = 200,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _pollingIntervalBox.ValueChanged += (s, e) =>
            {
                if (_pollingIntervalBox.Value >= _pollingIntervalBox.Minimum &&
                    _pollingIntervalBox.Value <= _pollingIntervalBox.Maximum)
                {
                    _settings.PollingIntervalMs = (int)_pollingIntervalBox.Value;
                }
            };

            pollingIntervalPanel.Children.Add(_pollingIntervalBox);
            contentPanel.Children.Add(pollingIntervalPanel);

            Grid.SetRow(contentPanel, 1);
            rootGrid.Children.Add(contentPanel);

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

            // Status Area
            _statusText = new TextBlock
            {
                Text = "",
                FontSize = 14,
                Margin = new Thickness(0, 12, 0, 0)
            };
            Grid.SetRow(_statusText, 3);
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

        private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _settings = await _settingsService.LoadSettingsAsync();
                _pollingIntervalBox.Value = _settings.PollingIntervalMs;
                UpdateStatus("Settings loaded", false);
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
                    _pollingIntervalBox.Value = _settings.PollingIntervalMs;
                }
                UpdateStatus("Settings reset to default values", false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error resetting settings: {ex.Message}", true);
            }
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
