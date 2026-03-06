using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LP2DTP.Pages;

namespace LP2DTP
{
    public sealed class HomePage : Grid
    {
        public HomePage()
        {
            this.Padding = new Thickness(24);
            this.VerticalAlignment = VerticalAlignment.Center;
            this.HorizontalAlignment = HorizontalAlignment.Center;

            var panel = new StackPanel { Spacing = 16 };

            var title = new TextBlock { Text = "LP2DTP", FontSize = 48 };
            panel.Children.Add(title);

            var subtitle = new TextBlock { Text = "Welcome to VISA Device Manager", FontSize = 24 };
            panel.Children.Add(subtitle);

            var description = new TextBlock { Text = "Select a page from the menu", FontSize = 16, Opacity = 0.7 };
            panel.Children.Add(description);

            // Navigation buttons
            var navPanel = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(0, 24, 0, 0)
            };

            var visaItemsButton = new Button
            {
                Content = "VISA Items Management",
                Padding = new Thickness(24, 12, 24, 12),
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 250,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(new Windows.UI.Color { A = 255, R = 156, G = 39, B = 176 }),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 })
            };
            visaItemsButton.Click += (s, e) => NavigateToPage("visa");
            navPanel.Children.Add(visaItemsButton);

            var modbusItemsButton = new Button
            {
                Content = "Modbus Items Management",
                Padding = new Thickness(24, 12, 24, 12),
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 250,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(new Windows.UI.Color { A = 255, R = 0, G = 153, B = 51 }),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 })
            };
            modbusItemsButton.Click += (s, e) => NavigateToPage("modbus");
            navPanel.Children.Add(modbusItemsButton);

            var pollingTestButton = new Button
            {
                Content = "Polling Test Page",
                Padding = new Thickness(24, 12, 24, 12),
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 250,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(new Windows.UI.Color { A = 255, R = 0, G = 102, B = 204 }),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 })
            };
            pollingTestButton.Click += (s, e) => NavigateToPage("polling");
            navPanel.Children.Add(pollingTestButton);

            panel.Children.Add(navPanel);
            this.Children.Add(panel);
        }

        private void NavigateToPage(string page)
        {
            // Find parent Frame to navigate
            var parent = this.Parent;
            while (parent != null && !(parent is Frame))
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
            }
        }
    }
}
