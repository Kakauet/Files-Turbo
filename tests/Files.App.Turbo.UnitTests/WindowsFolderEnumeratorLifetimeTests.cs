// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.Marshalling;
using Files.App.Storage;

namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class WindowsFolderEnumeratorLifetimeTests
	{
		private static readonly Guid HomeQuickAccessFolderId = new("3936e9e4-d92c-4eee-a85a-bc16d5ea0819");
		private static readonly Guid RecentFilesFolderId = new("679F85CB-0220-4080-B29B-5540CC05AAB6");

		[TestMethod]
		public void QuickAccessEnumeratorIsReleasedInsideFinally()
		{
			MethodInfo getItemsMethod = typeof(WindowsFolder).GetMethods(BindingFlags.Instance | BindingFlags.Public)
				.Single(method => method.Name == nameof(WindowsFolder.GetItemsAsync) && method.GetParameters().Length == 2);
			MethodInfo releaseMethod = typeof(ComObject).GetMethod("FinalRelease", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

			Assert.IsTrue(
				CallsMethodInsideFinally(getItemsMethod, releaseMethod),
				"WindowsFolder.GetItemsAsync must release its shell enumerator in a finally block. The Quick Access enumerator holds the destination-list lock until it is released.");
		}

		[TestMethod]
		public void RecentItemsEnumeratorIsReleasedInsideFinally()
		{
			string testDirectory = Path.GetDirectoryName(GetTestSourcePath())!;
			string servicePath = Path.GetFullPath(Path.Combine(testDirectory, "..", "..", "src", "Files.App", "Services", "Windows", "WindowsRecentItemsService.cs"));
			string source = File.ReadAllText(servicePath);
			int methodStart = source.IndexOf("private unsafe bool UpdateRecentItems(bool isFolder)", StringComparison.Ordinal);
			int methodEnd = source.IndexOf("private void Watcher_Changed", methodStart, StringComparison.Ordinal);

			Assert.IsTrue(methodStart >= 0 && methodEnd > methodStart, "Could not locate WindowsRecentItemsService.UpdateRecentItems.");
			string method = source[methodStart..methodEnd];
			List<string> finallyBodies = GetFinallyBodies(method);

			Assert.IsTrue(
				finallyBodies.Any(body => body.Contains("pEnumShellItems", StringComparison.Ordinal) && body.Contains("FinalRelease()", StringComparison.Ordinal)),
				"WindowsRecentItemsService.UpdateRecentItems must release its Quick Access shell enumerator in a finally block.");
		}

		[TestMethod]
		[Timeout(60_000)]
		public Task HomeQuickAccessEnumerationCompletesAcrossRepeatedCycles()
		{
			return EnumerateFolderAcrossRepeatedCycles(HomeQuickAccessFolderId);
		}

		[TestMethod]
		[Timeout(60_000)]
		public Task RecentFilesEnumerationCompletesAcrossRepeatedCycles()
		{
			return EnumerateFolderAcrossRepeatedCycles(RecentFilesFolderId);
		}

		private static async Task EnumerateFolderAcrossRepeatedCycles(Guid folderId)
		{
			using var quickAccessFolder = new WindowsFolder(folderId);
			const int cycles = 64;
			int completedCycles = 0;

			for (int cycle = 0; cycle < cycles; cycle++)
			{
				await foreach (var item in quickAccessFolder.GetItemsAsync())
				{
					_ = item;
				}

				completedCycles++;
			}

			Assert.AreEqual(cycles, completedCycles);
		}

		private static bool CallsMethodInsideFinally(MethodInfo method, MethodInfo target)
		{
			MethodBody? body = method.GetMethodBody();
			byte[] il = body?.GetILAsByteArray() ?? [];
			if (body is null)
				return false;

			for (int offset = 0; offset + sizeof(int) < il.Length; offset++)
			{
				if (il[offset] is not (0x28 or 0x6F))
					continue;

				int token = BitConverter.ToInt32(il, offset + 1);
				MethodBase? calledMethod;
				try
				{
					calledMethod = method.Module.ResolveMethod(token);
				}
				catch (ArgumentException)
				{
					continue;
				}

				if (calledMethod?.Module != target.Module || calledMethod.MetadataToken != target.MetadataToken)
					continue;

				return body.ExceptionHandlingClauses.Any(clause =>
					clause.Flags == ExceptionHandlingClauseOptions.Finally &&
					offset >= clause.HandlerOffset &&
					offset + 5 <= clause.HandlerOffset + clause.HandlerLength);
			}

			return false;
		}

		private static List<string> GetFinallyBodies(string source)
		{
			List<string> bodies = [];
			int searchFrom = 0;
			while ((searchFrom = source.IndexOf("finally", searchFrom, StringComparison.Ordinal)) >= 0)
			{
				int openingBrace = source.IndexOf('{', searchFrom + "finally".Length);
				if (openingBrace < 0)
					break;

				int depth = 1;
				int position = openingBrace + 1;
				for (; position < source.Length && depth > 0; position++)
				{
					if (source[position] == '{')
						depth++;
					else if (source[position] == '}')
						depth--;
				}

				if (depth == 0)
					bodies.Add(source[(openingBrace + 1)..(position - 1)]);

				searchFrom = position;
			}

			return bodies;
		}

		private static string GetTestSourcePath([CallerFilePath] string path = "") => path;
	}
}
