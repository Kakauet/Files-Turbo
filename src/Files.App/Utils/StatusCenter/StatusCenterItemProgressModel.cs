// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Operations;
using System.Runtime.CompilerServices;

namespace Files.App.Utils.StatusCenter
{
	/// <summary>
	/// Represents a model for file system operation progress.
	/// </summary>
	/// <remarks>
	/// Every instance that have the same <see cref="IProgress{T}"/> instance will update the same progress.
	/// <br/>
	/// Therefore, the storage operation classes can portably instance this class and update progress from everywhere with the same <see cref="IProgress{T}"/> instance.
	/// </remarks>
	public sealed partial class StatusCenterItemProgressModel : INotifyPropertyChanged
	{
		private readonly IProgress<StatusCenterItemProgressModel>? _progress;

		private int _dirtyProperties;
		private static readonly string[] PropertyNames =
		[
			nameof(Status), nameof(EnumerationCompleted), nameof(FileName), nameof(TotalSize),
			nameof(ProcessedSize), nameof(ItemsCount), nameof(ProcessedItemsCount),
			nameof(ProcessingSizeSpeed), nameof(ProcessingItemsCountSpeed), nameof(StartTime), nameof(CompletedTime)
		];

		private readonly ProgressNotificationGate _sampler;

		private bool _criticalReport;

		private long _previousProcessedSize;

		private long _previousProcessedItemsCount;

		private DateTimeOffset _previousReportTime;

		private FileSystemStatusCode? _Status;
		public FileSystemStatusCode? Status
		{
			get => _Status;
			set
			{
				if (_Status != value)
					_criticalReport = true;

				SetProperty(ref _Status, value);
			}
		}

		private bool _EnumerationCompleted;
		public bool EnumerationCompleted
		{
			get => _EnumerationCompleted;
			set
			{
				if (_EnumerationCompleted != value)
					_criticalReport = true;

				SetProperty(ref _EnumerationCompleted, value);
			}
		}

		private string? _FileName;
		public string? FileName
		{
			get => _FileName;
			set => SetProperty(ref _FileName, value);
		}

		private long _TotalSize;
		public long TotalSize
		{
			get => _TotalSize;
			set => SetProperty(ref _TotalSize, value);
		}

		private long _ProcessedSize;
		public long ProcessedSize
		{
			get => _ProcessedSize;
		}

		private long _ItemsCount;
		public long ItemsCount
		{
			get => _ItemsCount;
			set => SetProperty(ref _ItemsCount, value);
		}

		private long _ProcessedItemsCount;
		public long ProcessedItemsCount
		{
			get => _ProcessedItemsCount;
		}

		public double ProcessingSizeSpeed { get; private set; }

		public double ProcessingItemsCountSpeed { get; private set; }

		private DateTimeOffset _StartTime;
		public DateTimeOffset StartTime
		{
			get => _StartTime;
			set => SetProperty(ref _StartTime, value);
		}

		private DateTimeOffset _CompletedTime;

		public DateTimeOffset CompletedTime
		{
			get => _CompletedTime;
			set => SetProperty(ref _CompletedTime, value);
		}

		/// <summary>
		/// Only used when detailed count isn't available.
		/// You should NEVER set this property directly, instead, use <see cref="Report(double?)" /> to update the percentage.
		/// </summary>
		public double? Percentage { get; private set; }

		public event PropertyChangedEventHandler? PropertyChanged;

		public StatusCenterItemProgressModel(IProgress<StatusCenterItemProgressModel>? progress, bool enumerationCompleted = false, FileSystemStatusCode? status = null, long itemsCount = 0, int samplerInterval = 100)
		{
			// Initialize
			_progress = progress;
			_sampler = new(samplerInterval);
			EnumerationCompleted = enumerationCompleted;
			Status = status;
			ItemsCount = itemsCount;
			StartTime = DateTimeOffset.Now;
			_previousReportTime = StartTime - TimeSpan.FromSeconds(1);
		}

		public void AddProcessedItemsCount(long value)
		{
			Interlocked.Add(ref _ProcessedItemsCount, value);
			MarkDirty(nameof(ProcessedItemsCount));
		}

		public void SetProcessedSize(long value)
		{
			Interlocked.Exchange(ref _ProcessedSize, value);
			MarkDirty(nameof(ProcessedSize));
		}

		private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
		{
			field = value;

			if (propertyName is not null)
			{
				MarkDirty(propertyName);
			}
		}

		public void Report(double? percentage = null)
		{
			if (percentage is not null && Percentage != percentage)
			{
				SetProcessedSize((long)(TotalSize * percentage / 100));
				Percentage = percentage;
			}

			// Set the progress state as success
			if ((EnumerationCompleted &&
				ProcessedItemsCount == ItemsCount &&
				ProcessedSize == TotalSize &&
				TotalSize is not 0 ||
				(percentage is double p && Math.Abs(p - 100) <= double.Epsilon)) &&
				_Status is FileSystemStatusCode.InProgress or null)
			{
				Status = FileSystemStatusCode.Success;
			}

			if (!_sampler.TryEnter() && !_criticalReport)
				return;

			_criticalReport = false;
			var now = DateTimeOffset.Now;
			if (_Status is FileSystemStatusCode.Success && CompletedTime == default)
				CompletedTime = now;

			var elapsed = (now - _previousReportTime).TotalSeconds;
			if (elapsed > 0)
			{
				ProcessingSizeSpeed = (ProcessedSize - _previousProcessedSize) / elapsed;
				ProcessingItemsCountSpeed = (ProcessedItemsCount - _previousProcessedItemsCount) / elapsed;
				MarkDirty(nameof(ProcessingSizeSpeed));
				MarkDirty(nameof(ProcessingItemsCountSpeed));
			}
			_previousReportTime = now;
			_previousProcessedSize = ProcessedSize;
			_previousProcessedItemsCount = ProcessedItemsCount;

			int dirty = Interlocked.Exchange(ref _dirtyProperties, 0);
			for (int i = 0; i < PropertyNames.Length; i++)
			{
				if ((dirty & (1 << i)) != 0)
					PropertyChanged?.Invoke(this, new(PropertyNames[i]));
			}
			_progress?.Report(this);
		}

		private void MarkDirty(string propertyName)
		{
			int index = Array.IndexOf(PropertyNames, propertyName);
			if (index >= 0)
				Interlocked.Or(ref _dirtyProperties, 1 << index);
		}

		public void ReportStatus(FileSystemStatusCode status, double? percentage = null)
		{
			Status = status;

			Report(percentage);
		}
	}
}
