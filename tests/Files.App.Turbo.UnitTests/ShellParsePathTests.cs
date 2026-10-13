// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Shell;

namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class ShellParsePathTests
	{
		[TestMethod]
		[DataRow(@"C:\datos\dato 0001.bin", @"C:\datos", "dato 0001.bin")]
		[DataRow(@"C:\dato.bin", @"C:\", "dato.bin")]
		[DataRow(@"C:\carpeta\sub carpeta", @"C:\carpeta", "sub carpeta")]
		[DataRow(@"\\servidor\recurso\dato.bin", @"\\servidor\recurso", "dato.bin")]
		[DataRow(@"D:\名前\ñ.txt", @"D:\名前", "ñ.txt")]
		public void SplitsPlainPaths(string path, string folder, string name)
		{
			Assert.IsTrue(ShellParsePath.TrySplit(path, out var actualFolder, out var actualName));
			Assert.AreEqual(folder, actualFolder);
			Assert.AreEqual(name, actualName);
		}

		[TestMethod]
		[DataRow(null)]
		[DataRow("")]
		[DataRow(@"C:\")]
		[DataRow(@"C:\carpeta\")]
		[DataRow(@"dato.bin")]
		[DataRow(@"carpeta\dato.bin")]
		[DataRow(@"C:dato.bin")]
		[DataRow(@"C:\datos\dato.bin:secundario")]
		[DataRow(@"\\?\C:\datos\dato.bin")]
		[DataRow(@"\\.\PhysicalDrive0")]
		[DataRow(@"\\servidor\recurso")]
		[DataRow(@"::{645FF040-5081-101B-9F08-00AA002F954E}")]
		[DataRow(@"C:/datos/dato.bin")]
		public void KeepsTheFullParseForOtherPaths(string? path)
		{
			Assert.IsFalse(ShellParsePath.TrySplit(path, out _, out _));
		}
	}
}
