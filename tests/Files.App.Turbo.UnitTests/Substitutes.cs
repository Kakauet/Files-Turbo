// Copyright (c) Files Community
// Licensed under the MIT License.

// Minimal stand-ins for the app types referenced by the linked production files.
// They carry no behavior under test.

global using System.Collections;
global using System.Collections.Concurrent;
global using System.Collections.Specialized;
global using System.ComponentModel;
global using System.Diagnostics;
global using CommunityToolkit.Mvvm.DependencyInjection;
global using Files.App.Data.Enums;
global using Files.App.Data.Items;
global using Files.App.Extensions;
global using Files.App.Helpers;
global using Files.App.Services.DateTimeFormatter;
global using Files.App.Utils.Storage;
global using Files.Shared.Extensions;
global using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WinRT
{
	[AttributeUsage(AttributeTargets.Class)]
	internal sealed class GeneratedWinRTExposedTypeAttribute : Attribute { }

	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	internal sealed class GeneratedWinRTExposedExternalTypeAttribute(Type type) : Attribute
	{
		public Type Type { get; } = type;
	}
}

namespace Microsoft.UI.Xaml.Media
{
	public class ImageSource { }
}

namespace Files.App
{
	internal static class Strings
	{
		public const string GroupItemsCount_Plural = "{0} items";
		public const string GroupItemsCount_Singular = "{0} item";
	}

	internal static class LocalizationSubstitute
	{
		public static string GetLocalizedResource(this string value) => value;
	}
}

namespace Files.App.Utils
{
	public sealed class ListedItem(string name) : IGroupableItem
	{
		public string Name { get; } = name;

		public string Group { get; set; } = "A";

		public string? Key { get; set; }

		public override string ToString() => Name;
	}
}

namespace Files.App.Utils.Storage
{
	public sealed class GroupedHeaderViewModel
	{
		public string? Key { get; set; }

		public string? Text { get; set; }

		public string? CountText { get; set; }

		public bool Initialized { get; set; }

		public void PausePropertyChangedNotifications() { }

		public void ResumePropertyChangedNotifications(bool triggerPropertyChanged = true) { }
	}
}

namespace Files.App.Helpers
{
	public static partial class Win32PInvoke
	{
		public const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;

		public struct WIN32_FIND_DATA
		{
			public uint dwFileAttributes;
			public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
			public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
			public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
			public uint nFileSizeHigh;
			public uint nFileSizeLow;
			public uint dwReserved0;
			public uint dwReserved1;
			public string cFileName;
			public string cAlternateFileName;
		}
	}
}

namespace Files.App.Data.Items
{
	internal static class ShellLibraryItem
	{
		public const string EXTENSION = ".library-ms";
	}
}
