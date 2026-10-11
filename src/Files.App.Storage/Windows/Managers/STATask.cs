// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Windows.Win32;

namespace Files.App.Storage
{
	/// <summary>
	/// Lanes of the shared STA pool, served in this order.
	/// </summary>
	public enum STATaskPriority
	{
		/// <summary>Quick visible work, such as cached thumbnails and icons.</summary>
		Fast = 0,
		Normal = 1,
		/// <summary>Visible thumbnail generation; never takes every pool thread.</summary>
		Slow = 2,
		/// <summary>Overlays and prefetching.</summary>
		Background = 3,
	}

	/// <summary>
	/// Represents a work scheduled to execute on a STA thread.
	/// </summary>
	public partial class STATask
	{
		private const int PooledThreadCount = 12;

		private static readonly object _poolLock = new();
		private static readonly Queue<Action>[] _lanes = [new(), new(), new(), new()];
		private static readonly int[] _laneActive = new int[4];
		// Slow and background work leave threads free for quick visible requests
		private static readonly int[] _laneLimits = [PooledThreadCount, PooledThreadCount, PooledThreadCount - 4, PooledThreadCount / 3];
		private static bool _poolStarted;

		/// <summary>
		/// Schedules the specified work on a shared pool of persistent STA threads, avoiding per-call thread creation.
		/// </summary>
		/// <typeparam name="T">The type of the result returned by the function.</typeparam>
		/// <param name="func">The work to execute on an STA pool thread.</param>
		/// <param name="logger">A logger to capture any exception that occurs during execution.</param>
		/// <returns>A <see cref="Task{T}"/> that represents the scheduled work.</returns>
		public static Task<T> RunPooled<T>(Func<T> func, ILogger? logger, CancellationToken cancellationToken = default, STATaskPriority priority = STATaskPriority.Normal)
		{
			if (cancellationToken.IsCancellationRequested)
				return Task.FromCanceled<T>(cancellationToken);

			EnsurePoolStarted();

			var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
			var registration = cancellationToken.CanBeCanceled
				? cancellationToken.UnsafeRegister(static (state, token) => ((TaskCompletionSource<T>)state!).TrySetCanceled(token), tcs)
				: default;

			Enqueue(() =>
			{
				try
				{
					if (cancellationToken.IsCancellationRequested)
					{
						tcs.TrySetCanceled(cancellationToken);
						return;
					}

					tcs.TrySetResult(func());
				}
				catch (Exception ex)
				{
					tcs.TrySetResult(default!);
					logger?.LogWarning(ex, "An exception was occurred during the execution within STA.");
				}
				finally
				{
					registration.Dispose();
				}
			}, priority);

			return tcs.Task;
		}

		/// <summary>
		/// Cancels queued work promptly, but waits for a started native call before completing its task.
		/// </summary>
		public static Task<T> RunPooledUntilCompleted<T>(Func<T> func, ILogger? logger, CancellationToken cancellationToken = default, STATaskPriority priority = STATaskPriority.Normal)
		{
			if (cancellationToken.IsCancellationRequested)
				return Task.FromCanceled<T>(cancellationToken);

			EnsurePoolStarted();
			var work = new PooledWork<T>(func, logger, cancellationToken);
			Enqueue(work.Run, priority);
			return work.Task;
		}

		private static void Enqueue(Action work, STATaskPriority priority)
		{
			lock (_poolLock)
			{
				_lanes[(int)priority].Enqueue(work);
				Monitor.Pulse(_poolLock);
			}
		}

		private static bool TryTakeWork(out Action work, out int lane)
		{
			for (lane = 0; lane < _lanes.Length; lane++)
			{
				if (_lanes[lane].Count > 0 && _laneActive[lane] < _laneLimits[lane])
				{
					work = _lanes[lane].Dequeue();
					_laneActive[lane]++;
					return true;
				}
			}

			work = null!;
			lane = -1;
			return false;
		}

