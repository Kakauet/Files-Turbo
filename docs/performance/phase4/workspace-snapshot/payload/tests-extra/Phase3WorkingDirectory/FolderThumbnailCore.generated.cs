// Generated verbatim from the actual app method. Do not edit.
namespace Files.App.ViewModels { public partial class ShellViewModel {
public async Task UpdateFolderThumbnailImageSource()
		{
			var revision = workingDirectoryRevision;
			var path = WorkingDirectory;
			if (isDisposed || IsWorkingDirectoryUpdatePending)
				return;
			var icon = await NavigationHelpers.GetIconForPathAsync(path);
			if (!isDisposed && revision == workingDirectoryRevision && WorkingDirectory == path)
				FolderThumbnailImageSource = icon;
		}
}}
