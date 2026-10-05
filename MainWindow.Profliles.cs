//
// InkPenHook
// MainWindow.Profiles.cs	2026/10/04 01:20:00
//
using System;
using System.Collections.Generic;

namespace InkPenHook
{
	// ボタンの挙動モード
	public enum ButtonBehavior
	{
		Stroke, 	   // 単発ストローク (Down -> Up)
		ToggleHold,    // 1回目タップでDown維持、2回目タップでUp (CliStの一時切り替え用)
		Repeat		   // 長押し時に定期リピート (既存ロジック用)
	}

	public class AppProfile
	{
		public string AppName { get; set; } = "Default";
		public string ProcessName { get; set; } = "krita";

		// バレルボタン設定
		public ushort BarrelShortScanCode { get; set; } = 0x12;
		public ushort BarrelLongScanCode { get; set; } = 0x00;

		// 消しゴムボタン設定
		public ushort EraserShortScanCode { get; set; } = 0x12;
		public ButtonBehavior EraserShortBehavior { get; set; } = ButtonBehavior.Stroke;
		public ushort EraserPressDurationMs { get; set; } = 30; // キーを押し下げる長さ
		public ushort EraserLongScanCode { get; set; } = 0x00;

		// 安定化パラメータ
		public ushort EraserDebounceMs { get; set; } = 180;
		public ushort EraserReleaseDelayMs { get; set; } = 2;
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
}