		private static void EnsurePoolStarted()
		{
			if (!Interlocked.CompareExchange(ref _poolStarted, true, false))
			{
				for (int i = 0; i < PooledThreadCount; i++)
				{
					Thread thread =
						new(() =>
						{
							PInvoke.OleInitialize();

							// Consume synchronously: awaiting here would resume on an MTA thread pool thread, losing the STA apartment
							while (true)
							{
								Action work;
								int lane;
								lock (_poolLock)
								{
									while (!TryTakeWork(out work, out lane))
										Monitor.Wait(_poolLock);
								}

								try
								{
									work();
								}
								finally
								{
									lock (_poolLock)
									{
										_laneActive[lane]--;
										// A lane held back by its limit may now run
										Monitor.Pulse(_poolLock);
									}
								}
							}
						});

					thread.IsBackground = true;
					thread.SetApartmentState(ApartmentState.STA);
					thread.Name = $"STATask pool {i}";
					thread.Start();
				}
			}
		}

		private sealed class PooledWork<T>
		{
			private readonly Func<T> func;
			private readonly ILogger? logger;
			private readonly CancellationToken token;
			private readonly TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
			private readonly CancellationTokenRegistration registration;
			private int state;

			public PooledWork(Func<T> func, ILogger? logger, CancellationToken token)
			{
				this.func = func;
				this.logger = logger;
				this.token = token;
				registration = token.CanBeCanceled ? token.UnsafeRegister(static work => ((PooledWork<T>)work!).CancelQueued(), this) : default;
			}

			public Task<T> Task => completion.Task;

			private void CancelQueued()
			{
				if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
					completion.TrySetCanceled(token);
			}

			public void Run()
			{
				try
				{
					if (Interlocked.CompareExchange(ref state, 1, 0) != 0 || token.IsCancellationRequested)
					{
						completion.TrySetCanceled(token);
						return;
					}

					var result = func();
					if (token.IsCancellationRequested)
						completion.TrySetCanceled(token);
					else
						completion.TrySetResult(result);
				}
				catch (Exception ex)
				{
					if (token.IsCancellationRequested)
						completion.TrySetCanceled(token);
					else
						completion.TrySetResult(default!);
					logger?.LogWarning(ex, "An exception was occurred during the execution within STA.");
				}
				finally
				{
					registration.Dispose();
				}
			}
		}

		/// <summary>
		/// Schedules the specified work to execute in a new background thread initialized with STA state.
		/// </summary>
		/// <param name="action">The work to execute in the STA thread.</param>
		/// <param name="logger">A logger to capture any exception that occurs during execution.</param>
		/// <returns>A <see cref="Task"/> that represents the work scheduled to execute in the STA thread.</returns>
		public static Task Run(Action action, ILogger? logger)
		{
			var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			Thread thread =
				new(() =>
				{
					PInvoke.OleInitialize();

					try
					{
						action();
						tcs.SetResult();
					}
					catch (Exception ex)
					{
						tcs.SetResult();
						logger?.LogWarning(ex, "An exception was occurred during the execution within STA.");
					}
					finally
					{
						PInvoke.OleUninitialize();
					}
				});

			thread.IsBackground = true;
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();

			return tcs.Task;
		}

		/// <summary>
		/// Schedules the specified work to execute in a new background thread initialized with STA state.
		/// </summary>
		/// <typeparam name="T">The type of the result returned by the function.</typeparam>
		/// <param name="func">The work to execute in the STA thread.</param>
		/// <param name="logger">A logger to capture any exception that occurs during execution.</param>
		/// <returns>A <see cref="Task"/> that represents the work scheduled to execute in the STA thread.</returns>
		public static Task<T> Run<T>(Func<T> func, ILogger? logger)
		{
			var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

			Thread thread =
				new(() =>
				{
					PInvoke.OleInitialize();

					try
					{
						tcs.SetResult(func());
					}
					catch (Exception ex)
					{
						tcs.SetResult(default!);
						logger?.LogWarning(ex, "An exception was occurred during the execution within STA.");
					}
					finally
					{
						PInvoke.OleUninitialize();
					}
				});

			thread.IsBackground = true;
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();

			return tcs.Task;
		}

