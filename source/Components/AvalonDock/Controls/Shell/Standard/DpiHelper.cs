/************************************************************************
   AvalonDock

   Copyright (C) 2007-2013 Xceed Software Inc.

   This program is provided to you under the terms of the Microsoft Public
   License (Ms-PL) as published at https://opensource.org/licenses/MS-PL
 ************************************************************************/

/**************************************************************************\
    Copyright Microsoft Corporation. All Rights Reserved.
\**************************************************************************/

using System;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Media;

namespace Standard
{
	internal static class DpiHelper
	{
		private static Matrix _transformToDevice;
		private static Matrix _transformToDip;

		[SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline")]
		static DpiHelper()
		{
			using (var desktop = SafeDC.GetDesktop())
			{
				// Can get these in the static constructor.  They shouldn't vary window to window,
				// and changing the system DPI requires a restart.
				var pixelsPerInchX = NativeMethods.GetDeviceCaps(desktop, DeviceCap.LOGPIXELSX);
				var pixelsPerInchY = NativeMethods.GetDeviceCaps(desktop, DeviceCap.LOGPIXELSY);
				_transformToDip = Matrix.Identity;
				_transformToDip.Scale(96d / pixelsPerInchX, 96d / pixelsPerInchY);
				_transformToDevice = Matrix.Identity;
				_transformToDevice.Scale(pixelsPerInchX / 96d, pixelsPerInchY / 96d);
			}
		}

		#region Primary monitor DPI
		/// <summary>
		/// Convert a point in device independent pixels (1/96") to a point in the system coordinates (primary monitor DPI).
		/// </summary>
		/// <param name="logicalPoint">A point in the logical coordinate system.</param>
		/// <returns>Returns the parameter converted to the system's coordinates.</returns>
		public static Point LogicalPixelsToDevice(Point logicalPoint) => _transformToDevice.Transform(logicalPoint);

		/// <summary>
		/// Convert a point in system coordinates to a point in device independent pixels (1/96"). (primary monitor DPI)
		/// </summary>
		/// <param name="logicalPoint">A point in the physical coordinate system.</param>
		/// <returns>Returns the parameter converted to the device independent coordinate system.</returns>
		public static Point DevicePixelsToLogical(Point devicePoint) => _transformToDip.Transform(devicePoint);

		[SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
		public static Rect LogicalRectToDevice(Rect logicalRectangle)
		{
			var topLeft = LogicalPixelsToDevice(new Point(logicalRectangle.Left, logicalRectangle.Top));
			var bottomRight = LogicalPixelsToDevice(new Point(logicalRectangle.Right, logicalRectangle.Bottom));
			return new Rect(topLeft, bottomRight);
		}

		public static Rect DeviceRectToLogical(Rect deviceRectangle)
		{
			var topLeft = DevicePixelsToLogical(new Point(deviceRectangle.Left, deviceRectangle.Top));
			var bottomRight = DevicePixelsToLogical(new Point(deviceRectangle.Right, deviceRectangle.Bottom));
			return new Rect(topLeft, bottomRight);
		}

		[SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
		public static Size LogicalSizeToDevice(Size logicalSize)
		{
			var pt = LogicalPixelsToDevice(new Point(logicalSize.Width, logicalSize.Height));
			return new Size { Width = pt.X, Height = pt.Y };
		}

		public static Size DeviceSizeToLogical(Size deviceSize)
		{
			var pt = DevicePixelsToLogical(new Point(deviceSize.Width, deviceSize.Height));
			return new Size(pt.X, pt.Y);
		}
		#endregion

		#region Per-monitor DPI

		private static bool TryGetDpiForWindow(IntPtr hwnd, out uint dpiX, out uint dpiY)
		{
			// Defaults
			dpiX = dpiY = 96;
			if (hwnd == IntPtr.Zero) 
				return false;
			try
			{
				var ver = Environment.OSVersion.Version;
				// Windows 10 Anniversary (1607 / build 14393) or later offers GetDpiForWindow
				if (ver.Major >= 10 && ver.Build >= 14393)
				{
					var dpi = NativeMethods.GetDpiForWindow(hwnd);
					if (dpi != 0)
					{
						dpiX = dpiY = dpi; // returns the scale factor for both axes
						return true;
					}
				}
				// Windows 8.1 (6.3) provides GetDpiForMonitor
				if (ver.Major > 6 || (ver.Major == 6 && ver.Minor >= 3))
				{
					var hMon = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
					if (hMon != IntPtr.Zero)
					{
						if (NativeMethods.GetDpiForMonitor(hMon, Monitor_DPI_Type.MDT_EFFECTIVE_DPI, out dpiX, out dpiY) == 0)
							return true;
					}
				}
			}
			catch
			{
				return false; // fall back to system DPI
			}
			return false;
		}

		private static void GetScaleForWindow(IntPtr hwnd, out double scaleX, out double scaleY)
		{
			if (TryGetDpiForWindow(hwnd, out var dpiX, out var dpiY))
			{
				scaleX = dpiX / 96.0;
				scaleY = dpiY / 96.0;
			}
			else
			{
				// Fallback: system DPI
				scaleX = _transformToDevice.M11;
				scaleY = _transformToDevice.M22;
			}
		}

		public static Point LogicalPixelsToDevice(IntPtr hwnd, Point logicalPoint)
		{
			GetScaleForWindow(hwnd, out var sx, out var sy);
			return new Point(logicalPoint.X * sx, logicalPoint.Y * sy);
		}

		public static Point DevicePixelsToLogical(IntPtr hwnd, Point devicePoint)
		{
			GetScaleForWindow(hwnd, out var sx, out var sy);
			return new Point(devicePoint.X / sx, devicePoint.Y / sy);
		}

		public static Rect LogicalRectToDevice(IntPtr hwnd, Rect logicalRectangle)
		{
			var topLeft = LogicalPixelsToDevice(hwnd, new Point(logicalRectangle.Left, logicalRectangle.Top));
			var bottomRight = LogicalPixelsToDevice(hwnd, new Point(logicalRectangle.Right, logicalRectangle.Bottom));
			return new Rect(topLeft, bottomRight);
		}

		public static Rect DeviceRectToLogical(IntPtr hwnd, Rect deviceRectangle)
		{
			var topLeft = DevicePixelsToLogical(hwnd, new Point(deviceRectangle.Left, deviceRectangle.Top));
			var bottomRight = DevicePixelsToLogical(hwnd, new Point(deviceRectangle.Right, deviceRectangle.Bottom));
			return new Rect(topLeft, bottomRight);
		}

		public static Size LogicalSizeToDevice(IntPtr hwnd, Size logicalSize)
		{
			var pt = LogicalPixelsToDevice(hwnd, new Point(logicalSize.Width, logicalSize.Height));
			return new Size { Width = pt.X, Height = pt.Y };
		}

		public static Size DeviceSizeToLogical(IntPtr hwnd, Size deviceSize)
		{
			var pt = DevicePixelsToLogical(hwnd, new Point(deviceSize.Width, deviceSize.Height));
			return new Size(pt.X, pt.Y);
		}
		#endregion
	}
}