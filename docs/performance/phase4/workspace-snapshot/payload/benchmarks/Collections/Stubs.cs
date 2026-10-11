global using System.Collections;
global using System.ComponentModel;
global using System.Diagnostics;
global using Files.App.Helpers;
global using Files.Shared.Extensions;

namespace WinRT
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class GeneratedWinRTExposedTypeAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class GeneratedWinRTExposedExternalTypeAttribute(Type type) : Attribute { }
}

namespace Files.App.Utils
{
    internal sealed class ListedItem { }
}

namespace Files.App.Utils.Storage
{
    public sealed class GroupedHeaderViewModel
    {
        public string? Key { get; set; }
        public string Text { get; set; } = "";
        public string? CountText { get; set; }
        public bool Initialized { get; set; }
        public void PausePropertyChangedNotifications() { }
        public void ResumePropertyChangedNotifications(bool triggerUpdates = true) { }
    }

    internal static class Strings
    {
        public const string GroupItemsCount_Plural = "{0} items";
        public const string GroupItemsCount_Singular = "{0} item";
    }

    internal static class LocalizationStub
    {
        public static string GetLocalizedResource(this string value) => value;
    }
}
