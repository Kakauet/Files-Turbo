global using Files.App.Data.Contracts;
global using Files.App.Utils.Storage;
namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        public static DispatcherQueue GetForCurrentThread() => new();
    }
}
namespace Microsoft.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage { }
}
namespace Files.App.Data.Contracts
{
    internal interface IIconCacheService
    {
        Task<byte[]?> GetIconAsync(string? path, string? extension, bool folder, uint size);
        Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> GetIconImageAsync(string? path, string? extension, bool folder, uint size);
        void Clear();
    }
}
namespace Files.App.Utils.Storage
{
    public enum IconOptions { ReturnIconOnly }
    public static class FileThumbnailHelper
    {
        public static Task<byte[]?> GetIconAsync(string? path, uint size, bool folder, IconOptions options) => Task.FromResult<byte[]?>([1]);
    }
    public static class BitmapExtensions
    {
        private static int calls;
        public static int Calls => Volatile.Read(ref calls);
        public static Func<Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?>>? Handler;
        public static Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> ToBitmapAsync(this byte[] data)
        {
            Interlocked.Increment(ref calls);
            return Handler?.Invoke() ?? Task.FromResult<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?>(new());
        }
        public static void Reset() { calls = 0; Handler = null; }
    }
}
