using System.IO;
using EntryConstruction.Contracts;
using FileExtensionHelpers = Files.Shared.Helpers.Current.FileExtensionHelpers;
namespace EntryConstruction.Generated;
public static class CurrentClassification
{
    public static LinkKind Classify(string name, string root)
    {
        var findData = new FindData(name);
        var itemPath = Path.Combine(root, name);
        string? itemFileExtension = name.Contains('.') ? Path.GetExtension(itemPath) : null;
        bool isShortcut = string.Equals(itemFileExtension, ".lnk", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(itemFileExtension, ".url", StringComparison.OrdinalIgnoreCase);
        bool isUrl = isShortcut && (string.Equals(itemFileExtension, ".url", StringComparison.OrdinalIgnoreCase));
        return new LinkKind(isShortcut, isUrl);
    }
}