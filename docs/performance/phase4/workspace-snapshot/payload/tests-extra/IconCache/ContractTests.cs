using Files.App.Services;
using Microsoft.UI.Dispatching;

namespace FilesTurbo.IconCacheBench;

internal static class ContractTests
{
	public sealed record TestResult(string Name, string Status, string Detail);

	public static async Task<List<TestResult>> RunAsync(bool requireSingleFlight)
	{
		List<TestResult> results = [];
		await Check("generic icon key ignores item path", async () =>
		{
			var cache = Fresh();
			var first = await cache.GetIconAsync("synthetic-a.txt", ".txt", false, 32);
			var second = await cache.GetIconAsync("synthetic-b.txt", ".txt", false, 32);
			Require(ReferenceEquals(first, second) && FileThumbnailHelper.Calls == 1);
		});
		await Check("extension casing preserves invariant lowercase semantics", async () =>
		{
			var cache = Fresh();
			var first = await cache.GetIconAsync(null, ".TXT", false, 32);
			var second = await cache.GetIconAsync(null, ".txt", false, 32);
			Require(ReferenceEquals(first, second) && FileThumbnailHelper.Calls == 1);
			await cache.GetIconAsync(null, ".s", false, 32);
			await cache.GetIconAsync(null, ".ſ", false, 32);
			Require(FileThumbnailHelper.Calls == 3, "Invariant-lowercase keys .s and .ſ must stay distinct.");
		});
		await Check("null and empty extension retain separate original keys", async () =>
		{
			var cache = Fresh();
			await cache.GetIconAsync(null, null, false, 32);
			await cache.GetIconAsync(null, "", false, 32);
			Require(FileThumbnailHelper.Calls == 2);
		});
		await Check("folder ignores extension; size partitions keys", async () =>
		{
			var cache = Fresh();
			var first = await cache.GetIconAsync(null, ".txt", true, 32);
			var second = await cache.GetIconAsync(null, null, true, 32);
			Require(ReferenceEquals(first, second));
			await cache.GetIconAsync(null, null, true, 48);
			Require(FileThumbnailHelper.Calls == 2);
			Require(FileThumbnailHelper.Requests.All(x => x.IsFolder && x.Path == Environment.SystemDirectory));
		});
		await Check("clear releases byte and bitmap cache entries", async () =>
		{
			var cache = Fresh();
			var first = await cache.GetIconImageAsync(null, ".txt", false, 32);
			var cached = await cache.GetIconImageAsync(null, ".txt", false, 32);
			Require(first is not null && ReferenceEquals(first, cached));
			cache.Clear();
			var second = await cache.GetIconImageAsync(null, ".txt", false, 32);
			Require(second is not null && !ReferenceEquals(first, second) && FileThumbnailHelper.Calls == 2);
		});
		await Check("bitmap calls without dispatcher remain unsupported", async () =>
		{
			var cache = Fresh();
			DispatcherQueue.Enabled = false;
			Require(await cache.GetIconImageAsync(null, ".txt", false, 32) is null);
			Require(FileThumbnailHelper.Calls == 0);
		});
		await Check("null shell result stays cached", async () =>
		{
			var cache = Fresh();
			FileThumbnailHelper.Handler = (_, _, _) => Task.FromResult<byte[]?>(null);
			Require(await cache.GetIconAsync(null, ".txt", false, 32) is null);
			Require(await cache.GetIconAsync(null, ".TXT", false, 32) is null);
			Require(FileThumbnailHelper.Calls == 1);
		});
		await Check("shell exception propagates and subsequent request retries", async () =>
		{
			var cache = Fresh();
			FileThumbnailHelper.Handler = (_, _, _) => FileThumbnailHelper.Calls == 1
				? Task.FromException<byte[]?>(new IOException("Synthetic shell failure"))
				: Task.FromResult<byte[]?>([42]);
			try
			{
				await cache.GetIconAsync(null, ".txt", false, 32);
				throw new InvalidOperationException("Expected exception was swallowed.");
			}
			catch (IOException) { }
			var bytes = await cache.GetIconAsync(null, ".txt", false, 32);
			Require(bytes is [42] && FileThumbnailHelper.Calls == 2);
		});
		await Check("concurrent same-key requests", async () =>
		{
			var cache = Fresh();
			var gate = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
			FileThumbnailHelper.Handler = (_, _, _) => gate.Task;
			var tasks = Enumerable.Range(0, 32).Select(_ => cache.GetIconAsync(null, ".txt", false, 32)).ToArray();
			var calls = FileThumbnailHelper.Calls;
			gate.SetResult([17]);
			var bytes = await Task.WhenAll(tasks);
			Require(bytes.All(x => x is [17]));
			if (requireSingleFlight)
				Require(calls == 1, $"Expected one load for 32 requests; saw {calls}.");
			else
				results.Add(new("baseline concurrent duplicate loads", "observed", $"32 callers scheduled {calls} shell test-double loads."));
		});
		await Check("clear during pending load", async () =>
		{
			var cache = Fresh();
			var gate = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
			FileThumbnailHelper.Handler = (_, _, _) => FileThumbnailHelper.Calls == 1 ? gate.Task : Task.FromResult<byte[]?>([2]);
			var pending = cache.GetIconAsync(null, ".txt", false, 32);
			cache.Clear();
			gate.SetResult([1]);
			Require(await pending is [1]);
			var next = await cache.GetIconAsync(null, ".txt", false, 32);
			if (requireSingleFlight)
				Require(next is [2] && FileThumbnailHelper.Calls == 2, "A pre-clear pending load must not repopulate cleared cache.");
			else
				results.Add(new("baseline clear pending repopulation", "observed", $"Next icon marker={next?[0]}, calls={FileThumbnailHelper.Calls}; old request was completed after Clear."));
		});
		await Check("parallel cold callers share one pending load", async () =>
		{
			var cache = Fresh();
			var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var gate = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
			var callers = 0;
			FileThumbnailHelper.Handler = (_, _, _) => gate.Task;
			var tasks = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
			{
				await start.Task;
				var pending = cache.GetIconAsync(null, ".png", false, 64);
				if (Interlocked.Increment(ref callers) == 32)
					entered.SetResult();
				return await pending;
			})).ToArray();
			start.SetResult();
			int calls;
			try
			{
				await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
				calls = FileThumbnailHelper.Calls;
			}
			finally
			{
				gate.SetResult([29]);
			}
			var values = await Task.WhenAll(tasks);
			Require(values.All(x => x is [29]));
			if (requireSingleFlight)
				Require(calls == 1, $"Parallel cold callers scheduled {calls} loads.");
			else
				results.Add(new("baseline parallel duplicate loads", "observed", $"32 concurrent worker callers scheduled {calls} loads."));
		});
		await Check("old pending failure cannot delete newer entry", async () =>
		{
			var cache = Fresh();
			var oldGate = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
			var newGate = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
			FileThumbnailHelper.Handler = (_, _, _) => FileThumbnailHelper.Calls == 1 ? oldGate.Task : newGate.Task;
			var old = cache.GetIconAsync(null, ".txt", false, 32);
			cache.Clear();
			var newer = cache.GetIconAsync(null, ".txt", false, 32);
			oldGate.SetException(new IOException("Synthetic old-generation failure"));
			try { await old; } catch (IOException) { }
			var concurrent = cache.GetIconAsync(null, ".txt", false, 32);
			var calls = FileThumbnailHelper.Calls;
			newGate.SetResult([3]);
			Require(await newer is [3] && await concurrent is [3]);
			if (requireSingleFlight)
				Require(calls == 2, $"New entry should survive old failure; saw {calls} loads.");
			else
				results.Add(new("baseline pending request after old failure", "observed", $"Same-key request while new load is pending scheduled {calls} loads total."));
		});
		return results;

		async Task Check(string name, Func<Task> action)
		{
			try
			{
				await action();
				results.Add(new(name, "passed", ""));
			}
			catch (Exception exception)
			{
				results.Add(new(name, "failed", exception.ToString()));
			}
		}
	}

	private static IconCacheService Fresh()
	{
		FileThumbnailHelper.Reset();
		return new();
	}

	private static void Require(bool condition, string detail = "Contract assertion failed.")
	{
		if (!condition)
			throw new InvalidOperationException(detail);
	}
}
