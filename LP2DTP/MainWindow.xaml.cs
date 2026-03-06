using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LP2DTP.Pages;

namespace LP2DTP
{
	public sealed class MainWindow : Window
	{
		private NavigationView _navView;
		private Frame _contentFrame;

		public MainWindow()
		{
			_navView = new NavigationView
			{
				IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
				IsSettingsVisible = true,
				OpenPaneLength = 160,
				CompactModeThresholdWidth = 640,
				ExpandedModeThresholdWidth = 800
			};

			_navView.MenuItems.Add(new NavigationViewItem { Content = "Home", Tag = "home" });
			_navView.MenuItems.Add(new NavigationViewItem { Content = "VISA Items", Tag = "visaitems" });
			_navView.MenuItems.Add(new NavigationViewItem { Content = "Modbus Items", Tag = "modbusitems" });
			_navView.MenuItems.Add(new NavigationViewItem { Content = "Polling Test", Tag = "polling" });

			_contentFrame = new Frame();
			_navView.Content = _contentFrame;

			// Handle both menu items and settings
			_navView.SelectionChanged += NavView_SelectionChanged;
			_navView.ItemInvoked += NavView_ItemInvoked;

			this.Content = _navView;
			_navView.SelectedItem = _navView.MenuItems[0];
		}

		private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
		{
			if (args.SelectedItemContainer is NavigationViewItem item)
			{
				string tag = item.Tag?.ToString() ?? "";
				NavigateToTag(tag);
			}
		}

		private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
		{
			// Handle Settings item specially
			if (args.IsSettingsInvoked)
			{
				_contentFrame.Content = new SettingsPage();
			}
		}

		private void NavigateToTag(string tag)
		{
			if (tag == "home")
			{
				_contentFrame.Content = new HomePage();
			}
			else if (tag == "visaitems")
			{
				_contentFrame.Content = new VisaItemListContent();
			}
			else if (tag == "modbusitems")
			{
				_contentFrame.Content = new ModbusItemListContent();
			}
			else if (tag == "polling")
			{
				_contentFrame.Content = new PollingTestPage();
			}
		}

		public void NavigateToVisaItems()
		{
			_navView.SelectedItem = _navView.MenuItems[1];
		}

		public void NavigateToModbusItems()
		{
			_navView.SelectedItem = _navView.MenuItems[2];
		}

		public void NavigateToPollingTest()
		{
			_navView.SelectedItem = _navView.MenuItems[3];
		}
	}
}
