// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Diagnostics;

namespace Files.App.Utils.Storage.Operations
{
	internal sealed class ProgressNotificationGate
	{
		private readonly long intervalTicks;
		private long nextTick;

		public ProgressNotificationGate(int milliseconds = 100)
		{
			ArgumentOutOfRangeException.ThrowIfNegative(milliseconds);
			intervalTicks = Math.Max(1, (long)(Stopwatch.Frequency * (milliseconds / 1000.0)));
		}

		public bool TryEnter() => TryEnter(Stopwatch.GetTimestamp());

		internal bool TryEnter(long now)
		{
			long due = Volatile.Read(ref nextTick);
			return now >= due && Interlocked.CompareExchange(ref nextTick, now + intervalTicks, due) == due;
		}
	}
}