		/// <summary>
		/// Schedules the specified work to execute in a new background thread initialized with STA state.
		/// </summary>
		/// <param name="func">The work to execute in the STA thread.</param>
		/// <param name="logger">A logger to capture any exception that occurs during execution.</param>
		/// <returns>A <see cref="Task"/> that represents the work scheduled to execute in the STA thread.</returns>
		public static Task Run(Func<Task> func, ILogger? logger)
		{
			var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			Thread thread =
				new(() =>
				{
					PInvoke.OleInitialize();
					using var synchronizationContext = new StaSynchronizationContext();
					var previousContext = SynchronizationContext.Current;
					SynchronizationContext.SetSynchronizationContext(synchronizationContext);

					try
					{
						var task = func();
						synchronizationContext.Run(task);
						task.GetAwaiter().GetResult();
						tcs.SetResult();
					}
					catch (Exception ex)
					{
						tcs.SetResult();
						logger?.LogWarning(ex, "An exception was occurred during the execution within STA.");
					}
					finally
					{
						SynchronizationContext.SetSynchronizationContext(previousContext);
						PInvoke.OleUninitialize();
					}
				});

			thread.IsBackground = true;
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();

			return tcs.Task;
		}

		/// <summary>
		/// Schedules the specified work to execute in a new background thread initialized with STA state.
		/// </summary>
		/// <typeparam name="T">The type of the result returned by the function.</typeparam>
		/// <param name="func">The work to execute in the STA thread.</param>
		/// <param name="logger">A logger to capture any exception that occurs during execution.</param>
		/// <returns>A <see cref="Task"/> that represents the work scheduled to execute in the STA thread.</returns>
		public static Task<T?> Run<T>(Func<Task<T>> func, ILogger? logger)
		{
			var tcs = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);

			Thread thread =
				new(() =>
				{
					PInvoke.OleInitialize();
					using var synchronizationContext = new StaSynchronizationContext();
					var previousContext = SynchronizationContext.Current;
					SynchronizationContext.SetSynchronizationContext(synchronizationContext);

					try
					{
						var task = func();
						synchronizationContext.Run(task);
						tcs.SetResult(task.GetAwaiter().GetResult());
					}
					catch (Exception ex)
					{
						tcs.SetResult(default);
						logger?.LogWarning(ex, "An exception was occurred during the execution within STA.");
					}
					finally
					{
						SynchronizationContext.SetSynchronizationContext(previousContext);
						PInvoke.OleUninitialize();
					}
				});

			thread.IsBackground = true;
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();

			return tcs.Task;
		}

		private sealed class StaSynchronizationContext : SynchronizationContext, IDisposable
		{
			private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
			private readonly Lock _queueLock = new();
			private readonly int _threadId = Environment.CurrentManagedThreadId;
			private bool _acceptingWork = true;

			public override void Post(SendOrPostCallback callback, object? state)
				=> TryPost(callback, state);

			public override void Send(SendOrPostCallback callback, object? state)
			{
				if (Environment.CurrentManagedThreadId == _threadId)
				{
					callback(state);
					return;
				}

				Exception? exception = null;
				using var completed = new ManualResetEventSlim();
				if (!TryPost(
					_ =>
					{
						try
						{
							callback(state);
						}
						catch (Exception ex)
						{
							exception = ex;
						}
						finally
						{
							completed.Set();
						}
					},
					null))
				{
					throw new InvalidOperationException("The STA synchronization context has completed.");
				}
				completed.Wait();

				if (exception is not null)
					ExceptionDispatchInfo.Capture(exception).Throw();
			}

			public void Run(Task task)
			{
				var completionSignal = task.ContinueWith(
					_ => Complete(),
					CancellationToken.None,
					TaskContinuationOptions.ExecuteSynchronously,
					TaskScheduler.Default);

				foreach (var work in _queue.GetConsumingEnumerable())
					work.Callback(work.State);

				completionSignal.GetAwaiter().GetResult();
			}

			private bool TryPost(SendOrPostCallback callback, object? state)
			{
				lock (_queueLock)
				{
					if (!_acceptingWork)
						return false;

					_queue.Add((callback, state));
					return true;
				}
			}

			private void Complete()
			{
				lock (_queueLock)
				{
					if (!_acceptingWork)
						return;

					_acceptingWork = false;
					_queue.CompleteAdding();
				}
			}

			public void Dispose()
			{
				Complete();
				_queue.Dispose();
			}
		}
	}
}
