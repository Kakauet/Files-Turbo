// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Helpers
{
	/// <summary>
	/// Runs an action for bursts of requests: one run at a time, and one more after it when requests arrived while
	/// it was running, so the last request is always followed by a complete run.
	/// </summary>
	internal sealed class CoalescingRunner(Func<Task> action, TimeSpan delay = default)
	{
		private int pending;

		public async Task RequestAsync()
		{
			// Only the first request of a burst runs; later ones are folded into its next pass
			if (Interlocked.Increment(ref pending) != 1)
				return;

			try
			{
				do
				{
					if (delay > TimeSpan.Zero)
						await Task.Delay(delay);

					// Requests until here are covered by this pass
					Interlocked.Exchange(ref pending, 1);
					await action();
				}
				while (Interlocked.CompareExchange(ref pending, 0, 1) != 1);
			}
			catch
			{
				Interlocked.Exchange(ref pending, 0);
				throw;
			}
		}
	}
}
