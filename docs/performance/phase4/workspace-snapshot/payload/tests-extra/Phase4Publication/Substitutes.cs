global using System.Collections;
global using System.Collections.Concurrent;
global using System.Collections.Specialized;
global using System.ComponentModel;
global using System.Diagnostics;
global using Files.App.Helpers;
global using Files.App.Utils;
global using Files.App.Utils.Storage;
global using Files.App.ViewModels;

namespace WinRT
{
	[AttributeUsage(AttributeTargets.Class)]
	public sealed class GeneratedWinRTExposedTypeAttribute : Attribute { }
	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	public sealed class GeneratedWinRTExposedExternalTypeAttribute(Type type) : Attribute
	{
		public Type Type { get; } = type;
	}
}

namespace Files.App.Utils
{
	public sealed class ListedItem(string name) : IGroupableItem
	{
		public string Name { get; } = name;
		public string? Key { get; set; }
		public object? FileImage { get; set; }
		public bool ItemPropertiesInitialized { get; set; }
	}
}

namespace Files.App.Helpers
{
	public static class ContractEnumerableExtensions
	{
		public static void ForEach<T>(this IEnumerable<T> items, Action<T> action)
		{
			foreach (var item in items)
				action(item);
		}
		public static string GetLocalizedResource(this string value) => value;
	}

	public static class UiPerformanceTrace
	{
		public static bool IsEnabled { get; set; }
		public static List<TraceEntry> Entries { get; } = [];
		public static void Mark(long operation, string milestone, int count = 0, double durationMs = 0, long value = 0)
		{
			if (IsEnabled && operation != 0)
				Entries.Add(new(operation, milestone, count, durationMs, value));
		}
		public sealed record TraceEntry(long Operation, string Milestone, int Count, double DurationMs, long Value);
	}

	public static class AppMemoryHelper
	{
		public static int Activities { get; set; }
		public static void NotifyActivity() => Activities++;
	}
}

namespace Files.App.ViewModels
{
	public enum GroupOption { None, FileType }
	public sealed class LayoutPreferencesManager
	{
		public GroupOption DirectoryGroupOption { get; set; }
	}
	public sealed class GroupedHeaderViewModel
	{
		public string? Key { get; set; }
		public string? Text { get; set; }
		public string? CountText { get; set; }
		public bool Initialized { get; set; }
		public void PausePropertyChangedNotifications() { }
		public void ResumePropertyChangedNotifications(bool notify = true) { }
	}

	// The dispatcher is controlled; every queued action is completed once by the test pump.
	public sealed class PublicationDispatcher
	{
		private readonly ConcurrentQueue<(Action Action, TaskCompletionSource Completion)> actions = new();
		public int PendingCount => actions.Count;
		public Task EnqueueOrInvokeAsync(Action action)
		{
			var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			actions.Enqueue((action, completion));
			return completion.Task;
		}
		public bool RunOne()
		{
			if (!actions.TryDequeue(out var entry))
				return false;
			try
			{
				entry.Action();
				entry.Completion.SetResult();
			}
			catch (Exception ex)
			{
				entry.Completion.SetException(ex);
			}
			return true;
		}
	}

	public partial class ShellViewModel : IDisposable
	{
		private readonly CancellationTokenSource addFilesCTS = new();
		private readonly SemaphoreSlim bulkOperationSemaphore = new(1, 1);
		private readonly PublicationDispatcher dispatcherQueue = new();
		private readonly LayoutPreferencesManager folderSettings = new();
		private readonly List<ListedItem> pendingSearchResults = [];
		private ConcurrentCollection<ListedItem> filesAndFolders = [];
		public BulkConcurrentObservableCollection<ListedItem> FilesAndFolders { get; } = [];
		public string? FilesAndFoldersFilter { get; set; }
		public long PerformanceOperationId { get; set; } = 41;
		public int EmptyTextUpdates { get; private set; }
		public int NetworkInfoUpdates { get; private set; }
		public int GroupOrderCalls { get; private set; }
		public int DirectoryNotifications { get; private set; }
		public int SemaphoreCount => bulkOperationSemaphore.CurrentCount;
		public int PendingSearchCount => pendingSearchResults.Count;
		public PublicationDispatcher Dispatcher => dispatcherQueue;
		public event EventHandler? DirectoryInfoUpdated;

		public ShellViewModel(IEnumerable<ListedItem>? displayed = null)
		{
			var items = displayed?.ToList() ?? [];
			FilesAndFolders.AddRange(items);
			filesAndFolders.AddRange(items);
			DirectoryInfoUpdated += (_, _) => DirectoryNotifications++;
		}
		public void SetSource(IEnumerable<ListedItem> items) => filesAndFolders = new(items);
		public void AddSourceItem(ListedItem item) => filesAndFolders.Add(item);
		public void SetGroupPreference(bool grouped) => folderSettings.DirectoryGroupOption = grouped ? GroupOption.FileType : GroupOption.None;
		public void SetCollectionGrouped()
		{
			FilesAndFolders.ItemGroupKeySelector = _ => "group";
			FilesAndFolders.ResetGroups();
		}
		public void AddPendingSearchResult(ListedItem item) => pendingSearchResults.Add(item);
		public void CancelNavigation() => addFilesCTS.Cancel();
		public Task HoldPublicationAsync() => bulkOperationSemaphore.WaitAsync();
		public void ReleasePublication() => bulkOperationSemaphore.Release();
		private void OrderGroups() => GroupOrderCalls++;
		private void UpdateEmptyTextType() => EmptyTextUpdates++;
		private void UpdateNetworkAvailabilityInfoBar() => NetworkInfoUpdates++;
		public void Dispose()
		{
			addFilesCTS.Dispose();
			bulkOperationSemaphore.Dispose();
		}
	}
}

public static class Strings
{
	public static string GroupItemsCount_Plural => "{0} items";
	public static string GroupItemsCount_Singular => "{0} item";
}

public static class App
{
	public static ContractLogger Logger { get; } = new();
}
namespace Files.App
{
	public static class App
	{
		public static global::ContractLogger Logger => global::App.Logger;
	}
}
public sealed class ContractLogger
{
	public List<Exception> Errors { get; } = [];
	public void LogWarning(Exception exception, string message) => Errors.Add(exception);
}

// A controlled consumer makes row/selection retention observable without claiming WinUI coverage.
public sealed class PublicationObserver
{
	private readonly BulkConcurrentObservableCollection<ListedItem> source;
	public List<NotifyCollectionChangedEventArgs> Changes { get; } = [];
	public Dictionary<ListedItem, object> Rows { get; } = new(ReferenceEqualityComparer.Instance);
	public ListedItem? SelectedItem { get; set; }
	public PublicationObserver(BulkConcurrentObservableCollection<ListedItem> source)
	{
		this.source = source;
		foreach (var item in source)
			Rows.Add(item, new object());
		source.CollectionChanged += Changed;
	}
	private void Changed(object? sender, NotifyCollectionChangedEventArgs args)
	{
		Changes.Add(args);
		if (args.Action == NotifyCollectionChangedAction.Reset)
		{
			Rows.Clear();
			SelectedItem = null;
			foreach (var item in source)
				Rows.Add(item, new object());
		}
		else if (args.Action == NotifyCollectionChangedAction.Add)
		{
			foreach (ListedItem item in args.NewItems!)
				Rows.Add(item, new object());
		}
	}
}
