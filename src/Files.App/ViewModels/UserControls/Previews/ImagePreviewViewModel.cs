// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.ViewModels.Properties;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;

namespace Files.App.ViewModels.Previews
{
	public sealed partial class ImagePreviewViewModel : BasePreviewModel
	{
		private ImageSource? imageSource;
		private readonly TaskCompletionSource previewLayoutReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
		// Used when the preview has no layout yet, e.g. while the Details tab hides it
		private const int DefaultDecodeSide = 1024;
		private static readonly TimeSpan LayoutWait = TimeSpan.FromMilliseconds(500);
		private volatile int decodeWidth = DefaultDecodeSide;
		private volatile int decodeHeight = DefaultDecodeSide;
		public ImageSource? ImageSource
		{
			get => imageSource;
			private set => SetProperty(ref imageSource, value);
		}

		public ImagePreviewViewModel(ListedItem item)
			: base(item)
		{
			ImageSource = item.FileImage;
		}

		public void SetPreviewSize(double width, double height, double scale)
		{
			if (previewLayoutReady.Task.IsCompleted || width < 1 || height < 1 || scale <= 0)
				return;

			decodeWidth = (int)Math.Ceiling(width * scale);
			decodeHeight = (int)Math.Ceiling(height * scale);
			previewLayoutReady.TrySetResult();
		}

		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var token = LoadCancelledTokenSource.Token;
			try
			{
				// A hidden preview never gets a size; details must still load
				await previewLayoutReady.Task.WaitAsync(LayoutWait, token);
			}
			catch (TimeoutException)
			{
			}
			using IRandomAccessStream stream = await PreviewFile.OpenAsync(FileAccessMode.Read);
			var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
			var scale = Math.Min(1, Math.Min(decodeWidth / (double)Math.Max(1, decoder.OrientedPixelWidth),
				decodeHeight / (double)Math.Max(1, decoder.OrientedPixelHeight)));
			var targetWidth = Math.Max(1, (int)Math.Ceiling(decoder.OrientedPixelWidth * scale));
			stream.Seek(0);

			await MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
			{
				if (token.IsCancellationRequested)
					return;
				BitmapImage bitmap = new() { DecodePixelWidth = targetWidth, DecodePixelType = DecodePixelType.Physical };
				await bitmap.SetSourceAsync(stream);
				if (!token.IsCancellationRequested)
					ImageSource = bitmap;
			});

			return [];
		}
	}
}
