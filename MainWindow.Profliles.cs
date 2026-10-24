//
// InkPenHook
// MainWindow.Profiles.cs	2026/10/04 01:20:00
// 2026/10/14(水) 16:01:29 xaml構想を変更．S/L判定しきい値をボタン毎に．Eraserにだけあるストローク選択肢をBarrelにも．発行キースキャンの処置機を空に．

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
		public string AppName { get; set; } = "Default";	// 略称
		public string ProcessName { get; set; } = "nobody";	// タスクマネージャに現れれる識別名

		// バレルボタン設定
		public ushort BarrelShortScanCode { get; set; } = 0x00;	// 初期値は設けない
		public ushort BarrelLongScanCode { get; set; } = 0x00;

		// ボタン判定とキー生成パラメータ
		public ButtonBehavior BarrelShortBehavior { get; set; }	= ButtonBehavior.Stroke;
		public ushort BarrelLongPressMs { get; set; } 			= 350;	// 長押ししきい値
		public ushort BarrelPressDurationMs { get; set; } 		= 30;	// キーMAKE判定するまでの待機時間．チャタリング避け
		public ushort BarrelDebounceMs { get; set; } 			= 180;	// キーMAKEさせておく時間．アプリがボタンが押されたと認識出来る時間．
		public ushort BarrelReleaseDelayMs { get; set; } 		= 2;	// キーBREAK

		// 消しゴムボタン設定
		public ushort EraserShortScanCode { get; set; } = 0x00;	// 初期値は設けない
		public ushort EraserLongScanCode { get; set; } = 0x00;

		// ボタン判定とキー生成パラメータ
		public ButtonBehavior EraserShortBehavior { get; set; }	= ButtonBehavior.Stroke;
		public ushort EraserLongPressMs { get; set; } 			= 350;	// 長押ししきい値
		public ushort EraserPressDurationMs { get; set; } 		= 30; 	// キーMAKE判定するまでの待機時間．チャタリング避け
		public ushort EraserDebounceMs { get; set; } 			= 180;	// キーMAKEさせておく時間．アプリがボタンが押されたと認識出来る時間．
		public ushort EraserReleaseDelayMs { get; set; } 		= 2;	// キーBREAK
	}

	public class RootConfig
	{
		public List<AppProfile> Apps { get; set; } = new List<AppProfile>();
	}
}
