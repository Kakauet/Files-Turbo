using System.Drawing;
using System.Text.Json.Nodes;
using System.Windows.Automation;

namespace FilesBench;

/// <summary>
/// One benchmarked application window. Every driver receives the same input sequence:
/// focus the address bar (Ctrl+L), put the path in it, press Enter.
/// </summary>
internal abstract class AppDriver : IDisposable
{
	public abstract string Name { get; }
	public nint Hwnd { get; protected set; }
	public int ProcessId { get; protected set; }
	public AutomationElement Window => AutomationElement.FromHandle(Hwnd);

	public abstract void Start(string initialFolder);

	/// <summary>Requests a window on <paramref name="folder"/> without waiting for it.</summary>
	public abstract void Launch(string folder);

	/// <summary>Milliseconds from <paramref name="start"/> until the launched window is visible.</summary>
	public abstract double WaitForWindow(long start, TimeSpan timeout);

	public abstract string? CurrentFolder();
	protected abstract AutomationElement FocusAddressBox();
	public abstract void SetView(string view);

	public Process Process => System.Diagnostics.Process.GetProcessById(ProcessId);

	public void Focus()
	{
		if (Native.IsIconic(Hwnd))
			Native.ShowWindow(Hwnd, Native.SW_RESTORE);
		for (var attempt = 0; attempt < 10 && Native.GetForegroundWindow() != Hwnd; attempt++)
		{
			// Any synthetic key press lets a background process take the foreground; F24 has no meaning in these apps
			Native.Chord(Native.VK_F24);
			Native.SetForegroundWindow(Hwnd);
			Thread.Sleep(60);
		}
		if (Native.GetForegroundWindow() != Hwnd)
			throw new BenchmarkException($"{Name} could not be brought to the foreground.");
	}

	public void Place(Rectangle bounds)
	{
		Native.SetWindowPos(Hwnd, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, Native.SWP_NOZORDER);
		Thread.Sleep(300);
		var actual = Native.WindowRect(Hwnd);
		if (Math.Abs(actual.Width - bounds.Width) > 2 || Math.Abs(actual.Height - bounds.Height) > 2)
			throw new BenchmarkException($"{Name} window is {actual}, expected {bounds}.");
	}

	/// <summary>Types the path into the address bar without submitting it.</summary>
	public void PrepareNavigation(string path)
	{
		Focus();
		var box = FocusAddressBox();
		((ValuePattern)box.GetCurrentPattern(ValuePattern.Pattern)).SetValue(path);
		Thread.Sleep(80);
		Focus();
	}

	public void Submit() => Native.Chord(Native.VK_RETURN);

	/// <summary>Navigates outside a measurement; a busy window that drops the request gets two more attempts.</summary>
	public void Navigate(string path, TimeSpan timeout)
	{
		for (var attempt = 1; ; attempt++)
		{
			PrepareNavigation(path);
			Submit();
			if (TryWaitForFolder(path, attempt == 3 ? timeout : TimeSpan.FromSeconds(5)))
				return;
			if (attempt == 3)
			{
				SaveWindow("failure");
				throw new BenchmarkException($"{Name} did not reach {path}; it shows {CurrentFolder()}.");
			}
			Native.Chord(Native.VK_ESCAPE);
			Thread.Sleep(500);
		}
	}

