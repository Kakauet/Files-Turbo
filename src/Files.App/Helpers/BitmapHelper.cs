// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Buffers.Binary;
using System.IO;
using Windows.Foundation.Metadata;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Files.App.Helpers
{
	internal static class BitmapHelper
	{
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

		/// <summary>
		/// Rotates the image at the specified file path.
		/// </summary>
		/// <param name="filePath">The file path to the image.</param>
		/// <param name="rotation">The rotation direction.</param>
		/// <remarks>
		/// https://learn.microsoft.com/uwp/api/windows.graphics.imaging.bitmapdecoder?view=winrt-22000
		/// https://learn.microsoft.com/uwp/api/windows.graphics.imaging.bitmapencoder?view=winrt-22000
		/// </remarks>
		public static async Task RotateAsync(string filePath, BitmapRotation rotation)
		{
			try
			{
				if (string.IsNullOrEmpty(filePath))
				{
					return;
				}

				var file = await StorageHelpers.ToStorageItem<IStorageFile>(filePath);
				if (file is null)
				{
					return;
				}

				var fileStreamRes = await FilesystemTasks.Wrap(() => file.OpenAsync(FileAccessMode.ReadWrite).AsTask());
				using IRandomAccessStream? fileStream = fileStreamRes.Result;
				if (fileStream is null)
				{
					return;
				}

				BitmapDecoder decoder = await BitmapDecoder.CreateAsync(fileStream);
				using var memStream = new InMemoryRandomAccessStream();
				BitmapEncoder encoder = await BitmapEncoder.CreateForTranscodingAsync(memStream, decoder);

				for (int i = 0; i < decoder.FrameCount - 1; i++)
				{
					encoder.BitmapTransform.Rotation = rotation;
					await encoder.GoToNextFrameAsync();
				}

				encoder.BitmapTransform.Rotation = rotation;

				await encoder.FlushAsync();

				memStream.Seek(0);
				fileStream.Seek(0);
				fileStream.Size = 0;

				await RandomAccessStream.CopyAsync(memStream, fileStream);
			}
			catch (Exception ex)
			{
				var errorDialog = new ContentDialog()
				{
					Title = Strings.FailedToRotateImage.GetLocalizedResource(),
					Content = ex.Message,
					PrimaryButtonText = Strings.OK.GetLocalizedResource(),
				};

				if (ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 8))
					errorDialog.XamlRoot = MainWindow.Instance.Content.XamlRoot;

				await errorDialog.TryShowAsync();
			}
		}

		/// <summary>
		/// This function encodes a software bitmap with the specified encoder and saves it to a file
		/// </summary>
		/// <param name="softwareBitmap"></param>
		/// <param name="outputFile"></param>
		/// <param name="encoderId">The guid of the image encoder type</param>
		/// <returns></returns>
		public static async Task SaveSoftwareBitmapToFileAsync(SoftwareBitmap softwareBitmap, BaseStorageFile outputFile, Guid encoderId)
		{
			using IRandomAccessStream stream = await outputFile.OpenAsync(FileAccessMode.ReadWrite);
			// Create an encoder with the desired format
			BitmapEncoder encoder = await BitmapEncoder.CreateAsync(encoderId, stream);

			// Set the software bitmap
			encoder.SetSoftwareBitmap(softwareBitmap);

			try
			{
				await encoder.FlushAsync();
			}
			catch (Exception err)
			{
				const int WINCODEC_ERR_UNSUPPORTEDOPERATION = unchecked((int)0x88982F81);
				switch (err.HResult)
				{
					case WINCODEC_ERR_UNSUPPORTEDOPERATION:
						// If the encoder does not support writing a thumbnail, then try again
						// but disable thumbnail generation.
						encoder.IsThumbnailGenerated = false;
						break;

					default:
						throw;
				}
			}

			if (encoder.IsThumbnailGenerated == false)
			{
				await encoder.FlushAsync();
			}
		}
	}
}
