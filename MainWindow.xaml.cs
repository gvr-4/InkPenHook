using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;

namespace InkPenHook
{
	// アプリケーション全体のバージョン情報
	public static class AppInfo
	{
		public const string Version = "2026.0910.1620";
	}

	// XAMLマッピング用のパブリッククラス定義（インナークラスにしない）
	public class AppProfile
	{
		public string AppName { get; set; } = "Default";
		public string ProcessName { get; set; } = "krita";
		public ushort BarrelShortScanCode { get; set; } = 0x12;
		public ushort BarrelLongScanCode { get; set; } = 0x00;
		public ushort EraserShortScanCode { get; set; } = 0x12;
		public ushort EraserLongScanCode { get; set; } = 0x00;
	}

	public class GlobalSettings
	{
		public int LongPressMs { get; set; } = 350;
	}

	public class RootConfig
	{
		public GlobalSettings GlobalSettings { get; set; } = new GlobalSettings();
		public List<AppProfile> Apps { get; set; } = new List<AppProfile>();
	}

	public partial class MainWindow : Window
	{
		private bool _lastBarrelState = false;
		private bool _lastEraserState = false;
		private DateTime _barrelPressStartTime = DateTime.MinValue;
		private bool _isLongPressed = false;
		private bool _isKeyDownSent = false;

		private IntPtr _mouseHookHandle = IntPtr.Zero;
		private IntPtr _keyboardHookHandle = IntPtr.Zero;
		private LowLevelHookProc _mouseHookProc;
		private LowLevelHookProc _keyboardHookProc;

