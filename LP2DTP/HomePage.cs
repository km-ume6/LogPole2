using System;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using LP2DTP.Pages;
using Windows.ApplicationModel;

namespace LP2DTP
{
    public sealed class HomePage : Grid
    {
        public HomePage()
        {
            Padding = new Thickness(32);
            VerticalAlignment = VerticalAlignment.Stretch;
            HorizontalAlignment = HorizontalAlignment.Stretch;

            var rootPanel = new StackPanel
            {
                Spacing = 24,
                MaxWidth = 860,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var heroBorder = new Border
            {
                CornerRadius = new CornerRadius(24),
                Padding = new Thickness(28),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 58, G = 87, B = 136 }),
                Background = CreateHeroBackground(),
                Shadow = new ThemeShadow()
            };

            var heroGrid = new Grid();
            heroGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            heroGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var heroPanel = new StackPanel { Spacing = 10 };

            var title = new TextBlock
            {
                Text = "LP2DTP",
                FontSize = 52,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 })
            };
            heroPanel.Children.Add(title);

            var subtitle = new TextBlock
            {
                Text = "LogPole 2 Device & Service Console",
                FontSize = 24,
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 219, G = 234, B = 254 })
            };
            heroPanel.Children.Add(subtitle);

            var description = new TextBlock
            {
                Text = "VISA / Modbus management, polling test, and LP2SVR service monitoring in one place.",
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 620,
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 191, G = 219, B = 254 })
            };
            heroPanel.Children.Add(description);

            Grid.SetRow(heroPanel, 0);
            heroGrid.Children.Add(heroPanel);

            var versionBadge = CreateVersionBadge();
            versionBadge.HorizontalAlignment = HorizontalAlignment.Right;
            versionBadge.Margin = new Thickness(0, 24, 0, 0);
            Grid.SetRow(versionBadge, 1);
            heroGrid.Children.Add(versionBadge);

            heroBorder.Child = heroGrid;
            rootPanel.Children.Add(heroBorder);

            var navPanel = new StackPanel
            {
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var visaItemsButton = new Button
            {
                Content = "VISA Items Management",
                Padding = new Thickness(24, 12, 24, 12),
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 280,
                Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 156, G = 39, B = 176 }),
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 })
            };
            visaItemsButton.Click += (s, e) => NavigateToPage("visa");
            navPanel.Children.Add(visaItemsButton);

            var modbusItemsButton = new Button
            {
                Content = "Modbus Items Management",
                Padding = new Thickness(24, 12, 24, 12),
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 280,
                Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 0, G = 153, B = 51 }),
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 })
            };
            modbusItemsButton.Click += (s, e) => NavigateToPage("modbus");
            navPanel.Children.Add(modbusItemsButton);

            var pollingTestButton = new Button
            {
                Content = "Polling Test Page",
                Padding = new Thickness(24, 12, 24, 12),
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 280,
                Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 0, G = 102, B = 204 }),
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 })
            };
            pollingTestButton.Click += (s, e) => NavigateToPage("polling");
            navPanel.Children.Add(pollingTestButton);

            var serviceButton = new Button
            {
                Content = "LP2SVR Service Management",
                Padding = new Thickness(24, 12, 24, 12),
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 280,
                Background = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 204, G = 102, B = 0 }),
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 })
            };
            serviceButton.Click += (s, e) => NavigateToPage("service");
            navPanel.Children.Add(serviceButton);

            rootPanel.Children.Add(navPanel);
            Children.Add(rootPanel);
        }

        private static Brush CreateHeroBackground()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1)
            };
            brush.GradientStops.Add(new GradientStop { Color = new Windows.UI.Color { A = 255, R = 15, G = 23, B = 42 }, Offset = 0.0 });
            brush.GradientStops.Add(new GradientStop { Color = new Windows.UI.Color { A = 255, R = 30, G = 41, B = 59 }, Offset = 0.55 });
            brush.GradientStops.Add(new GradientStop { Color = new Windows.UI.Color { A = 255, R = 12, G = 74, B = 110 }, Offset = 1.0 });
            return brush;
        }

        private static Border CreateVersionBadge()
        {
            var versionPanel = new StackPanel { Spacing = 4 };

            versionPanel.Children.Add(new TextBlock
            {
                Text = "VERSION",
                FontSize = 11,
                CharacterSpacing = 180,
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 147, G = 197, B = 253 })
            });

            versionPanel.Children.Add(new TextBlock
            {
                Text = GetDisplayVersion(),
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 })
            });

            versionPanel.Children.Add(new TextBlock
            {
                Text = $"Runtime: {NETVersionDisplay}",
                FontSize = 12,
                Foreground = new SolidColorBrush(new Windows.UI.Color { A = 255, R = 191, G = 219, B = 254 })
            });

            return new Border
            {
                Padding = new Thickness(16, 12, 16, 12),
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(new Windows.UI.Color { A = 64, R = 255, G = 255, B = 255 }),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(new Windows.UI.Color { A = 96, R = 191, G = 219, B = 254 }),
                Child = versionPanel
            };
        }

        private static string GetDisplayVersion()
        {
            try
            {
                var version = Package.Current.Id.Version;
                return $"v{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            }
            catch
            {
            }

            var informationalVersion = Assembly
                .GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                return $"v{informationalVersion}";
            }

            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            return assemblyVersion != null
                ? $"v{assemblyVersion}"
                : "v-";
        }

        private static string NETVersionDisplay => Environment.Version.ToString();

        private void NavigateToPage(string page)
        {
            var parent = Parent;
            while (parent != null && parent is not Frame)
            {
                parent = (parent as FrameworkElement)?.Parent;
            }

            if (parent is Frame frame)
            {
                if (page == "visa")
                {
                    frame.Content = new VisaItemListContent();
                }
                else if (page == "modbus")
                {
                    frame.Content = new ModbusItemListContent();
                }
                else if (page == "polling")
                {
                    frame.Content = new PollingTestPage();
                }
                else if (page == "service")
                {
                    frame.Content = new SettingsPage();
                }
            }
        }
    }
}
