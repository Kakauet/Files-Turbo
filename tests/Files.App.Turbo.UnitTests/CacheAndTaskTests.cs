// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Services;
using Files.App.Storage;
using Microsoft.UI.Xaml.Media;

namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class ThumbnailImageCacheTests
	{
		private static readonly DateTimeOffset Modified = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

		private static ThumbnailImageKey Key(string path, int size = 96, ThumbnailImageKind kind = ThumbnailImageKind.Thumbnail)
			=> ThumbnailImageKey.Create(path, Modified, size, kind);

		[TestMethod]
		public void Least_recently_used_images_are_evicted_by_byte_budget()
		{
			var cache = new ThumbnailImageCache(300);
			cache.Add(Key("a"), new ImageSource(), 100);
			cache.Add(Key("b"), new ImageSource(), 100);
			cache.Add(Key("c"), new ImageSource(), 100);
			Assert.IsTrue(cache.TryGet(Key("a"), out _));

			cache.Add(Key("d"), new ImageSource(), 100);

			Assert.IsFalse(cache.TryGet(Key("b"), out _), "The least recently used image must go first.");
			Assert.IsTrue(cache.TryGet(Key("a"), out _));
			Assert.AreEqual(300, cache.UsedBytes);
		}

		[TestMethod]
		public void Image_larger_than_the_budget_is_not_retained()
		{
			var cache = new ThumbnailImageCache(100);
			cache.Add(Key("big"), new ImageSource(), 101);
			Assert.AreEqual(0, cache.Count);
			Assert.AreEqual(0, cache.UsedBytes);
		}

		[TestMethod]
		public void Another_version_or_size_is_a_different_image()
		{
			var cache = new ThumbnailImageCache(1000);
			cache.Add(Key("a"), new ImageSource(), 10);
			Assert.IsFalse(cache.TryGet(ThumbnailImageKey.Create("a", Modified.AddSeconds(1), 96, ThumbnailImageKind.Thumbnail), out _));
			Assert.IsFalse(cache.TryGet(Key("a", 192), out _));
			Assert.IsFalse(cache.TryGet(Key("a", kind: ThumbnailImageKind.Icon), out _));
		}

		[TestMethod]
		public void Remove_and_rename_respect_case_sensitive_paths()
		{
			var cache = new ThumbnailImageCache(1000);
			cache.Add(Key(@"C:\x\a.jpg"), new ImageSource(), 10);
			cache.Add(Key(@"C:\x\A.jpg"), new ImageSource(), 10);

			cache.Remove(@"C:\x\a.jpg");
			Assert.IsFalse(cache.TryGet(Key(@"C:\x\a.jpg"), out _));
			Assert.IsTrue(cache.TryGet(Key(@"C:\x\A.jpg"), out _));

			cache.Rename(@"C:\x\A.jpg", @"C:\x\B.jpg");
			Assert.IsFalse(cache.TryGet(Key(@"C:\x\A.jpg"), out _));
			Assert.IsTrue(cache.TryGet(Key(@"C:\x\B.jpg"), out _));
			Assert.AreEqual(10, cache.UsedBytes);
		}

		[TestMethod]
		public void Missing_marks_expire_and_are_cleared_by_changes()
		{
			long now = 0;
			var cache = new ThumbnailImageCache(1000, () => now);
			cache.MarkMissing("a", Modified);
			Assert.IsTrue(cache.IsMissing("a", Modified));
			Assert.IsFalse(cache.IsMissing("a", Modified.AddTicks(1)), "A new version must be retried.");

			now += (long)TimeSpan.FromMinutes(3).TotalMilliseconds;
			Assert.IsFalse(cache.IsMissing("a", Modified), "Transient failures are retried after a while.");

			cache.MarkMissing("b", Modified);
			cache.Remove("b");
			Assert.IsFalse(cache.IsMissing("b", Modified));
		}
	}

	[TestClass]
	public sealed class StorageEnumerationTaskTests
	{
		private sealed class Handle : IDisposable
		{
			public bool Disposed { get; private set; }
			public void Dispose() => Disposed = true;
		}

		[TestMethod]
		public async Task Timeout_returns_default_and_releases_the_late_result()
		{
			var late = new TaskCompletionSource<Handle>(TaskCreationOptions.RunContinuationsAsynchronously);
			var result = await StorageEnumerationTask.AwaitAsync(late.Task, TimeSpan.FromMilliseconds(20), CancellationToken.None, handle => handle.Dispose());
			Assert.IsNull(result);

			var handle = new Handle();
			late.SetResult(handle);
			await Task.Delay(50);
			Assert.IsTrue(handle.Disposed, "A handle produced after the timeout must not leak.");
		}

		[TestMethod]
		public async Task Cancellation_propagates_and_releases_the_late_result()
		{
			using var cancellation = new CancellationTokenSource();
			var late = new TaskCompletionSource<Handle>(TaskCreationOptions.RunContinuationsAsynchronously);
			var waiting = StorageEnumerationTask.AwaitAsync(late.Task, TimeSpan.FromSeconds(30), cancellation.Token, handle => handle.Dispose());
			cancellation.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(() => waiting);

			var handle = new Handle();
			late.SetResult(handle);
			await Task.Delay(50);
			Assert.IsTrue(handle.Disposed);
		}
	}

	[TestClass]
	public sealed class STATaskTests
	{
		[TestMethod]
		public async Task Pooled_work_runs_on_an_STA_thread()
		{
			var apartment = await STATask.RunPooled(() => Thread.CurrentThread.GetApartmentState(), null);
			Assert.AreEqual(ApartmentState.STA, apartment);
		}

		[TestMethod]
		public async Task Exceptions_become_default_results()
		{
			var result = await STATask.RunPooled<string?>(() => throw new InvalidOperationException(), null);
			Assert.IsNull(result);
		}

		[TestMethod]
		public async Task Queued_work_is_cancelled_without_running()
		{
			using var cancellation = new CancellationTokenSource();
			cancellation.Cancel();
			var ran = false;
			await Assert.ThrowsAsync<OperationCanceledException>(() => STATask.RunPooledUntilCompleted(() => ran = true, null, cancellation.Token));
			Assert.IsFalse(ran);
		}

		[TestMethod]
		public async Task Started_native_call_is_awaited_before_cancellation_completes()
		{
			using var cancellation = new CancellationTokenSource();
			using var started = new ManualResetEventSlim();
			using var release = new ManualResetEventSlim();
			var finished = false;
			var task = STATask.RunPooledUntilCompleted(() =>
			{
				started.Set();
				release.Wait(TimeSpan.FromSeconds(10));
				finished = true;
				return 1;
			}, null, cancellation.Token, STATaskPriority.Fast);

			Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(10)));
			cancellation.Cancel();
			await Task.Delay(50);
			Assert.IsFalse(task.IsCompleted, "The task must not complete while the native call still runs.");

			release.Set();
			await Assert.ThrowsAsync<OperationCanceledException>(() => task);
			Assert.IsTrue(finished);
		}

		[TestMethod]
		public async Task Slow_work_never_takes_the_whole_pool()
		{
			// Fill the slow lane, then check that fast work still runs promptly.
			using var release = new ManualResetEventSlim();
			var slow = Enumerable.Range(0, 20).Select(_ => STATask.RunPooled(() => release.Wait(TimeSpan.FromSeconds(10)), null, priority: STATaskPriority.Slow)).ToList();
			try
			{
				var fast = STATask.RunPooled(() => 42, null, priority: STATaskPriority.Fast);
				var winner = await Task.WhenAny(fast, Task.Delay(TimeSpan.FromSeconds(3)));
				Assert.AreSame(fast, winner, "Fast work waited behind slow work.");
			}
			finally
			{
				release.Set();
				await Task.WhenAll(slow);
			}
		}
	}

	[TestClass]
	public sealed class LatestPathNavigationCoordinatorTests
	{
		[TestMethod]
		public void Only_the_latest_request_for_the_same_owner_is_current()
		{
			var coordinator = new LatestPathNavigationCoordinator<object>();
			var owner = new object();
			var first = coordinator.Begin(owner);
			var second = coordinator.Begin(owner);
			Assert.IsFalse(coordinator.IsCurrent(first, owner));
			Assert.IsTrue(coordinator.IsCurrent(second, owner));
			Assert.IsFalse(coordinator.IsCurrent(second, new object()), "A request belongs to the tab that issued it.");
			coordinator.Invalidate();
			Assert.IsFalse(coordinator.IsCurrent(second, owner));
		}
	}
}
