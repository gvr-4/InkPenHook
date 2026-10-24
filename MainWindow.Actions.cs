//
// InkPenHook
// MainWindow.Actions.cs	2026/10/04 01:20:00
//	2026/10/11(日) 01:57:29  Debug.Write() をShowOSD()に．各加工出力をOSDにも追加
//	2026/10/18(日) 00:23:55 BarrelのアクションをErase処理を移植して同等機能に．

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using static System.Runtime.InteropServices.JavaScript.JSType;
using MessageBox = System.Windows.MessageBox;

namespace InkPenHook{
	public partial class MainWindow{
		IntPtr _lastForegroundHwnd = IntPtr.Zero;
		DateTime _lastProfileUpdateTime = new DateTime();
		DateTime _barrelPressStartTime = new DateTime();
		DateTime _eraserPressStartTime = new DateTime();
        DateTime _lastEraserShortTriggerTime = new DateTime();
        DateTime _lastBarrelShortTriggerTime = new DateTime();
        string _cachedProcessName = string.Empty;
		bool _isVirtualToggleHoldActive = false;
		private CancellationTokenSource _barrelLoopToken;
		private CancellationTokenSource _eraserLoopToken;
        bool _isEraserKeyDownSent = false;
        bool _isBarrelKeyDownSent = false;
        bool _isEraserLongPressed = false;
        bool _isBarrelLongPressed = false;

