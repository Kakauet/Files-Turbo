// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Shell
{
	internal static class ShellParsePath
	{
		/// <summary>
		/// Splits a plain file system path into its folder and the name to parse below it; other paths (roots,
		/// device and shell namespace paths, alternate streams, trailing separators) keep the full parse.
		/// </summary>
		public static bool TrySplit(string? path, out string folder, out string name)
		{
			folder = name = string.Empty;
			if (string.IsNullOrEmpty(path) || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal) ||
				path.StartsWith("::", StringComparison.Ordinal) || path.Contains('/') || !System.IO.Path.IsPathFullyQualified(path))
				return false;

			var separator = path.LastIndexOf('\\');
			// A colon after the drive designator is an alternate stream
			if (separator < 2 || separator == path.Length - 1 || path.IndexOf(':', 2) >= 0)
				return false;

			var parent = separator == 2 && path[1] == ':' ? path[..3] : path[..separator];
			// "\\server\share" is the shortest folder a UNC name can be parsed below
			if (path.StartsWith(@"\\", StringComparison.Ordinal) && parent.AsSpan(2).IndexOf('\\') < 0)
				return false;

			folder = parent;
			name = path[(separator + 1)..];
			return true;
		}
	}
}
