// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Files.App.UserControls
{
	public partial class ComboBoxEx : ComboBox
	{
		double _cachedWidth;

		protected override void OnDropDownOpened(object e)
		{
			Width = _cachedWidth;

			base.OnDropDownOpened(e);
		}

		protected override void OnDropDownClosed(object e)
		{
			Width = double.NaN;

			base.OnDropDownClosed(e);
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			var probeLayout = Stopwatch.GetTimestamp();
			var baseSize = base.MeasureOverride(availableSize);
			SettingsOpeningProbe.Stage("combo-" + Name, probeLayout);

			if (baseSize.Width != 64)
				_cachedWidth = baseSize.Width;

			return baseSize;
		}
	}
}
