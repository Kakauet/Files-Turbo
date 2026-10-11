// Generated literally from the production tag-search lookup. Do not edit.
#nullable enable
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
namespace Files.App.Utils.Storage;
internal static class TagLookup
{
    public static async Task<(FindCloseSafeHandle? Handle, WIN32_FIND_DATAW Data)> OpenAsync(string filePath, CancellationToken token)
    {
        var match = new Match(filePath);
        var findTask = Task.Run(() =>
				{
					WIN32_FIND_DATAW findDataTsk = default;
					FindCloseSafeHandle hFileTsk;
					unsafe
					{
						hFileTsk = PInvoke.FindFirstFileEx(match.FilePath, FINDEX_INFO_LEVELS.FindExInfoBasic,
							&findDataTsk, FINDEX_SEARCH_OPS.FindExSearchNameMatch, FIND_FIRST_EX_FLAGS.FIND_FIRST_EX_LARGE_FETCH);
					}
					return (hFileTsk, findDataTsk);
				});
				(FindCloseSafeHandle? hFile, WIN32_FIND_DATAW findData) = await StorageEnumerationTask.AwaitAsync(
					findTask, TimeSpan.FromSeconds(5), token, abandoned => abandoned.Item1.Dispose());
				
        return (hFile, findData);
    }
    private sealed record Match(string FilePath);
}