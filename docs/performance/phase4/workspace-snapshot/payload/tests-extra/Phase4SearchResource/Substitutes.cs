namespace Windows.Win32.Foundation
{
    public sealed class FindCloseSafeHandle : IDisposable
    {
        private int disposals;
        public int Disposals => Volatile.Read(ref disposals);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose() { Interlocked.Increment(ref disposals); Disposed.TrySetResult(); }
    }
}

namespace Windows.Win32.Storage.FileSystem
{
    public struct WIN32_FIND_DATAW { public int Value; }
    public enum FINDEX_INFO_LEVELS { FindExInfoBasic }
    public enum FINDEX_SEARCH_OPS { FindExSearchNameMatch }
    public enum FIND_FIRST_EX_FLAGS { FIND_FIRST_EX_LARGE_FETCH }
}

namespace Windows.Win32
{
    using Windows.Win32.Foundation;
    using Windows.Win32.Storage.FileSystem;
    internal static class PInvoke
    {
        public static Func<string, (FindCloseSafeHandle Handle, WIN32_FIND_DATAW Data)> Lookup { get; set; } = _ => throw new InvalidOperationException();
        public static unsafe FindCloseSafeHandle FindFirstFileEx(string path, FINDEX_INFO_LEVELS info, WIN32_FIND_DATAW* data, FINDEX_SEARCH_OPS search, FIND_FIRST_EX_FLAGS flags)
        {
            var result = Lookup(path);
            *data = result.Data;
            return result.Handle;
        }
    }
}
