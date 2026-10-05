//
// InkPenHook
// MainWindow.Native.cs	2026/10/04 01:20:00
//
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace InkPenHook
{
	public static class NativeMethods
	{
		[DllImport("user32.dll", SetLastError = true)]
		public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);
		[DllImport("user32.dll", SetLastError = true)]
		public static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
		[DllImport("user32.dll", SetLastError = true)]
		public static extern uint SendInput(uint nInputs, INPUT_FLAT[] pInputs, int cbSize);
		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);
		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool UnhookWindowsHookEx(IntPtr hhk);
		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		public static extern IntPtr GetModuleHandle(string lpModuleName);
		[DllImport("user32.dll")]
		public static extern IntPtr GetForegroundWindow();
		[DllImport("user32.dll")]
		public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
		[DllImport("user32.dll")]
		public static extern uint MapVirtualKey(uint uCode, uint uMapType);
		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern int GetKeyNameText(int lParam, [Out] StringBuilder lpString, int nSize);

		public delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

		public const int WH_MOUSE_LL = 14;
		public const int WH_KEYBOARD_LL = 13;
		public const int WM_RBUTTONDOWN = 0x0204;
		public const int WM_RBUTTONUP = 0x0205;
		public const int WM_MBUTTONDOWN = 0x0207;
		public const int WM_MBUTTONUP = 0x0208;
		public const ushort HID_USAGE_PAGE_DIGITIZER = 0x0D;
		public const ushort HID_USAGE_DIGITIZER_PEN = 0x02;
		public const uint RIDEV_INPUTSINK = 0x00000100;
		public const int WM_INPUT = 0x00FF;
		public const uint RID_INPUT = 0x10000003;
		public const int INPUT_KEYBOARD = 1;
		public const uint KEYEVENTF_SCANCODE = 0x0008;
		public const uint KEYEVENTF_KEYUP = 0x0002;

		[StructLayout(LayoutKind.Sequential)]
		public struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }
		[StructLayout(LayoutKind.Sequential)]
		public struct RAWINPUTHEADER { public uint dwType; public uint dwSize; public IntPtr hDevice; public IntPtr wParam; }

		[StructLayout(LayoutKind.Explicit, Size = 40)]
		public struct INPUT_FLAT
		{
			[FieldOffset(0)] public int type;
			[FieldOffset(8)] public ushort wVk;
			[FieldOffset(10)] public ushort wScan;
			[FieldOffset(12)] public uint dwFlags;
			[FieldOffset(16)] public uint time;
			[FieldOffset(24)] public IntPtr dwExtraInfo;
		}

		public static INPUT_FLAT CreateInput(ushort scanCode, bool isKeyUp)
		{
			INPUT_FLAT input = new INPUT_FLAT { type = INPUT_KEYBOARD, wScan = scanCode };
			input.wVk = (ushort)MapVirtualKey(scanCode, 1);
			input.dwFlags = KEYEVENTF_SCANCODE | (isKeyUp ? KEYEVENTF_KEYUP : 0);
			return input;
		}
	}
}
