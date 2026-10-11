// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Helpers
{
	internal sealed class LatestPathNavigationCoordinator<TOwner> where TOwner : class
	{
		private long revision;

		public readonly record struct Request(long Revision, TOwner? Owner);

		public Request Begin(TOwner? owner) => new(Interlocked.Increment(ref revision), owner);

		public bool IsCurrent(Request request, TOwner? owner)
			=> Volatile.Read(ref revision) == request.Revision && ReferenceEquals(request.Owner, owner);

		public void Invalidate() => Interlocked.Increment(ref revision);
	}
}
