global using System.Diagnostics;
global using Files.App.Helpers;
global using Files.App.Utils;
global using Files.App.ViewModels;
global using DispatcherQueueTimer=Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Microsoft.UI.Dispatching
{
    public enum DispatcherQueuePriority { Low, Normal }
    public sealed class DispatcherQueueTimer
    {
        public TimeSpan Interval;
        public bool IsRepeating;
        public bool IsRunning;
        public int Starts;
        public int Stops;
        public event Action<DispatcherQueueTimer,object?>? Tick;
        public void Start() {Starts++; IsRunning=true;}
        public void Stop() {Stops++; IsRunning=false;}
        public void Fire() {if(!IsRunning)return; IsRunning=IsRepeating; Tick?.Invoke(this,null);}
    }
}
namespace Files.App
{
    public static class App { public static StubLogger Logger { get; } = new(); }
    public sealed class StubLogger { public int Faults; public void LogWarning(Exception error, string message) => Faults++; }
}
namespace Files.App.Helpers
{
    internal static class UiPerformanceTrace
    {
        public static bool IsEnabled;
        public static List<(long Operation,string Milestone,int Count,double DurationMs,long Value)> Records {get;}=new();
        public static void Mark(long operation,string milestone,int count=0,double durationMs=0,long value=0) {if(IsEnabled&&operation!=0) Records.Add((operation,milestone,count,durationMs,value));}
    }
    internal sealed class UiPerformanceViewProbe : IDisposable
    {
        public UiPerformanceViewProbe(Files.App.Views.Layouts.StubItemsControl items,Func<long> operation,Func<(int,int)> visibleRange) { }
        public void Dispose() { }
    }
    internal static class AppMemoryHelper { public static int ActivityNotifications; public static void NotifyActivity()=>ActivityNotifications++; }
}
namespace Files.App.Utils
{
    public enum GitProperties { None, Status, Commit, All }
    public interface IGitItem { bool StatusPropertiesInitialized { get; set; } bool CommitPropertiesInitialized { get; set; } }
    public class ListedItem { public bool ItemPropertiesInitialized; public bool IsRealChanges; public string? ItemTooltipText; public int Epoch; public int Thumbnails; public int MetadataCalls; public void UpdateReal()=>IsRealChanges=false; }
    public sealed class GitItem : ListedItem, IGitItem { public bool StatusPropertiesInitialized { get; set; } public bool CommitPropertiesInitialized { get; set; } }
}
namespace Files.App.ViewModels
{
    public sealed class ShellViewModel
    {
        public readonly record struct InitialThumbnailLoadResult(ListedItem? Item, int Epoch, CancellationToken Token, bool LoadCompleted);
        public bool IsScrollInFlight;
        public long PerformanceOperationId;
        public GitProperties EnabledGitProperties;
        public Func<ListedItem,CancellationToken,Task>? ThumbnailProvider;
        public List<ListedItem> FilesAndFolders { get; }=new();
        public Func<ListedItem,CancellationToken,Task>? MetadataProvider;
        public List<ListedItem> ThumbnailStartOrder { get; }=new();
        public List<bool> ScrollStateChanges {get;}=new();
        public void NotifyScrollStateChanged(bool active) {IsScrollInFlight=active; ScrollStateChanges.Add(active);}
        public void ReleaseExtendedProperties(ListedItem item)=>item.ItemPropertiesInitialized=false;
        public void CancelExtendedPropertiesLoadingForItem(ListedItem item) { }
        public void CancelExtendedPropertiesLoading() { }
        public bool IsViewportThumbnailCurrent(ListedItem item,InitialThumbnailLoadResult initial) => initial.LoadCompleted && ReferenceEquals(item,initial.Item) && item.Epoch==initial.Epoch && !initial.Token.IsCancellationRequested;
        public async Task<InitialThumbnailLoadResult> LoadViewportThumbnailAsync(ListedItem item,CancellationToken token) { token.ThrowIfCancellationRequested(); item.Thumbnails++; ThumbnailStartOrder.Add(item); if(ThumbnailProvider is not null) await ThumbnailProvider(item,token); token.ThrowIfCancellationRequested(); return new InitialThumbnailLoadResult(item,item.Epoch,token,true); }
        public async Task LoadExtendedItemPropertiesAsync(ListedItem item,CancellationToken token,InitialThumbnailLoadResult thumbnail) { item.MetadataCalls++; if(MetadataProvider is not null) await MetadataProvider(item,token); token.ThrowIfCancellationRequested(); item.ItemPropertiesInitialized=true; }
        public Task LoadGitPropertiesAsync(IGitItem item,CancellationToken token) { token.ThrowIfCancellationRequested(); if(EnabledGitProperties is GitProperties.Status or GitProperties.All) item.StatusPropertiesInitialized=true; if(EnabledGitProperties is GitProperties.Commit or GitProperties.All) item.CommitPropertiesInitialized=true; return Task.CompletedTask; }
    }
}
namespace Files.App.Views.Layouts
{
    public sealed class ScrollViewerViewChangedEventArgs : EventArgs {public bool IsIntermediate;}
    public sealed class ScrollViewer
    {
        public double HorizontalOffset;
        public double VerticalOffset;
        public float ZoomFactor=1;
        public double ExtentHeight;
        public double ViewportHeight;
        public event EventHandler<ScrollViewerViewChangedEventArgs>? ViewChanged;
        public void Raise(bool intermediate)=>ViewChanged?.Invoke(this,new() {IsIntermediate=intermediate});
    }
    public sealed class ContainerContentChangingEventArgs
    {
        public SelectorItem ItemContainer {get;init;}=null!;
        public object Item {get;init;}=null!;
        public int ItemIndex {get;init;}
        public uint Phase {get;init;}
        public bool InRecycleQueue {get;init;}
        public int RegisteredCallbacks;
        public void RegisterUpdateCallback(uint phase,Action<object?,ContainerContentChangingEventArgs> callback)=>RegisteredCallbacks++;
    }
    public sealed class SelectorItem { public ListedItem? Content; public int Index; }
    public sealed class StubPanel { public List<SelectorItem> Children { get; }=new(); }
    public sealed class StubItemsControl
    {
        public StubPanel ItemsPanelRoot { get; }=new();
        public ScrollViewer ScrollViewer {get;}=new();
        public T? FindDescendant<T>() where T:class=>ScrollViewer as T;
        public int IndexFromContainer(SelectorItem container)=>container.Index;
        public object? ContainerFromIndex(int index)=>ItemsPanelRoot.Children.FirstOrDefault(container=>container.Index==index);
    }
    public sealed class ShellPage { public ShellViewModel? ShellViewModel; }
    public static class ShellPageExtensions {public static ShellViewModel GetRequiredShellViewModel(this ShellPage? page)=>page?.ShellViewModel??throw new InvalidOperationException("No Shell view model.");}
    public sealed class StubDispatcher : SynchronizationContext
    {
        private readonly Queue<Action> normalActions=new();
        private readonly Queue<Action> lowActions=new();
        public List<DispatcherQueueTimer> Timers {get;}=new();
        public int PendingCount=>normalActions.Count+lowActions.Count;
        public int NormalPending=>normalActions.Count;
        public int LowPending=>lowActions.Count;
        public override void Post(SendOrPostCallback callback,object? state)=>normalActions.Enqueue(()=>callback(state));
        public bool TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority priority,Action action) {(priority==Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal?normalActions:lowActions).Enqueue(action); return true;}
        public DispatcherQueueTimer CreateTimer() {var timer=new DispatcherQueueTimer(); Timers.Add(timer); return timer;}
        public void EnqueueLow(Action action)=>TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,action);
        public void DrainNormal(int maximum=20000)=>DrainCore(true,maximum);
        public void Drain(int maximum=20000)=>DrainCore(false,maximum);
        private void DrainCore(bool normalOnly,int maximum)
        {
            var count=0;
            while(normalActions.TryDequeue(out var action)||!normalOnly&&lowActions.TryDequeue(out action))
            {
                if(++count>maximum)throw new InvalidOperationException("Refill did not reach quiescence.");
                var previous=Current; SetSynchronizationContext(this); try {action();} finally {SetSynchronizationContext(previous);}
            }
        }
    }
    public partial class BaseLayoutPage
    {
        private readonly Dictionary<SelectorItem,ListedItem> realizedItems=new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<ListedItem,ShellViewModel.InitialThumbnailLoadResult> viewportThumbnails=new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<ListedItem,int> metadataFailures=new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<ListedItem,int> thumbnailFailures=new(ReferenceEqualityComparer.Instance);
        private ViewportWorkQueue<ListedItem>? viewportWork;
        private ViewportWorkQueue<ListedItem>? metadataWork;
        private bool viewportRefillQueued;
        private bool isReloadingRealizedItems;
        private bool isDisposed;
        private ScrollViewer? deferScrollViewer;
        private double lastScrollHorizontalOffset;
        private double lastScrollVerticalOffset;
        private float lastScrollZoomFactor;
        private DispatcherQueueTimer? scrollSettleTimer;
        private UiPerformanceViewProbe? performanceViewProbe;
        private object? navigationArguments=new();
        private (int First,int Last) visibleRange=(-1,-1);
        public StubItemsControl ItemsControl { get; }=new();
        public ShellPage? ParentShellPageInstance;
        public StubDispatcher DispatcherQueue { get; }=new();
        public int CachedStages=>viewportThumbnails.Count;
        public int MetadataPending=>metadataWork?.PendingCount??0;
        public int FailedRows=>metadataFailures.Count;
        public BaseLayoutPage(ShellViewModel shell) => ParentShellPageInstance=new ShellPage { ShellViewModel=shell };
        public SelectorItem Track(ListedItem item,int? index=null) { var container=new SelectorItem {Content=item,Index=index??realizedItems.Count}; realizedItems.Add(container,item); ItemsControl.ItemsPanelRoot.Children.Add(container); ParentShellPageInstance!.ShellViewModel!.FilesAndFolders.Add(item); return container; }
        public ContainerContentChangingEventArgs RealizePhaseZero(ListedItem item,int index)
        {
            var container=new SelectorItem {Content=item,Index=index}; ItemsControl.ItemsPanelRoot.Children.Add(container);
            ParentShellPageInstance!.ShellViewModel!.FilesAndFolders.Add(item);
            var args=new ContainerContentChangingEventArgs {ItemContainer=container,Item=item,ItemIndex=index,Phase=0};
            RefreshItem(container,item,false,args); return args;
        }
        public void HookScroll()=>HookScrollDeferTracking();
        public void FireScrollSettle()=>scrollSettleTimer?.Fire();
        public void Start() { foreach(var pair in realizedItems.ToArray()) QueueItemProperties(pair.Key,pair.Value,ParentShellPageInstance!.ShellViewModel!); }
        public void StartVisible()=>LoadVisibleItemsAfterScrollSettled();
        public void RequestVisibleRefill()=>RefillVisibleItemWork();
        public void SetVisibleRange(int first,int last)=>visibleRange=(first,last);
        public void Reprioritize()=>ReprioritizeViewportWork();
        public Task Reload()=>ReloadRealizedItemPropertiesAsync();
        private (int First,int Last) GetVisibleIndexRange()=>visibleRange;
        public void Retry(SelectorItem container,ListedItem item) => QueueItemProperties(container,item,ParentShellPageInstance!.ShellViewModel!);
        private static void UpdateItemToolTip(SelectorItem container,string? text) { }
        private void InitializeDrag(SelectorItem container,ListedItem item) { }
        public void Dispose() {isDisposed=true; navigationArguments=null; UnhookScrollDeferTracking();}
    }
}
