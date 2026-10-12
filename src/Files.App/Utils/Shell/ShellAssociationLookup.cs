// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Shell
{
	internal enum AssociationAnswer
	{
		Found,
		Missing,
		Failed,
	}

	/// <summary>Read-only access to the file association data the shell resolves for an extension.</summary>
	internal interface IAssociationSource
	{
		/// <summary>AssocQueryString(ASSOCSTR_SHELLEXTENSION) for <paramref name="handler"/>.</summary>
		AssociationAnswer QueryShellExtension(string extension, string handler);

		/// <summary>AssocQueryString(ASSOCSTR_DEFAULTICON), Found only when <paramref name="accept"/> accepts the value.</summary>
		AssociationAnswer QueryDefaultIcon(string extension, Func<string, bool> accept);

		/// <summary>
		/// The class keys the shell consults for the extension, in order: the user's choice ProgID, the extension,
		/// its default ProgID, SystemFileAssociations\extension and SystemFileAssociations\perceived type.
		/// </summary>
		IEnumerable<string> GetClassKeys(string extension);

		bool HasShellExtension(string classKey, string handler);

		string? GetDefaultIcon(string classKey);
	}

	/// <summary>
	/// Answers whether an extension can have a thumbnail or a per-file icon. AssocQueryString misses handlers
	/// registered only for a perceived type (SystemFileAssociations\image provides thumbnails for .psd, .tga, .exr…)
	/// or only on the default ProgID in some cases, so a "missing" answer is checked against the class keys.
	/// </summary>
	internal static class ShellAssociationLookup
	{
		public const string ThumbnailProviderId = "{E357FCCD-A995-4576-B01F-234630154E96}";
		public const string ExtractImageId = "{BB2E617C-0920-11D1-9A0B-00C04FC2D6C1}";
		public const string IconHandler = "IconHandler";

		/// <summary>False only when no thumbnail provider or image extractor is registered; lookup failures answer true.</summary>
		public static bool HasThumbnailProvider(IAssociationSource source, string extension)
			=> HasHandler(source, extension, ThumbnailProviderId) || HasHandler(source, extension, ExtractImageId);

		/// <summary>True when an icon handler or a "%1" default icon lets each file carry its own icon.</summary>
		public static bool HasPerFileIcon(IAssociationSource source, string extension)
		{
			if (HasHandler(source, extension, IconHandler))
				return true;

			return source.QueryDefaultIcon(extension, IsPerFileIcon) switch
			{
				AssociationAnswer.Found or AssociationAnswer.Failed => true,
				_ => source.GetClassKeys(extension).Any(key => source.GetDefaultIcon(key) is { } icon && IsPerFileIcon(icon)),
			};
		}

		private static bool HasHandler(IAssociationSource source, string extension, string handler)
			=> source.QueryShellExtension(extension, handler) switch
			{
				AssociationAnswer.Found or AssociationAnswer.Failed => true,
				_ => source.GetClassKeys(extension).Any(key => source.HasShellExtension(key, handler)),
			};

		private static bool IsPerFileIcon(string icon)
			=> icon.Contains("%1", StringComparison.Ordinal);
	}
}
