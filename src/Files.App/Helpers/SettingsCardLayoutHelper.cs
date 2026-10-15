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
	internal static void Prepare(object? content)
	{
		if (content is UIElement { Visibility: Visibility.Collapsed })
			return;

		switch (content)
		{
			case SettingsCard card:
				card.Loading += Card_Loading;
				break;
			case SettingsExpander expander:
				expander.Loading += Expander_Loading;
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

	private static void Expander_Loading(FrameworkElement sender, object args)
	{
		sender.Loading -= Expander_Loading;
		PrepareHeader(sender);
	}

	[DynamicWindowsRuntimeCast(typeof(Control))]
	private static void PrepareHeader(DependencyObject element)
	{
		if (element is SettingsCard card)
		{
			PrepareCard(card);
			return;
		}

		(element as Control)?.ApplyTemplate();
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
			PrepareHeader(VisualTreeHelper.GetChild(element, i));
	}

	[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
	private static void PrepareCard(SettingsCard card)
	{
		var probeCard = Stopwatch.GetTimestamp();
		card.ApplyTemplate();
		SettingsOpeningProbe.Stage("card-template-" + VisualTreeHelper.GetChildrenCount(card), probeCard);
		if (VisualTreeHelper.GetChildrenCount(card) == 0 ||
			VisualTreeHelper.GetChild(card, 0) is not FrameworkElement root || root.ActualWidth > 0)
			return;

		var triggers = VisualStateManager.GetVisualStateGroups(root)
			.SelectMany(group => group.States)
			.SelectMany(state => state.StateTriggers)
			.OfType<ControlSizeTrigger>()
			.Where(trigger => trigger.CanTrigger && ReferenceEquals(trigger.TargetElement, root))
			.ToArray();
		if (triggers.Length == 0)
			return;
		SettingsOpeningProbe.Stage("card-triggers-" + triggers.Length, probeCard);

		// Zero is not the offered width: avoid wrapping before the first arrange.
		foreach (var trigger in triggers)
			trigger.CanTrigger = false;

		void RestoreSizeTriggers(object sender, SizeChangedEventArgs args)
		{
			if (root.ActualWidth <= 0)
				return;

			root.SizeChanged -= RestoreSizeTriggers;
			foreach (var trigger in triggers)
				trigger.CanTrigger = true;
		}
		root.SizeChanged += RestoreSizeTriggers;
	}
}
