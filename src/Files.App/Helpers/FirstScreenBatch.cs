// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Helpers
{
	internal static class FirstScreenBatch
	{
		internal static int GetCount(double width, double height, double itemWidth, double itemHeight)
		{
			if (!double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(itemWidth) ||
				!double.IsFinite(itemHeight) || width <= 0 || height <= 0 || itemWidth <= 0 || itemHeight <= 0)
				return 64;

			var columns = Math.Max(1, Math.Floor(width / itemWidth));
			var rows = Math.Ceiling(height / itemHeight) + 1;
			return (int)Math.Clamp(columns * rows, 64, 512);
		}
	}
}
