// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;
using Microsoft.Win32;
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
		// Types whose icon is embedded in the file itself rather than shared by the extension
		private static readonly string[] _perFileIconExtensions = [".ico", ".cur", ".ani", ".scr", ".msc", ".appref-ms", ".lnk", ".url", ".library-ms"];

		private static readonly ConcurrentDictionary<string, bool> _thumbnailProviders = new(StringComparer.OrdinalIgnoreCase);
		private static readonly ConcurrentDictionary<string, bool> _perFileIcons = new(StringComparer.OrdinalIgnoreCase);

		internal static bool AreImageTraitsCached(string? extension)
			=> string.IsNullOrEmpty(extension) || _thumbnailProviders.ContainsKey(extension) && _perFileIcons.ContainsKey(extension);

		/// <summary>
		/// False only when no thumbnail or image extractor is registered; lookup failures answer true.
		/// </summary>
		public static bool HasThumbnailProvider(string? extension)
		{
			if (string.IsNullOrEmpty(extension))
				return false;

			return _thumbnailProviders.GetOrAdd(extension, static ext =>
				ShellAssociationLookup.HasThumbnailProvider(RegistryAssociationSource.Instance, ext));
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
				ShellAssociationLookup.HasPerFileIcon(RegistryAssociationSource.Instance, ext));
		}

		private sealed class RegistryAssociationSource : IAssociationSource
		{
			public static readonly RegistryAssociationSource Instance = new();

			public AssociationAnswer QueryShellExtension(string extension, string handler)
				=> QueryAssociation(extension, ASSOCSTR.ASSOCSTR_SHELLEXTENSION, handler);

			public AssociationAnswer QueryDefaultIcon(string extension, Func<string, bool> accept)
				=> QueryAssociation(extension, ASSOCSTR.ASSOCSTR_DEFAULTICON, null, accept);

			public IEnumerable<string> GetClassKeys(string extension)
			{
				var keys = new List<string>(5);
				try
				{
					using (var userChoice = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice"))
					{
						if (userChoice?.GetValue("ProgId") is string { Length: > 0 } userProgId)
							keys.Add(userProgId);
					}

					keys.Add(extension);
					using (var extensionKey = Registry.ClassesRoot.OpenSubKey(extension))
					{
						if (extensionKey?.GetValue(null) is string { Length: > 0 } progId)
							keys.Add(progId);
						keys.Add($@"SystemFileAssociations\{extension}");
						if (extensionKey?.GetValue("PerceivedType") is string { Length: > 0 } perceivedType)
							keys.Add($@"SystemFileAssociations\{perceivedType}");
					}
				}
				catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
				{
				}

				return keys;
			}

			public bool HasShellExtension(string classKey, string handler)
			{
				try
				{
					using var key = Registry.ClassesRoot.OpenSubKey($@"{classKey}\ShellEx\{handler}");
					return key is not null;
				}
				catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
				{
					return false;
				}
			}

			public string? GetDefaultIcon(string classKey)
			{
				try
				{
					using var key = Registry.ClassesRoot.OpenSubKey($@"{classKey}\DefaultIcon");
					return key?.GetValue(null) as string;
				}
				catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
				{
					return null;
				}
			}

			private static unsafe AssociationAnswer QueryAssociation(string extension, ASSOCSTR kind, string? extra, Func<string, bool>? accept = null)
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
						return accept is null || accept(new string(buffer)) ? AssociationAnswer.Found : AssociationAnswer.Missing;

					// ERROR_NO_ASSOCIATION and ERROR_FILE_NOT_FOUND mean the handler is not registered
					return hr.Value is unchecked((int)0x80070483) or unchecked((int)0x80070002) ? AssociationAnswer.Missing : AssociationAnswer.Failed;
				}
				catch
				{
					return AssociationAnswer.Failed;
				}
			}
		}
	}
}