	public bool TryWaitForFolder(string path, TimeSpan timeout)
	{
		var watch = Stopwatch.StartNew();
		while (watch.Elapsed < timeout)
		{
			if (string.Equals(CurrentFolder()?.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
				return true;
			Thread.Sleep(50);
		}
		return false;
	}

	public void SaveWindow(string label)
	{
		if (Program.Output is null)
			return;
		using var probe = new ScreenProbe(Native.WindowRect(Hwnd));
		probe.Capture();
		probe.Save(Path.Combine(Program.Output, $"{label}-{Name}-{DateTime.Now:HHmmss-fff}.png"));
	}

	/// <summary>With FILESBENCH_DEBUG set, saves the window and the focused element at a checkpoint.</summary>
	public void Checkpoint(string label)
	{
		if (Environment.GetEnvironmentVariable("FILESBENCH_DEBUG") is null || Program.Output is null)
			return;
		SaveWindow(label);
		string focus;
		try
		{
			var focused = AutomationElement.FocusedElement;
			focus = $"{focused.Current.ControlType.ProgrammaticName} '{focused.Current.Name}' id={focused.Current.AutomationId} pid={focused.Current.ProcessId}";
		}
		catch (Exception ex) { focus = ex.Message; }
		File.AppendAllText(Path.Combine(Program.Output, "debug.log"), $"{DateTime.Now:HH:mm:ss.fff} {Name} {label}: focus {focus}{Environment.NewLine}");
	}

	/// <summary>Throws unless the window shows exactly <paramref name="folder"/>; called before any destructive key.</summary>
	public void AssertShowing(string folder)
	{
		if (!string.Equals(CurrentFolder()?.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
			throw new BenchmarkException($"{Name} is not showing {folder}; no input sent.");
	}

	public AutomationElement FocusSearchBox()
	{
		Native.Chord(Native.VK_CONTROL, 'F');
		return WaitForFocusedEdit();
	}

	public AutomationElement WaitForFocusedEdit(int timeoutMs = 3000)
	{
		var watch = Stopwatch.StartNew();
		while (watch.ElapsedMilliseconds < timeoutMs)
		{
			try
			{
				var focused = AutomationElement.FocusedElement;
				if (focused is not null && focused.Current.ProcessId == ProcessId &&
					focused.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && !((ValuePattern)pattern).Current.IsReadOnly)
					return focused;
			}
			catch (ElementNotAvailableException) { }
			Thread.Sleep(50);
		}
		throw new BenchmarkException($"{Name}: no editable element received focus.");
	}

	/// <summary>Moves keyboard focus to the items view, the largest list in the window.</summary>
	public void FocusItemsView()
	{
		var lists = Window.FindAll(TreeScope.Descendants, new OrCondition(
			new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List),
			new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataGrid)));
		AutomationElement? best = null;
		double area = 0;
		foreach (AutomationElement element in lists)
		{
			var r = element.Current.BoundingRectangle;
			if (!r.IsEmpty && r.Width * r.Height > area)
			{
				best = element;
				area = r.Width * r.Height;
			}
		}
		if (best is null)
			throw new BenchmarkException($"{Name}: items view not found.");
		best.SetFocus();
		Thread.Sleep(250);
	}

	public void SelectItem(string name)
	{
		var watch = Stopwatch.StartNew();
		while (watch.Elapsed < TimeSpan.FromSeconds(5))
		{
			var candidates = Window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name));
			foreach (AutomationElement candidate in candidates)
			{
				// The name may sit on a text element inside the selectable list item
				var walker = TreeWalker.RawViewWalker;
				for (var element = candidate; element is not null && element.Current.ProcessId == ProcessId; element = walker.GetParent(element))
				{
					if (!element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
						continue;
					// UI Automation focus does not always move keyboard focus into Explorer's view; a click does.
					// The point is right of the name, so the click selects instead of starting an in-place rename.
					var r = element.Current.BoundingRectangle;
					var window = Native.WindowRect(Hwnd);
					var point = new Point((int)(r.Right - Math.Min(40, r.Width / 4)), (int)(r.Y + r.Height / 2));
					if (r.IsEmpty || !window.Contains(point))
						throw new BenchmarkException($"{Name}: item {name} is not on screen.");
					Native.Click(point);
					Thread.Sleep(400);
					if (!((SelectionItemPattern)pattern).Current.IsSelected)
						throw new BenchmarkException($"{Name}: item {name} was not selected by the click.");
					return;
				}
			}
			Thread.Sleep(100);
		}
		throw new BenchmarkException($"{Name}: item {name} not selectable.");
	}

	private static readonly string[] ConfirmNames = ["Eliminar", "Sí", "Delete", "Yes"];

	/// <summary>Finds the confirm button of this process's delete dialog (an in-window dialog in Files, a window in Explorer).</summary>
	public InvokePattern WaitForConfirmation(int timeoutMs = 10_000)
	{
		var watch = Stopwatch.StartNew();
		var buttons = new AndCondition(
			new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
			new OrCondition(ConfirmNames.Select(name => (Condition)new PropertyCondition(AutomationElement.NameProperty, name)).ToArray()));
		while (watch.ElapsedMilliseconds < timeoutMs)
		{
			try
			{
				var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, ProcessId));
				foreach (AutomationElement window in windows)
				{
					if (window.FindFirst(TreeScope.Descendants, buttons) is { } button && button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
						return (InvokePattern)pattern;
				}
			}
			catch (ElementNotAvailableException) { }
			Thread.Sleep(50);
		}
		throw new BenchmarkException($"{Name}: confirmation dialog not shown.");
	}

