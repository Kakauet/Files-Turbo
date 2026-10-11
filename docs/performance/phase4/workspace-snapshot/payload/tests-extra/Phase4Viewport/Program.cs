using Files.App.Helpers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

var results = new List<ContractResult>();
Run("ConcurrencyAndPendingCapacity", ConcurrencyAndPendingCapacity);
Run("VisiblePriorityAndRecentViewport", VisiblePriorityAndRecentViewport);
Run("DuplicateRealizationCoalesces", DuplicateRealizationCoalesces);
Run("RecycleBeforeDispatcherSkipsWork", RecycleBeforeDispatcherSkipsWork);
Run("RecycleCancelsRunningNativeWait", RecycleCancelsRunningNativeWait);
Run("SameItemGenerationsNeverOverlap", SameItemGenerationsNeverOverlap);
Run("CompletedLifetimeCancelsDetachedRetry", CompletedLifetimeCancelsDetachedRetry);
Run("NavigationDropsOldRequests", NavigationDropsOldRequests);
Run("VisibleRequestEvictsCachedRequest", VisibleRequestEvictsCachedRequest);
Run("CachedRequestCannotEvictVisibleRequest", CachedRequestCannotEvictVisibleRequest);
Run("RejectedDispatcherDoesNotLeakActiveSlots", RejectedDispatcherDoesNotLeakActiveSlots);
Run("FailureIsObservedAndQueueContinues", FailureIsObservedAndQueueContinues);
Run("DisposeCancelsCompletedAndRunningLifetimes", DisposeCancelsCompletedAndRunningLifetimes);
Run("RepeatedScrollGenerationsRemainBounded", RepeatedScrollGenerationsRemainBounded);
Run("FailedJobCanRetrySameVersion", FailedJobCanRetrySameVersion);
Run("ChangedGitRequestVersionRestartsCompletedWork", ChangedGitRequestVersionRestartsCompletedWork);
Run("DisposedQueueReleasesRealizedItem", DisposedQueueReleasesRealizedItem);

Run("PausedViewportPreservesVisibleWork", PausedViewportPreservesVisibleWork);
Run("ReprioritizedViewportStartsBeforeCachedRows", ReprioritizedViewportStartsBeforeCachedRows);
Run("DisposeWhilePausedCancelsPending", DisposeWhilePausedCancelsPending);
Run("ViewportStartsFromTopWithFourParallelSlots", ViewportStartsFromTopWithFourParallelSlots);

var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../source/src/Files.App/Helpers/ViewportWorkQueue.cs"));
var document = new
{
	generatedAtUtc = DateTime.UtcNow,
	measurementKind = "Production-linked scheduler contracts; no WinUI rendering or native thumbnail measurements",
	productionSource = source,
	productionSourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),
	contracts = results
};
var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("reports/phase4-viewport-contracts.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{results.Count(x => x.Passed)}/{results.Count} viewport contracts passed. {output}");
return results.All(x => x.Passed) ? 0 : 1;

void Run(string name, Action test)
{
	var timer = Stopwatch.StartNew();
	try
	{
		test();
		results.Add(new(name, true, timer.Elapsed.TotalMilliseconds, null));
		Console.WriteLine($"PASS {name}");
	}
	catch (Exception exception)
	{
		results.Add(new(name, false, timer.Elapsed.TotalMilliseconds, exception.ToString()));
		Console.WriteLine($"FAIL {name}: {exception.Message}");
	}
}

static void ConcurrencyAndPendingCapacity()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(4, 128, dispatcher.Post);
	var work = new HeldWork();
	for (var i = 0; i < 1_024; i++)
		queue.Enqueue(new object(), work.Start, 1);
	Equal(4, queue.ActiveCount);
	Equal(128, queue.PendingCount);
	dispatcher.Drain();
	Equal(4, work.Started);
	queue.CancelAll();
	Equal(4, queue.ActiveCount);
	Equal(0, queue.PendingCount);
	Equal(4, work.Cancelled);
	work.Finish();
	dispatcher.Drain();
	Equal(0, queue.ActiveCount);
	Equal(4, work.Started);
}

