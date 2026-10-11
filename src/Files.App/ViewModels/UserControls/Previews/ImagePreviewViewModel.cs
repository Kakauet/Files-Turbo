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
		private int decodeWidth = 512;
		private int decodeHeight = 512;
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
			decodeWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
			decodeHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
			previewLayoutReady.TrySetResult();
		}

		public override async Task<List<FileProperty>> LoadPreviewAndDetailsAsync()
		{
			var token = LoadCancelledTokenSource.Token;
			await previewLayoutReady.Task.WaitAsync(token);
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
