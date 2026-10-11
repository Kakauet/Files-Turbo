// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.App.Helpers
{
	/// <summary>
	/// Bounds deferred row work and starts current visible requests ahead of cached rows.
	/// </summary>
	internal sealed partial class ViewportWorkQueue<TKey> : IDisposable where TKey : class
	{
		private readonly object gate = new();
		private readonly Dictionary<TKey, Request> requests = new(ReferenceEqualityComparer.Instance);
		private readonly LinkedList<Request> pending = new();
		private readonly HashSet<Request> active = new();
		private readonly HashSet<TKey> activeKeys = new(ReferenceEqualityComparer.Instance);
		private readonly int concurrency;
		private readonly int capacity;
		private readonly Func<Action, bool> dispatch;
		private readonly Action? completed;
		private readonly Action<Exception>? faulted;
		private readonly bool newestFirst;
		private bool pumping;
		private bool paused;
		private bool disposed;

		private sealed class Request(TKey key, Func<CancellationToken, Task> work, int priority, int version)
		{
			public TKey Key { get; } = key;
			public Func<CancellationToken, Task> Work { get; } = work;
			public CancellationTokenSource Cancellation { get; } = new();
			public int Priority { get; set; } = priority;
			public LinkedListNode<Request>? Node { get; set; }
			public bool Completed { get; set; }
			public int Version { get; } = version;
		}

		public ViewportWorkQueue(int concurrency, int capacity, Func<Action, bool> dispatch, Action? completed = null, Action<Exception>? faulted = null, bool newestFirst = true)
		{
			ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
			ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
			this.concurrency = concurrency;
			this.capacity = capacity;
			this.dispatch = dispatch;
			this.completed = completed;
			this.faulted = faulted;
			this.newestFirst = newestFirst;
		}

		public int PendingCount { get { lock (gate) return pending.Count; } }
		public int ActiveCount { get { lock (gate) return active.Count; } }

		public void SetPaused(bool value)
		{
			lock (gate)
				paused = value;
			if (!value)
				Pump();
		}

		public void SetPriority(TKey key, int priority)
		{
			lock (gate)
				if (requests.TryGetValue(key, out var request))
					request.Priority = priority;
		}

		public bool Enqueue(TKey key, Func<CancellationToken, Task> work, int priority = 0, int version = 0)
		{
			Request? evicted = null;
			Request? finished = null;
			lock (gate)
			{
				if (disposed)
					return false;
				if (requests.TryGetValue(key, out var existing))
				{
					if (!existing.Completed || existing.Version == version)
					{
						existing.Priority = priority;
						return true;
					}
					finished = existing;
				}

				if (pending.Count == capacity)
				{
					// Discard the oldest least-urgent request rather than delaying a newly visible row.
					var candidate = pending.First!;
					for (var node = candidate.Next; node is not null; node = node.Next)
					{
						if (node.Value.Priority > candidate.Value.Priority)
							candidate = node;
					}
					if (candidate.Value.Priority < priority)
						return false;
					evicted = candidate.Value;
					pending.Remove(candidate);
					requests.Remove(evicted.Key);
				}

				var request = new Request(key, work, priority, version);
				request.Node = pending.AddLast(request);
				requests[key] = request;
			}
			if (finished is not null)
				CancelRequest(finished, false);
			evicted?.Cancellation.Dispose();
			Pump();
			return true;
		}

		public void Cancel(TKey key)
		{
			Request? request;
			bool running;
			lock (gate)
			{
				if (!requests.Remove(key, out request))
					return;
				running = active.Contains(request);
				if (request.Node is not null)
					pending.Remove(request.Node);
				request.Node = null;
			}
			CancelRequest(request, running);
		}

		public void CancelAll()
		{
			Request[] inactive;
			Request[] running;
			lock (gate)
			{
				running = active.ToArray();
				inactive = requests.Values.Where(request => !active.Contains(request)).ToArray();
				pending.Clear();
				requests.Clear();
			}
			foreach (var request in inactive)
				CancelRequest(request, false);
			foreach (var request in running)
				CancelRequest(request, true);
		}

		private static void CancelRequest(Request request, bool running)
		{
			// An already-running native call still occupies its slot until its actual completion.
			try { request.Cancellation.Cancel(); }
			catch (ObjectDisposedException) { }
			if (!running)
				request.Cancellation.Dispose();
		}

		private void Pump()
		{
			lock (gate)
			{
				if (pumping)
					return;
				pumping = true;
			}

			while (true)
			{
				Request request;
				lock (gate)
				{
					if (disposed || paused || active.Count >= concurrency || pending.Count == 0)
					{
						pumping = false;
						return;
					}
					LinkedListNode<Request>? next = null;
					for (var node = pending.First; node is not null; node = node.Next)
					{
						if (!activeKeys.Contains(node.Value.Key) && (next is null || node.Value.Priority < next.Value.Priority || newestFirst && node.Value.Priority == next.Value.Priority))
							next = node;
					}
					if (next is null)
					{
						pumping = false;
						return;
					}
					request = next.Value;
					pending.Remove(next);
					request.Node = null;
					active.Add(request);
					activeKeys.Add(request.Key);
				}

				if (!TryDispatch(() => _ = RunAsync(request)))
					Complete(request);
			}
		}

		private async Task RunAsync(Request request)
		{
			var successful = false;
			try
			{
				request.Cancellation.Token.ThrowIfCancellationRequested();
				await request.Work(request.Cancellation.Token);
				successful = true;
			}
			catch (OperationCanceledException) { }
			catch (Exception exception) { faulted?.Invoke(exception); }
			finally
			{
				Complete(request, successful);
			}
		}

		private void Complete(Request request, bool successful = false)
		{
			bool notify;
			bool disposeSource;
			lock (gate)
			{
				active.Remove(request);
				activeKeys.Remove(request.Key);
				request.Completed = true;
				disposeSource = !requests.TryGetValue(request.Key, out var current) || !ReferenceEquals(current, request);
				if (!successful && !disposeSource)
				{
					requests.Remove(request.Key);
					disposeSource = true;
				}
				notify = !disposed;
			}
			// Completed rows retain their token until recycling so detached thumbnail retries can still cancel.
			if (disposeSource)
				CancelRequest(request, false);
			Pump();
			if (notify && completed is not null)
				TryDispatch(completed);
		}

		private bool TryDispatch(Action action)
		{
			try { return dispatch(action); }
			catch (Exception exception)
			{
				faulted?.Invoke(exception);
				return false;
			}
		}

		public void Dispose()
		{
			lock (gate)
				disposed = true;
			CancelAll();
		}
	}
}
