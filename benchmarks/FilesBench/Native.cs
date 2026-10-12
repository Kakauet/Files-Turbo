using System.Drawing;
using System.Runtime.InteropServices;

namespace FilesBench;

internal static partial class Native
{
	[StructLayout(LayoutKind.Sequential)]
	public struct RECT { public int Left, Top, Right, Bottom; }

	[StructLayout(LayoutKind.Sequential)]
	public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

	[StructLayout(LayoutKind.Sequential)]
	public struct INPUT
	{
		public uint type;
		public KEYBDINPUT ki;
		private readonly long padding;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct KEYBDINPUT
	{
		public ushort wVk;
		public ushort wScan;
		public uint dwFlags;
		public uint time;
		public nint dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct BITMAPINFOHEADER
	{
		public int biSize, biWidth, biHeight;
		public short biPlanes, biBitCount;
		public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
	}

	public const uint INPUT_KEYBOARD = 1;
	public const uint KEYEVENTF_KEYUP = 0x0002;
	public const ushort VK_RETURN = 0x0D, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_SHIFT = 0x10, VK_ESCAPE = 0x1B, VK_DELETE = 0x2E, VK_F2 = 0x71, VK_F5 = 0x74, VK_F24 = 0x87;
	public const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
	public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
	public const int SW_RESTORE = 9;

	[DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
	[DllImport("user32.dll")] public static extern nint GetForegroundWindow();
	[DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int command);
	[DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
	[DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
	[DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
	[DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
	[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
	[DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
	[DllImport("user32.dll")] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
	[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out int pid);
	[DllImport("user32.dll")] public static extern nint GetDC(nint hwnd);
	[DllImport("user32.dll")] public static extern int ReleaseDC(nint hwnd, nint dc);
	[DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint hwnd);
	[DllImport("gdi32.dll")] public static extern nint CreateCompatibleDC(nint dc);
	[DllImport("gdi32.dll")] public static extern bool DeleteDC(nint dc);
	[DllImport("gdi32.dll")] public static extern nint SelectObject(nint dc, nint obj);
	[DllImport("gdi32.dll")] public static extern bool DeleteObject(nint obj);
	[DllImport("gdi32.dll")] public static extern bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sx, int sy, int rop);
	[DllImport("gdi32.dll")] public static extern nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER info, uint usage, out nint bits, nint section, uint offset);
	[DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint period);
	[DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint period);

	public static uint LastInputTick()
	{
		var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
		GetLastInputInfo(ref info);
		return info.dwTime;
	}

	public static Rectangle WindowRect(nint hwnd)
	{
		GetWindowRect(hwnd, out var r);
		return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
	}

	private static INPUT Key(ushort vk, bool up) => new()
	{
		type = INPUT_KEYBOARD,
		ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 },
	};

	/// <summary>Presses the keys in order and releases them in reverse order.</summary>
	public static void Chord(params ushort[] keys)
	{
		var inputs = keys.Select(k => Key(k, false)).Concat(keys.Reverse().Select(k => Key(k, true))).ToArray();
		SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
	}
}

/// <summary>
/// Copies a fixed screen rectangle into a reusable 32 bpp DIB. The copy reads the composed desktop,
/// so it observes what the user would see, not what an application has merely published.
/// </summary>
internal sealed unsafe class ScreenProbe : IDisposable
{
	private readonly nint screenDc;
	private readonly nint memoryDc;
	private readonly nint bitmap;
	private readonly nint previous;
	private readonly byte* bits;

	public ScreenProbe(Rectangle area)
	{
		Area = area;
		screenDc = Native.GetDC(0);
		memoryDc = Native.CreateCompatibleDC(screenDc);
		var header = new Native.BITMAPINFOHEADER { biSize = sizeof(Native.BITMAPINFOHEADER), biWidth = area.Width, biHeight = -area.Height, biPlanes = 1, biBitCount = 32 };
		bitmap = Native.CreateDIBSection(memoryDc, ref header, 0, out var pointer, 0, 0);
		if (bitmap == 0)
			throw new InvalidOperationException("CreateDIBSection failed.");
		bits = (byte*)pointer;
		previous = Native.SelectObject(memoryDc, bitmap);
	}

	public Rectangle Area { get; }

	public int Stride => Area.Width * 4;

	public ReadOnlySpan<byte> Pixels => new(bits, Area.Width * Area.Height * 4);

	public long Capture()
	{
		Native.BitBlt(memoryDc, 0, 0, Area.Width, Area.Height, screenDc, Area.X, Area.Y, Native.SRCCOPY);
		return Stopwatch.GetTimestamp();
	}

	public byte[] Snapshot() => Pixels.ToArray();

	/// <summary>Mean absolute per-channel difference inside <paramref name="region"/> (screen coordinates).</summary>
	public double Difference(byte[] reference, Rectangle region)
	{
		var local = Rectangle.Intersect(new Rectangle(region.X - Area.X, region.Y - Area.Y, region.Width, region.Height), new Rectangle(Point.Empty, Area.Size));
		if (local.IsEmpty)
			return double.MaxValue;
		long sum = 0;
		var pixels = Pixels;
		for (var y = local.Top; y < local.Bottom; y++)
		{
			var offset = y * Stride + local.Left * 4;
			for (var i = 0; i < local.Width * 4; i++)
			{
				if ((i & 3) == 3)
					continue;
				sum += Math.Abs(pixels[offset + i] - reference[offset + i]);
			}
		}
		return sum / (double)(local.Width * local.Height * 3);
	}

	/// <summary>Fraction of pixels in <paramref name="region"/> that differ from the region's most common colour.</summary>
	public double Ink(Rectangle region)
	{
		var local = Rectangle.Intersect(new Rectangle(region.X - Area.X, region.Y - Area.Y, region.Width, region.Height), new Rectangle(Point.Empty, Area.Size));
		if (local.IsEmpty)
			return 0;
		var pixels = Pixels;
		var background = MemoryMarshal.Read<uint>(pixels.Slice(local.Top * Stride + local.Left * 4)) & 0xFFFFFF;
		var different = 0;
		for (var y = local.Top; y < local.Bottom; y++)
			for (var x = local.Left; x < local.Right; x++)
				if ((MemoryMarshal.Read<uint>(pixels.Slice(y * Stride + x * 4)) & 0xFFFFFF) != background)
					different++;
		return different / (double)(local.Width * local.Height);
	}

	public void Save(string path)
	{
		using var image = new Bitmap(Area.Width, Area.Height, Stride, System.Drawing.Imaging.PixelFormat.Format32bppRgb, (nint)bits);
		image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
	}

	public void Dispose()
	{
		Native.SelectObject(memoryDc, previous);
		Native.DeleteObject(bitmap);
		Native.DeleteDC(memoryDc);
		Native.ReleaseDC(0, screenDc);
	}
}
