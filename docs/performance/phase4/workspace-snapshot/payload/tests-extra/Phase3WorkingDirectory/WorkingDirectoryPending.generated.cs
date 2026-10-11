// Generated verbatim from the actual app method. Do not edit.
namespace Files.App.ViewModels { public partial class ShellViewModel {
public bool IsWorkingDirectoryUpdatePending => !isDisposed &&
			!string.Equals(requestedWorkingDirectory, WorkingDirectory, StringComparison.Ordinal);
}}
