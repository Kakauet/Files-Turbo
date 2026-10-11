// Controlled dependency doubles; PNG headers are synthetic and are not decoded or rendered.
global using Files.App.Data.Contracts;
global using Files.App.Helpers;
global using Files.App.Utils.Storage;

using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        public static bool Enabled { get; set; } = true;
        public static DispatcherQueue? GetForCurrentThread() => Enabled ? new() : null;
    }
}

namespace Microsoft.UI.Xaml.Media.Imaging
{
    public enum DecodePixelType { Logical, Physical }

    public sealed class BitmapImage
    {
        public int DecodePixelWidth { get; set; }
        public int DecodePixelHeight { get; set; }
        public DecodePixelType DecodePixelType { get; set; }
        public byte[]? SourceData { get; private set; }

        public void SetSource(Windows.Storage.Streams.TinyStream stream)
        {
            SourceData = stream.Data;
            TinyCodec.Record(this, stream.Data, true);
            TinyCodec.SyncHandler?.Invoke(this);
        }

        public Task SetSourceAsync(Windows.Storage.Streams.TinyStream stream)
        {
            SourceData = stream.Data;
            TinyCodec.Record(this, stream.Data, false);
            return TinyCodec.AsyncHandler?.Invoke(this) ?? Task.CompletedTask;
        }
    }
}

namespace Windows.Storage.Streams
{
    public sealed record TinyStream(byte[] Data);

    public static class StreamExtensions
    {
        public static TinyStream AsRandomAccessStream(this Stream stream)
        {
            if (stream is not MemoryStream memory)
                throw new InvalidOperationException("Expected a tiny in-memory test stream.");
            return new(memory.ToArray());
        }
    }
}

namespace Files.App
{
    public static class App
    {
        public static AppModel AppModel { get; } = new();
    }

    public sealed class AppModel
    {
        public float AppWindowDPI { get; set; } = 1f;
    }
}

namespace Files.App.Data.Contracts
{
    internal interface IIconCacheService
    {
        Task<byte[]?> GetIconAsync(string? itemPath, string? extension, bool isFolder, uint size);
        Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> GetIconImageAsync(string? itemPath, string? extension, bool isFolder, uint size);
        void Clear();
    }
}

namespace Files.App.Utils.Storage
{
    [Flags]
    public enum IconOptions { ReturnIconOnly = 2 }

    public static class FileThumbnailHelper
    {
        private static int calls;
        public static int Calls => Volatile.Read(ref calls);
        public static ConcurrentQueue<(string? Path, uint Size, bool IsFolder, IconOptions Options)> Requests { get; } = new();
        public static Func<string?, uint, bool, Task<byte[]?>>? Handler { get; set; }

        public static Task<byte[]?> GetIconAsync(string? path, uint size, bool isFolder, IconOptions options)
        {
            Interlocked.Increment(ref calls);
            Requests.Enqueue((path, size, isFolder, options));
            return Handler?.Invoke(path, size, isFolder) ?? Task.FromResult<byte[]?>(TinyCodec.Png(64, 64));
        }

        public static void Reset()
        {
            Interlocked.Exchange(ref calls, 0);
            Requests.Clear();
            Handler = null;
            Microsoft.UI.Dispatching.DispatcherQueue.Enabled = true;
            global::Files.App.App.AppModel.AppWindowDPI = 1f;
            TinyCodec.Reset();
        }
    }
}

internal sealed record DecodeRecord(
    Microsoft.UI.Xaml.Media.Imaging.BitmapImage Image,
    int EncodedBytes,
    int Width,
    int Height,
    Microsoft.UI.Xaml.Media.Imaging.DecodePixelType PixelType,
    bool Synchronous);

internal static class TinyCodec
{
    public static ConcurrentQueue<DecodeRecord> Records { get; } = new();
    public static Action<Microsoft.UI.Xaml.Media.Imaging.BitmapImage>? SyncHandler { get; set; }
    public static Func<Microsoft.UI.Xaml.Media.Imaging.BitmapImage, Task>? AsyncHandler { get; set; }

    public static void Record(Microsoft.UI.Xaml.Media.Imaging.BitmapImage image, byte[] data, bool synchronous)
        => Records.Enqueue(new(image, data.Length, image.DecodePixelWidth, image.DecodePixelHeight, image.DecodePixelType, synchronous));

    public static byte[] Png(uint width, uint height, int bytes = 33)
    {
        if (bytes < 33)
            throw new ArgumentOutOfRangeException(nameof(bytes));
        var data = new byte[bytes];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(data.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16, 4), width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20, 4), height);
        return data;
    }

    public static void Reset()
    {
        Records.Clear();
        SyncHandler = null;
        AsyncHandler = null;
    }
}
