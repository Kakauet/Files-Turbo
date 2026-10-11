// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// A shell icon or thumbnail as premultiplied BGRA pixels (top-down), or PNG bytes when only an encoded image exists.
	/// </summary>
	public sealed class ShellImageData
	{
		private ShellImageData(byte[]? pixels, int width, int height, byte[]? encoded)
		{
			Pixels = pixels;
			Width = width;
			Height = height;
			Encoded = encoded;
		}

		public byte[]? Pixels { get; }

		public int Width { get; }

		public int Height { get; }

		public byte[]? Encoded { get; }

		/// <summary>
		/// Approximate memory held by the decoded image.
		/// </summary>
		public long ByteCount => Pixels?.LongLength ?? (long)Math.Max(1, Width) * Math.Max(1, Height) * 4;

		public static ShellImageData FromEncoded(byte[] encoded)
			=> new(null, 0, 0, encoded);

		public static ShellImageData FromPixels(byte[] pixels, int width, int height)
			=> new(pixels, width, height, null);

		/// <summary>
		/// Copies a 32bpp DIB section into premultiplied BGRA, scaled down so its longer side fits <paramref name="maxSide"/>.
		/// </summary>
		/// <returns><see langword="null"/> when the bitmap is not a 32bpp DIB section.</returns>
		public static unsafe ShellImageData? FromHBitmap(HBITMAP hBitmap, int maxSide)
		{
			DIBSECTION section = default;
			if (PInvoke.GetObject(hBitmap, sizeof(DIBSECTION), &section) != sizeof(DIBSECTION))
				return null;

			var bitmap = section.dsBm;
			if (bitmap.bmBits is null || bitmap.bmBitsPixel != 32 || bitmap.bmWidth <= 0 || bitmap.bmHeight <= 0)
				return null;

			int width = bitmap.bmWidth, height = bitmap.bmHeight, stride = bitmap.bmWidthBytes;
			var bottomUp = section.dsBmih.biHeight > 0;
			var source = new byte[width * height * 4];
			fixed (byte* destination = source)
			{
				for (var y = 0; y < height; y++)
				{
					var row = (byte*)bitmap.bmBits + (bottomUp ? height - 1 - y : y) * stride;
					Buffer.MemoryCopy(row, destination + y * width * 4, width * 4, width * 4);
				}
			}

			NormalizeAlpha(source);
			if (Math.Max(width, height) <= maxSide || maxSide <= 0)
				return new(source, width, height, null);

			var scale = maxSide / (double)Math.Max(width, height);
			var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
			var targetHeight = Math.Max(1, (int)Math.Round(height * scale));
			return new(Downscale(source, width, height, targetWidth, targetHeight), targetWidth, targetHeight, null);
		}

		/// <summary>
		/// Converts a GDI+ bitmap (any format) into premultiplied BGRA scaled to <paramref name="maxSide"/>.
		/// </summary>
		public static unsafe ShellImageData? FromGdiBitmap(System.Drawing.Bitmap bitmap, int maxSide)
		{
			int width = bitmap.Width, height = bitmap.Height;
			if (width <= 0 || height <= 0)
				return null;

			var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
			var source = new byte[width * height * 4];
			try
			{
				fixed (byte* destination = source)
				{
					for (var y = 0; y < height; y++)
						Buffer.MemoryCopy((byte*)data.Scan0 + y * data.Stride, destination + y * width * 4, width * 4, width * 4);
				}
			}
			finally
			{
				bitmap.UnlockBits(data);
			}

			if (Math.Max(width, height) <= maxSide || maxSide <= 0)
				return new(source, width, height, null);

			var scale = maxSide / (double)Math.Max(width, height);
			var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
			var targetHeight = Math.Max(1, (int)Math.Round(height * scale));
			return new(Downscale(source, width, height, targetWidth, targetHeight), targetWidth, targetHeight, null);
		}

		/// <summary>
		/// Decodes PNG data into pixels off the UI thread, keeping the encoded form when decoding fails.
		/// </summary>
		public static ShellImageData FromEncodedPixels(byte[] encoded, int maxSide)
		{
			try
			{
				using var stream = new System.IO.MemoryStream(encoded);
				using var bitmap = new System.Drawing.Bitmap(stream);
				return FromGdiBitmap(bitmap, maxSide) ?? FromEncoded(encoded);
			}
			catch (Exception)
			{
				return FromEncoded(encoded);
			}
		}

		// Opaque bitmaps report zero alpha; straight-alpha ones have a channel above alpha somewhere.
		internal static void NormalizeAlpha(byte[] bgra)
		{
			var anyAlpha = false;
			var straight = false;
			for (var i = 0; i < bgra.Length; i += 4)
			{
				var a = bgra[i + 3];
				if (a != 0)
					anyAlpha = true;
				if (bgra[i] > a || bgra[i + 1] > a || bgra[i + 2] > a)
					straight = true;
			}

			if (!anyAlpha)
			{
				for (var i = 3; i < bgra.Length; i += 4)
					bgra[i] = 255;
				return;
			}

			if (!straight)
				return;

			for (var i = 0; i < bgra.Length; i += 4)
			{
				var a = bgra[i + 3];
				if (a == 255)
					continue;
				bgra[i] = (byte)((bgra[i] * a + 127) / 255);
				bgra[i + 1] = (byte)((bgra[i + 1] * a + 127) / 255);
				bgra[i + 2] = (byte)((bgra[i + 2] * a + 127) / 255);
			}
		}

		// Area-average resampling; valid on premultiplied pixels.
		internal static byte[] Downscale(byte[] source, int width, int height, int targetWidth, int targetHeight)
		{
			var result = new byte[targetWidth * targetHeight * 4];
			var xRatio = width / (double)targetWidth;
			var yRatio = height / (double)targetHeight;
			Span<double> sum = stackalloc double[4];
			for (var ty = 0; ty < targetHeight; ty++)
			{
				var y0 = ty * yRatio;
				var y1 = y0 + yRatio;
				for (var tx = 0; tx < targetWidth; tx++)
				{
					var x0 = tx * xRatio;
					var x1 = x0 + xRatio;
					sum.Clear();
					var area = 0d;
					for (var sy = (int)y0; sy < Math.Min(height, (int)Math.Ceiling(y1)); sy++)
					{
						var wy = Math.Min(y1, sy + 1) - Math.Max(y0, sy);
						for (var sx = (int)x0; sx < Math.Min(width, (int)Math.Ceiling(x1)); sx++)
						{
							var w = wy * (Math.Min(x1, sx + 1) - Math.Max(x0, sx));
							var index = (sy * width + sx) * 4;
							sum[0] += source[index] * w;
							sum[1] += source[index + 1] * w;
							sum[2] += source[index + 2] * w;
							sum[3] += source[index + 3] * w;
							area += w;
						}
					}

					var target = (ty * targetWidth + tx) * 4;
					for (var c = 0; c < 4; c++)
						result[target + c] = (byte)Math.Clamp((int)Math.Round(sum[c] / area), 0, 255);
				}
			}

			return result;
		}

		/// <summary>
		/// Creates the image synchronously on the UI thread; encoded data has no synchronous form.
		/// </summary>
		public ImageSource? TryCreateImage()
		{
			if (Pixels is null)
				return null;

			var image = new WriteableBitmap(Width, Height);
			Pixels.CopyTo(image.PixelBuffer);
			image.Invalidate();
			return image;
		}

		/// <summary>
		/// Creates the image on the UI thread, decoding PNG data when no pixels are available.
		/// </summary>
		public async Task<ImageSource?> CreateImageAsync(int decodeSize)
		{
			if (TryCreateImage() is { } image)
				return image;

			return Encoded is null ? null : await Encoded.ToBitmapAsync(decodeSize, DecodePixelType.Physical, preserveAspectRatio: true, useSynchronousSmallThumbnail: true);
		}
	}
}
