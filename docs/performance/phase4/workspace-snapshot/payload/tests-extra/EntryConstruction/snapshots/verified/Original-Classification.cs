using System.IO;
using EntryConstruction.Contracts;
using FileExtensionHelpers = Files.Shared.Helpers.Original.FileExtensionHelpers;
namespace EntryConstruction.Generated;
public static class OriginalClassification
{
    public static LinkKind Classify(string name, string root)
    {
        var findData = new FindData(name);
        var itemPath = Path.Combine(root, name);
        string? itemFileExtension = name.Contains('.') ? Path.GetExtension(itemPath) : null;
        bool isShortcut = FileExtensionHelpers.IsShortcutOrUrlFile(findData.cFileName);
        bool isUrl = isShortcut && (FileExtensionHelpers.IsWebLinkFile(findData.cFileName));
        return new LinkKind(isShortcut, isUrl);
    }
}