// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.FileTags;

namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class FileTagsOperationSyncTests
	{
		private sealed class FakeTarget : IFileTagsSyncTarget
		{
			public readonly Dictionary<string, (ulong? Frn, string[] Tags)> Entries = new(StringComparer.OrdinalIgnoreCase);
			public readonly HashSet<string> Folders = new(StringComparer.OrdinalIgnoreCase);
			public readonly List<string> FrnReads = [];
			public readonly List<(string Path, string[] Tags)> FolderWrites = [];
			public bool ThrowOnFrn { get; set; }

			public bool HasEntry(string path) => Entries.ContainsKey(path);

			public string[] GetTags(string path) => Entries.TryGetValue(path, out var entry) ? entry.Tags : [];

			public void SetTags(string path, ulong? frn, string[] tags)
			{
				if (tags.Length == 0)
					Entries.Remove(path);
				else
					Entries[path] = (frn, tags);
			}

			public void MoveEntry(string oldPath, ulong? frn, string newPath)
			{
				if (!Entries.Remove(oldPath, out var entry))
					return;
				Entries[newPath] = (frn ?? entry.Frn, entry.Tags);
			}

			public IReadOnlyList<(string Path, string[] Tags)> GetAllUnderPath(string folderPath)
				=> Entries.Where(e => e.Key.StartsWith(folderPath + "\\", StringComparison.OrdinalIgnoreCase)).Select(e => (e.Key, e.Value.Tags)).ToList();

			public ulong? GetFrn(string path)
			{
				FrnReads.Add(path);
				if (ThrowOnFrn)
					throw new IOException("locked");
				return (ulong)path.Length;
			}

			public bool IsFolder(string path) => Folders.Contains(path);

			public void WriteFolderTags(string path, string[] tags) => FolderWrites.Add((path, tags));
		}

		private static readonly string[] Red = ["red"];

		[TestMethod]
		public void Copy_of_untagged_file_does_not_open_the_destination()
		{
			var target = new FakeTarget();
			FileTagsOperationSync.Apply(target, FileTagsOperation.Copy, @"C:\a\x.txt", @"C:\b\x.txt", childrenNotProcessed: false);

			Assert.AreEqual(0, target.FrnReads.Count);
			Assert.AreEqual(0, target.Entries.Count);
			Assert.AreEqual(0, target.FolderWrites.Count);
		}

		[TestMethod]
		public void Copy_of_tagged_file_copies_tags_with_the_new_frn()
		{
			var target = new FakeTarget();
			target.Entries[@"C:\a\x.txt"] = (1, Red);
			FileTagsOperationSync.Apply(target, FileTagsOperation.Copy, @"C:\a\x.txt", @"C:\b\x.txt", childrenNotProcessed: false);

			CollectionAssert.AreEqual(new[] { @"C:\b\x.txt" }, target.FrnReads);
			CollectionAssert.AreEqual(Red, target.Entries[@"C:\b\x.txt"].Tags);
			Assert.AreEqual((ulong)@"C:\b\x.txt".Length, target.Entries[@"C:\b\x.txt"].Frn);
			Assert.IsTrue(target.Entries.ContainsKey(@"C:\a\x.txt"), "The source keeps its tags");
		}

		[TestMethod]
		public void Copy_of_tagged_folder_writes_the_folder_tag()
		{
			var target = new FakeTarget();
			target.Entries[@"C:\a\dir"] = (1, Red);
			target.Folders.Add(@"C:\b\dir");
			FileTagsOperationSync.Apply(target, FileTagsOperation.Copy, @"C:\a\dir", @"C:\b\dir", childrenNotProcessed: false);

			Assert.AreEqual(1, target.FolderWrites.Count);
			Assert.AreEqual(@"C:\b\dir", target.FolderWrites[0].Path);
			CollectionAssert.AreEqual(Red, target.FolderWrites[0].Tags);
		}

		[TestMethod]
		public void Untagged_copy_over_a_tagged_item_clears_its_tags()
		{
			var target = new FakeTarget();
			target.Entries[@"C:\b\x.txt"] = (7, Red);
			FileTagsOperationSync.Apply(target, FileTagsOperation.Copy, @"C:\a\x.txt", @"C:\b\x.txt", childrenNotProcessed: false);

			Assert.IsFalse(target.Entries.ContainsKey(@"C:\b\x.txt"));
		}

		[TestMethod]
		public void Move_and_rename_of_untagged_items_do_not_open_the_destination()
		{
			var target = new FakeTarget();
			FileTagsOperationSync.Apply(target, FileTagsOperation.Move, @"C:\a\x.txt", @"C:\b\x.txt", childrenNotProcessed: false);
			FileTagsOperationSync.Apply(target, FileTagsOperation.Rename, @"C:\a\y.txt", @"C:\a\z.txt", childrenNotProcessed: false);
			FileTagsOperationSync.Apply(target, FileTagsOperation.Delete, @"C:\a\w.txt", @"C:\$Recycle.Bin\S-1\$R1.txt", childrenNotProcessed: false);

			Assert.AreEqual(0, target.FrnReads.Count);
			Assert.AreEqual(0, target.Entries.Count);
		}

		[TestMethod]
		public void Move_rename_and_recycle_carry_tags_to_the_new_path()
		{
			var target = new FakeTarget();
			target.Entries[@"C:\a\x.txt"] = (1, Red);
			FileTagsOperationSync.Apply(target, FileTagsOperation.Move, @"C:\a\x.txt", @"C:\b\x.txt", childrenNotProcessed: false);
			FileTagsOperationSync.Apply(target, FileTagsOperation.Rename, @"C:\b\x.txt", @"C:\b\renamed.txt", childrenNotProcessed: false);
			FileTagsOperationSync.Apply(target, FileTagsOperation.Delete, @"C:\b\renamed.txt", @"C:\$Recycle.Bin\S-1\$R1.txt", childrenNotProcessed: false);

			Assert.AreEqual(1, target.Entries.Count);
			CollectionAssert.AreEqual(Red, target.Entries[@"C:\$Recycle.Bin\S-1\$R1.txt"].Tags);
			Assert.AreEqual(3, target.FrnReads.Count);
		}

		[TestMethod]
		public void Permanent_delete_removes_tags_of_the_item_and_its_unprocessed_children()
		{
			var target = new FakeTarget();
			target.Entries[@"C:\a\dir"] = (1, Red);
			target.Entries[@"C:\a\dir\child.txt"] = (2, Red);
			target.Entries[@"C:\a\other.txt"] = (3, Red);
			FileTagsOperationSync.Apply(target, FileTagsOperation.Delete, @"C:\a\dir", null, childrenNotProcessed: true);

			CollectionAssert.AreEquivalent(new[] { @"C:\a\other.txt" }, target.Entries.Keys.ToArray());
		}

		[TestMethod]
		public void Unprocessed_children_follow_a_folder_copy_and_move()
		{
			var copy = new FakeTarget();
			copy.Entries[@"C:\a\dir\child.txt"] = (2, Red);
			FileTagsOperationSync.Apply(copy, FileTagsOperation.Copy, @"C:\a\dir", @"C:\b\dir", childrenNotProcessed: true);
			Assert.IsTrue(copy.Entries.ContainsKey(@"C:\a\dir\child.txt"));
			CollectionAssert.AreEqual(Red, copy.Entries[@"C:\b\dir\child.txt"].Tags);

			var move = new FakeTarget();
			move.Entries[@"C:\a\dir\child.txt"] = (2, Red);
			FileTagsOperationSync.Apply(move, FileTagsOperation.Move, @"C:\a\dir", @"C:\b\dir", childrenNotProcessed: true);
			CollectionAssert.AreEquivalent(new[] { @"C:\b\dir\child.txt" }, move.Entries.Keys.ToArray());
		}

		[TestMethod]
		public void A_failing_frn_read_is_reported_and_does_not_escape()
		{
			var target = new FakeTarget { ThrowOnFrn = true };
			target.Entries[@"C:\a\x.txt"] = (1, Red);
			var errors = new List<Exception>();
			FileTagsOperationSync.Apply(target, FileTagsOperation.Copy, @"C:\a\x.txt", @"C:\b\x.txt", childrenNotProcessed: false, errors.Add);

			Assert.AreEqual(1, errors.Count);
		}
	}
}