static void VisiblePriorityAndRecentViewport()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(1, 8, dispatcher.Post);
	var held = new HeldWork();
	queue.Enqueue(new object(), held.Start);
	dispatcher.Drain();
	var order = new List<string>();
	Add("cached-old", 1);
	Add("cached-new", 1);
	Add("visible-old", 0);
	Add("visible-new", 0);
	held.Finish();
	dispatcher.Drain();
	Equal("visible-new,visible-old,cached-new,cached-old", string.Join(',', order));

	void Add(string name, int priority) => queue.Enqueue(new object(), _ =>
	{
		order.Add(name);
		return Task.CompletedTask;
	}, priority);
}

static void DuplicateRealizationCoalesces()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(4, 8, dispatcher.Post);
	var held = new HeldWork();
	var item = new object();
	for (var i = 0; i < 1_000; i++) queue.Enqueue(item, held.Start);
	dispatcher.Drain();
	Equal(1, held.Started);
	held.Finish();
	dispatcher.Drain();
	queue.Enqueue(item, held.Start);
	dispatcher.Drain();
	Equal(1, held.Started);
	queue.Enqueue(item, held.Start, version: 1);
	dispatcher.Drain();
	Equal(2, held.Started);
}

static void RecycleBeforeDispatcherSkipsWork()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(4, 8, dispatcher.Post);
	var item = new object();
	var held = new HeldWork();
	queue.Enqueue(item, held.Start);
	queue.Cancel(item);
	dispatcher.Drain();
	Equal(0, held.Started);
	Equal(0, queue.ActiveCount);
}

static void RecycleCancelsRunningNativeWait()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(1, 8, dispatcher.Post);
	var item = new object();
	var held = new HeldWork();
	queue.Enqueue(item, held.Start);
	dispatcher.Drain();
	queue.Cancel(item);
	Equal(1, held.Cancelled);
	Equal(1, queue.ActiveCount);
	held.Finish();
	dispatcher.Drain();
	Equal(0, queue.ActiveCount);
}

static void SameItemGenerationsNeverOverlap()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(4, 8, dispatcher.Post);
	var item = new object();
	var old = new HeldWork();
	var current = new HeldWork();
	queue.Enqueue(item, old.Start);
	dispatcher.Drain();
	queue.Cancel(item);
	queue.Enqueue(item, current.Start);
	dispatcher.Drain();
	Equal(0, current.Started);
	Equal(1, queue.PendingCount);
	old.Finish();
	dispatcher.Drain();
	Equal(1, current.Started);
	queue.Cancel(item);
	Equal(1, current.Cancelled);
	current.Finish();
	dispatcher.Drain();
	Equal(0, queue.ActiveCount);
}

static void CompletedLifetimeCancelsDetachedRetry()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(1, 8, dispatcher.Post);
	var item = new object();
	CancellationToken lifetime = default;
	var detachedCancelled = 0;
	queue.Enqueue(item, token =>
	{
		lifetime = token;
		token.Register(() => detachedCancelled++);
		return Task.CompletedTask;
	});
	dispatcher.Drain();
	Equal(0, queue.ActiveCount);
	Equal(false, lifetime.IsCancellationRequested);
	queue.Cancel(item);
	Equal(true, lifetime.IsCancellationRequested);
	Equal(1, detachedCancelled);
}

static void NavigationDropsOldRequests()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(1, 8, dispatcher.Post);
	var old = new HeldWork();
	var pendingStarted = 0;
	var currentStarted = 0;
	queue.Enqueue(new object(), old.Start);
	dispatcher.Drain();
	queue.Enqueue(new object(), _ => { pendingStarted++; return Task.CompletedTask; });
	queue.CancelAll();
	queue.Enqueue(new object(), _ => { currentStarted++; return Task.CompletedTask; });
	dispatcher.Drain();
	Equal(0, currentStarted);
	old.Finish();
	dispatcher.Drain();
	Equal(0, pendingStarted);
	Equal(1, currentStarted);
}

static void VisibleRequestEvictsCachedRequest()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(1, 1, dispatcher.Post);
	var held = new HeldWork();
	queue.Enqueue(new object(), held.Start);
	dispatcher.Drain();
	var cached = 0;
	var visible = 0;
	queue.Enqueue(new object(), _ => { cached++; return Task.CompletedTask; }, 1);
	Equal(true, queue.Enqueue(new object(), _ => { visible++; return Task.CompletedTask; }));
	held.Finish();
	dispatcher.Drain();
	Equal(0, cached);
	Equal(1, visible);
}