	public (Rectangle? Area, int? Count) FindAnyItemCountRegion()
	{
		var cache = new CacheRequest { TreeFilter = Automation.RawViewCondition, TreeScope = TreeScope.Subtree };
		cache.Add(AutomationElement.NameProperty);
		cache.Add(AutomationElement.BoundingRectangleProperty);
		cache.Add(AutomationElement.IsOffscreenProperty);
		AutomationElement root;
		using (cache.Activate())
			root = AutomationElement.FromHandle(Hwnd);
		var window = Native.WindowRect(Hwnd);
		var stack = new Stack<AutomationElement>([root]);
		var visits = 0;
		while (stack.Count > 0 && visits++ < 20000)
		{
			var element = stack.Pop();
			foreach (AutomationElement child in element.CachedChildren)
				stack.Push(child);
			var name = element.Cached.Name;
			if (string.IsNullOrEmpty(name) || element.Cached.IsOffscreen || !System.Text.RegularExpressions.Regex.IsMatch(name, @"^\s*[\d.,\u00A0\u202F ]+\s+(elementos?|items?)\s*$"))
				continue;
			var r = element.Cached.BoundingRectangle;
			if (!r.IsEmpty && r.Top > window.Top + window.Height / 2)
				return (new Rectangle((int)r.X, (int)r.Y, (int)Math.Min(r.Width, 200), (int)r.Height), int.Parse(new string(name.Where(char.IsDigit).ToArray())));
		}
		return (null, null);
	}

	/// <summary>The status bar text that shows the folder's item count, e.g. "100.000 elementos".</summary>
	public Rectangle? FindItemCountRegion(int count)
	{
		var cache = new CacheRequest { TreeFilter = Automation.RawViewCondition, TreeScope = TreeScope.Subtree };
		cache.Add(AutomationElement.NameProperty);
		cache.Add(AutomationElement.BoundingRectangleProperty);
		cache.Add(AutomationElement.IsOffscreenProperty);
		AutomationElement root;
		using (cache.Activate())
			root = AutomationElement.FromHandle(Hwnd);
		var window = Native.WindowRect(Hwnd);
		var stack = new Stack<AutomationElement>([root]);
		var visits = 0;
		while (stack.Count > 0 && visits++ < 20000)
		{
			var element = stack.Pop();
			foreach (AutomationElement child in element.CachedChildren)
				stack.Push(child);
			var name = element.Cached.Name;
			if (string.IsNullOrEmpty(name) || element.Cached.IsOffscreen || !System.Text.RegularExpressions.Regex.IsMatch(name, @"^\s*[\d.,\u00A0\u202F ]+\s+(elementos?|items?)\s*$"))
				continue;
			var digits = new string(name.Where(char.IsDigit).ToArray());
			var r = element.Cached.BoundingRectangle;
			if (digits == count.ToString() && !r.IsEmpty && r.Top > window.Top + window.Height / 2)
				return new Rectangle((int)r.X, (int)r.Y, (int)Math.Min(r.Width, 200), (int)r.Height);
		}
		return null;
	}

