param(
    [Parameter(Mandatory)][string]$TracePath,
    [Parameter(Mandatory)][string]$OutputPath,
    [int]$TraceProcessId = 19100,
    [string]$WindowsJson
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$traceLibrary = Get-ChildItem -LiteralPath (Join-Path $taskRoot 'tooling/diagnostics/.store/dotnet-trace/10.0.750501') -Recurse -Filter Microsoft.Diagnostics.Tracing.TraceEvent.dll | Select-Object -First 1
$fastSerialization = Join-Path $traceLibrary.DirectoryName 'Microsoft.Diagnostics.FastSerialization.dll'
[System.Reflection.Assembly]::LoadFrom($fastSerialization) | Out-Null
[System.Reflection.Assembly]::LoadFrom($traceLibrary.FullName) | Out-Null

# Offline diagnostic code only. No application binaries or global tracing are changed.
$taskReferences = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName) + @($traceLibrary.FullName, $fastSerialization)
Add-Type -ReferencedAssemblies $taskReferences -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.EventPipe;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

public static class Phase3EventPipeAnalysis
{
    public sealed class Window
    {
        public string Name { get; set; }
        public DateTime StartUtc { get; set; }
        public DateTime EndUtc { get; set; }
        public int Samples { get; set; }
        public List<ThreadSummary> Threads { get; set; } = new List<ThreadSummary>();
        public List<StackSummary> UiStacks { get; set; } = new List<StackSummary>();
    }
    public sealed class ThreadSummary
    {
        public int ThreadId { get; set; }
        public int Samples { get; set; }
        public int ManagedSamples { get; set; }
        public int ExternalSamples { get; set; }
        public bool HasProgramMain { get; set; }
        public List<StackSummary> TopLeaves { get; set; } = new List<StackSummary>();
    }
    public sealed class StackSummary
    {
        public string Stack { get; set; }
        public int Samples { get; set; }
    }
    public sealed class Suspension
    {
        public string Reason { get; set; }
        public int ThreadId { get; set; }
        public int ClrInstanceId { get; set; }
        public DateTime StartUtc { get; set; }
        public DateTime EndUtc { get; set; }
        public double StartRelativeMs { get; set; }
        public double EndRelativeMs { get; set; }
        public double SuspensionAcquireMs { get; set; }
        public double FullySuspendedMs { get; set; }
        public double TotalSuspensionMs { get; set; }
        public bool HasSuspendStop { get; set; }
    }
    public sealed class Collection
    {
        public int Count { get; set; }
        public string Reason { get; set; }
        public string Type { get; set; }
        public int Depth { get; set; }
        public DateTime StartUtc { get; set; }
        public double StartRelativeMs { get; set; }
    }
    public sealed class HeapSnapshot
    {
        public DateTime Utc { get; set; }
        public long TotalHeapSize { get; set; }
        public long TotalPromoted { get; set; }
        public long Gen2Bytes { get; set; }
        public long LohBytes { get; set; }
    }
    public sealed class Result
    {
        public string TraceFile { get; set; }
        public DateTime TraceStartUtc { get; set; }
        public DateTime TraceEndUtc { get; set; }
        public double DurationMs { get; set; }
        public int EventCount { get; set; }
        public long EventsLost { get; set; }
        public bool Truncated { get; set; }
        public int ProcessId { get; set; }
        public int StackSamples { get; set; }
        public int UnmatchedSuspensionEnds { get; set; }
        public int UnmatchedSuspensionStarts { get; set; }
        public int OtherSuspensions { get; set; }
        public double OtherSuspensionTotalMs { get; set; }
        public double OtherSuspensionMaxMs { get; set; }
        public List<Suspension> Suspensions { get; set; } = new List<Suspension>();
        public List<Collection> Collections { get; set; } = new List<Collection>();
        public List<HeapSnapshot> HeapSnapshots { get; set; } = new List<HeapSnapshot>();
        public List<Window> Windows { get; set; } = new List<Window>();
    }
    sealed class PendingSuspension
    {
        public Suspension Value;
        public double StopMs;
    }
    sealed class ThreadAccumulator
    {
        public ThreadSummary Value = new ThreadSummary();
        public Dictionary<string, int> Leaves = new Dictionary<string, int>();
    }
    static void Increment(Dictionary<string, int> counts, string key)
    {
        int old;
        counts.TryGetValue(key, out old);
        counts[key] = old + 1;
    }
    static List<StackSummary> Top(Dictionary<string, int> counts, int limit)
    {
        return counts.OrderByDescending(p => p.Value).Take(limit)
            .Select(p => new StackSummary { Stack = p.Key, Samples = p.Value }).ToList();
    }
    static Window NewWindow(string name, string start, string end)
    {
        return new Window { Name = name, StartUtc = DateTime.Parse(start).ToUniversalTime(), EndUtc = DateTime.Parse(end).ToUniversalTime() };
    }
    public static Result Analyze(string tracePath, int processId, Window[] windows)
    {
        string etlxPath = tracePath + ".etlx";
        if (!System.IO.File.Exists(etlxPath))
            TraceLog.CreateFromEventPipeDataFile(tracePath, etlxPath, new TraceLogOptions());
        using (TraceLog trace = new TraceLog(etlxPath))
        {
            var result = new Result { TraceFile = tracePath, TraceStartUtc = trace.SessionStartTime.ToUniversalTime(),
                TraceEndUtc = trace.SessionEndTime.ToUniversalTime(), DurationMs = trace.SessionDuration.TotalMilliseconds,
                EventCount = trace.EventCount, EventsLost = trace.EventsLost, Truncated = trace.Truncated, ProcessId = processId };
            result.Windows.Add(new Window { Name = "EntireTrace", StartUtc = result.TraceStartUtc, EndUtc = result.TraceEndUtc });
            if (windows != null) result.Windows.AddRange(windows);
            var pending = new Dictionary<string, PendingSuspension>();
            var perWindowThreads = result.Windows.Select(w => new Dictionary<int, ThreadAccumulator>()).ToArray();
            var perWindowUiStacks = result.Windows.Select(w => new Dictionary<string, int>()).ToArray();
            foreach (TraceEvent evt in trace.Events)
            {
                if (evt.ProcessID != result.ProcessId) continue;
                var suspend = evt as GCSuspendEETraceData;
                if (suspend != null && evt.EventName == "GC/SuspendEEStart")
                {
                    string key = suspend.ThreadID + ":" + suspend.ClrInstanceID;
                    if (pending.ContainsKey(key)) result.UnmatchedSuspensionStarts++;
                    pending[key] = new PendingSuspension { Value = new Suspension { Reason = suspend.Reason.ToString(),
                        ThreadId = suspend.ThreadID, ClrInstanceId = suspend.ClrInstanceID,
                        StartUtc = evt.TimeStamp.ToUniversalTime(), StartRelativeMs = evt.TimeStampRelativeMSec } };
                    continue;
                }
                var noData = evt as GCNoUserDataTraceData;
                if (noData != null && evt.EventName == "GC/SuspendEEStop")
                {
                    PendingSuspension value;
                    if (pending.TryGetValue(evt.ThreadID + ":" + noData.ClrInstanceID, out value))
                    {
                        value.StopMs = evt.TimeStampRelativeMSec;
                        value.Value.HasSuspendStop = true;
                    }
                    continue;
                }
                if (noData != null && evt.EventName == "GC/RestartEEStop")
                {
                    string key = evt.ThreadID + ":" + noData.ClrInstanceID;
                    PendingSuspension value;
                    if (pending.TryGetValue(key, out value))
                    {
                        pending.Remove(key);
                        value.Value.EndUtc = evt.TimeStamp.ToUniversalTime();
                        value.Value.EndRelativeMs = evt.TimeStampRelativeMSec;
                        value.Value.TotalSuspensionMs = value.Value.EndRelativeMs - value.Value.StartRelativeMs;
                        if (value.Value.HasSuspendStop)
                        {
                            value.Value.SuspensionAcquireMs = value.StopMs - value.Value.StartRelativeMs;
                            value.Value.FullySuspendedMs = value.Value.EndRelativeMs - value.StopMs;
                        }
                        if (value.Value.Reason == "SuspendForGC" || value.Value.Reason == "SuspendForGCPrep")
                            result.Suspensions.Add(value.Value);
                        else
                        {
                            // SampleProfiler emits SuspendOther; this is not a GC pause.
                            result.OtherSuspensions++;
                            result.OtherSuspensionTotalMs += value.Value.TotalSuspensionMs;
                            result.OtherSuspensionMaxMs = Math.Max(result.OtherSuspensionMaxMs, value.Value.TotalSuspensionMs);
                        }
                    }
                    else result.UnmatchedSuspensionEnds++;
                    continue;
                }
                var gc = evt as GCStartTraceData;
                if (gc != null)
                {
                    result.Collections.Add(new Collection { Count = gc.Count, Reason = gc.Reason.ToString(), Type = gc.Type.ToString(),
                        Depth = gc.Depth, StartUtc = evt.TimeStamp.ToUniversalTime(), StartRelativeMs = evt.TimeStampRelativeMSec });
                    continue;
                }
                var heap = evt as GCHeapStatsTraceData;
                if (heap != null)
                {
                    result.HeapSnapshots.Add(new HeapSnapshot { Utc = evt.TimeStamp.ToUniversalTime(), TotalHeapSize = heap.TotalHeapSize,
                        TotalPromoted = heap.TotalPromoted, Gen2Bytes = heap.GenerationSize2, LohBytes = heap.GenerationSize3 });
                    continue;
                }
                var sample = evt as ClrThreadSampleTraceData;
                if (sample == null) continue;
                result.StackSamples++;
                var frames = new List<string>();
                for (TraceCallStack stack = evt.CallStack(); stack != null && frames.Count < 64; stack = stack.Caller)
                    frames.Add(stack.CodeAddress.FullMethodName ?? stack.CodeAddress.ModuleName ?? "[unresolved]");
                bool uiMain = frames.Any(f => f.Contains("Program.Main"));
                string leaf = frames.Count == 0 ? "[no managed stack]" : frames[0];
                DateTime utc = evt.TimeStamp.ToUniversalTime();
                for (int i = 0; i < result.Windows.Count; i++)
                {
                    var window = result.Windows[i];
                    if (utc < window.StartUtc || utc > window.EndUtc) continue;
                    window.Samples++;
                    ThreadAccumulator acc;
                    if (!perWindowThreads[i].TryGetValue(evt.ThreadID, out acc))
                    {
                        acc = new ThreadAccumulator(); acc.Value.ThreadId = evt.ThreadID; perWindowThreads[i][evt.ThreadID] = acc;
                    }
                    acc.Value.Samples++;
                    if (sample.Type == ClrThreadSampleType.Managed) acc.Value.ManagedSamples++;
                    if (sample.Type == ClrThreadSampleType.External) acc.Value.ExternalSamples++;
                    acc.Value.HasProgramMain |= uiMain;
                    Increment(acc.Leaves, leaf);
                    if (uiMain) Increment(perWindowUiStacks[i], string.Join("\n", frames.Take(30)));
                }
            }
            result.UnmatchedSuspensionStarts += pending.Count;
            for (int i = 0; i < result.Windows.Count; i++)
            {
                foreach (var acc in perWindowThreads[i].Values) acc.Value.TopLeaves = Top(acc.Leaves, 8);
                result.Windows[i].Threads = perWindowThreads[i].Values.OrderByDescending(a => a.Value.Samples).Select(a => a.Value).ToList();
                result.Windows[i].UiStacks = Top(perWindowUiStacks[i], 12);
            }
            return result;
        }
    }
}
'@

$absoluteTrace = (Resolve-Path -LiteralPath $TracePath).Path
$taskWindows = if ($WindowsJson) { Get-Content -LiteralPath $WindowsJson -Raw | ConvertFrom-Json } else { @() }
$result = [Phase3EventPipeAnalysis]::Analyze($absoluteTrace, $TraceProcessId, [Phase3EventPipeAnalysis+Window[]]$taskWindows)
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output ('Analyzed {0} samples; {1} paired runtime suspensions; {2} collections.' -f $result.StackSamples, $result.Suspensions.Count, $result.Collections.Count)