static void CachedRequestCannotEvictVisibleRequest()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(1, 1, dispatcher.Post);
	var held = new HeldWork();
	queue.Enqueue(new object(), held.Start);
	dispatcher.Drain();
	var visible = 0;
	queue.Enqueue(new object(), _ => { visible++; return Task.CompletedTask; });
	Equal(false, queue.Enqueue(new object(), _ => Task.CompletedTask, 1));
	held.Finish();
	dispatcher.Drain();
	Equal(1, visible);
}

static void RejectedDispatcherDoesNotLeakActiveSlots()
{
	using var queue = new ViewportWorkQueue<object>(4, 8, _ => false);
	for (var i = 0; i < 100; i++) queue.Enqueue(new object(), _ => throw new InvalidOperationException());
	Equal(0, queue.ActiveCount);
	Equal(0, queue.PendingCount);
}

static void FailureIsObservedAndQueueContinues()
{
	var dispatcher = new ManualDispatcher();
	var faults = 0;
	using var queue = new ViewportWorkQueue<object>(1, 8, dispatcher.Post, faulted: _ => faults++);
	var started = 0;
	queue.Enqueue(new object(), _ => throw new InvalidOperationException("Synthetic shell failure"));
	queue.Enqueue(new object(), _ => { started++; return Task.CompletedTask; });
	dispatcher.Drain();
	Equal(1, faults);
	Equal(1, started);
	Equal(0, queue.ActiveCount);
}

static void DisposeCancelsCompletedAndRunningLifetimes()
{
	var dispatcher = new ManualDispatcher();
	var queue = new ViewportWorkQueue<object>(2, 8, dispatcher.Post);
	var completedCancelled = 0;
	queue.Enqueue(new object(), token => { token.Register(() => completedCancelled++); return Task.CompletedTask; });
	var running = new HeldWork();
	queue.Enqueue(new object(), running.Start);
	dispatcher.Drain();
	queue.Dispose();
	Equal(1, completedCancelled);
	Equal(1, running.Cancelled);
	Equal(false, queue.Enqueue(new object(), _ => Task.CompletedTask));
	running.Finish();
	dispatcher.Drain();
	Equal(0, queue.ActiveCount);
}

static void RepeatedScrollGenerationsRemainBounded()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(4, 128, dispatcher.Post);
	for (var round = 0; round < 100; round++)
	{
		var held = new HeldWork();
		for (var i = 0; i < 1_000; i++) queue.Enqueue(new object(), held.Start, i % 10 == 0 ? 0 : 1);
		dispatcher.Drain();
		Equal(4, held.Started);
		Equal(128, queue.PendingCount);
		queue.CancelAll();
		held.Finish();
		dispatcher.Drain();
		Equal(0, queue.ActiveCount);
		Equal(0, queue.PendingCount);
	}
}

static void FailedJobCanRetrySameVersion()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(1, 8, dispatcher.Post);
	var item = new object();
	queue.Enqueue(item, _ => throw new OperationCanceledException());
	dispatcher.Drain();
	var attempts = 0;
	queue.Enqueue(item, _ => { attempts++; return Task.CompletedTask; });
	dispatcher.Drain();
	Equal(1, attempts);
}

static void ChangedGitRequestVersionRestartsCompletedWork()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(1, 8, dispatcher.Post);
	var item = new object();
	var old = new HeldWork();
	queue.Enqueue(item, old.Start, version: 1);
	dispatcher.Drain();
	var nextStarted = 0;
	queue.Enqueue(item, _ => { nextStarted++; return Task.CompletedTask; }, version: 2);
	dispatcher.Drain();
	Equal(0, nextStarted);
	old.Finish();
	dispatcher.Drain();
	queue.Enqueue(item, _ => { nextStarted++; return Task.CompletedTask; }, version: 2);
	dispatcher.Drain();
	Equal(1, nextStarted);
	queue.Enqueue(item, _ => { nextStarted++; return Task.CompletedTask; }, version: 2);
	dispatcher.Drain();
	Equal(1, nextStarted);
}

