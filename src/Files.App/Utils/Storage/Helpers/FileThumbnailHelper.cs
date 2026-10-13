// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;
using System.IO;
using Windows.Storage.FileProperties;

namespace Files.App.Utils.Storage
{
	public static class FileThumbnailHelper
	{
		// The shell serves these from hand-tuned icon frames and cache buckets; in-between sizes can yield padded or regenerated results
		private static readonly uint[] _standardSizes = [16, 24, 32, 48, 64, 96, 128, 256];

		/// <summary>
		/// Returns icon or thumbnail for given file or folder
		/// </summary>
		public static async Task<byte[]?> GetIconAsync(string? path, uint requestedSize, bool isFolder, IconOptions iconOptions, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var scaledSize = requestedSize * App.AppModel.AppWindowDPI;

			// Snap up to a standard frame, or request the full physical size above the largest bucket.
			// Callers can opt out (sidebar) because snapping a small icon up makes the shell return a folder-style variant.
			var size = scaledSize > _standardSizes[^1]
				? (uint)Math.Ceiling(scaledSize)
				: iconOptions.HasFlag(IconOptions.SkipSizeSnapping)
					? Math.Max(1u, (uint)Math.Round(scaledSize))
					: _standardSizes.FirstOrDefault(s => s >= scaledSize, _standardSizes[^1]);

			if (!isFolder && !iconOptions.HasFlag(IconOptions.ReturnIconOnly) && !iconOptions.HasFlag(IconOptions.ReturnOnlyIfCached))
			{
				var extension = Path.GetExtension(path);

				//Restrict to only %windir%\fonts
				if (FileExtensionHelpers.IsFontFile(extension) && path is not null && PathHelpers.IsInSystemFontsFolder(path))
				{
					var winrtThumbnail = await FontFileHelper.GetWinRTThumbnailAsync(path, (uint)size);
					cancellationToken.ThrowIfCancellationRequested();
					if (winrtThumbnail is not null)
						return winrtThumbnail;

					if (!string.Equals(extension, ".fon", StringComparison.OrdinalIgnoreCase))
					{
						var fontThumbnail = await STATask.Run(() => FontFileHelper.GenerateFontThumbnail(path, (int)size), App.Logger);
						cancellationToken.ThrowIfCancellationRequested();
						if (fontThumbnail is not null)
							return fontThumbnail;
					}
				}
			}

			var resolvedPath = path is not null && path.StartsWith(@"\\?\", StringComparison.Ordinal)
				? MtpHelpers.ResolveMtpShellPath(path) ?? path
				: path;

			return await STATask.RunPooledUntilCompleted(() => Win32Helper.GetIcon(resolvedPath, (int)size, isFolder, iconOptions), App.Logger, cancellationToken);
		}

		/// <summary>
		/// Physical pixel size an icon of <paramref name="requestedSize"/> logical pixels is displayed at.
		/// </summary>
		public static int GetPhysicalSize(uint requestedSize)
			=> Math.Max(1, (int)Math.Ceiling(requestedSize * App.AppModel.AppWindowDPI));

		/// <summary>
		/// Returns an icon or thumbnail as pixels already scaled to the displayed physical size.
		/// </summary>
		public static Task<ShellImageData?> GetImageDataAsync(string? path, uint requestedSize, bool isFolder, IconOptions iconOptions,
			CancellationToken cancellationToken = default, STATaskPriority priority = STATaskPriority.Fast)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var scaledSize = requestedSize * App.AppModel.AppWindowDPI;
			var size = scaledSize > _standardSizes[^1]
				? (uint)Math.Ceiling(scaledSize)
				: _standardSizes.FirstOrDefault(s => s >= scaledSize, _standardSizes[^1]);
			var maxSide = GetPhysicalSize(requestedSize);
			var resolvedPath = path is not null && path.StartsWith(@"\\?\", StringComparison.Ordinal)
				? MtpHelpers.ResolveMtpShellPath(path) ?? path
				: path;

			return STATask.RunPooledUntilCompleted(() => Win32Helper.GetIconImageData(resolvedPath, (int)size, isFolder, iconOptions, maxSide),
				App.Logger, cancellationToken, priority);
		}

		/// <summary>
		/// Returns overlay for given file or folder
		/// /// </summary>
		/// <param name="path"></param>
		/// <param name="isFolder"></param>
		/// <returns></returns>
		public static async Task<byte[]?> GetIconOverlayAsync(string? path, uint requestedSize, bool isFolder, CancellationToken cancellationToken = default,
			STATaskPriority priority = STATaskPriority.Normal)
		{
			cancellationToken.ThrowIfCancellationRequested();
			// Overlays render at 32px in thumbnail layouts and 16px in details/columns; scale by DPI so the badge isn't upscaled at fractional scaling
			var overlaySize = (requestedSize >= 48 ? 32u : 16u) * App.AppModel.AppWindowDPI;
			return await STATask.RunPooledUntilCompleted(() => Win32Helper.GetIconOverlay(path, (int)overlaySize, isFolder), App.Logger, cancellationToken, priority);
		}

		[Obsolete]
		public static async Task<byte[]?> LoadIconFromPathAsync(string filePath, uint thumbnailSize, ThumbnailMode thumbnailMode, ThumbnailOptions thumbnailOptions, bool isFolder = false)
		{
			var result = await GetIconAsync(filePath, thumbnailSize, isFolder, IconOptions.None);
			return result;
		}
	}
}