	/// <summary>Whole item tiles (image and label) fully inside <paramref name="viewport"/>, in reading order.</summary>
	public List<Rectangle> FindItemTiles(ISet<string> names, Rectangle viewport)
	{
		var cache = new CacheRequest { TreeFilter = Automation.RawViewCondition, TreeScope = TreeScope.Subtree };
		cache.Add(AutomationElement.NameProperty);
		cache.Add(AutomationElement.BoundingRectangleProperty);
		cache.Add(AutomationElement.IsOffscreenProperty);
		AutomationElement root;
		using (cache.Activate())
			root = AutomationElement.FromHandle(Hwnd);
		var found = new Dictionary<string, Rectangle>(StringComparer.Ordinal);
		var stack = new Stack<AutomationElement>([root]);
		var visits = 0;
		while (stack.Count > 0 && visits++ < 20000)
		{
			var element = stack.Pop();
			foreach (AutomationElement child in element.CachedChildren)
				stack.Push(child);
			var name = element.Cached.Name;
			if (string.IsNullOrEmpty(name) || element.Cached.IsOffscreen || !names.Contains(name))
				continue;
			var r = element.Cached.BoundingRectangle;
			var rect = new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
			if (rect.Width < 32 || rect.Height < 32 || !viewport.Contains(rect))
				continue;
			if (!found.TryGetValue(name, out var existing) || rect.Width * rect.Height > existing.Width * existing.Height)
				found[name] = rect;
		}
		return found.Values.OrderBy(r => r.Top / 20).ThenBy(r => r.Left).ToList();
	}

	/// <summary>Name cells of the visible rows, top to bottom, found through UI Automation.</summary>
	public List<NameRegion> FindNameRegions(ISet<string> names, Rectangle viewport)
	{
		var cache = new CacheRequest { TreeFilter = Automation.RawViewCondition, TreeScope = TreeScope.Subtree };
		cache.Add(AutomationElement.NameProperty);
		cache.Add(AutomationElement.BoundingRectangleProperty);
		cache.Add(AutomationElement.IsOffscreenProperty);
		cache.Add(AutomationElement.AutomationIdProperty);
		AutomationElement root;
		using (cache.Activate())
			root = AutomationElement.FromHandle(Hwnd);

		var found = new Dictionary<string, Rectangle>(StringComparer.Ordinal);
		var queue = new Queue<AutomationElement>([root]);
		var visits = 0;
		while (queue.Count > 0 && visits++ < 20000)
		{
			var element = queue.Dequeue();
			foreach (AutomationElement child in element.CachedChildren)
				queue.Enqueue(child);

			var name = element.Cached.Name;
			if (string.IsNullOrEmpty(name) || element.Cached.IsOffscreen)
				continue;
			// Explorer exposes "name, details" on rows; the bare name is on the cell
			if (!names.Contains(name))
				continue;
			var r = element.Cached.BoundingRectangle;
			if (r.IsEmpty || r.Width < 5 || r.Height < 5)
				continue;
			var rect = new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
			if (!viewport.Contains(rect))
				continue;
			if (!found.TryGetValue(name, out var existing) || rect.Width < existing.Width)
				found[name] = rect;
		}

		return found.Select(pair => new NameRegion(pair.Key, ClipNameCell(pair.Value, NameTextOffset)))
			.OrderBy(region => region.Bounds.Top).ThenBy(region => region.Bounds.Left).ToList();
	}

	/// <summary>Pixels between the name cell's left edge and its text (Explorer's cell includes the icon).</summary>
	protected virtual int NameTextOffset => 0;

	// Compare text only: the start of the cell, without icon or the background of a wide column
	private static Rectangle ClipNameCell(Rectangle cell, int offset)
		=> new(cell.X + offset, cell.Y + 1, Math.Min(cell.Width - offset, 220), Math.Max(4, cell.Height - 2));

	public virtual void Dispose() { }

