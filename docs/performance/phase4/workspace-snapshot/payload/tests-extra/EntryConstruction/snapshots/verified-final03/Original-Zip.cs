using System.Diagnostics.CodeAnalysis;
using IO = EntryConstruction.Contracts.ProbeIO;
using FileExtensionHelpers = Files.Shared.Helpers.Original.FileExtensionHelpers;
namespace EntryConstruction.Generated;
public static class OriginalZip
{
    public static bool IsZipPath([NotNullWhen(true)] string? path, bool includeRoot = true)
		{
			if (path is null || !FileExtensionHelpers.IsBrowsableZipFile(path, out var ext))
			{
				return false;
			}
			var marker = path.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
			if (marker is -1)
			{
				return false;
			}
			marker += ext.Length;
			// If IO.Path.Exists returns true, it is not a zip path but a normal directory path that contains ".zip".
			return (marker == path.Length && includeRoot && !IO.Path.Exists(path + "\\"))
				|| (marker < path.Length && path[marker] is '\\' && !IO.Path.Exists(path));
		}
}