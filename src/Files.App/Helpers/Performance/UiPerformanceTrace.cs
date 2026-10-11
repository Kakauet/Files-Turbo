// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.Threading.Channels;
using Windows.Storage;

namespace Files.App.Helpers
{
	/// <summary>Opt-in diagnostics for explicitly selected synthetic fixtures. Disabled sessions allocate no events.</summary>
	internal static class UiPerformanceTrace
	{
		private const int MaximumOperations = 256;
		private const int MaximumRecords = 100000;
		private static readonly long sessionStarted = Stopwatch.GetTimestamp();
		private static readonly Lock operationLock = new();
		private static readonly Dictionary<long, Operation> operations = new();
		private static readonly Queue<long> operationOrder = new();
		private static readonly Configuration? configuration = LoadConfiguration();
		private static readonly Channel<UiPerformanceRecord>? records = CreateWriter();
		private static long nextOperation;
		private static int recordCount;
		private static int droppedRecords;
		private static volatile bool writerFailed;

		public static bool IsEnabled => records is not null && !writerFailed && Volatile.Read(ref recordCount) < MaximumRecords;

		public static long BeginOperation(string kind, string path)
		{
			if (!IsEnabled || !IsFixturePath(path))
				return 0;

			var id = Interlocked.Increment(ref nextOperation);
			lock (operationLock)
			{
				operations.Add(id, new(Stopwatch.GetTimestamp(), kind, Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar), false, 0, false, 0));
				operationOrder.Enqueue(id);
				if (operationOrder.Count > MaximumOperations)
					operations.Remove(operationOrder.Dequeue());
			}
			Mark(id, "Requested");
			return id;
		}

		public static void Mark(long operationId, string milestone, int count = 0, double durationMs = 0, long value = 0)
		{
			if (!IsEnabled || operationId == 0)
				return;

			Operation operation;
			lock (operationLock)
			{
				if (!operations.TryGetValue(operationId, out operation))
					return;
				if (milestone == "DataPublished" && count > 0)
					operations[operationId] = operation = operation with { DataPublished = true };
				if (milestone == "ListingCompleted")
					operations[operationId] = operation = operation with { ListingCompleted = Stopwatch.GetTimestamp() };
			}
			var now = Stopwatch.GetTimestamp();
			Write(new()
			{
				OperationId = operationId,
				Kind = operation.Kind,
				Milestone = milestone,
				ElapsedMs = Stopwatch.GetElapsedTime(operation.Started, now).TotalMilliseconds,
				SessionMs = Stopwatch.GetElapsedTime(sessionStarted, now).TotalMilliseconds,
				Count = count,
				DurationMs = durationMs,
				Value = value,
				DroppedRecords = Volatile.Read(ref droppedRecords)
			});
		}

