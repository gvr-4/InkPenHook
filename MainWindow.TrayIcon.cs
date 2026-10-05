/* ========================================================================= */
/* [PROJECT] InkPenHook - Tray GUI & Global HotKey Control Panel 2026/09/25(金) 00:25:00 */
/* ========================================================================= */

using System;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using FontFamily = System.Windows.Media.FontFamily;

namespace InkPenHook{
	public partial class MainWindow : Window{
		private NotifyIcon _notifyIcon;
		private Window _osdWindow;
		private TextBlock _osdTextBlock;
		private DispatcherTimer _osdTimer;
		// 💡 2重ホットキー定義 (DASHBOARD=9999, ERASER_F20=8888)
		private const int HOTKEY_DASH_ID = 9999;
		private const int HOTKEY_ERAS_ID = 8888;
		[DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
		[DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
		private void SetupTaskTrayIcon(){
			if (_notifyIcon != null) return;
			_notifyIcon = new NotifyIcon { Text = $"InkPenHook Core v{AppInfo.Version}" };
			var contextMenu = new ContextMenuStrip();

			contextMenu.Items.Add("設定を再読み込み", null, (s, e) => TriggerReload());
			contextMenu.Items.Add("現在の設定を確認", null, (s, e) => OpenControlPanel());
			contextMenu.Items.Add(new ToolStripSeparator());
			contextMenu.Items.Add("終了", null, (s, e) => TriggerShutdown());
			_notifyIcon.ContextMenuStrip = contextMenu;

			try	{
				var iconUri = new Uri("pack://application:,,,/InkPenHook_Icon_16-256.ico", UriKind.Absolute);
				var streamInfo = System.Windows.Application.GetResourceStream(iconUri);
				if (streamInfo != null) _notifyIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);
			}
			catch {
				_notifyIcon.Icon = System.Drawing.SystemIcons.Application;
			}
			_notifyIcon.Visible = true;
			CreateOSDWindow();
			IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
			// 網1: ダッシュボード (Ctrl + Shift + F12)
			RegisterHotKey(hwnd, HOTKEY_DASH_ID, 0x0002 | 0x0004, 0x7B);
			// 網2: 💡 Wacomテールスイッチパルス (VK_F20: 0x83, 修飾キーなし: 0) をシステムへ正規バインド！
			RegisterHotKey(hwnd, HOTKEY_ERAS_ID, 0x0000, 0x83);
			HwndSource.FromHwnd(hwnd)?.AddHook(HotKeyWinProc);
		}

		private IntPtr HotKeyWinProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled){
			if (msg == 0x0312){ // WM_HOTKEY
				int id = wParam.ToInt32();
				if (id == HOTKEY_DASH_ID){
					UpdateCurrentProfile();
					OpenControlPanel();
					handled = true;
				}else if (id == HOTKEY_ERAS_ID){
					// 💡 取りこぼし100%ゼロ：Wacomの消しゴムパルスが届いた瞬間、ダイレクトにトグルを叩き起こす
					// 廃止 TriggerEraserToggle();
					handled = true;
				}
			}
			return IntPtr.Zero;
		}

		private void ShowOSD(string message){
			this.Dispatcher.BeginInvoke(new Action(() => {
				if (_osdWindow == null) return; _osdTimer.Stop(); _osdTextBlock.Text = message;
				_osdWindow.Show();
				_osdWindow.Left = (SystemParameters.PrimaryScreenWidth - _osdWindow.ActualWidth) / 2;
				_osdWindow.Top = SystemParameters.PrimaryScreenHeight - _osdWindow.ActualHeight - 120;
				_osdTimer.Start();
			}));
		}

		private void OpenControlPanel(int initialTabIndex = 0){
			// 2026/10/05(月) 23:45:53
			// 引数で「初期選択したいタブインデックス」を渡せるように拡張 (デフォルト0: 有効構成)
			var panel = new Window{
				  Title = "InkPenHook ダッシュボード"
				, WindowStyle = WindowStyle.ToolWindow
				, Topmost = true
				, Width = 550
				, Height = 420
				, WindowStartupLocation = WindowStartupLocation.CenterScreen
				, Background = new SolidColorBrush(Color.FromArgb(245, 30, 30, 30))
				, Foreground = Brushes.White
			};

			var mainGrid = new System.Windows.Controls.Grid();
			mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
			mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

			var tabControl = new System.Windows.Controls.TabControl {
				Background = Brushes.Transparent,
				BorderThickness = new Thickness(0),
				Margin = new Thickness(10)
			};

			var tabHeaderStyle = new Style(typeof(TabItem));
			tabHeaderStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.LightGray));
			tabHeaderStyle.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
			tabControl.ItemContainerStyle = tabHeaderStyle;

			// ==========================================
			// タブ①：有効構成
			// ==========================================
			var tabConfig = new TabItem { Header = "有効構成" };
			var configStack = new System.Windows.Controls.StackPanel();

