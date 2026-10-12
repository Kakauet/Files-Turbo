// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class ViewportWorkQueueTests
	{
		private sealed class Key(string name)
		{
			public override string ToString() => name;
		}

		// Work items whose completion the test controls.
		private sealed class Gate
		{
			private readonly Dictionary<Key, TaskCompletionSource> running = new();
			public List<Key> Started { get; } = new();
			public List<CancellationToken> Tokens { get; } = new();

			public Func<CancellationToken, Task> For(Key key) => token =>
			{
				lock (running)
				{
					Started.Add(key);
					Tokens.Add(token);
					var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
					running[key] = completion;
					return completion.Task;
				}
			};

			public void Finish(Key key)
			{
				TaskCompletionSource completion;
				lock (running)
					completion = running[key];
				completion.SetResult();
			}
		}

		private static ViewportWorkQueue<Key> Queue(int concurrency, int capacity = 64, bool newestFirst = false, Action<Exception>? faulted = null)
			=> new(concurrency, capacity, action => { action(); return true; }, faulted: faulted, newestFirst: newestFirst);

		private static async Task WaitUntil(Func<bool> condition)
		{
			var watch = Stopwatch.StartNew();
			while (!condition())
			{
				if (watch.ElapsedMilliseconds > 5000)
					Assert.Fail("Condition not reached.");
				await Task.Delay(5);
			}
		}

		[TestMethod]
		public async Task Runs_no_more_than_the_concurrency_limit()
		{
			var gate = new Gate();
			using var queue = Queue(2);
			var keys = Enumerable.Range(0, 5).Select(i => new Key($"k{i}")).ToArray();
			foreach (var key in keys)
				queue.Enqueue(key, gate.For(key));

			Assert.AreEqual(2, queue.ActiveCount);
			Assert.AreEqual(3, queue.PendingCount);

			gate.Finish(gate.Started[0]);
			await WaitUntil(() => gate.Started.Count == 3);
			Assert.AreEqual(2, queue.ActiveCount);
		}

		[TestMethod]
		public async Task Starts_the_most_urgent_request_first()
		{
			var gate = new Gate();
			using var queue = Queue(1);
			var blocker = new Key("blocker");
			var low = new Key("low");
			var high = new Key("high");
			queue.Enqueue(blocker, gate.For(blocker), priority: 0);
			queue.Enqueue(low, gate.For(low), priority: 50);
			queue.Enqueue(high, gate.For(high), priority: 1);

			gate.Finish(blocker);
			await WaitUntil(() => gate.Started.Count == 2);
			Assert.AreSame(high, gate.Started[1]);
		}

		[TestMethod]
		public async Task Reprioritized_request_moves_ahead()
		{
			var gate = new Gate();
			using var queue = Queue(1);
			var blocker = new Key("blocker");
			var a = new Key("a");
			var b = new Key("b");
			queue.Enqueue(blocker, gate.For(blocker), priority: 0);
			queue.Enqueue(a, gate.For(a), priority: 1);
			queue.Enqueue(b, gate.For(b), priority: 2);
			queue.SetPriority(b, 0);

			gate.Finish(blocker);
			await WaitUntil(() => gate.Started.Count == 2);
			Assert.AreSame(b, gate.Started[1]);
		}

		[TestMethod]
		public void Full_queue_evicts_the_least_urgent_request_and_rejects_less_urgent_ones()
		{
			var gate = new Gate();
			using var queue = Queue(1, capacity: 2);
			var blocker = new Key("blocker");
			queue.Enqueue(blocker, gate.For(blocker), priority: 0);
			var visible = new Key("visible");
			var offscreen = new Key("offscreen");
			Assert.IsTrue(queue.Enqueue(visible, gate.For(visible), priority: 1));
			Assert.IsTrue(queue.Enqueue(offscreen, gate.For(offscreen), priority: 100));

			// A newly visible row replaces the off-screen one
			Assert.IsTrue(queue.Enqueue(new Key("newly visible"), _ => Task.CompletedTask, priority: 2));
			Assert.AreEqual(2, queue.PendingCount);

			// Something less urgent than everything queued is rejected
			Assert.IsFalse(queue.Enqueue(new Key("far"), _ => Task.CompletedTask, priority: 500));
		}

		[TestMethod]
		public async Task Cancel_signals_a_running_request_and_frees_its_slot_only_when_it_ends()
		{
			var gate = new Gate();
			using var queue = Queue(1);
			var first = new Key("first");
			var second = new Key("second");
			queue.Enqueue(first, gate.For(first));
			queue.Enqueue(second, gate.For(second));

			queue.Cancel(first);
			Assert.IsTrue(gate.Tokens[0].IsCancellationRequested);
			Assert.AreEqual(1, gate.Started.Count, "A cancelled native call still owns its slot until it returns.");

			gate.Finish(first);
			await WaitUntil(() => gate.Started.Count == 2);
			Assert.AreSame(second, gate.Started[1]);
		}

		[TestMethod]
		public async Task Same_key_never_runs_twice_concurrently()
		{
			var gate = new Gate();
			using var queue = Queue(4);
			var key = new Key("row");
			queue.Enqueue(key, gate.For(key));
			queue.Cancel(key);
			queue.Enqueue(key, gate.For(key));

			Assert.AreEqual(1, gate.Started.Count);
			gate.Finish(key);
			await WaitUntil(() => gate.Started.Count == 2);
		}

		[TestMethod]
		public async Task Completed_request_reruns_only_for_a_new_version()
		{
			var runs = 0;
			using var queue = Queue(1);
			var key = new Key("row");
			queue.Enqueue(key, _ => { runs++; return Task.CompletedTask; }, version: 1);
			await WaitUntil(() => queue.ActiveCount == 0);
			queue.Enqueue(key, _ => { runs++; return Task.CompletedTask; }, version: 1);
			Assert.AreEqual(1, runs);
			queue.Enqueue(key, _ => { runs++; return Task.CompletedTask; }, version: 2);
			await WaitUntil(() => runs == 2);
		}

		[TestMethod]
		public async Task Paused_queue_starts_nothing_until_resumed()
		{
			var gate = new Gate();
			using var queue = Queue(2);
			queue.SetPaused(true);
			var key = new Key("row");
			queue.Enqueue(key, gate.For(key));
			Assert.AreEqual(0, gate.Started.Count);

			queue.SetPaused(false);
			await WaitUntil(() => gate.Started.Count == 1);
		}

		[TestMethod]
		public void Dispose_cancels_running_and_rejects_new_work()
		{
			var gate = new Gate();
			var queue = Queue(1);
			var key = new Key("row");
			queue.Enqueue(key, gate.For(key));
			queue.Dispose();

			Assert.IsTrue(gate.Tokens[0].IsCancellationRequested);
			Assert.IsFalse(queue.Enqueue(new Key("late"), _ => Task.CompletedTask));
		}

		[TestMethod]
		public async Task Failures_are_reported_and_do_not_stall_the_queue()
		{
			var failures = new List<Exception>();
			var gate = new Gate();
			using var queue = Queue(1, faulted: failures.Add);
			queue.Enqueue(new Key("bad"), _ => Task.FromException(new InvalidOperationException("boom")));
			var next = new Key("next");
			queue.Enqueue(next, gate.For(next));

			await WaitUntil(() => gate.Started.Count == 1);
			Assert.AreEqual(1, failures.Count);
		}
	}
}
