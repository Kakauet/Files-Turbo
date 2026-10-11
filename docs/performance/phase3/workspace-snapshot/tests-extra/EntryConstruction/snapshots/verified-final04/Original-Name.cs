using System.IO;
using EntryConstruction.Contracts;
namespace EntryConstruction.Generated;
public class OriginalListedItem
{
    protected static FakeUserSettings UserSettingsService => Settings.UserSettingsService;
    private string? itemNameRaw;
    public string? ItemNameRaw { get => itemNameRaw; set => itemNameRaw = value; }
    public StorageItemTypes PrimaryItemAttribute { get; set; }
    public virtual string? Name
		{
			get
			{
				if (PrimaryItemAttribute == StorageItemTypes.File)
				{
					var nameWithoutExtension = Path.GetFileNameWithoutExtension(itemNameRaw);
					if (!string.IsNullOrEmpty(nameWithoutExtension) && !UserSettingsService.FoldersSettingsService.ShowFileExtensions)
					{
						return nameWithoutExtension;
					}
				}
				return itemNameRaw;
			}
		}
}