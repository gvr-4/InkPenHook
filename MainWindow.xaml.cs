// InkPenHook
// MainWindow.xaml.cs	2026/10/24(土) 10:42:55 負荷軽減のためログ出力を抑制
// 重複起動防止(Mutex)・設定エラーログ・モニターフック連動

// 2026/10/13(火) 20:45:40 CliStのbarrelボタンはデフォルト（マウス右ボタンでスポイト）のためキーコードは書かない．．

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace InkPenHook{

	public static class AppInfo{
		public const string Version = "n/a";

		public static string GetBuildDateTime(){
			try{
				// exeファイルのファイル名と更新日時を…回りくどいんで廃止
				//string path = System.Reflection.Assembly.GetExecutingAssembly().Location;
				//if (string.IsNullOrEmpty(path)) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InkPenHook.dll");
				//if (File.Exists(path)) return File.GetLastWriteTime(path).ToString("yyyy/MM/dd HH:mm:ss");

				// アセンブリからメタデータを取得
				var _assembly = Assembly.GetExecutingAssembly();
				var _infoVersion = _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
				// 例: 1.0.0+2026-10-05 00:02:00Z な形式．めんどうだからこのまんま．
				return _infoVersion;



			}
			catch { }
			return "取得失敗";
		}
	}

	public partial class MainWindow : Window{
		private static Mutex? _mutex;
		public string ConfigErrorLog { get; private set; } = "正常";

		// 各種フック・状態変数（継続）
		private bool _lastBarrelState = false, _lastEraserState = false;
		private IntPtr _mouseHookHandle = IntPtr.Zero, _keyboardHookHandle = IntPtr.Zero;
		private NativeMethods.LowLevelHookProc _mouseHookProc, _keyboardHookProc;
		private RootConfig _configRoot = new RootConfig();
		private AppProfile _currentProfile = new AppProfile();

		private volatile bool _isPhysicalBarrelDown = false, _isPhysicalEraserDown = false;

		public MainWindow()	{
			// 1. 重複起動防止
			_mutex = new Mutex(true, "Global\\InkPenHook_Unique_Mutex_2026", out bool createdNew);
			if (!createdNew){
				MessageBox.Show("InkPenHook は既に起動しています。", "二重起動防止", MessageBoxButton.OK, MessageBoxImage.Warning);
				Application.Current.Shutdown();
				return;
			}

			InitializeComponent();
			this.Loaded += MainWindow_Loaded;
			this.Closed += (s, e) => { RemoveHooks(); _notifyIcon?.Dispose(); _mutex?.ReleaseMutex(); };

			// フックプロシージャの定義（モニター送出用に拡張）
			_mouseHookProc = LowLevelMouseProc;
			_keyboardHookProc = LowLevelKeyboardProc;
			SetupTaskTrayIcon();

			// 2. 設定ファイル読込とエラーキャッチ
			LoadOrCreateConfig();
		}

		private bool LoadOrCreateConfig(){
			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InkPenHook.xaml");
			try	{
				if (File.Exists(path)){
					_configRoot = (RootConfig)XamlReader.Parse(File.ReadAllText(path));
					ConfigErrorLog = "正常 (外部ファイルを読み込みました)";
				}else{
					StringBuilder sb = new StringBuilder();
					sb.AppendLine("<RootConfig xmlns=\"clr-namespace:InkPenHook;assembly=InkPenHook\">");
					sb.AppendLine("    <RootConfig.Apps>");
					sb.AppendLine("        <AppProfile AppName=\"KRITA\" ProcessName=\"krita\"");
					sb.AppendLine("            BarrelShortScanCode=\"0\"");
					sb.AppendLine("            BarrelLongScanCode=\"0\"");

					sb.AppendLine("            BarrelLongPressMs=\"350\"");

					sb.AppendLine("            BarrelShortBehavior=\"Stroke\"");
					sb.AppendLine("            BarrelPressDurationMs=\"40\"");
					sb.AppendLine("            BarrelDebounceMs=\"180\"");
					sb.AppendLine("            BarrelReleaseDelayMs=\"75\"");

					sb.AppendLine("            EraserShortScanCode=\"18\"");
					sb.AppendLine("            EraserLongScanCode=\"0\"");

					sb.AppendLine("            EraserLongPressMs=\"350\"");

					sb.AppendLine("            EraserShortBehavior=\"Stroke\"");
					sb.AppendLine("            EraserPressDurationMs=\"40\"");
					sb.AppendLine("            EraserDebounceMs=\"180\"");
					sb.AppendLine("            EraserReleaseDelayMs=\"75\" />");

					sb.AppendLine("        <AppProfile AppName=\"CliSt\" ProcessName=\"CLIPStudioPaint\"");
					sb.AppendLine("            BarrelShortScanCode=\"0\"");
					sb.AppendLine("            BarrelLongScanCode=\"0\"");

					sb.AppendLine("            BarrelLongPressMs=\"350\"");

					sb.AppendLine("            BarrelShortBehavior=\"Stroke\"");
					sb.AppendLine("            BarrelPressDurationMs=\"40\"");
					sb.AppendLine("            BarrelDebounceMs=\"180\"");
					sb.AppendLine("            BarrelReleaseDelayMs=\"75\"");

					sb.AppendLine("            EraserShortScanCode=\"18\"");
					sb.AppendLine("            EraserShortBehavior=\"ToggleHold\"");
					sb.AppendLine("            EraserDebounceMs=\"180\"");
					sb.AppendLine("            EraserReleaseDelayMs=\"2\" />");
					sb.AppendLine("    </RootConfig.Apps>");
					sb.AppendLine("</RootConfig>");
					File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
					_configRoot = (RootConfig)XamlReader.Parse(sb.ToString());
					ConfigErrorLog = "正常 (デフォルトファイルを自動生成しました)";
				}

				ConfigErrorLog += DumpProfileTree( _configRoot );
				return true;
			}
			catch (XamlParseException xex){
				ConfigErrorLog = $"[XAML エラー] 行 {xex.LineNumber}, 列 {xex.LinePosition}: {xex.Message}";
				return false;
			}
			catch (Exception ex){
				ConfigErrorLog = $"[システムエラー] {ex.Message}";
				return false;
			}
		}

		private IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam){
			if (nCode >= 0)	{
				int msg = (int)wParam;

 				// ログはモニタを観てる時だけ
 				if (_isMonitorTabSelected || IsLoggingActive) {
					// モニターへログをリレー
					AppendDeviceLog("Mouse", $"Msg: 0x{msg:X4}");
				}

				if (_isPhysicalBarrelDown || _isPhysicalEraserDown){
					if (msg == NativeMethods.WM_RBUTTONDOWN || msg == NativeMethods.WM_RBUTTONUP ||
						msg == NativeMethods.WM_MBUTTONDOWN || msg == NativeMethods.WM_MBUTTONUP)
						return (IntPtr)1; // ペンボタン押下中はマウス本来の挙動をブロック
				}
			}
			return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
		}

		private void MainWindow_Loaded(object sender, RoutedEventArgs e){
			this.Visibility = Visibility.Hidden;
			IntPtr hwnd = new WindowInteropHelper(this).Handle;
			HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int m, IntPtr w, IntPtr l, ref bool hd) => {
				if (m == NativeMethods.WM_INPUT) ParsePenRawInput(l);
				return IntPtr.Zero;
			});

			var rid = new NativeMethods.RAWINPUTDEVICE[] {
				new NativeMethods.RAWINPUTDEVICE { usUsagePage = NativeMethods.HID_USAGE_PAGE_DIGITIZER, usUsage = NativeMethods.HID_USAGE_DIGITIZER_PEN, dwFlags = NativeMethods.RIDEV_INPUTSINK, hwndTarget = hwnd }
			};

			if (NativeMethods.RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTDEVICE)))){
				_mouseHookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseHookProc, NativeMethods.GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName), 0);
				_keyboardHookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardHookProc, NativeMethods.GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName), 0);
			}
		}

		private void RemoveHooks()		{
			if (_mouseHookHandle != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);
			if (_keyboardHookHandle != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
		}
	}
}
