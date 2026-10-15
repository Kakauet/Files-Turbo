// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App.Utils.Storage.Operations
{
	internal static class ArchiveBatchPlan
	{
		public static bool IsPublicationRename(string source, string destination)
			=> Path.GetExtension(destination).Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
				source.StartsWith(destination + ".tmp-", StringComparison.OrdinalIgnoreCase) &&
				Guid.TryParseExact(source[(destination.Length + 5)..], "N", out _);
		public static T[] ScanInOrder<T>(IReadOnlyList<string> paths, Func<string, T> scan, bool parallel)
			=> parallel
				? paths.AsParallel().AsOrdered().WithDegreeOfParallelism(2).Select(scan).ToArray()
				: paths.Select(scan).ToArray();
		public static bool TryGetCommonRootLength(IReadOnlyList<string> sources, out int length)
		{
			length = 0;
			if (sources.Count < 2)
				return false;

			string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(sources[0])));
			if (parent is null)
				return false;
			for (int i = 1; i < sources.Count; i++)
			{
				if (!string.Equals(parent, Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(sources[i]))), StringComparison.Ordinal))
					return false;
			}
			length = parent.TrimEnd(Path.DirectorySeparatorChar).Length + 1;
			return true;
		}
	}
}
