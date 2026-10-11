// Generated verbatim from NavigationToolbarViewModel methods. Do not edit.
#nullable enable
namespace Files.App.ViewModels.UserControls { public partial class NavigationToolbarViewModel {
public async Task<bool> HandleItemNavigationAsync(string path)
		{
			var shellPage = ContentPageContext.ShellPage;
			var request = pathNavigationCoordinator.Begin(shellPage);
			if (_isDisposed || shellPage is null)
				return true;

			var shellViewModel = shellPage.ShellViewModel
				?? throw new InvalidOperationException("The current shell page does not have a view model.");

			bool IsCurrentRequest() => !_isDisposed
				&& pathNavigationCoordinator.IsCurrent(request, ContentPageContext.ShellPage)
				&& ReferenceEquals(shellViewModel, shellPage.ShellViewModel);

			var isFtp = FtpHelpers.IsFtpPath(path);
			var normalizedInput = NormalizePathInput(path, isFtp);
			if (string.IsNullOrWhiteSpace(normalizedInput))
				return true;

			if (normalizedInput.Equals(shellViewModel.WorkingDirectory) &&
				!shellViewModel.IsWorkingDirectoryUpdatePending &&
				shellPage.CurrentPageType != typeof(HomePage) &&
				!shellViewModel.IsSearchResults)
				return true;

			if (normalizedInput.Equals("Home", StringComparison.OrdinalIgnoreCase) ||
				normalizedInput.Equals(Strings.Home.GetLocalizedResource(), StringComparison.OrdinalIgnoreCase))
			{
				SavePathToHistory("Home");
				shellPage.NavigateHome();
			}
			else if (normalizedInput.Equals("ReleaseNotes", StringComparison.OrdinalIgnoreCase) ||
				normalizedInput.Equals(Strings.ReleaseNotes.GetLocalizedResource(), StringComparison.OrdinalIgnoreCase))
			{
				SavePathToHistory("ReleaseNotes");
				shellPage.NavigateToReleaseNotes();
			}
			else if (normalizedInput.Equals("Settings", StringComparison.OrdinalIgnoreCase) ||
				normalizedInput.Equals(Strings.Settings.GetLocalizedResource(), StringComparison.OrdinalIgnoreCase))
			{
				SavePathToHistory("Settings");
				shellPage.NavigateToSettings();
			}
			else
			{
				normalizedInput = StorageFileExtensions.GetResolvedPath(normalizedInput, isFtp);
				if (normalizedInput.Equals(shellViewModel.WorkingDirectory) &&
					!shellViewModel.IsWorkingDirectoryUpdatePending &&
					shellPage.CurrentPageType != typeof(HomePage) && !shellViewModel.IsSearchResults)
					return true;

				var item = await FilesystemTasks.Wrap(() => DriveHelpers.GetRootFromPathAsync(normalizedInput));
				if (!IsCurrentRequest())
					return true;

				var resFolder = await FilesystemTasks.Wrap(() => StorageFileExtensions.DangerousGetFolderWithPathFromPathAsync(normalizedInput, item));
				if (!IsCurrentRequest())
					return true;

				if (resFolder || FolderHelpers.CheckFolderAccessWithWin32(normalizedInput))
				{
					var matchingDrive = drivesViewModel.Drives.Cast<DriveItem>().FirstOrDefault(x => PathNormalization.NormalizePath(normalizedInput).StartsWith(
						PathNormalization.NormalizePath(x.GetRequiredPath()),
						StringComparison.Ordinal));
					if (matchingDrive is not null && matchingDrive.Type == Data.Items.DriveType.CDRom && matchingDrive.MaxSpace == ByteSizeLib.ByteSize.FromBytes(0))
					{
						var drivePath = matchingDrive.GetRequiredPath();
						bool ejectButton = await DialogDisplayHelper.ShowDialogAsync(Strings.InsertDiscDialogTitle.GetLocalizedResource(), string.Format(Strings.InsertDiscDialogText.GetLocalizedResource(), drivePath), Strings.InsertDiscDialog_OpenDriveButton.GetLocalizedResource(), Strings.Close.GetLocalizedResource());
						if (!IsCurrentRequest())
							return true;
						if (ejectButton)
							DriveHelpers.EjectDeviceAsync(drivePath);
						return true;
					}

					var pathToNavigate = resFolder.Result?.Path ?? normalizedInput;
					SavePathToHistory(pathToNavigate);
					shellPage.NavigateToPath(pathToNavigate);
				}
				else if (isFtp)
				{
					SavePathToHistory(normalizedInput);
					shellPage.NavigateToPath(normalizedInput);
				}
				else // Not a folder or inaccessible
				{
					var resFile = await FilesystemTasks.Wrap(() => StorageFileExtensions.DangerousGetFileWithPathFromPathAsync(normalizedInput, item));
					if (!IsCurrentRequest())
						return true;

					if (resFile)
					{
						var storageFile = resFile.Result
							?? throw new InvalidOperationException("A successful file lookup did not return a storage file.");
						await Win32Helper.InvokeWin32ComponentAsync(storageFile.Path, shellPage);
						if (!IsCurrentRequest())
							return true;
					}
					else // Not a file or not accessible
					{
						shellViewModel = shellPage.ShellViewModel
							?? throw new InvalidOperationException("The current shell page does not have a view model.");

						var workingDir =
							string.IsNullOrEmpty(shellViewModel.WorkingDirectory) ||
							shellPage.CurrentPageType == typeof(HomePage)
								? Constants.UserEnvironmentPaths.HomePath
								: shellViewModel.WorkingDirectory;
						var pathText = PathText
							?? throw new InvalidOperationException("The navigation path has not been initialized.");

						var launched = await LaunchApplicationFromPath(pathText, workingDir);
						if (!IsCurrentRequest() || launched)
							return true;

						var isValid = false;
						try
						{
							isValid = await Windows.System.Launcher.LaunchUriAsync(new Uri(pathText));
						}
						catch (Exception ex) when (ex is UriFormatException || ex is ArgumentException)
						{
						}
						if (!IsCurrentRequest())
							return true;

						if (!isValid)
						{
							await DialogDisplayHelper.ShowDialogAsync(Strings.InvalidItemDialogTitle.GetLocalizedResource(),
								string.Format(Strings.InvalidItemDialogContent.GetLocalizedResource(), Environment.NewLine, resFolder.ErrorCode.ToString()));
							if (!IsCurrentRequest())
								return true;
							return false;
						}
					}
				}
			}

			if (!IsCurrentRequest())
				return true;
			PathControlDisplayText = shellViewModel.WorkingDirectory;
			return true;
		}

public async Task CheckPathInputAsync(string currentInput, string? currentSelectedPath, IShellPage shellPage)
		{
			var request = pathNavigationCoordinator.Begin(shellPage);
			if (_isDisposed)
				return;
			var shellViewModel = shellPage.ShellViewModel
				?? throw new InvalidOperationException("The shell page does not have a view model.");
			bool IsCurrentRequest() => !_isDisposed
				&& pathNavigationCoordinator.IsCurrent(request, ContentPageContext.ShellPage)
				&& ReferenceEquals(shellViewModel, shellPage.ShellViewModel);
			if (!IsCurrentRequest())
				return;

			if (currentInput.StartsWith('>'))
			{
				var code = currentInput.Substring(1).Trim();
				var command = Commands[code];

				if (command == Commands.None)
					await DialogDisplayHelper.ShowDialogAsync(Strings.InvalidCommand.GetLocalizedResource(),
						string.Format(Strings.InvalidCommandContent.GetLocalizedResource(), code));
				else if (!command.IsExecutable)
					await DialogDisplayHelper.ShowDialogAsync(Strings.CommandNotExecutable.GetLocalizedResource(),
						string.Format(Strings.CommandNotExecutableContent.GetLocalizedResource(), command.Code));
				else
					await command.ExecuteAsync();

				return;
			}

			var isFtp = FtpHelpers.IsFtpPath(currentInput);

			var normalizedInput = NormalizePathInput(currentInput, isFtp);

			if (string.IsNullOrWhiteSpace(normalizedInput))
				return;

			if (normalizedInput != shellViewModel.WorkingDirectory || shellViewModel.IsWorkingDirectoryUpdatePending || shellPage.CurrentPageType == typeof(HomePage))
			{
				if (normalizedInput.Equals("Home", StringComparison.OrdinalIgnoreCase) || normalizedInput.Equals(Strings.Home.GetLocalizedResource(), StringComparison.OrdinalIgnoreCase))
				{
					SavePathToHistory("Home");
					shellPage.NavigateHome();
				}
				else if (normalizedInput.Equals("ReleaseNotes", StringComparison.OrdinalIgnoreCase) || normalizedInput.Equals(Strings.ReleaseNotes.GetLocalizedResource(), StringComparison.OrdinalIgnoreCase))
				{
					SavePathToHistory("ReleaseNotes");
					shellPage.NavigateToReleaseNotes();
				}
				else if (normalizedInput.Equals("Settings", StringComparison.OrdinalIgnoreCase) || normalizedInput.Equals(Strings.Settings.GetLocalizedResource(), StringComparison.OrdinalIgnoreCase))
				{
					SavePathToHistory("Settings");
					shellPage.NavigateToSettings();
				}
				else
				{
					normalizedInput = StorageFileExtensions.GetResolvedPath(normalizedInput, isFtp);
					if (normalizedInput == shellViewModel.WorkingDirectory && !shellViewModel.IsWorkingDirectoryUpdatePending && shellPage.CurrentPageType != typeof(HomePage))
						return;

					var item = await FilesystemTasks.WrapNullable(() => DriveHelpers.GetRootFromPathAsync(normalizedInput));
					if (!IsCurrentRequest())
						return;

					var resFolder = await FilesystemTasks.Wrap(() => StorageFileExtensions.DangerousGetFolderWithPathFromPathAsync(normalizedInput, item));
					if (!IsCurrentRequest())
						return;
					if (resFolder || FolderHelpers.CheckFolderAccessWithWin32(normalizedInput))
					{
						var matchingDrive = drivesViewModel.Drives.Cast<DriveItem>().FirstOrDefault(x => PathNormalization.NormalizePath(normalizedInput).StartsWith(
							PathNormalization.NormalizePath(x.GetRequiredPath()),
							StringComparison.Ordinal));
						if (matchingDrive is not null && matchingDrive.Type == Data.Items.DriveType.CDRom && matchingDrive.MaxSpace == ByteSizeLib.ByteSize.FromBytes(0))
						{
							var drivePath = matchingDrive.GetRequiredPath();
							bool ejectButton = await DialogDisplayHelper.ShowDialogAsync(Strings.InsertDiscDialogTitle.GetLocalizedResource(), string.Format(Strings.InsertDiscDialogText.GetLocalizedResource(), drivePath), Strings.InsertDiscDialog_OpenDriveButton.GetLocalizedResource(), Strings.Close.GetLocalizedResource());
							if (!IsCurrentRequest())
								return;
							if (ejectButton)
								DriveHelpers.EjectDeviceAsync(drivePath);
							return;
						}
						var pathToNavigate = resFolder.Result?.Path ?? normalizedInput;
						SavePathToHistory(pathToNavigate);
						shellPage.NavigateToPath(pathToNavigate);
					}
					else if (isFtp)
					{
						SavePathToHistory(normalizedInput);
						shellPage.NavigateToPath(normalizedInput);
					}
					else // Not a folder or inaccessible
					{
						var resFile = await FilesystemTasks.Wrap(() => StorageFileExtensions.DangerousGetFileWithPathFromPathAsync(normalizedInput, item));
						if (!IsCurrentRequest())
							return;
						if (resFile)
						{
							var storageFile = resFile.Result
								?? throw new InvalidOperationException("A successful file lookup did not return a storage file.");
							await Win32Helper.InvokeWin32ComponentAsync(storageFile.Path, shellPage);
							if (!IsCurrentRequest())
								return;
						}
						else // Not a file or not accessible
						{
							var workingDir =
								string.IsNullOrEmpty(shellViewModel.WorkingDirectory) ||
								shellPage.CurrentPageType == typeof(HomePage) ?
									Constants.UserEnvironmentPaths.HomePath :
									shellViewModel.WorkingDirectory;

							var launched = await LaunchApplicationFromPath(currentInput, workingDir);
							if (!IsCurrentRequest() || launched)
								return;

							try
							{
								var launchedUri = await Windows.System.Launcher.LaunchUriAsync(new Uri(currentInput));
								if (!IsCurrentRequest())
									return;
								if (!launchedUri)
									await DialogDisplayHelper.ShowDialogAsync(Strings.InvalidItemDialogTitle.GetLocalizedResource(),
										string.Format(Strings.InvalidItemDialogContent.GetLocalizedResource(), Environment.NewLine, resFolder.ErrorCode.ToString()));
							}
							catch (Exception ex) when (ex is UriFormatException || ex is ArgumentException)
							{
								if (!IsCurrentRequest())
									return;
								await DialogDisplayHelper.ShowDialogAsync(Strings.InvalidItemDialogTitle.GetLocalizedResource(),
									string.Format(Strings.InvalidItemDialogContent.GetLocalizedResource(), Environment.NewLine, resFolder.ErrorCode.ToString()));
							}
						}
					}
				}

				if (IsCurrentRequest())
					PathControlDisplayText = shellViewModel.WorkingDirectory;
			}
		}

private static string NormalizePathInput(string currentInput, bool isFtp)
		{
			if (currentInput.Contains('/') && !isFtp)
				currentInput = currentInput.Replace('/', '\\');

			currentInput = currentInput.Replace("\\\\", "\\", StringComparison.Ordinal);

			if (currentInput.StartsWith('\\') && !currentInput.StartsWith("\\\\", StringComparison.Ordinal))
				currentInput = currentInput.Insert(0, "\\");

			return currentInput;
		}
}}