			// 再読込時に動的書き換えができるよう、TextBlockをローカル変数として分離
			var statusText = new TextBlock{
				 FontSize = 13
				, FontWeight = FontWeights.Bold
				, Margin = new Thickness(15)
				, Foreground = Brushes.LightGray
			};

			// 構成情報の更新用ローカル関数
			Action updateConfigText = () => {
				statusText.Text = $"【InkPenHook 現在の有効構成】\n\n"
				  + $"🎯 適用アプリ: {_currentProfile.AppName} ({_currentProfile.ProcessName})\n"
				  + $"⚙ Eraseトグルモード: {_currentProfile.EraserShortBehavior}\n"
				  + $"📦 コアビルド日時: {AppInfo.GetBuildDateTime()}\n\n"
				  + $"📘 ※詳細な設定書式やサンプルは、Exeと同梱されている\n"
				  + $"    「InkPenHook_ReadMe.txt」を参照してください。";
			};
			updateConfigText();

			configStack.Children.Add(statusText);
			tabConfig.Content = configStack;
			tabControl.Items.Add(tabConfig);

			// ==========================================
			// タブ②：リアルタイムモニター
			// ==========================================
			var tabMonitor = new TabItem { Header = "リアルタイムモニター" };
			var monitorGrid = new System.Windows.Controls.Grid();
			monitorGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
			monitorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

			var monitorTextBox = new System.Windows.Controls.TextBox {
				Text = GetMonitorText(), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				Background = new SolidColorBrush(Color.FromArgb(255, 15, 15, 15)),
				Foreground = Brushes.LimeGreen, FontFamily = new FontFamily("Consolas"), FontSize = 11,
				Margin = new Thickness(5), BorderThickness = new Thickness(1), BorderBrush = Brushes.DimGray
			};
			System.Windows.Controls.Grid.SetRow(monitorTextBox, 0);
			monitorGrid.Children.Add(monitorTextBox);

			var monitorCtrlStack = new System.Windows.Controls.StackPanel {
				Orientation = System.Windows.Controls.Orientation.Horizontal,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
				Margin = new Thickness(5)
			};

