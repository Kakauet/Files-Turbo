// Generated from production date members/methods. The baseline method name alone is changed.
#nullable enable
namespace Files.App.Utils { public partial class ListedItem {
private string? itemDateModified;
		private bool itemDateModifiedInitialized;
		public string? ItemDateModified
		{
			get => itemDateModifiedInitialized ? itemDateModified ??= dateTimeFormatter.ToShortLabel(itemDateModifiedReal) : null;
			private set => SetProperty(ref itemDateModified, value);
		}

		private string? itemDateCreated;
		private bool itemDateCreatedInitialized;
		public string? ItemDateCreated
		{
			get => itemDateCreatedInitialized ? itemDateCreated ??= dateTimeFormatter.ToShortLabel(itemDateCreatedReal) : null;
			private set => SetProperty(ref itemDateCreated, value);
		}

		private string? itemDateAccessed;
		private bool itemDateAccessedInitialized;
		public string? ItemDateAccessed
		{
			get => itemDateAccessedInitialized ? itemDateAccessed ??= dateTimeFormatter.ToShortLabel(itemDateAccessedReal) : null;
			private set => SetProperty(ref itemDateAccessed, value);
		}

		private DateTimeOffset itemDateModifiedReal;
		public DateTimeOffset ItemDateModifiedReal
		{
			get => itemDateModifiedReal;
			set
			{
				if (itemDateModifiedInitialized && itemDateModifiedReal == value)
					return;

				itemDateModifiedReal = value;
				itemDateModifiedInitialized = true;
				itemDateModified = null;
				OnPropertyChanged(nameof(ItemDateModified));
			}
		}

		private DateTimeOffset itemDateCreatedReal;
		public DateTimeOffset ItemDateCreatedReal
		{
			get => itemDateCreatedReal;
			set
			{
				if (itemDateCreatedInitialized && itemDateCreatedReal == value)
					return;

				itemDateCreatedReal = value;
				itemDateCreatedInitialized = true;
				itemDateCreated = null;
				OnPropertyChanged(nameof(ItemDateCreated));
			}
		}

		private DateTimeOffset itemDateAccessedReal;
		public DateTimeOffset ItemDateAccessedReal
		{
			get => itemDateAccessedReal;
			set
			{
				if (itemDateAccessedInitialized && itemDateAccessedReal == value)
					return;

				itemDateAccessedReal = value;
				itemDateAccessedInitialized = true;
				itemDateAccessed = null;
				OnPropertyChanged(nameof(ItemDateAccessed));
			}
		}

		
public virtual bool IsRealChanges => itemDateAccessed is not null && itemDateAccessed != dateTimeFormatter.ToShortLabel(ItemDateAccessedReal)
			|| itemDateCreated is not null && itemDateCreated != dateTimeFormatter.ToShortLabel(ItemDateCreatedReal)
			|| itemDateModified is not null && itemDateModified != dateTimeFormatter.ToShortLabel(ItemDateModifiedReal);
public virtual void UpdateReal()
		{
			if (itemDateAccessed is not null)
				ItemDateAccessed = dateTimeFormatter.ToShortLabel(ItemDateAccessedReal);
			if (itemDateCreated is not null)
				ItemDateCreated = dateTimeFormatter.ToShortLabel(ItemDateCreatedReal);
			if (itemDateModified is not null)
				ItemDateModified = dateTimeFormatter.ToShortLabel(ItemDateModifiedReal);
		}
} }
namespace Files.App.ViewModels { public partial class ShellViewModel {
public void UpdateDateDisplay(IEnumerable<ListedItem>? items = null)
		{
			foreach (var item in items ?? filesAndFolders)
			{
				if (item.IsRealChanges)
					item.UpdateReal();
			}
		}
public void BaselineUpdateDateDisplay()
		{
			App.Logger.LogDebug($"UpdateDateDisplay: itemCount={filesAndFolders.Count}");

			foreach (var item in filesAndFolders)
			{
				if (item.IsRealChanges)
					item.UpdateReal();
			}
		}
} }
namespace Files.App.Views.Layouts { public partial class BaseLayoutPage {
public void UpdateDateDisplay()
		{
			ParentShellPageInstance?.ShellViewModel?.UpdateDateDisplay(realizedItems.ToArray()
				.Where(pair => IsCurrentContainer(pair.Key, pair.Value)).Select(pair => pair.Value));
		}
private bool IsCurrentContainer(SelectorItem container, ListedItem item)
			=> !isDisposed && navigationArguments is not null && realizedItems.TryGetValue(container, out var current) &&
				ReferenceEquals(current, item) && ReferenceEquals(container.Content, item);
public void RefreshRealizationDate(ListedItem listedItem, StubArgs args) {
if (args.Phase == 0 && listedItem.IsRealChanges)
					listedItem.UpdateReal();
}
} }