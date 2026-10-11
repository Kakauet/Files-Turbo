// Generated verbatim from the actual app method. Do not edit.
namespace Files.App.Views.Shells { public partial class BaseShellPage {
public async Task UpdatePathUIToWorkingDirectoryAsync(string? newWorkingDir, string? singleItemOverride = null)
		{
			var revision = Interlocked.Increment(ref pathUiRevision);
			if (isDisposed)
				return;
			if (string.IsNullOrWhiteSpace(singleItemOverride))
			{
				if (newWorkingDir is null)
					throw new InvalidOperationException("The working directory is not available for the path bar.");

				var components = await StorageFileExtensions.GetDirectoryPathComponentsWithDisplayNameAsync(newWorkingDir);

				if (isDisposed || revision != pathUiRevision)
					return;

				// Guard against a rare race where a native CollectionChanged subscriber (e.g. the
				// bound BreadcrumbBar) is in a torn-down state during navigation and throws NRE.
				try
				{
					ToolbarViewModel.PathComponents.Clear();
					foreach (var component in components)
					{
						if (isDisposed || revision != pathUiRevision)
							return;
						ToolbarViewModel.PathComponents.Add(component);
					}
				}
				catch (NullReferenceException)
				{
				}
			}
			else
			{
				try
				{
					// Clear the path UI
					ToolbarViewModel.PathComponents.Clear();
					if (isDisposed || revision != pathUiRevision)
						return;
					ToolbarViewModel.IsSingleItemOverride = true;
					if (isDisposed || revision != pathUiRevision)
						return;
					ToolbarViewModel.PathComponents.Add(
						new()
						{
							Path = null,
							Title = singleItemOverride,
							ChevronToolTip = string.Format(Strings.BreadcrumbBarChevronButtonToolTip.GetLocalizedResource(), singleItemOverride),
						});
				}
				catch (NullReferenceException)
				{
				}
			}
		}
}}
