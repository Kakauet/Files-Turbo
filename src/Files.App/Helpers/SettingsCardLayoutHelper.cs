// Copyright (c) Files Community
// Licensed under the MIT License.

using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT;

namespace Files.App.Helpers;

internal static class SettingsCardLayoutHelper
{
	[DynamicWindowsRuntimeCast(typeof(Panel))]
	[DynamicWindowsRuntimeCast(typeof(UIElement))]
	internal static void Prepare(object? content)
	{
		if (content is UIElement { Visibility: Visibility.Collapsed })
			return;

		switch (content)
		{
			case SettingsCard card:
				card.Loading += Card_Loading;
				break;
			case Panel panel:
				foreach (var child in panel.Children)
					Prepare(child);
				break;
		}
	}

	private static void Card_Loading(FrameworkElement sender, object args)
	{
		sender.Loading -= Card_Loading;
		if (sender is SettingsCard card)
			PrepareCard(card);
	}

	[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
	private static void PrepareCard(SettingsCard card)
	{
		card.ApplyTemplate();
		if (VisualTreeHelper.GetChildrenCount(card) == 0 ||
			VisualTreeHelper.GetChild(card, 0) is not FrameworkElement root || root.ActualWidth > 0)
			return;

		var triggers = VisualStateManager.GetVisualStateGroups(root)
			.SelectMany(group => group.States)
			.SelectMany(state => state.StateTriggers)
			.OfType<ControlSizeTrigger>()
			.Where(trigger => trigger.CanTrigger && ReferenceEquals(trigger.TargetElement, root) &&
				trigger.ReadLocalValue(ControlSizeTrigger.CanTriggerProperty) == DependencyProperty.UnsetValue)
			.ToArray();
		if (triggers.Length == 0)
			return;

		// Zero is not the offered width: avoid wrapping before the first arrange.
		foreach (var trigger in triggers)
			trigger.CanTrigger = false;

		void Restore()
		{
			root.SizeChanged -= RestoreSizeTriggers;
			root.Unloaded -= Root_Unloaded;
			foreach (var trigger in triggers)
				trigger.ClearValue(ControlSizeTrigger.CanTriggerProperty);
		}

		void RestoreSizeTriggers(object sender, SizeChangedEventArgs args)
		{
			if (root.ActualWidth <= 0)
				return;
			Restore();
		}
		void Root_Unloaded(object sender, RoutedEventArgs args) => Restore();

		root.SizeChanged += RestoreSizeTriggers;
		root.Unloaded += Root_Unloaded;
	}
}
