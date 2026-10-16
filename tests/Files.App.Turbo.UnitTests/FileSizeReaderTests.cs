// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Operations;
using System.IO;

namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class FileSizeReaderTests
	{
		[TestMethod]
		public void TryGetSize_reads_a_file_that_another_handle_locks_exclusively()
		{
			var path = Path.Combine(Path.GetTempPath(), $"turbo-size-{Guid.NewGuid():N}.bin");
			File.WriteAllBytes(path, new byte[1234]);
			try
			{
				// Opening the file would fail here; a delete running beside the size count must not meet such a handle either
				using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
				{
					Assert.IsTrue(FileSizeReader.TryGetSize(path, out var size));
					Assert.AreEqual(1234, size);
				}

				File.Delete(path);
				Assert.IsFalse(File.Exists(path));
			}
			finally
			{
				File.Delete(path);
			}
		}

		[TestMethod]
		public void TryGetSize_returns_false_for_missing_files_and_folders()
		{
			Assert.IsFalse(FileSizeReader.TryGetSize(Path.Combine(Path.GetTempPath(), $"turbo-missing-{Guid.NewGuid():N}.bin"), out var size));
			Assert.AreEqual(0, size);
			Assert.IsFalse(FileSizeReader.TryGetSize(Path.GetTempPath(), out _));
		}
	}
}
