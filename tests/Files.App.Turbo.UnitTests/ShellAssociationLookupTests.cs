// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Shell;

namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class ShellAssociationLookupTests
	{
		/// <summary>A registry stand-in: class keys per extension and the handlers and icons registered on each key.</summary>
		private sealed class FakeAssociations : IAssociationSource
		{
			public Dictionary<string, AssociationAnswer> Api { get; } = new(StringComparer.OrdinalIgnoreCase);
			public Dictionary<string, List<string>> ClassKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
			public HashSet<string> Handlers { get; } = new(StringComparer.OrdinalIgnoreCase);
			public Dictionary<string, string> Icons { get; } = new(StringComparer.OrdinalIgnoreCase);
			public string? ApiIcon { get; set; }

			public AssociationAnswer QueryShellExtension(string extension, string handler)
				=> Api.GetValueOrDefault($"{extension}|{handler}", AssociationAnswer.Missing);

			public AssociationAnswer QueryDefaultIcon(string extension, Func<string, bool> accept)
				=> ApiIcon is null ? AssociationAnswer.Missing : accept(ApiIcon) ? AssociationAnswer.Found : AssociationAnswer.Missing;

			public IEnumerable<string> GetClassKeys(string extension) => ClassKeys.GetValueOrDefault(extension, [extension]);

			public bool HasShellExtension(string classKey, string handler) => Handlers.Contains($"{classKey}|{handler}");

			public string? GetDefaultIcon(string classKey) => Icons.GetValueOrDefault(classKey);
		}

		[TestMethod]
		public void Thumbnail_handler_of_the_perceived_type_counts_when_AssocQueryString_misses_it()
		{
			// .psd on a machine with Photoshop: only SystemFileAssociations\image registers the photo thumbnail provider
			var source = new FakeAssociations();
			source.ClassKeys[".psd"] = ["Photoshop.Image.26", ".psd", @"SystemFileAssociations\.psd", @"SystemFileAssociations\image"];
			source.Handlers.Add($@"SystemFileAssociations\image|{ShellAssociationLookup.ThumbnailProviderId}");

			Assert.IsTrue(ShellAssociationLookup.HasThumbnailProvider(source, ".psd"));
		}

		[TestMethod]
		public void Thumbnail_handler_of_the_user_choice_ProgID_counts()
		{
			var source = new FakeAssociations();
			source.ClassKeys[".foo"] = ["UserApp.Foo", ".foo", "Vendor.Foo"];
			source.Handlers.Add($"UserApp.Foo|{ShellAssociationLookup.ExtractImageId}");

			Assert.IsTrue(ShellAssociationLookup.HasThumbnailProvider(source, ".foo"));
		}

		[TestMethod]
		public void No_handler_anywhere_means_no_thumbnail()
		{
			var source = new FakeAssociations();
			source.ClassKeys[".txt"] = [".txt", "txtfile", @"SystemFileAssociations\text"];

			Assert.IsFalse(ShellAssociationLookup.HasThumbnailProvider(source, ".txt"));
		}

		[TestMethod]
		public void A_failed_lookup_assumes_a_thumbnail_can_exist()
		{
			var source = new FakeAssociations();
			source.Api[$".bin|{ShellAssociationLookup.ThumbnailProviderId}"] = AssociationAnswer.Failed;

			Assert.IsTrue(ShellAssociationLookup.HasThumbnailProvider(source, ".bin"));
		}

		[TestMethod]
		public void Per_file_icon_from_an_icon_handler_on_the_default_ProgID()
		{
			// .sln: Visual Studio's launcher registers an icon handler that AssocQueryString does not report
			var source = new FakeAssociations();
			source.ClassKeys[".sln"] = [".sln", "VisualStudio.Launcher.sln"];
			source.Handlers.Add($"VisualStudio.Launcher.sln|{ShellAssociationLookup.IconHandler}");

			Assert.IsTrue(ShellAssociationLookup.HasPerFileIcon(source, ".sln"));
		}

		[TestMethod]
		public void Per_file_icon_from_a_percent_one_default_icon()
		{
			var source = new FakeAssociations();
			source.ClassKeys[".abc"] = [".abc", "Abc.File"];
			source.Icons["Abc.File"] = "%1";
			Assert.IsTrue(ShellAssociationLookup.HasPerFileIcon(source, ".abc"));

			var shared = new FakeAssociations { ApiIcon = @"C:\app.exe,0" };
			shared.Icons[".def"] = @"C:\app.exe,1";
			Assert.IsFalse(ShellAssociationLookup.HasPerFileIcon(shared, ".def"));
		}
	}
}