	protected static AutomationElement? FindById(AutomationElement root, string id)
		=> root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
}

internal sealed record NameRegion(string Name, Rectangle Bounds);

internal sealed class BenchmarkException(string message) : Exception(message);

/// <summary>Files or Files Turbo, launched through its package identity and identified by executable path.</summary>
internal sealed class FilesDriver(string name, string packageFamily, string layout) : AppDriver
{
	public override string Name { get; } = name;
	public string Executable { get; } = Path.Combine(layout, "Files.exe");
	public string LocalState { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", packageFamily, "LocalState");

	/// <summary>Extra settings written before the next start, identical for every Files build.</summary>
	public Dictionary<string, bool> Settings { get; set; } = new() { ["LeaveAppRunning"] = false };

	public override void Start(string initialFolder)
	{
		foreach (var process in OwnProcesses())
			throw new BenchmarkException($"{Name} is already running (pid {process.Id}); close it first.");
		PrepareSettings(initialFolder);
		var start = Stopwatch.GetTimestamp();
		Launch(initialFolder);
		WaitForWindow(start, TimeSpan.FromSeconds(60));
		var watch = Stopwatch.StartNew();
		while (watch.Elapsed < TimeSpan.FromSeconds(60))
		{
			if (FindById(Window, "CurrentPathGet") is not null)
				return;
			Thread.Sleep(100);
		}
		throw new BenchmarkException($"{Name} did not finish opening its window.");
	}

	// The folder comes from the startup settings written by PrepareSettings
	public override void Launch(string folder)
		=> Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{packageFamily}!App") { UseShellExecute = false });

	public override double WaitForWindow(long start, TimeSpan timeout)
	{
		Process? process = null;
		while (Stopwatch.GetElapsedTime(start) < timeout)
		{
			process ??= OwnProcesses().FirstOrDefault();
			if (process is not null)
			{
				process.Refresh();
				var hwnd = process.MainWindowHandle;
				if (hwnd != 0 && Native.IsWindowVisible(hwnd))
				{
					var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
					Hwnd = hwnd;
					ProcessId = process.Id;
					return elapsed;
				}
			}
			Thread.Sleep(process is null ? 10 : 2);
		}
		throw new BenchmarkException($"{Name} did not open a window.");
	}

	/// <summary>Closes the window; with LeaveAppRunning the process stays resident in the background.</summary>
	public void CloseWindowOnly()
	{
		foreach (var process in OwnProcesses())
			process.CloseMainWindow();
		var watch = Stopwatch.StartNew();
		while (watch.Elapsed < TimeSpan.FromSeconds(10) && OwnProcesses().Any(p => { p.Refresh(); return p.MainWindowHandle != 0; }))
			Thread.Sleep(100);
		Thread.Sleep(1000);
	}

	/// <summary>
	/// Gives every Files build the same start: one tab on <paramref name="initialFolder"/>, no restored session.
	/// Only benchmark identities are touched; the user's own Files Turbo settings never are.
	/// </summary>
	private void PrepareSettings(string initialFolder)
	{
		if (packageFamily.StartsWith("FilesTurbo.Experimental", StringComparison.OrdinalIgnoreCase))
			throw new BenchmarkException("Refusing to change the settings of the user's Files Turbo installation.");
		var path = Path.Combine(LocalState, "settings", "user_settings.json");
		var settings = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : new JsonObject();
		settings["ContinueLastSessionOnStartUp"] = false;
		settings["RestoreTabsOnStartup"] = false;
		settings["OpenNewTabOnStartup"] = false;
		settings["OpenSpecificPageOnStartup"] = true;
		settings["TabsOnStartupList"] = new JsonArray(initialFolder);
		settings["LastSessionTabList"] = new JsonArray();
		foreach (var (key, value) in Settings)
			settings[key] = value;
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, settings.ToJsonString(new() { WriteIndented = true }));
	}

