// Bitmap methods extracted literally; IconCacheService is linked directly from production.
#nullable enable
using System.Buffers.Binary;
using System.IO;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
namespace Files.App.Helpers { internal static class BitmapHelper {
public static async Task<BitmapImage?> ToBitmapAsync(this byte[]? data, int decodeSize = -1, DecodePixelType decodePixelType = DecodePixelType.Logical,
			bool preserveAspectRatio = false, bool useSynchronousSmallThumbnail = false)
		{
			if (data is null)
			{
				return null;
			}

			try
			{
				using var ms = new MemoryStream(data);
				var image = new BitmapImage();
				SetDecodeSize(image, data, decodeSize, preserveAspectRatio);
				image.DecodePixelType = decodePixelType;
				if (ShouldUseSynchronousSmallThumbnail(data, decodeSize, decodePixelType, preserveAspectRatio, useSynchronousSmallThumbnail))
					image.SetSource(ms.AsRandomAccessStream());
				else
					await image.SetSourceAsync(ms.AsRandomAccessStream());
				return image;
			}
			catch (Exception)
			{
				return null;
			}
		}
private static bool ShouldUseSynchronousSmallThumbnail(byte[] data, int decodeSize, DecodePixelType decodePixelType,
			bool preserveAspectRatio, bool useSynchronousSmallThumbnail)
		{
			// Avoid a dispatcher handoff only for bounded Shell thumbnails; larger images stay asynchronous.
			return useSynchronousSmallThumbnail && preserveAspectRatio && decodePixelType == DecodePixelType.Physical &&
				decodeSize > 0 && decodeSize <= 256 && data.Length <= 256 * 1024 &&
				TryGetPngDimensions(data, out var width, out var height) && width <= 256 && height <= 256;
		}
private static void SetDecodeSize(BitmapImage image, byte[] data, int decodeSize, bool preserveAspectRatio)
		{
			if (decodeSize <= 0)
				return;

			if (!preserveAspectRatio)
			{
				image.DecodePixelWidth = decodeSize;
				image.DecodePixelHeight = decodeSize;
				return;
			}

			// Shell bitmaps are encoded as PNG; constrain only their longer side to preserve proportions.
			if (TryGetPngDimensions(data, out var width, out var height) && height > width)
			{
				image.DecodePixelHeight = decodeSize;
				return;
			}

			image.DecodePixelWidth = decodeSize;
		}
private static bool TryGetPngDimensions(byte[] data, out uint width, out uint height)
		{
			width = height = 0;
			ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
			if (data.Length < 33 || !data.AsSpan(0, 8).SequenceEqual(pngSignature) ||
				BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8, 4)) != 13 || !data.AsSpan(12, 4).SequenceEqual("IHDR"u8))
			{
				return false;
			}

			width = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16, 4));
			height = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20, 4));
			return width > 0 && width <= int.MaxValue && height > 0 && height <= int.MaxValue;
		}
} }
internal static class IconCacheBuildStamp {
    public const string IconSourceSha256 = "031F15B06BFFC8EFF096A7211C264F8F7589FD07C4D85E95295FD2B95033BDC9";
    public const string BitmapSourceSha256 = "F15297CB0E4C16052D7C40A2DCDD25C46C840B6B6EA181F6385B2E43FD9A8F82";
    public const string SourceRevision = "bf4390ee0a2eed6cece0e99d1a021ce7fd255574";
}
