// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.App.Views.Settings
{
	public sealed partial class GeneralPage : Page
	{

		public GeneralPage()
		{
			var probeStart = Stopwatch.GetTimestamp();
			InitializeComponent();
			SettingsOpeningProbe.Stage("Views/Settings/GeneralPage.xaml.cs", probeStart);
		}

		private void RemoveStartupPage(object sender, RoutedEventArgs e)
		{
			if (sender is FrameworkElement { DataContext: PageOnStartupViewModel page })
				ViewModel.RemovePageCommand.Execute(page);
		}
	}
}
