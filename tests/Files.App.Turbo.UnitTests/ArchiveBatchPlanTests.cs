// Copyright (c) Files Community
// Licensed under the MIT License.
using Files.App.Utils.Storage.Operations;
namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class ArchiveBatchPlanTests
	{
		[TestMethod]
		public void PublicationRenameOnlyMatchesOwnedZipTemporaryNameShape()
		{
			string destination = @"C:\out\a.zip";
			string suffix = Guid.NewGuid().ToString("N");
			Assert.IsTrue(ArchiveBatchPlan.IsPublicationRename(destination + ".tmp-" + suffix, destination));
			Assert.IsFalse(ArchiveBatchPlan.IsPublicationRename(destination + ".tmp-invalid", destination));
			Assert.IsFalse(ArchiveBatchPlan.IsPublicationRename(@"C:\other\a.zip.tmp-" + suffix, destination));
			Assert.IsFalse(ArchiveBatchPlan.IsPublicationRename(@"C:\out\a.7z.tmp-" + suffix, @"C:\out\a.7z"));
			Assert.IsFalse(ArchiveBatchPlan.IsPublicationRename(destination, destination));
		}
		[TestMethod, Timeout(10000)]
		public async Task ParallelReadChecksKeepSelectionOrderAndUseAtMostTwoReaders()
		{
			string[] paths = ["b", "a", "d", "c"];
			using var entered = new CountdownEvent(2);
			int active = 0, maximum = 0, started = 0;
			var result = await Task.Run(() => ArchiveBatchPlan.ScanInOrder(paths, path =>
			{
				int current = Interlocked.Increment(ref active), observed;
				do { observed = Volatile.Read(ref maximum); }
				while (current > observed && Interlocked.CompareExchange(ref maximum, current, observed) != observed);
				try
				{
					if (Interlocked.Increment(ref started) <= 2)
					{
						entered.Signal();
						if (!entered.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
					}
					Thread.Sleep(path == "b" ? 40 : 10);
					return path;
				}
				finally { Interlocked.Decrement(ref active); }
			}, true));
			CollectionAssert.AreEqual(paths, result);
			Assert.AreEqual(2, maximum);
		}

		[TestMethod]
		public void IneligibleSelectionsKeepSerialScanOrder()
		{
			var visited = new List<string>();
			string[] paths = ["b", "a"];
			var result = ArchiveBatchPlan.ScanInOrder(paths, path => { visited.Add(path); return path; }, false);
			CollectionAssert.AreEqual(paths, result);
			CollectionAssert.AreEqual(paths, visited);
		}
		[TestMethod]
		public void SiblingsKeepTheirDirectoryNamesUnderOneCommonRoot()
		{
			Assert.IsTrue(ArchiveBatchPlan.TryGetCommonRootLength([@"C:\root\a\", @"C:\root\b", @"C:\root\file.txt"], out int length));
			Assert.AreEqual(@"C:\root\".Length, length);
		}
		[TestMethod]
		public void SingleOrDifferentParentSelectionsRetainExistingEntryConstruction()
		{
			Assert.IsFalse(ArchiveBatchPlan.TryGetCommonRootLength([], out _));
			Assert.IsFalse(ArchiveBatchPlan.TryGetCommonRootLength([@"C:\root\a"], out _));
			Assert.IsFalse(ArchiveBatchPlan.TryGetCommonRootLength([@"C:\root\a", @"D:\root\b"], out _));
			Assert.IsFalse(ArchiveBatchPlan.TryGetCommonRootLength([@"C:\root\a", @"C:\other\b"], out _));
		}
	}
}