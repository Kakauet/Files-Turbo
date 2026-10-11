// Copyright (c) Files Community
// Licensed under the MIT License.

using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WinRT;

namespace Files.App.Helpers
{
	/// <summary>Observes XAML callbacks and input delivery, without claiming physical display presentation.</summary>
	internal sealed partial class UiPerformanceViewProbe : IDisposable
	{
		private readonly ItemsControl list;
		private readonly Func<long> getOperationId;
		private readonly Func<(int First, int Last)> getVisibleRange;
		private readonly ScrollViewer? scrollViewer;
		private readonly Timer? dispatcherTimer;
		private long observedOperation;
		private long visibleOperation;
		private long inputOperation;
		private long previousScroll;
		private long previousRender;
		private int pendingDispatcher;
		private int dispatcherSamples;
		private volatile bool disposed;

		[DynamicWindowsRuntimeCast(typeof(ScrollViewer))]
		public UiPerformanceViewProbe(ItemsControl list, Func<long> getOperationId, Func<(int First, int Last)> getVisibleRange)
		{
			this.list = list;
			this.getOperationId = getOperationId;
			this.getVisibleRange = getVisibleRange;
			if (!UiPerformanceTrace.IsEnabled)
				return;

			list.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
			list.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKeyDown), true);
			list.Unloaded += OnUnloaded;
			scrollViewer = list.FindDescendant<ScrollViewer>();
			if (scrollViewer is not null)
				scrollViewer.ViewChanged += OnViewChanged;
			CompositionTarget.Rendered += OnRendered;
			dispatcherTimer = new Timer(QueueDispatcherProbe, null, 100, 100);
		}

		public static void RunAfterNextRender(FrameworkElement element, Action action) => element.RunAfterNextRender(action);

		// The layout calls this directly at non-recycled phase 0, before the lazy scroll probe attaches.
		public static void RecordContainerRealized(long operationId) => UiPerformanceTrace.RecordContainerCallback(operationId);

		private long CurrentOperation()
		{
			var operation = getOperationId();
			if (operation != observedOperation)
			{
				observedOperation = operation;
				previousRender = previousScroll = 0;
				UiPerformanceDisplayProbe.RecordRefreshRate(operation);
			}
			return operation;
		}

		private void OnRendered(object? sender, RenderedEventArgs args)
		{
			if (disposed || !list.IsLoaded || !UiPerformanceTrace.IsEnabled)
				return;
			var operation = CurrentOperation();
			if (operation == 0)
				return;
			var now = Stopwatch.GetTimestamp();
			if (UiPerformanceTrace.ShouldRecordNavigationFrame(operation))
				UiPerformanceTrace.Mark(operation, "NavigationXamlFrameDuration", durationMs: args.FrameDuration.TotalMilliseconds);
			if (previousScroll != 0 && Stopwatch.GetElapsedTime(previousScroll, now).TotalMilliseconds <= 250)
			{
				UiPerformanceTrace.Mark(operation, "ScrollXamlFrameDuration", durationMs: args.FrameDuration.TotalMilliseconds);
				if (previousRender != 0)
					UiPerformanceTrace.Mark(operation, "ScrollRenderCallbackGap", durationMs: Stopwatch.GetElapsedTime(previousRender, now).TotalMilliseconds);
				previousRender = now;
			}
			else
				previousRender = 0;

			if (visibleOperation != operation && HasVisibleName(operation))
			{
				visibleOperation = operation;
				UiPerformanceTrace.Mark(operation, "RenderCallbackWithVisibleContainer", UiPerformanceTrace.GetContainerCallbackCount(operation));
			}
		}

		[DynamicWindowsRuntimeCast(typeof(SelectorItem))]
		[DynamicWindowsRuntimeCast(typeof(TextBlock))]
		private bool HasVisibleName(long operation)
		{
			if (list.Visibility != Visibility.Visible || list.ActualWidth <= 0 || list.ActualHeight <= 0)
				return false;
			var (first, last) = getVisibleRange();
			if (first >= 0 && last >= first)
			{
				for (var index = first; index <= Math.Min(last, first + 127); index++)
				{
					if (list.ContainerFromIndex(index) is SelectorItem container && IsVisibleName(container, operation))
						return true;
				}
			}
			else if (list.ItemsPanelRoot is { } panel)
			{
				foreach (var child in panel.Children.Take(128))
				{
					if (child is SelectorItem container && IsVisibleName(container, operation))
						return true;
				}
			}
			return false;
		}

		[DynamicWindowsRuntimeCast(typeof(TextBlock))]
		private bool IsVisibleName(SelectorItem container, long operation)
		{
			if (container.Content is not ListedItem { ItemPath: { } path } item
				|| !UiPerformanceTrace.IsPublishedItem(operation, path)
				|| container.FindDescendant("ItemName") is not TextBlock name
				|| name.Text != item.Name || string.IsNullOrEmpty(name.Text)
				|| !name.IsLoaded || name.Visibility != Visibility.Visible || name.Opacity <= 0
				|| name.ActualWidth <= 0 || name.ActualHeight <= 0)
				return false;
			var bounds = name.TransformToVisual(list).TransformBounds(new Rect(0, 0, name.ActualWidth, name.ActualHeight));
			return bounds.X < list.ActualWidth && bounds.Y < list.ActualHeight && bounds.X + bounds.Width > 0 && bounds.Y + bounds.Height > 0;
		}

		private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
		{
			var now = Stopwatch.GetTimestamp();
			if (previousScroll == 0 || Stopwatch.GetElapsedTime(previousScroll, now).TotalMilliseconds > 250)
				previousRender = 0;
			previousScroll = now;
			UiPerformanceTrace.Mark(CurrentOperation(), args.IsIntermediate ? "ScrollViewChanging" : "ScrollViewChanged");
		}

		private void OnPointerPressed(object sender, PointerRoutedEventArgs args) => RecordInput("PointerInputDelivered");
		private void OnKeyDown(object sender, KeyRoutedEventArgs args) => RecordInput("KeyInputDelivered");

		private void RecordInput(string milestone)
		{
			var operation = CurrentOperation();
			if (operation != 0 && visibleOperation == operation && inputOperation != operation)
			{
				inputOperation = operation;
				UiPerformanceTrace.Mark(operation, milestone);
			}
		}

		private void QueueDispatcherProbe(object? state)
		{
			if (disposed || !UiPerformanceTrace.IsEnabled || Interlocked.CompareExchange(ref pendingDispatcher, 1, 0) != 0)
				return;
			var queued = Stopwatch.GetTimestamp();
			if (!list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
			{
				Interlocked.Exchange(ref pendingDispatcher, 0);
				if (disposed || !list.IsLoaded)
					return;
				var operation = CurrentOperation();
				UiPerformanceTrace.Mark(operation, "DispatcherQueueDelay", durationMs: Stopwatch.GetElapsedTime(queued).TotalMilliseconds);
				if (++dispatcherSamples % 10 == 0)
				{
					UiPerformanceTrace.Mark(operation, "RealizedContainerCallbacks", UiPerformanceTrace.GetContainerCallbackCount(operation));
					UiPerformanceTrace.Mark(operation, "ManagedHeapBytes", value: GC.GetTotalMemory(false));
					UiPerformanceTrace.Mark(operation, "AllocatedBytesTotal", value: GC.GetTotalAllocatedBytes(false));
					for (var generation = 0; generation <= GC.MaxGeneration; generation++)
						UiPerformanceTrace.Mark(operation, "GCCollectionCount", count: generation, value: GC.CollectionCount(generation));
				}
			}))
				Interlocked.Exchange(ref pendingDispatcher, 0);
		}

		private void OnUnloaded(object sender, RoutedEventArgs args) => Dispose();

		public void Dispose()
		{
			if (disposed)
				return;
			disposed = true;
			dispatcherTimer?.Dispose();
			list.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed));
			list.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKeyDown));
			list.Unloaded -= OnUnloaded;
			if (scrollViewer is not null)
				scrollViewer.ViewChanged -= OnViewChanged;
			CompositionTarget.Rendered -= OnRendered;
		}
	}
}
