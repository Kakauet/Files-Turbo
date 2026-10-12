// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class CoalescingRunnerTests
	{
		[TestMethod]
		public async Task A_burst_of_requests_runs_a_few_times_not_once_per_request()
		{
			var running = 0;
			var maxConcurrent = 0;
			var runs = 0;
			var runner = new CoalescingRunner(async () =>
			{
				var now = Interlocked.Increment(ref running);
				InterlockedMax(ref maxConcurrent, now);
				Interlocked.Increment(ref runs);
				await Task.Delay(20);
				Interlocked.Decrement(ref running);
			});

			// 2,000 watcher events, as when 1,000 items are recycled
			await Task.WhenAll(Enumerable.Range(0, 2000).Select(_ => Task.Run(runner.RequestAsync)));

			Assert.AreEqual(1, maxConcurrent, "Runs must not overlap");
			Assert.IsTrue(runs is >= 1 and < 50, $"{runs} runs for 2,000 requests");
		}

		[TestMethod]
		public async Task A_request_during_a_run_is_followed_by_another_complete_run()
		{
			var started = new TaskCompletionSource();
			var release = new TaskCompletionSource();
			var runs = 0;
			var runner = new CoalescingRunner(async () =>
			{
				if (Interlocked.Increment(ref runs) == 1)
				{
					started.SetResult();
					await release.Task;
				}
			});

			var first = runner.RequestAsync();
			await started.Task;
			var second = runner.RequestAsync(); // arrives while the first run is still going
			release.SetResult();
			await Task.WhenAll(first, second);

			Assert.AreEqual(2, runs);
		}

		[TestMethod]
		public async Task A_failing_run_does_not_block_later_requests()
		{
			var calls = 0;
			var runner = new CoalescingRunner(() =>
			{
				if (Interlocked.Increment(ref calls) == 1)
					throw new InvalidOperationException("boom");
				return Task.CompletedTask;
			});

			var failed = false;
			try { await runner.RequestAsync(); }
			catch (InvalidOperationException) { failed = true; }
			Assert.IsTrue(failed);
			await runner.RequestAsync();

			Assert.AreEqual(2, calls);
		}

		private static void InterlockedMax(ref int target, int value)
		{
			int current;
			while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current)
			{
			}
		}
	}
}
