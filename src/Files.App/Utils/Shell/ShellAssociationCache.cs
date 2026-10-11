// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;
using System.Collections.Concurrent;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Files.App.Utils.Shell
{
	/// <summary>
	/// Per-extension answers to "can the shell make a thumbnail?" and "is the icon specific to each file?".
	/// </summary>
	public static class ShellAssociationCache
	{
		private const string ThumbnailProviderId = "{E357FCCD-A995-4576-B01F-234630154E96}";
		private const string ExtractImageId = "{BB2E617C-0920-11D1-9A0B-00C04FC2D6C1}";

		// Types whose icon is embedded in the file itself rather than shared by the extension
		private static readonly string[] _perFileIconExtensions = [".ico", ".cur", ".ani", ".scr", ".msc", ".appref-ms", ".lnk", ".url", ".library-ms"];

		private static readonly ConcurrentDictionary<string, bool> _thumbnailProviders = new(StringComparer.OrdinalIgnoreCase);
		private static readonly ConcurrentDictionary<string, bool> _perFileIcons = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// False only when no thumbnail or image extractor is registered; lookup failures answer true.
		/// </summary>
		public static bool HasThumbnailProvider(string? extension)
		{
			if (string.IsNullOrEmpty(extension))
				return false;

			return _thumbnailProviders.GetOrAdd(extension, static ext =>
				QueryAssociation(ext, ASSOCSTR.ASSOCSTR_SHELLEXTENSION, ThumbnailProviderId) is not QueryResult.Missing ||
				QueryAssociation(ext, ASSOCSTR.ASSOCSTR_SHELLEXTENSION, ExtractImageId) is not QueryResult.Missing);
		}

		/// <summary>
		/// True when each file can carry its own icon (executables, shortcuts, icon handlers or a "%1" default icon).
		/// </summary>
		public static bool HasPerFileIcon(string? extension)
		{
			if (string.IsNullOrEmpty(extension))
				return false;

			return _perFileIcons.GetOrAdd(extension, static ext =>
				_perFileIconExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase) ||
				FileExtensionHelpers.IsExecutableFile(ext) ||
				QueryAssociation(ext, ASSOCSTR.ASSOCSTR_SHELLEXTENSION, "IconHandler") is QueryResult.Found or QueryResult.Failed ||
				QueryAssociation(ext, ASSOCSTR.ASSOCSTR_DEFAULTICON, null, value => value.Contains("%1", StringComparison.Ordinal)) is QueryResult.Found or QueryResult.Failed);
		}

		private enum QueryResult { Found, Missing, Failed }

		private static unsafe QueryResult QueryAssociation(string extension, ASSOCSTR kind, string? extra, Func<string, bool>? accept = null)
		{
			try
			{
				const int capacity = 520;
				var buffer = stackalloc char[capacity];
				var length = (uint)capacity;
				HRESULT hr;
				fixed (char* pszAssoc = extension)
				fixed (char* pszExtra = extra)
					hr = PInvoke.AssocQueryString(ASSOCF.ASSOCF_NONE, kind, new PCWSTR(pszAssoc), new PCWSTR(pszExtra), new PWSTR(buffer), &length);

				if (hr.Succeeded)
					return accept is null || accept(new string(buffer)) ? QueryResult.Found : QueryResult.Missing;

				// ERROR_NO_ASSOCIATION and ERROR_FILE_NOT_FOUND mean the handler is not registered
				return hr.Value is unchecked((int)0x80070483) or unchecked((int)0x80070002) ? QueryResult.Missing : QueryResult.Failed;
			}
			catch
			{
				return QueryResult.Failed;
			}
		}
	}
}
