// InkPenHook
// MainWindow.Monitor.cs	2026/10/05 23:45:00
// 入出力リアルタイムモニター、制限付きログ保存・重複カットコアロジック
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace InkPenHook
{
    public partial class MainWindow
    {
        private static readonly ConcurrentQueue<string> _monitorLogQueue = new ConcurrentQueue<string>();
        private static readonly int MaxLogLines = 200;

        // ログ制御ステート
        public static bool IsLoggingActive { get; private set; } = false;
        private static string _currentLogFilePath = string.Empty;
        private static readonly long MaxLogSizeBytes = 1024 * 1024; // 最大1MB

        // 重複カット用キャッシュ
        private static string _lastCategory = string.Empty;
        private static string _lastMessage = string.Empty;
        private static int _repeatCount = 0;

        private IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                int scanCode = Marshal.ReadInt32(lParam, 4);
                int flags = Marshal.ReadInt32(lParam, 8);

                bool isKeyUp = (flags & 0x80) != 0;
                bool isInjected = (flags & 0x10) != 0;

                string direction = isKeyUp ? "UP" : "DOWN";
                string source = isInjected ? "AppOutput" : "PhysicalKey";

                AppendDeviceLog(source, $"VK: 0x{vkCode:X2}, SC: 0x{scanCode:X2} ({direction})");
            }
            return NativeMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
        }

        public static void LogPenRawInput(bool barrel, bool eraser, uint pressure = 0)
        {
            AppendDeviceLog("RawPen", $"Barrel: {(barrel ? "ON " : "OFF")}, Eraser: {(eraser ? "ON " : "OFF")}, Press: {pressure}");
        }

        public static void StartFileLogging()
        {
            if (IsLoggingActive) return;
            try
            {
                string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloadsPath)) Directory.CreateDirectory(downloadsPath);

                string timeStamp = DateTime.Now.ToString("yyMMdd-HHmmss");
                _currentLogFilePath = Path.Combine(downloadsPath, $"InkPenHook_mon_{timeStamp}.log");

                IsLoggingActive = true;
                AppendDeviceLog("System", $"File Logging Started. Path: {_currentLogFilePath}");
            }
            catch (Exception ex)
            {
                AppendDeviceLog("System", $"Failed to start logging: {ex.Message}");
            }
        }

        public static void StopFileLogging(string reason = "User Stopped")
        {
            if (!IsLoggingActive) return;
            IsLoggingActive = false;
            AppendDeviceLog("System", $"File Logging Stopped. Reason: {reason}");
            _currentLogFilePath = string.Empty;
            _lastCategory = string.Empty; // キャッシュクリア
            _lastMessage = string.Empty;
        }

        // 統合ログ記録・重複制御機構
        public static void AppendDeviceLog(string category, string message)
        {
            // 同一内容の連続発生を検知
            if (category == _lastCategory && message == _lastMessage)
            {
                _repeatCount++;
                // 画面バッファやファイルが溢れないよう、内部カウントのみ進めてスルー
                return;
            }

            // 別のログが来た場合、それまでの重複分があればフラッシュ表示
            FlushRepeatCount();

            // 新規ログの設定
            _lastCategory = category;
            _lastMessage = message;
            _repeatCount = 0;

            WriteLogLine(category, message);
        }

        private static void FlushRepeatCount()
        {
            if (_repeatCount > 0)
            {
                WriteLogLine(_lastCategory, $".. snip .. (repeated {_repeatCount} times)");
                _repeatCount = 0;
            }
        }

        private static void WriteLogLine(string category, string message)
        {
            string timeStamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = $"[{timeStamp}] [{category}] {message}";

            _monitorLogQueue.Enqueue(line);

            while (_monitorLogQueue.Count > MaxLogLines)
            {
                _monitorLogQueue.TryDequeue(out _);
            }

            if (IsLoggingActive && !string.IsNullOrEmpty(_currentLogFilePath))
            {
                try
                {
                    var fileInfo = new FileInfo(_currentLogFilePath);
                    if (fileInfo.Exists && fileInfo.Length >= MaxLogSizeBytes)
                    {
                        StopFileLogging("Auto-Stopped (Size limit 1MB reached)");
                        return;
                    }
                    File.AppendAllText(_currentLogFilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
        }

        public string GetMonitorText()
        {
            // 現在蓄積中の重複分もリアルタイムに反映させるためのフラッシュ
            // （タイマー同期によりUI側でガタつかないよう、ゲッター側でスナップショットに反映）
            if (_repeatCount > 0)
            {
                string timeStamp = DateTime.Now.ToString("HH:mm:ss.fff");
                return string.Join(Environment.NewLine, _monitorLogQueue.ToArray())
                    + Environment.NewLine + $"[{timeStamp}] [{_lastCategory}] .. snip .. (repeated {_repeatCount} times)";
            }
            return string.Join(Environment.NewLine, _monitorLogQueue.ToArray());
        }

        public void ClearMonitorLogBuffer()
        {
            _monitorLogQueue.Clear();
            _lastCategory = string.Empty;
            _lastMessage = string.Empty;
            _repeatCount = 0;
            AppendDeviceLog("System", "In-memory log buffer cleared.");
        }
    }
}