	public IEnumerable<Process> OwnProcesses()
		=> System.Diagnostics.Process.GetProcessesByName("Files").Where(p =>
		{
			try { return string.Equals(p.MainModule?.FileName, Executable, StringComparison.OrdinalIgnoreCase); }
			catch { return false; }
		});

	public override string? CurrentFolder()
	{
		var element = FindById(Window, "CurrentPathGet");
		return element is null ? null : ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
	}

	protected override AutomationElement FocusAddressBox()
	{
		Native.Chord(Native.VK_CONTROL, 'L');
		var watch = Stopwatch.StartNew();
		while (watch.Elapsed < TimeSpan.FromSeconds(3))
		{
			var box = Window.FindFirst(TreeScope.Descendants, new AndCondition(
				new PropertyCondition(AutomationElement.AutomationIdProperty, "PART_TextBox"),
				new PropertyCondition(AutomationElement.IsOffscreenProperty, false)));
			if (box is not null)
				return box;
			Thread.Sleep(50);
		}
		throw new BenchmarkException($"{Name} address box not found.");
	}

	public override void SetView(string view)
	{
		Focus();
		// Files layout shortcuts: Ctrl+Shift+1 Details, Ctrl+Shift+4 Grid
		Native.Chord(Native.VK_CONTROL, Native.VK_SHIFT, view == "Details" ? (ushort)'1' : (ushort)'4');
		Thread.Sleep(600);
	}

	public void Close()
	{
		foreach (var process in OwnProcesses())
		{
			process.CloseMainWindow();
			if (!process.WaitForExit(5000))
				process.Kill();
			process.WaitForExit(5000);
		}
	}

	public override void Dispose() => Close();
}

/// <summary>A new File Explorer window, owned through the Shell.Application window list.</summary>
internal sealed class ExplorerDriver : AppDriver
{
	private dynamic? window;

	public override string Name => "Explorer";

	protected override int NameTextOffset => 24;

	private dynamic? shell;
	private HashSet<long> existing = [];

	public override void Start(string initialFolder)
	{
		var start = Stopwatch.GetTimestamp();
		Launch(initialFolder);
		WaitForWindow(start, TimeSpan.FromSeconds(20));
	}

	public override void Launch(string folder)
	{
		shell ??= Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
		existing = [];
		foreach (var w in shell!.Windows())
			existing.Add((long)w.HWND);
		shell.Explore(folder);
	}

	public override double WaitForWindow(long start, TimeSpan timeout)
	{
		while (Stopwatch.GetElapsedTime(start) < timeout)
		{
			foreach (var w in shell!.Windows())
			{
				var hwnd = (nint)(long)w.HWND;
				if (existing.Contains(hwnd) || !Native.IsWindowVisible(hwnd))
					continue;
				var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
				window = w;
				Hwnd = hwnd;
				Native.GetWindowThreadProcessId(Hwnd, out var pid);
				ProcessId = pid;
				return elapsed;
			}
			Thread.Sleep(2);
		}
		throw new BenchmarkException("Explorer window not created.");
	}

	public override string? CurrentFolder()
	{
		string url = window!.LocationURL;
		return string.IsNullOrEmpty(url) ? null : new Uri(url).LocalPath;
	}

	protected override AutomationElement FocusAddressBox()
	{
		Native.Chord(Native.VK_CONTROL, 'L');
		Thread.Sleep(150);
		var focused = AutomationElement.FocusedElement;
		if (focused.Current.ProcessId != ProcessId || !focused.TryGetCurrentPattern(ValuePattern.Pattern, out _))
			throw new BenchmarkException("Explorer address box not focused.");
		return focused;
	}

	public override void SetView(string view)
	{
		var document = window!.Document;
		document.CurrentViewMode = view == "Details" ? 4 : 1;
		if (view != "Details")
			document.IconSize = 96;
		document.SortColumns = "prop:System.ItemNameDisplay;";
		document.GroupBy = "System.Null";
		Thread.Sleep(600);
	}

	public void Close()
	{
		try { window?.Quit(); } catch { }
	}

	public override void Dispose() => Close();
}