		private RootConfig _configRoot = new RootConfig();
		private AppProfile _currentProfile = new AppProfile();
		private const string CONFIG_FILE_NAME = "InkPenHook.xaml"; // プロジェクト名.xamlに固定
		private System.Windows.Forms.NotifyIcon _notifyIcon;

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);
		[DllImport("user32.dll", SetLastError = true)]
		private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
		[DllImport("user32.dll", SetLastError = true)]
		private static extern uint SendInput(uint nInputs, INPUT_FLAT[] pInputs, int cbSize);
		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);
		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool UnhookWindowsHookEx(IntPtr hhk);
		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr GetModuleHandle(string lpModuleName);
		[DllImport("user32.dll")]
		private static extern IntPtr GetForegroundWindow();
		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
		[DllImport("user32.dll")]
		private static extern uint MapVirtualKey(uint uCode, uint uMapType);
		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		private static extern int GetKeyNameText(int lParam, [Out] StringBuilder lpString, int nSize);

		private delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

		private const int WH_MOUSE_LL = 14;
		private const int WH_KEYBOARD_LL = 13;
		private const int WM_RBUTTONDOWN = 0x0204;
		private const int WM_RBUTTONUP = 0x0205;
		private const int WM_MBUTTONDOWN = 0x0207;
		private const int WM_MBUTTONUP = 0x0208;
		private const ushort HID_USAGE_PAGE_DIGITIZER = 0x0D;
		private const ushort HID_USAGE_DIGITIZER_PEN = 0x02;
		private const uint RIDEV_INPUTSINK = 0x00000100;
		private const int WM_INPUT = 0x00FF;
		private const uint RID_INPUT = 0x10000003;
		private const int INPUT_KEYBOARD = 1;
		private const uint KEYEVENTF_SCANCODE = 0x0008;
		private const uint KEYEVENTF_KEYUP = 0x0002;

		[StructLayout(LayoutKind.Sequential)]
		private struct RAWINPUTDEVICE
		{
			public ushort usUsagePage;
			public ushort usUsage;
			public uint dwFlags;
			public IntPtr hwndTarget;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct RAWINPUTHEADER
		{
			public uint dwType;
			public uint dwSize;
			public IntPtr hDevice;
			public IntPtr wParam;
		}

		[StructLayout(LayoutKind.Explicit, Size = 40)]
		struct INPUT_FLAT
		{
			[FieldOffset(0)] public int type;
			[FieldOffset(8)] public ushort wVk;
			[FieldOffset(10)] public ushort wScan;
			[FieldOffset(12)] public uint dwFlags;
			[FieldOffset(16)] public uint time;
			[FieldOffset(24)] public IntPtr dwExtraInfo;
		}

		public MainWindow()
		{
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
			this.Closed += MainWindow_Closed;
			_mouseHookProc = LowLevelMouseProc;
			_keyboardHookProc = LowLevelKeyboardProc;

			// 構成のロード（エラーならダイアログを出して安全に落とす）
			if (!LoadOrCreateConfig())
			{
				System.Windows.MessageBox.Show(
					"設定ファイル(XAML)の読み込みに失敗したため、起動を中止します。",
					"InkPenHook エラー", MessageBoxButton.OK, MessageBoxImage.Error);
				System.Windows.Application .Current.Shutdown();
			}
			SetupTaskTrayIcon();
		}

		private bool LoadOrCreateConfig()
		{
			try
			{
				string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILE_NAME);
				if (File.Exists(configPath))
				{
					string xamlString = File.ReadAllText(configPath);
					try
					{
						_configRoot = (RootConfig)XamlReader.Parse(xamlString);
					}
					catch (System.Windows.Markup.XamlParseException xex)
					{
						// 行番号・列番号を含んだ詳細メッセージを構築
						string errorDetail = $"【XAML構文エラー】\n" +
											 $"ファイル名: {CONFIG_FILE_NAME}\n" +
											 $"発生箇所: {xex.LineNumber}行目 {xex.LinePosition}桁目\n\n" +
											 $"エラー内容:\n{xex.Message}";

						System.Windows.MessageBox.Show(errorDetail, "XAMLパース例外発生",
							MessageBoxButton.OK, MessageBoxImage.Warning);
						return false;
					}

					// 特製データツリーダンパーで内容を出力確認
					string dumperText = ConfigDataDumper.Dump(_configRoot);
					Debug.WriteLine("⚙️ XAML設定ファイルを正常に展開しました。\n--\n" + dumperText + "--\n");
				}
				else
				{
					// デフォルトXAMLの手動生成 (※assembly指定を維持してパースを安定化)
					StringBuilder sb = new StringBuilder();
					sb.AppendLine("<RootConfig xmlns=\"clr-namespace:InkPenHook;assembly=InkPenHook\">");
					sb.AppendLine("    <!-- 全体共通設定 -->");
					sb.AppendLine("    <RootConfig.GlobalSettings>");
					sb.AppendLine("        <GlobalSettings LongPressMs=\"350\" />");
					sb.AppendLine("    </RootConfig.GlobalSettings>");
					sb.AppendLine("    <!-- アプリ別プロファイル -->");
					sb.AppendLine("    <RootConfig.Apps>");
					sb.AppendLine("        <AppProfile AppName=\"KRITA\" ProcessName=\"krita\" BarrelShortScanCode=\"18\" BarrelLongScanCode=\"0\" EraserShortScanCode=\"0\" EraserLongScanCode=\"0\" />");
					sb.AppendLine("        <AppProfile AppName=\"CliSta\" ProcessName=\"CLIPStudioPaint\" BarrelShortScanCode=\"18\" BarrelLongScanCode=\"0\" EraserShortScanCode=\"0\" EraserLongScanCode=\"23\" />");
					sb.AppendLine("    </RootConfig.Apps>");
					sb.AppendLine("</RootConfig>");
					File.WriteAllText(configPath, sb.ToString(), Encoding.UTF8);

					// メモリ上にも退避
					_configRoot = new RootConfig();
					_configRoot.Apps.Add(new AppProfile { AppName = "KRITA", ProcessName = "krita", BarrelShortScanCode = 18 });
					_configRoot.Apps.Add(new AppProfile { AppName = "CliSta", ProcessName = "CLIPStudioPaint", BarrelShortScanCode = 18, EraserLongScanCode = 23 });
					Debug.WriteLine($"⚙️ {CONFIG_FILE_NAME} を新規自動生成しました。");
				}
				return true;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"⚠️ ファイルアクセス重大エラー: {ex.Message}");
				return false;
			}
		}


		// 3. マウスフックを「プロファイル基準」に切り替えて、RawInputの遅れをカバー
		private IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam)
		{
			if (nCode >= 0)
			{
				int msg = (int)wParam;

				// ★RawInputの状態変化を待たず、現在Kritaかクリスタが開いているなら、
				// 割り込みの原因となる右・中クリックを「常に先手を打ってもみ消す」
				if (!string.IsNullOrEmpty(_currentProfile.ProcessName))
				{
					if (msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP || msg == WM_MBUTTONDOWN || msg == WM_MBUTTONUP)
					{
						// ただし、バレル長押し（0x17など）が指定されていて、かつ物理的に押されていない時はOS規定右クリックを通す
						if (_currentProfile.BarrelLongScanCode != 0 && !_lastBarrelState)
						{
							return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
						}

						// それ以外（消しゴム動作中やトグル動作中）のマウス偽装は100%即座にもみ消す
						return (IntPtr)1;
					}
				}
			}
			return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
		}

        private IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
        }

        private void InstallHooks()
		{
			if (_mouseHookHandle == IntPtr.Zero)
			{
				using (Process curProcess = Process.GetCurrentProcess())
				using (ProcessModule curModule = curProcess.MainModule)
				{
					_mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, GetModuleHandle(curModule.ModuleName), 0);
					_keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookProc, GetModuleHandle(curModule.ModuleName), 0);
				}
				Debug.WriteLine("⚓ 乗っ取り用WindowsHookを設置しました。");
			}
		}
		private void RemoveHooks()
		{
			if (_mouseHookHandle != IntPtr.Zero)
			{
				UnhookWindowsHookEx(_mouseHookHandle);
				UnhookWindowsHookEx(_keyboardHookHandle);
				_mouseHookHandle = IntPtr.Zero;
				_keyboardHookHandle = IntPtr.Zero;
				Debug.WriteLine("⚓ WindowsHookを解除しました。");
			}
		}
		private void MainWindow_Loaded(object sender, RoutedEventArgs e)
		{
			this.Visibility = Visibility.Hidden;
			WindowInteropHelper helper = new WindowInteropHelper(this);
			HwndSource source = HwndSource.FromHwnd(helper.Handle);
			if (source != null) source.AddHook(HwndHook);
			RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
			rid[0].usUsagePage = HID_USAGE_PAGE_DIGITIZER;
			rid[0].usUsage = HID_USAGE_DIGITIZER_PEN;
			rid[0].dwFlags = RIDEV_INPUTSINK;
			rid[0].hwndTarget = helper.Handle;
			if (RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
			{
				InstallHooks();
				Debug.WriteLine("★生ペン入力監視・完全上書きモードが開始されました。");
			}
		}
		private void MainWindow_Closed(object sender, EventArgs e)
		{
			RemoveHooks();
			if (_notifyIcon != null)
			{
				_notifyIcon.Visible = false;
				_notifyIcon.Dispose();
			}
		}
		private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (msg == WM_INPUT) ParsePenRawInput(lParam);
			return IntPtr.Zero;
		}


		// 1. クラスのフィールドに「不感時間用」と「アクティブウィンドウキャッシュ用」の変数を追加
		private DateTime _lastEraserShortTriggerTime = DateTime.MinValue;
		private const int ERASER_DEBOUNCE_MS = 180; // テールスイッチ用チャタリング防止（調整枠）

		private IntPtr _lastForegroundHwnd = IntPtr.Zero;
		private string _cachedProcessName = "";
		private DateTime _lastProfileUpdateTime = DateTime.MinValue;

		// 2. 毎回のプロセス確認を劇的に高速化（ミリ秒単位の負荷をほぼゼロ化）
		private void UpdateCurrentProfile()
		{
			IntPtr hwnd = GetForegroundWindow();
			if (hwnd == IntPtr.Zero) return;

			// 前回のチェックから500ms未満、かつ同じウィンドウならキャッシュをそのまま使う（超軽量化）
			if (hwnd == _lastForegroundHwnd && (DateTime.Now - _lastProfileUpdateTime).TotalMilliseconds < 500)
			{
				return;
			}

			_lastForegroundHwnd = hwnd;
			_lastProfileUpdateTime = DateTime.Now;

			GetWindowThreadProcessId(hwnd, out uint processId);
			try
			{
				using (Process proc = Process.GetProcessById((int)processId))
				{
					string targetName = proc.ProcessName.ToLower();

					if (targetName.Contains("explorer") ||
						targetName.Contains("inkpenhook") ||
						targetName.Contains("shellexperiencehost"))
					{
						return;
					}

					if (_cachedProcessName == targetName) return; // プロセス名に変更がなければ何もしない
					_cachedProcessName = targetName;

					foreach (var app in _configRoot.Apps)
					{
						if (targetName.Contains(app.ProcessName.ToLower()))
						{
							if (_currentProfile.ProcessName != app.ProcessName)
							{
								_currentProfile = app;
								Debug.WriteLine($"🎯 プロファイル確定変更: [{app.AppName}] を固定適用");
							}
							return;
						}
					}
				}
			}
			catch { }

			_currentProfile = new AppProfile { AppName = "Default_None", ProcessName = "" };
			_cachedProcessName = "";
		}

		// ループ制御用のキャンセル・トークン
		private System.Threading.CancellationTokenSource? _barrelLoopToken;
		private System.Threading.CancellationTokenSource? _eraserLoopToken;

		private void ParsePenRawInput(IntPtr hRawInput)
		{
			uint dwSize = 0;
			GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero,
				ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
			if (dwSize == 0) return;

			IntPtr pData = Marshal.AllocHGlobal((int)dwSize);
			try
			{
				if (GetRawInputData(hRawInput, RID_INPUT, pData, ref dwSize,
					(uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) == dwSize)
				{
					int headerSize = Marshal.SizeOf(typeof(RAWINPUTHEADER));
					int rawDataSize = (int)dwSize - headerSize;
					if (rawDataSize > 9)
					{
						byte[] rawBytes = new byte[rawDataSize];
						Marshal.Copy(pData + headerSize, rawBytes, 0, rawDataSize);

						byte statusByte = rawBytes[9]; // アドレス09のデータ

						// 1. 各ボタンの物理フラグを厳密に抽出 (マスク処理)
						bool barrel = (statusByte & 0x02) != 0;
						bool eraser = (statusByte & 0x08) != 0;
						bool tip = (statusByte & 0x01) != 0;

						// ★バレルボタンと消しゴムボタンの干渉防止・完全独立化
						// Wacom AESがテール側で近づいてきた時、バレル側を強制クリアする
						if (eraser) barrel = false;

						// 2. プロファイルのリアルタイム確認
						UpdateCurrentProfile();
						if (string.IsNullOrEmpty(_currentProfile.ProcessName)) return;

						// 3. 互いに干渉しない独立したスマートアクションの実行
						ProcessBarrelSmartActions(barrel, tip);
						ProcessEraserSmartActions(eraser, tip);
					}
				}
			}
			catch (Exception ex) { Debug.WriteLine($"Error: {ex.Message}"); }
			finally { Marshal.FreeHGlobal(pData); }
		}

		private void ProcessBarrelSmartActions(bool currentBarrel, bool currentTip)
		{
			int longPressMs = _configRoot.GlobalSettings != null ?
				_configRoot.GlobalSettings.LongPressMs : 200;

			if (currentBarrel && !_lastBarrelState)
			{
				_barrelPressStartTime = DateTime.Now;
				_isLongPressed = false;
				_isKeyDownSent = false;
				_lastBarrelState = true;
				Debug.WriteLine("◆バレルボタン Down");
			}

			if (currentBarrel && _lastBarrelState)
			{
				if (!_isLongPressed &&
					(DateTime.Now - _barrelPressStartTime).TotalMilliseconds >= longPressMs)
				{
					_isLongPressed = true;

					if (_currentProfile.BarrelLongScanCode != 0 && !_isKeyDownSent)
					{
						_isKeyDownSent = true;
						_barrelLoopToken = new System.Threading.CancellationTokenSource();
						var token = _barrelLoopToken.Token;
						ushort code = _currentProfile.BarrelLongScanCode;

						Debug.WriteLine($"★[バレル:長押しホールド開始] ➔ 0x{code:X2}");
						Task.Run(async () => {
							try
							{
								while (!token.IsCancellationRequested)
								{
									SendHardwareKeyDownOnly(code);
									await Task.Delay(25, token);
								}
							}
							catch (OperationCanceledException)
							{
								Debug.WriteLine("💡 [バレルホールド] ループが安全にキャンセルされました。");
							}
						}, token);
					}
				}
			}

			if (!currentBarrel && _lastBarrelState)
			{
				if (_barrelLoopToken != null)
				{
					_barrelLoopToken.Cancel();
					_barrelLoopToken.Dispose();
					_barrelLoopToken = null;
				}

				if (!_isLongPressed && _currentProfile.BarrelShortScanCode != 0)
				{
					Debug.WriteLine($"★[バレル:短押しトグル] ➔ 0x{_currentProfile.BarrelShortScanCode:X2}");
					SendHardwareKeyStroke(_currentProfile.BarrelShortScanCode);
				}
				else if (_isKeyDownSent && _currentProfile.BarrelLongScanCode != 0)
				{
					Debug.WriteLine($"★[バレル:長押し物理解放ブレイク] ➔ 0x{_currentProfile.BarrelLongScanCode:X2}");
					SendHardwareKeyUpOnly(_currentProfile.BarrelLongScanCode);
				}
				_isKeyDownSent = false;
				_lastBarrelState = false;
			}
		}

		// 消しゴムボタン専用の独立したタイムスタンプと状態フラグ
		private DateTime _eraserPressStartTime = DateTime.MinValue;
		private bool _isEraserLongPressed = false;
		private bool _isEraserKeyDownSent = false;

		// 4. 消しゴムの短押しトグル処理に「不感時間」と「後出しスレッド」を導入
		private void ProcessEraserSmartActions(bool currentEraser, bool currentTip)
		{
			int longPressMs = _configRoot.GlobalSettings != null ?
				_configRoot.GlobalSettings.LongPressMs : 200;

			if (currentEraser && !_lastEraserState)
			{
				_eraserPressStartTime = DateTime.Now;
				_isEraserLongPressed = false;
				_isEraserKeyDownSent = false;
				_lastEraserState = true;
				Debug.WriteLine("🧹 消しゴムボタン Down");
			}

			if (currentEraser && _lastEraserState)
			{
				if (!_isEraserLongPressed &&
					(DateTime.Now - _eraserPressStartTime).TotalMilliseconds >= longPressMs)
				{
					_isEraserLongPressed = true;

					if (_currentProfile.EraserLongScanCode != 0 && !_isEraserKeyDownSent)
					{
						_isEraserKeyDownSent = true;
						_eraserLoopToken = new System.Threading.CancellationTokenSource();
						var token = _eraserLoopToken.Token;
						ushort code = _currentProfile.EraserLongScanCode;

						Debug.WriteLine($"★[消しゴム:長押しホールド開始] ➔ 0x{code:X2}");
						Task.Run(async () => {
							try
							{
								while (!token.IsCancellationRequested)
								{
									SendHardwareKeyDownOnly(code);
									await Task.Delay(25, token);
								}
							}
							catch (OperationCanceledException)
							{
								Debug.WriteLine("💡 [消しゴムホールド] ループが安全にキャンセルされました。");
							}
						}, token);
					}
				}
			}

			if (!currentEraser && _lastEraserState)
			{
				if (_eraserLoopToken != null)
				{
					_eraserLoopToken.Cancel();
					_eraserLoopToken.Dispose();
					_eraserLoopToken = null;
				}

				if (!_isEraserLongPressed && _currentProfile.EraserShortScanCode != 0)
				{
					// ★ チャタリングガード：前回のトリガーから指定ミリ秒以内なら無視して相殺を防ぐ
					double elapsed = (DateTime.Now - _lastEraserShortTriggerTime).TotalMilliseconds;
					if (elapsed > ERASER_DEBOUNCE_MS)
					{
						_lastEraserShortTriggerTime = DateTime.Now;
						ushort code = _currentProfile.EraserShortScanCode;

						Debug.WriteLine($"★[消しゴム:短押しトグル実行] ➔ 0x{code:X2}");

						// OSのマウスイベント処理が完全に終了した直後に滑り込ませるため、タスクでわずかにずらす
						Task.Run(async () => {
							await Task.Delay(2); // KritaのQtインクキューの裏をかく極小ディレイ
							SendHardwareKeyStroke(code);
						});
					}
					else
					{
						Debug.WriteLine("⚠️ [消しゴムチャタリング感知] トグルをガードしました。");
					}
				}
				else if (_isEraserKeyDownSent && _currentProfile.EraserLongScanCode != 0)
				{
					Debug.WriteLine($"★[消しゴム:長押し物理解放] ➔ 0x{_currentProfile.EraserLongScanCode:X2}");
					SendHardwareKeyUpOnly(_currentProfile.EraserLongScanCode);
				}
				_isEraserKeyDownSent = false;
				_lastEraserState = false;
			}
		}

		private void SendHardwareKeyStroke(ushort scanCode)
		{
			INPUT_FLAT[] inputs = new INPUT_FLAT[2];
			inputs[0] = CreateInput(scanCode, false);
			inputs[1] = CreateInput(scanCode, true);
			SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT_FLAT)));
		}
		private void SendHardwareKeyDownOnly(ushort scanCode)
		{
			INPUT_FLAT[] inputs = new INPUT_FLAT[1];
			inputs[0] = CreateInput(scanCode, false);
			SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT_FLAT)));
		}
		private void SendHardwareKeyUpOnly(ushort scanCode)
		{
			INPUT_FLAT[] inputs = new INPUT_FLAT[1];
			inputs[0] = CreateInput(scanCode, true);
			SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT_FLAT)));
		}
		private INPUT_FLAT CreateInput(ushort scanCode, bool isKeyUp)
		{
			INPUT_FLAT input = new INPUT_FLAT();
			input.type = INPUT_KEYBOARD;
			input.wVk = (ushort)MapVirtualKey(scanCode, 1);
			input.wScan = scanCode;
			input.dwFlags = KEYEVENTF_SCANCODE | (isKeyUp ? KEYEVENTF_KEYUP : 0);
			return input;
		}
		private string GetFriendlyKeyName(ushort scanCode)
		{
			if (scanCode == 0) return "なし";
			int lParam = scanCode << 16;
			StringBuilder sb = new StringBuilder(50);
			if (GetKeyNameText(lParam, sb, sb.Capacity) > 0) return sb.ToString();
			return $"Code: 0x{scanCode:X2}";
		}
		private void SetupTaskTrayIcon()
		{
			_notifyIcon = new System.Windows.Forms.NotifyIcon();
			_notifyIcon.Text = $"InkPenHook v{AppInfo.Version}";
			var contextMenu = new System.Windows.Forms.ContextMenuStrip();

			try
			{
				// アプリケーション内の埋め込みリソース (.ico) を安全に読み込む
				// ※ "app.ico" の部分は、実際のアイコンファイル名に合わせて書き換えてください。
				var iconUri = new Uri("pack://application:,,,/InkPenHook_Icon_16-256.ico", UriKind.Absolute);
				var streamInfo = System.Windows.Application.GetResourceStream(iconUri);

				if (streamInfo != null)
				{
					_notifyIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);
				}
				else
				{
					_notifyIcon.Icon = System.Drawing.SystemIcons.Application; // 失敗時のフォールバック
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"⚠️ アイコン読み込み失敗: {ex.Message}");
				_notifyIcon.Icon = System.Drawing.SystemIcons.Application;
			}

			_notifyIcon.Text = $"InkPenHook v{AppInfo.Version}";

			contextMenu.Items.Add("設定を再読み込み", null, (s, e) =>
			{
				// 安全な再読み込みのための退避ロジック
				RootConfig backup = _configRoot;
				if (!LoadOrCreateConfig())
				{
					_configRoot = backup;
					// 失敗時は直前の正常な状態をキープ
					System.Windows.MessageBox.Show("XAML設定ファイルの再読み込みに失敗したため、前の設定を維持します。", "再読み込みエラー");
				}
				else
				{
					System.Windows.MessageBox.Show("設定を正常に再読み込みしました。", "InkPenHook");
				}
			}
			);

			contextMenu.Items.Add("現在の設定を確認", null, (s, e) =>
			{
				int longPressMs = _configRoot.GlobalSettings != null ? _configRoot.GlobalSettings.LongPressMs : 350;
				string msg = $"【InkPenHook v{AppInfo.Version}状態】\n・長押し判定:	{longPressMs}ms\n\n";
				msg += $"【現在の適用プロファイル】\n🎯 アプリ: {_currentProfile.AppName}({_currentProfile.ProcessName})\n";
				msg += $" ・バレル短(Alt): {GetFriendlyKeyName(_currentProfile.BarrelShortScanCode)}\n";
				msg += $" ・バレル長(Mmt): {GetFriendlyKeyName(_currentProfile.BarrelLongScanCode)}\n";
				msg += $" ・消しゴム短(Alt): {GetFriendlyKeyName(_currentProfile.EraserShortScanCode)}\n";
				msg += $" ・消しゴム長(Mmt): {GetFriendlyKeyName(_currentProfile.EraserLongScanCode)}\n\n";
				msg += $"※構成ファイル: {CONFIG_FILE_NAME}";

				System.Windows.MessageBox.Show(msg, "InkPenHook ステータス確認");
			}
			);

			contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

			contextMenu.Items.Add("終了", null, (s, e) =>
			{
				this.Close();
			}
			);
			_notifyIcon.ContextMenuStrip = contextMenu;
			_notifyIcon.Visible = true;
		}


	}

	// ★★★ Visualオブジェクト用から「データ構造解析用」へと完全に刷新された特製ダンパー ★★★
	public static class ConfigDataDumper
	{
		public static string Dump(RootConfig config)
		{
			if (config == null) return "(Configuration Object is null)";
			StringBuilder sb = new StringBuilder();
			sb.AppendLine("[RootConfig Node]");
			sb.AppendLine($"  └── [GlobalSettings] LongPressMs = {config.GlobalSettings?.LongPressMs}ms");
			sb.AppendLine("  └── [Apps List]");
			if (config.Apps == null || config.Apps.Count == 0)
			{
				sb.AppendLine("        └── (No App Profiles Registered)");
			}
			else
			{
				foreach (var app in config.Apps)
				{
					sb.AppendLine($"        ├── [AppProfile: {app.AppName}] (Process: {app.ProcessName}.exe)");
					sb.AppendLine($"        │     ├── Barrel  Short=0x{app.BarrelShortScanCode:X2}, Long=0x{app.BarrelLongScanCode:X2}");
					sb.AppendLine($"        │     └── Eraser  Short=0x{app.EraserShortScanCode:X2}, Long=0x{app.EraserLongScanCode:X2}");
				}
			}
			return sb.ToString();
		}
	}
}

