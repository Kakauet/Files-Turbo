// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Files.App.Helpers
{
	internal static class UiPerformanceDisplayProbe
	{
		public static unsafe void RecordRefreshRate(long operationId)
		{
			if (!UiPerformanceTrace.IsEnabled || operationId == 0)
				return;
			var monitor = PInvoke.MonitorFromWindow(new HWND(MainWindow.Instance.WindowHandle), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
			MONITORINFOEXW info = default;
			info.monitorInfo.cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>();
			if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
				return;
			DEVMODEW mode = default;
			mode.dmSize = (ushort)sizeof(DEVMODEW);
			if (PInvoke.EnumDisplaySettings(info.szDevice.ToString(), ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref mode) && mode.dmDisplayFrequency > 1)
				UiPerformanceTrace.Mark(operationId, "MonitorReportedRefreshRateHz", value: mode.dmDisplayFrequency);
		}
	}
}
