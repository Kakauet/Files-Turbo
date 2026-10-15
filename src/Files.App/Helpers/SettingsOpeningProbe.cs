// Temporary measurement helper; excluded from the production build.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Files.App.Views.Settings;
using Files.App.ViewModels.Settings;
using System.Globalization;
using System.IO;
using Windows.Foundation;
using Windows.Storage;

namespace Files.App.Helpers;

internal static class SettingsOpeningProbe
{
    private static readonly List<string> lines = [];
    private static readonly long origin = Stopwatch.GetTimestamp();
    private static long request;
    private static bool started;
    private static string label = "", target = "", previous = "";
    private static int stable;
    private static double observerMs;
    private static Microsoft.UI.Xaml.Controls.Page? observedPage;
    private static FrameworkElement[] observed = [];
    private static TaskCompletionSource? ready;
    internal static SettingsPage? Page;
    private static string F(double value) => value.ToString("F2", CultureInfo.InvariantCulture);
    internal static void Stage(string name, long start) { if (request != 0) lines.Add($"STAGE|{label}|{name}|{F(Stopwatch.GetElapsedTime(start).TotalMilliseconds)}|at={F(Stopwatch.GetElapsedTime(request).TotalMilliseconds)}"); }
    private static void Log(string value) => lines.Add(value);

    internal static void Start()
    {
        if (started || Windows.ApplicationModel.Package.Current.Id.Name != "FilesTurbo.Dev01") return;
        started = true;
        if (!File.Exists(Path.Combine(ApplicationData.Current.LocalFolder.Path, "settings-probe.txt"))) return;
        CompositionTarget.Rendering += Render;
        _ = Script();
    }

    private static IEnumerable<FrameworkElement> LogicalElements(object parent)
    {
        if (parent is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is FrameworkElement element) yield return element;
                foreach (var item in LogicalElements(child)) yield return item;
            }
        }
    }

    private static string Geometry()
    {
        if (Page?.FindName("SettingsContentFrame") is not Frame { Content: Microsoft.UI.Xaml.Controls.Page content } ||
            content.GetType().Name != target || !content.IsLoaded || content.ActualWidth < 100 || content.ActualHeight < 30 ||
            Page.FindName("SettingsContentScrollViewer") is not ScrollViewer { ViewportHeight: > 100 } scroll) return "";
        if (!ReferenceEquals(observedPage, content))
        {
            observedPage = content;
            observed = LogicalElements(content.Content).ToArray();
        }
        if (target == "GeneralPage" && (content.FindName("AppLanguagesComboBox") is not ComboBox { IsEnabled: true, SelectedIndex: >= 0, ActualWidth: > 0 } ||
            content.FindName("DateFormatChooser") is not ComboBox { IsEnabled: true, SelectedIndex: >= 0, ActualWidth: > 0 })) return "";
        var geometry = new List<string> { F(content.ActualWidth), F(content.ActualHeight), F(scroll.ViewportHeight), F(scroll.ExtentHeight) };
        foreach (var e in observed)
        {
            if (!e.IsLoaded || e.Visibility != Visibility.Visible || e.ActualWidth <= 0 || e.ActualHeight <= 0) continue;
            var p = e.TransformToVisual(Page).TransformPoint(new Point());
            if (p.Y + e.ActualHeight < 0 || p.Y > Page.ActualHeight) continue;
            geometry.Add($"{e.GetType().Name}:{F(p.X)},{F(p.Y)},{F(e.ActualWidth)},{F(e.ActualHeight)}");
        }
        return string.Join(";", geometry);
    }

    private static void Render(object? sender, object args)
    {
        if (ready is null || ready.Task.IsCompleted) return;
        var observer = Stopwatch.GetTimestamp();
        var geometry = Geometry();
        observerMs += Stopwatch.GetElapsedTime(observer).TotalMilliseconds;
        if (geometry.Length == 0) { stable = 0; return; }
        stable = geometry == previous ? stable + 1 : 0;
        previous = geometry;
        Log($"LAYOUT|{label}|{F(Stopwatch.GetElapsedTime(request).TotalMilliseconds)}|stable={stable}");
        if (stable < 1) return;
        Log($"READY|{label}|{F(Stopwatch.GetElapsedTime(request).TotalMilliseconds)}|{geometry}");
        ready.TrySetResult();
    }

    private static async Task Measure(string name, string page, Func<Task> action)
    {
        label = name; target = page; previous = ""; stable = 0;
        observerMs = 0;
        ready = new(); request = Stopwatch.GetTimestamp();
        await action();
        var timeout = Task.Delay(15000);
        if (await Task.WhenAny(ready.Task, timeout) != ready.Task) throw new TimeoutException(name);
        var atReady = previous;
        await Task.Delay(300);
        Log($"VERIFY|{name}|stable={atReady == Geometry()}");
        Log($"OBSERVER|{name}|{F(observerMs)}");
        request = 0; ready = null;
        observedPage = null; observed = [];
    }

    private static void Memory(string name)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var process = Process.GetCurrentProcess();
        Log($"MEMORY|{name}|{GC.GetTotalMemory(false)}|{process.PrivateMemorySize64}|{process.WorkingSet64}");
    }

    private static async Task Script()
    {
        var local = ApplicationData.Current.LocalFolder.Path;
        try
        {
            await MainPageViewModel.StartupTabsLoadedTask;
            Log($"STARTUP-LOADED|{F((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds)}");
            MainWindow.Instance.AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 1000));
            await Task.Delay(3000);
            Log($"STARTUP|{F(Stopwatch.GetElapsedTime(origin).TotalMilliseconds)}|tabs={MainPageViewModel.AppInstances.Count}");
            Memory("before");
            var commands = Ioc.Default.GetRequiredService<ICommandManager>();
            for (int opening = 0; opening < 3; opening++)
            {
                await Measure($"open-{opening}", "GeneralPage", () => commands.OpenSettings.ExecuteAsync());
                if (opening == 0)
                {
                    for (int round = 0; round < 2; round++)
                        foreach (var kind in Enum.GetValues<SettingsPageKind>())
                            await Measure($"section-{round}-{kind}", kind.ToString(), () => { Page!.NavigateTo(new SettingsNavigationParams { PageKind = kind }); return Task.CompletedTask; });
                    var start = Stopwatch.GetTimestamp();
                    Page!.ApplySearchQuery("a");
                    Log($"SEARCH|first|{F(Stopwatch.GetElapsedTime(start).TotalMilliseconds)}|{Page.ViewModel.SearchResults.Count}");
                    Log("INDEX|" + string.Join(";", SettingsSearchIndexer.BuildIndex().Select(x => $"{x.PageKind}:{x.HeaderText}:{x.ParentHeaderText}")));
                    Page.ApplySearchQuery("");
                    Memory("visited");
                }
                await commands.CloseSelectedTab.ExecuteAsync();
                Page = null;
                await Task.Delay(600);
            }
            Memory("closed");
            Log("DONE");
        }
        catch (Exception e) { Log("ERROR " + e); }
        finally
        {
            CompositionTarget.Rendering -= Render;
            File.WriteAllLines(Path.Combine(local, "settings-probe.log"), lines);
        }
    }
}