		public static bool IsPublishedItem(long operationId, string itemPath)
		{
			lock (operationLock)
			{
				return operations.TryGetValue(operationId, out var operation)
					&& operation.DataPublished
					&& itemPath.StartsWith(operation.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
			}
		}

		public static void RecordContainerCallback(long operationId)
		{
			if (!IsEnabled || operationId == 0)
				return;
			int count;
			lock (operationLock)
			{
				if (!operations.TryGetValue(operationId, out var operation))
					return;
				count = operation.ContainerCallbacks + 1;
				operations[operationId] = operation with { ContainerCallbacks = count };
			}
			if (count == 1)
				Mark(operationId, "ContainerRealized", count);
		}

		public static int GetContainerCallbackCount(long operationId)
		{
			lock (operationLock)
				return operations.TryGetValue(operationId, out var operation) ? operation.ContainerCallbacks : 0;
		}

		public static bool ShouldRecordNavigationFrame(long operationId)
		{
			lock (operationLock)
			{
				if (!operations.TryGetValue(operationId, out var operation)
					|| operation.Kind != "Navigation" || !operation.DataPublished || operation.NavigationFrameWindowClosed)
					return false;
				// Include one late finishing callback so a frame stalled beyond the window is not hidden.
				if (operation.ListingCompleted != 0 && Stopwatch.GetElapsedTime(operation.ListingCompleted).TotalMilliseconds > 2000)
					operations[operationId] = operation with { NavigationFrameWindowClosed = true };
				return true;
			}
		}

		public static bool IsFixturePath(string? path)
		{
			if (configuration is null || string.IsNullOrWhiteSpace(path))
				return false;
			try
			{
				var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
				return fullPath.Equals(configuration.FixtureRoot, StringComparison.OrdinalIgnoreCase)
					|| fullPath.StartsWith(configuration.FixtureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static Configuration? LoadConfiguration()
		{
			try
			{
				// Both configuration and output belong only to the experimental app's normal LocalState.
				if (Windows.ApplicationModel.Package.Current.Id.Name != "FilesTurbo.Experimental")
					return null;
				var file = Path.Combine(ApplicationData.Current.LocalFolder.Path, "phase3-performance.json");
				if (!File.Exists(file) || new FileInfo(file).Length > 4096)
					return null;
				var result = JsonSerializer.Deserialize(File.ReadAllText(file), UiPerformanceJsonContext.Default.Configuration);
				if (result?.Enabled is not true || string.IsNullOrWhiteSpace(result.FixtureRoot))
					return null;
				result.FixtureRoot = Path.GetFullPath(result.FixtureRoot).TrimEnd(Path.DirectorySeparatorChar);
				return Directory.Exists(result.FixtureRoot) ? result : null;
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static Channel<UiPerformanceRecord>? CreateWriter()
		{
			if (configuration is null)
				return null;
			try
			{
				var directory = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Phase3Performance");
				Directory.CreateDirectory(directory);
				var file = Path.Combine(directory, $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid():N}.jsonl");
				var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 16384, FileOptions.Asynchronous);
				var channel = Channel.CreateBounded<UiPerformanceRecord>(new BoundedChannelOptions(2048)
				{
					FullMode = BoundedChannelFullMode.Wait,
					SingleReader = true,
					SingleWriter = false
				});
				_ = Task.Run(async () =>
				{
					try
					{
						using var writer = new StreamWriter(stream);
						var anchorBefore = Stopwatch.GetTimestamp();
						var anchorUtc = DateTime.UtcNow.Ticks;
						var anchorAfter = Stopwatch.GetTimestamp();
						await writer.WriteLineAsync(JsonSerializer.Serialize(new UiPerformanceRecord
						{
							Milestone = "SessionStarted",
							Kind = "Diagnostics",
							Value = Stopwatch.Frequency,
							UtcTicks = anchorUtc,
							SessionMs = Stopwatch.GetElapsedTime(sessionStarted, anchorBefore + (anchorAfter - anchorBefore) / 2).TotalMilliseconds,
							DurationMs = Stopwatch.GetElapsedTime(anchorBefore, anchorAfter).TotalMilliseconds
						}, UiPerformanceJsonContext.Default.UiPerformanceRecord));
						while (await channel.Reader.WaitToReadAsync())
						{
							while (channel.Reader.TryRead(out var record))
								await writer.WriteLineAsync(JsonSerializer.Serialize(record, UiPerformanceJsonContext.Default.UiPerformanceRecord));
							await writer.FlushAsync();
							if (stream.Position >= 32 * 1024 * 1024)
							{
								writerFailed = true;
								channel.Writer.TryComplete();
							}
						}
					}
					catch (Exception)
					{
						writerFailed = true;
						channel.Writer.TryComplete();
						stream.Dispose();
					}
				});
				return channel;
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static void Write(UiPerformanceRecord record)
		{
			if (Interlocked.Increment(ref recordCount) > MaximumRecords)
			{
				records?.Writer.TryComplete();
				return;
			}
			if (records?.Writer.TryWrite(record) is not true)
				Interlocked.Increment(ref droppedRecords);
			if (Volatile.Read(ref recordCount) == MaximumRecords)
				records?.Writer.TryComplete();
		}

		private readonly record struct Operation(long Started, string Kind, string Path, bool DataPublished, long ListingCompleted, bool NavigationFrameWindowClosed, int ContainerCallbacks);

		internal sealed class Configuration
		{
			public bool Enabled { get; set; }
			public string FixtureRoot { get; set; } = string.Empty;
		}
	}

	internal sealed class UiPerformanceRecord
	{
		public long OperationId { get; set; }
		public string Kind { get; set; } = string.Empty;
		public string Milestone { get; set; } = string.Empty;
		public double ElapsedMs { get; set; }
		public double SessionMs { get; set; }
		public int Count { get; set; }
		public double DurationMs { get; set; }
		public long Value { get; set; }
		public long UtcTicks { get; set; }
		public int DroppedRecords { get; set; }
	}

	[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization | JsonSourceGenerationMode.Metadata)]
	[JsonSerializable(typeof(UiPerformanceRecord))]
	[JsonSerializable(typeof(UiPerformanceTrace.Configuration))]
	internal partial class UiPerformanceJsonContext : JsonSerializerContext
	{
	}
}