        private void ParsePenRawInput(IntPtr hRawInput)	{
			uint dwSize = 0;
			NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT, IntPtr.Zero, ref dwSize, (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTHEADER)));
			if (dwSize == 0) return;
			IntPtr pData = Marshal.AllocHGlobal((int)dwSize);
			try	{
				if (NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT, pData, ref dwSize, (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTHEADER))) == dwSize)	{
					int headerSize = Marshal.SizeOf(typeof(NativeMethods.RAWINPUTHEADER));
					byte[] rawBytes = new byte[(int)dwSize - headerSize];
					Marshal.Copy(pData + headerSize, rawBytes, 0, rawBytes.Length);
					if (rawBytes.Length > 9){
						byte statusByte = rawBytes[9];
						bool barrel = (statusByte & 0x02) != 0;
						bool eraser = (statusByte & 0x08) != 0;
						bool tip = (statusByte & 0x01) != 0;
						if (eraser) barrel = false;
						_isPhysicalBarrelDown = barrel;
						_isPhysicalEraserDown = eraser;
						UpdateCurrentProfile();
						if (string.IsNullOrEmpty(_currentProfile.ProcessName)) return;
						ProcessBarrelSmartActions(barrel, tip);
						ProcessEraserSmartActions(eraser, tip);
					}
				}
			}
			catch (Exception ex) {
				ShowOSD( $"RawInput Error: {ex.Message}" );
			}
			finally { Marshal.FreeHGlobal(pData); }
		}

		private void UpdateCurrentProfile()	{
			IntPtr hwnd = NativeMethods.GetForegroundWindow();
			if (hwnd == IntPtr.Zero) return;
			if (hwnd == _lastForegroundHwnd && (DateTime.Now - _lastProfileUpdateTime).TotalMilliseconds < 500) return;
			_lastForegroundHwnd = hwnd;
			_lastProfileUpdateTime = DateTime.Now;
			NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
			try	{
				using (Process proc = Process.GetProcessById((int)processId)){
					string targetName = proc.ProcessName.ToLower();
					if (targetName.Contains("explorer") || targetName.Contains("inkpenhook")) return;
					if (_cachedProcessName == targetName) return;
					_cachedProcessName = targetName;
					foreach (var app in _configRoot.Apps){
						if (targetName.Contains(app.ProcessName.ToLower())){
							if (_currentProfile.ProcessName != app.ProcessName){
								// プロファイル切り替え時に古いトグルホールド状態があれば安全にクリア
								if (_isVirtualToggleHoldActive) {
									SendHardwareKeyUpOnly(_currentProfile.EraserShortScanCode);
									_isVirtualToggleHoldActive = false;
								}
								_currentProfile = app;
							}
							ShowOSD( $"[Actions] {_currentProfile.AppName} Up Rising!" );
							return;
						}
					}
				}
			}
			catch { }
			_currentProfile = new AppProfile { AppName = "Default_None", ProcessName = "" };
		}

		private void ProcessBarrelSmartActions(bool currentBarrel, bool currentTip)	{
			ushort BarrellongPressMs = _currentProfile.BarrelLongPressMs;
			if (	currentBarrel
				&&	! _lastBarrelState) {
				_barrelPressStartTime = DateTime.Now;
				_isBarrelLongPressed = _isBarrelKeyDownSent = false;
				_lastBarrelState = true;
			}
			if (	currentBarrel
				&&	_lastBarrelState
				&&	! _isBarrelLongPressed
				&&	(DateTime.Now - _barrelPressStartTime).TotalMilliseconds >= BarrellongPressMs ){

				_isBarrelLongPressed = true;

				if (	_currentProfile.BarrelLongScanCode != 0
					&&	! _isBarrelKeyDownSent ){

					_isBarrelKeyDownSent = true;
					_barrelLoopToken = new System.Threading.CancellationTokenSource();

					var token = _barrelLoopToken.Token;
					ushort code = _currentProfile.BarrelLongScanCode;
					Task.Run(
						async () => {
							try {
								while (!token.IsCancellationRequested) {
									SendHardwareKeyDownOnly(code);
									await Task.Delay(25, token);
								}
							}
							catch { }
						}
						,token
					);
					ShowOSD( $"Barrel V-LongPressed:{code}" );

				}
			}
			if (	!currentBarrel
				&&	_lastBarrelState){

				_barrelLoopToken?.Cancel();
				_barrelLoopToken?.Dispose();
				_barrelLoopToken = null;

				ushort code = _currentProfile.BarrelShortScanCode;

				if (	! _isBarrelLongPressed
					&&	code != 0){
					if ( ( DateTime.Now - _lastBarrelShortTriggerTime).TotalMilliseconds > _currentProfile.BarrelDebounceMs ){
						_lastBarrelShortTriggerTime = DateTime.Now;
						int delay = _currentProfile.BarrelReleaseDelayMs;
						int duration = _currentProfile.BarrelPressDurationMs;
						if ( _currentProfile.BarrelShortBehavior == ButtonBehavior.ToggleHold){
							if (!_isVirtualToggleHoldActive){
								Task.Run(
									async () => {
										if (delay > 0) await Task.Delay(delay);
										SendHardwareKeyDownOnly(code);
									}
								);
								_isVirtualToggleHoldActive = true;
								ShowOSD( $"Barrel V-Toggle:keep key press hold:{code}" );

							}else{
								Task.Run(
									async () => {
										if (delay > 0) await Task.Delay(delay); SendHardwareKeyUpOnly(code);
									}
								);
								_isVirtualToggleHoldActive = false;
								ShowOSD( $"Barrel V-Toggle:release key:{code}" );
							}
						}else{
							Task.Run(
								async () => {
									if (delay > 0) await Task.Delay(delay);
									SendHardwareKeyDownOnly(code);
									await Task.Delay(duration);
									SendHardwareKeyUpOnly(code);
								}
							);
							ShowOSD( $"Barrel V-Shortpressed:{code}" );

						}
					}
				}else if (	_isBarrelKeyDownSent
						&& _currentProfile.BarrelLongScanCode != 0){
					SendHardwareKeyUpOnly(_currentProfile.BarrelLongScanCode);
				}
				_isBarrelKeyDownSent = _lastBarrelState = false;
				_isPhysicalBarrelDown = false;
			}
		}


		private void ProcessEraserSmartActions(bool currentEraser, bool currentTip)	{
			ushort EraserlongPressMs = _currentProfile.EraserLongPressMs;
			if (	currentEraser
				&&	! _lastEraserState) {
				_eraserPressStartTime = DateTime.Now;
				_isEraserLongPressed = _isEraserKeyDownSent = false;
				_lastEraserState = true;
			}
			if (	currentEraser
				&&	_lastEraserState
				&&	! _isEraserLongPressed
				&&	(DateTime.Now - _eraserPressStartTime).TotalMilliseconds >= EraserlongPressMs ){

				_isEraserLongPressed = true;

				if (	_currentProfile.EraserLongScanCode != 0
					&&	! _isEraserKeyDownSent ){

					_isEraserKeyDownSent = true;
					_eraserLoopToken = new System.Threading.CancellationTokenSource();

					var token = _eraserLoopToken.Token;
					ushort code = _currentProfile.EraserLongScanCode;
					Task.Run(
						async () => {
							try {
								while (!token.IsCancellationRequested) {
									SendHardwareKeyDownOnly(code);
									await Task.Delay(25, token);
								}
							}
							catch { }
						}
						,token
					);
					ShowOSD( $"Erase V-LongPressed:{code}" );

				}
			}
			if (	!currentEraser
				&&	_lastEraserState){
				_eraserLoopToken?.Cancel();
				_eraserLoopToken?.Dispose();
				_eraserLoopToken = null;

				ushort code = _currentProfile.EraserShortScanCode;
				if (	! _isEraserLongPressed
					&&	code != 0){
					if ( ( DateTime.Now - _lastEraserShortTriggerTime).TotalMilliseconds > _currentProfile.EraserDebounceMs ){
						_lastEraserShortTriggerTime = DateTime.Now;
						int delay = _currentProfile.EraserReleaseDelayMs;
						int duration = _currentProfile.EraserPressDurationMs;
						if ( _currentProfile.EraserShortBehavior == ButtonBehavior.ToggleHold){
							// クリップスタジオ用：交互にホールドと解除を切り替える
							if (!_isVirtualToggleHoldActive){
								Task.Run(
									async () => {
										if (delay > 0) await Task.Delay(delay);
										SendHardwareKeyDownOnly(code);
									}
								);
								_isVirtualToggleHoldActive = true;
								ShowOSD( $"Eraser V-Toggle:keep key press hold:{code}" );

							}else{
								Task.Run(
									async () => {
										if (delay > 0) await Task.Delay(delay); SendHardwareKeyUpOnly(code);
									}
								);
								_isVirtualToggleHoldActive = false;
								ShowOSD( $"Eraser V-Toggle:release key:{code}" );
							}
						}else{
							// Krita用：正確なホールド時間を持ったワンショットストローク
							Task.Run(
								async () => {
									if (delay > 0) await Task.Delay(delay);
									SendHardwareKeyDownOnly(code);
									await Task.Delay(duration);
									SendHardwareKeyUpOnly(code);
								}
							);
							ShowOSD( $"Eraser V-Shortpressed:{code}" );

						}
					}
				}else if (	_isEraserKeyDownSent
						&& _currentProfile.EraserLongScanCode != 0){
					SendHardwareKeyUpOnly(_currentProfile.EraserLongScanCode);
				}
				_isEraserKeyDownSent = _lastEraserState = false;
				_isPhysicalEraserDown = false;
			}
		}

		private void SendHardwareKeyStrokeQuick(ushort scanCode, int durationMs){
			Task.Run(async () => { SendHardwareKeyDownOnly(scanCode); await Task.Delay(durationMs); SendHardwareKeyUpOnly(scanCode); });
		}
		private void SendHardwareKeyDownOnly(ushort scanCode){
			var inputs = new NativeMethods.INPUT_FLAT[] { NativeMethods.CreateInput(scanCode, false) };
			NativeMethods.SendInput(1, inputs, Marshal.SizeOf(typeof(NativeMethods.INPUT_FLAT)));
		}
		private void SendHardwareKeyUpOnly(ushort scanCode){
			var inputs = new NativeMethods.INPUT_FLAT[] { NativeMethods.CreateInput(scanCode, true) };
			NativeMethods.SendInput(1, inputs, Marshal.SizeOf(typeof(NativeMethods.INPUT_FLAT)));
		}
	}
}