			var btnFileLog = new System.Windows.Controls.Button {
				Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 10, 0),
				FontWeight = FontWeights.Bold
			};

			Action updateLogButtonState = () => {
				if (IsLoggingActive) {
					btnFileLog.Content = "🔴 採取終了 (ダウンロードへ出力中)";
					btnFileLog.Background = Brushes.DarkRed;
					btnFileLog.Foreground = Brushes.White;
				} else {
					btnFileLog.Content = "▶ ログ採取開始 (最大1MB)";
					btnFileLog.Background = Brushes.DarkGreen;
					btnFileLog.Foreground = Brushes.White;
				}
			};
			updateLogButtonState();

			btnFileLog.Click += (s, e) => {
				if (IsLoggingActive) StopFileLogging();
				else StartFileLogging();
				updateLogButtonState();
			};

			var btnClearBuf = new System.Windows.Controls.Button {
				Content = "画面クリア", Padding = new Thickness(10, 5, 10, 5),
				Background = Brushes.SlateGray, Foreground = Brushes.White
			};
			btnClearBuf.Click += (s, e) => { ClearMonitorLogBuffer(); monitorTextBox.Text = GetMonitorText(); };

			monitorCtrlStack.Children.Add(btnFileLog);
			monitorCtrlStack.Children.Add(btnClearBuf);
			System.Windows.Controls.Grid.SetRow(monitorCtrlStack, 1);
			monitorGrid.Children.Add(monitorCtrlStack);
			tabMonitor.Content = monitorGrid;
			tabControl.Items.Add(tabMonitor);

			var refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
			refreshTimer.Tick += (s, e) => {
				updateLogButtonState();
				if (tabControl.SelectedItem == tabMonitor) {
					monitorTextBox.Text = GetMonitorText();
					monitorTextBox.ScrollToEnd();
				}
			};
			refreshTimer.Start();
			panel.Closed += (s, e) => refreshTimer.Stop();

			// ==========================================
			// タブ③：エラーログ
			// ==========================================
			var tabError = new TabItem { Header = "エラーログ" };
			var errorStack = new System.Windows.Controls.StackPanel();

			var errorTitle = new TextBlock {
				Text = "【XAML構成ファイル解析ログ】", FontSize = 13, FontWeight = FontWeights.Bold,
				Foreground = Brushes.LightGray, Margin = new Thickness(15, 15, 15, 10)
			};

			var errorContent = new System.Windows.Controls.TextBox {
				IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
				FontFamily = new FontFamily("Consolas"), Padding = new Thickness(8), BorderThickness = new Thickness(1), Height = 220,
				Margin = new Thickness(15, 0, 15, 15)
			};

			// エラー描画リフレッシュ関数
			Action updateErrorVisuals = () => {
				errorContent.Text = ConfigErrorLog;
				bool hasError = ConfigErrorLog.Contains("[XAML エラー]") || ConfigErrorLog.Contains("[システムエラー]");
				errorContent.Background = new SolidColorBrush(Color.FromArgb(255, 25, 20, 20));
				errorContent.Foreground = hasError ? Brushes.Tomato : Brushes.LightGreen;
				errorContent.BorderBrush = hasError ? Brushes.Brown : Brushes.DimGray;
			};
			updateErrorVisuals();

			errorStack.Children.Add(errorTitle); errorStack.Children.Add(errorContent);
			tabError.Content = errorStack;
			tabControl.Items.Add(tabError);

			// 初期指定されたタブを有効化（エラー検知時のジャンプ等に対応）
			if (initialTabIndex >= 0 && initialTabIndex < tabControl.Items.Count) {
				tabControl.SelectedIndex = initialTabIndex;
			}

			System.Windows.Controls.Grid.SetRow(tabControl, 0);
			mainGrid.Children.Add(tabControl);

			// ==========================================
			// 最下部：共通アクションボタンエリア
			// ==========================================
			var btnGrid = new System.Windows.Controls.Grid { Margin = new Thickness(20, 0, 20, 15) };
			btnGrid.ColumnDefinitions.Add(new ColumnDefinition());
			btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15) });
			btnGrid.ColumnDefinitions.Add(new ColumnDefinition());

			var btnReload = new System.Windows.Controls.Button{ Content = "構成再読込", Padding = new Thickness(15, 8, 15, 8), Background = Brushes.DarkSlateGray, Foreground = Brushes.White, FontWeight = FontWeights.Bold };
			// 【修正】panel.Close() を外して画面を維持。その場で中身を最新状態にリフレッシュします。
			btnReload.Click += (s, e) => {
				TriggerReload();
				updateConfigText();
				updateErrorVisuals();
				// 再読込した結果エラーに反転した場合は、その場でエラータブへ自動ジャンプ
				if (ConfigErrorLog.Contains("[XAML エラー]") || ConfigErrorLog.Contains("[システムエラー]")) {
					tabControl.SelectedIndex = 2;
				}
			};
			System.Windows.Controls.Grid.SetColumn(btnReload, 0); btnGrid.Children.Add(btnReload);

			var btnClose = new System.Windows.Controls.Button{ Content = "コア完全終了", Padding = new Thickness(15, 8, 15, 8), Background = Brushes.Firebrick, Foreground = Brushes.White, FontWeight = FontWeights.Bold };
			btnClose.Click += (s, e) => { panel.Close(); TriggerShutdown(); };
			System.Windows.Controls.Grid.SetColumn(btnClose, 2); btnGrid.Children.Add(btnClose);

			System.Windows.Controls.Grid.SetRow(btnGrid, 1);
			mainGrid.Children.Add(btnGrid);

			panel.Content = mainGrid;
			panel.ShowDialog();
		}

		private void TriggerReload() {
			// 2026/10/05(月) 23:49:26 【修正】トレイメニュー側から呼び出される「再読込トリガー」の割り込み修正
			if (LoadOrCreateConfig()) {
				ShowOSD("Config Reloaded Successful.");
			} else {
				// トレイ側からの読込時に失敗した場合、親切設計として自動でエラーログタブ(Index: 2)を開く
				ShowOSD("Config Load Error!");
				OpenControlPanel(2);
			}
		}

		private void TriggerShutdown() {
			if (_notifyIcon != null) _notifyIcon.Visible = false;
			IntPtr hwnd = new WindowInteropHelper(this).Handle;
			 UnregisterHotKey(hwnd, HOTKEY_DASH_ID);
			 UnregisterHotKey(hwnd, HOTKEY_ERAS_ID);
			 System.Windows.Application.Current.Shutdown();
		}
		private void CreateOSDWindow() {
			_osdWindow = new Window {
				  WindowStyle = WindowStyle.None
				, AllowsTransparency = true
				, Background = Brushes.Transparent
				, Topmost = true
				, ShowInTaskbar = false
				, SizeToContent = SizeToContent.WidthAndHeight
				, IsHitTestVisible = false
			};
			var border = new Border {
				  Background = new SolidColorBrush(Color.FromArgb(180, 20, 20, 20))
				, CornerRadius = new CornerRadius(8)
				, Padding = new Thickness(24, 12, 24, 12)
				, BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255))
				, BorderThickness = new Thickness(1)
			};
			_osdTextBlock = new TextBlock {
				  Foreground = Brushes.White
				, FontSize = 18, FontWeight = FontWeights.Bold
				, HorizontalAlignment = System.Windows.HorizontalAlignment.Center
				, VerticalAlignment = VerticalAlignment.Center
			};
			border.Child = _osdTextBlock;
			_osdWindow.Content = border;
			_osdTimer = new DispatcherTimer();
			_osdTimer.Interval = TimeSpan.FromMilliseconds(1200);
			_osdTimer.Tick += (s, e) => {
				 _osdWindow.Hide();
				_osdTimer.Stop();
			};
		}
		private void MainWindow_Closed(object sender, EventArgs e) {
			TriggerShutdown();
		}
	}
}
