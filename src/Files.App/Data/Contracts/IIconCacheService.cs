// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Media;

namespace Files.App.Data.Contracts
{
	internal interface IIconCacheService
	{
		Task<ShellImageData?> GetIconDataAsync(string? extension, bool isFolder, uint size);

		/// <summary>
		/// Returns the generic type icon without waiting, or <see langword="null"/> when it has not been loaded yet.
		/// </summary>
		ImageSource? TryGetIconImage(string? extension, bool isFolder, uint size);

		Task<ImageSource?> GetIconImageAsync(string? extension, bool isFolder, uint size);

		/// <summary>
		/// Loads the generic icons of the given types in parallel.
		/// </summary>
		Task PreloadAsync(IEnumerable<(string? Extension, bool IsFolder)> types, uint size, CancellationToken cancellationToken = default);

		void Clear();
	}
}
