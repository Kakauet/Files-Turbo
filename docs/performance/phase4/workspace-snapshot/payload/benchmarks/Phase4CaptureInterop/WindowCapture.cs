using System;
using System.Drawing;
using System.Drawing.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Storage.Xps;

public static class Phase4WindowCapture
{
	public static Bitmap Capture(IntPtr window, Size windowSize, Rectangle crop)
	{
		if (!new Rectangle(Point.Empty, windowSize).Contains(crop))
			throw new ArgumentOutOfRangeException(nameof(crop));
		using var surface = new Bitmap(windowSize.Width, windowSize.Height);
		using var graphics = Graphics.FromImage(surface);
		var context = graphics.GetHdc();
		try
		{
			if (!PInvoke.PrintWindow((HWND)window, (HDC)context, (PRINT_WINDOW_FLAGS)2))
				throw new InvalidOperationException("Experimental window surface capture failed.");
		}
		finally
		{
			graphics.ReleaseHdc(context);
		}
		return surface.Clone(crop, PixelFormat.Format32bppArgb);
	}
}
