// Generated verbatim from the actual app method. Do not edit.
namespace Files.App.ViewModels { public partial class ShellViewModel {
public async Task SetWorkingDirectoryAsync(string? value)
		{
			var revision = Interlocked.Increment(ref workingDirectoryRevision);
			requestedWorkingDirectory = string.IsNullOrWhiteSpace(value) ? WorkingDirectory : value;
			if (isDisposed || string.IsNullOrWhiteSpace(value))
				return;
			
			if (value != WorkingDirectory)
				FilesAndFoldersFilter = null;
			if (isDisposed || revision != workingDirectoryRevision)
				return;

			var isLibrary = false;
			string? name = null;
			if (App.LibraryManager.TryGetLibrary(value, out var library))
			{
				isLibrary = true;
				name = library.Text;
			}

			WorkingDirectoryModified?.Invoke(this, new WorkingDirectoryModifiedEventArgs { Path = value, IsLibrary = isLibrary, Name = name });
			if (isDisposed || revision != workingDirectoryRevision)
				return;

			if (isLibrary || !Path.IsPathRooted(value))
				workingRoot = currentStorageFolder = null;
			else if (!Path.IsPathRooted(WorkingDirectory) || Path.GetPathRoot(WorkingDirectory) != Path.GetPathRoot(value))
			{
				var root = await FilesystemTasks.WrapNullable(() => DriveHelpers.GetRootFromPathAsync(value));
				if (isDisposed || revision != workingDirectoryRevision)
					return;
				workingRoot = root;
			}

			if (value == "Home" || value == "ReleaseNotes" || value == "Settings")
				currentStorageFolder = null;
			else
				_ = Task.Run(() => jumpListService.AddFolderAsync(value));

			WorkingDirectory = value;

			string? pathRoot = null;
			if (!FtpHelpers.IsFtpPath(WorkingDirectory))
			{
				pathRoot = Path.GetPathRoot(WorkingDirectory);
			}

			var gitDirectory = await Task.Run(() => GitHelpers.GetGitRepositoryPath(value, pathRoot));
			if (isDisposed || revision != workingDirectoryRevision)
				return;

			GitDirectory = gitDirectory;
			var headName = await GitHelpers.GetRepositoryHeadName(gitDirectory);
			if (isDisposed || revision != workingDirectoryRevision)
				return;

			IsValidGitDirectory = !string.IsNullOrEmpty(headName);

			_ = UpdateFolderThumbnailImageSource();

			OnPropertyChanged(nameof(WorkingDirectory));
			OnPropertyChanged(nameof(ShowFilterHeader));
		}
}}