static void DisposedQueueReleasesRealizedItem()
{
	var (queue, reference) = CreateRealizedItemLifetime();
	GC.Collect();
	GC.WaitForPendingFinalizers();
	GC.Collect();
	Equal(true, reference.IsAlive);
	queue.Dispose();
	GC.Collect();
	GC.WaitForPendingFinalizers();
	GC.Collect();
	Equal(false, reference.IsAlive);
}

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static (ViewportWorkQueue<object> Queue, WeakReference Reference) CreateRealizedItemLifetime()
{
	var dispatcher = new ManualDispatcher();
	var queue = new ViewportWorkQueue<object>(1, 8, dispatcher.Post);
	var item = new object();
	queue.Enqueue(item, _ => Task.CompletedTask);
	dispatcher.Drain();
	return (queue, new WeakReference(item));
}

static void PausedViewportPreservesVisibleWork()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(1, 8, dispatcher.Post);
	var visible = new HeldWork();
	queue.Enqueue(new object(), visible.Start);
	dispatcher.Drain();
	queue.SetPaused(true);
	var next = new HeldWork();
	queue.Enqueue(new object(), next.Start);
	visible.Finish();
	dispatcher.Drain();
	Equal(0, visible.Cancelled);
	Equal(0, next.Started);
	Equal(1, queue.PendingCount);
	queue.SetPaused(false);
	dispatcher.Drain();
	Equal(1, next.Started);
	next.Finish();
	dispatcher.Drain();
}

static void ReprioritizedViewportStartsBeforeCachedRows()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(1, 8, dispatcher.Post);
	queue.SetPaused(true);
	var order = new List<string>();
	var entering = new object();
	queue.Enqueue(entering, _ => { order.Add("entering"); return Task.CompletedTask; }, 1);
	queue.Enqueue(new object(), _ => { order.Add("old-visible"); return Task.CompletedTask; }, 1);
	queue.SetPriority(entering, 0);
	queue.SetPaused(false);
	dispatcher.Drain();
	Equal("entering,old-visible", string.Join(',', order));
}

static void DisposeWhilePausedCancelsPending()
{
	var dispatcher = new ManualDispatcher();
	var queue = new ViewportWorkQueue<object>(1, 8, dispatcher.Post);
	queue.SetPaused(true);
	var work = new HeldWork();
	queue.Enqueue(new object(), work.Start);
	queue.Dispose();
	queue.SetPaused(false);
	dispatcher.Drain();
	Equal(0, queue.ActiveCount);
	Equal(0, queue.PendingCount);
	Equal(0, work.Started);
}

static void ViewportStartsFromTopWithFourParallelSlots()
{
	var dispatcher = new ManualDispatcher();
	using var queue = new ViewportWorkQueue<object>(4, 128, dispatcher.Post, newestFirst: false);
	queue.SetPaused(true);
	var held = new HeldWork();
	var order = new List<int>();
	for (var index = 9; index >= 0; index--)
	{
		var captured = index;
		queue.Enqueue(new object(), token => { order.Add(captured); return held.Start(token); }, index);
	}
	queue.SetPaused(false);
	dispatcher.Drain();
	Equal("0,1,2,3", string.Join(',', order));
	Equal(4, queue.ActiveCount);
	held.Finish();
	dispatcher.Drain();
	Equal("0,1,2,3,4,5,6,7,8,9", string.Join(',', order));
}

static void Equal<T>(T expected, T actual)
{
	if (!EqualityComparer<T>.Default.Equals(expected, actual))
		throw new InvalidOperationException($"Expected {expected}; got {actual}.");
}

sealed class ManualDispatcher : SynchronizationContext
{
	private readonly System.Collections.Concurrent.ConcurrentQueue<Action> actions = new();
	public bool Post(Action action) { actions.Enqueue(action); return true; }
	public override void Post(SendOrPostCallback callback, object? state) => Post(() => callback(state));
	public override SynchronizationContext CreateCopy() => this;
	public void Drain()
	{
		var previous = Current;
		SetSynchronizationContext(this);
		try
		{
			var count = 0;
			while (actions.TryDequeue(out var action))
			{
				if (++count > 100_000) throw new InvalidOperationException("Dispatcher did not quiesce.");
				action();
			}
		}
		finally
		{
			SetSynchronizationContext(previous);
		}
	}
}

sealed class HeldWork
{
	private readonly TaskCompletionSource completion = new();
	public int Started { get; private set; }
	public int Cancelled { get; private set; }
	public Task Start(CancellationToken token)
	{
		Started++;
		token.Register(() => Cancelled++);
		return completion.Task;
	}
	public void Finish() => completion.TrySetResult();
}

record ContractResult(string Name, bool Passed, double ContractDurationMs, string? Failure);
