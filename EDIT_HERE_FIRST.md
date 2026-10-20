# 楽フェクター — ソース編集用ガイド（WPF / C#）

このZIPは **ソースコードがすべて入った Visual Studio 2022 用の編集可能な WPF ソリューション**です。実行ファイルだけを配布したものではありません。

## 最初の開き方

1. ZIPを **すべて展開** する（ZIP内から直接開かない）。
2. 展開後のフォルダ `RakuFector` にある `REPAIR_AND_BUILD.cmd` を実行する。これは前回のNuGetエラー対策用。`SUCCESS` が出れば復元・ビルドが完了。
3. 同じ場所の **`楽フェクター.sln`** を Visual Studio 2022 で開く（`exe`や個別の `.cs` ではなく **ソリューション**を開く）。
4. **表示 → ソリューション エクスプローラー**（`Ctrl + Alt + L`）を開く。
5. `楽フェクター` プロジェクト → `Views` フォルダを展開すると `.xaml` ファイルが表示される。
6. `.xaml` 左の **▶ / ▸** をクリックすると対応する `.xaml.cs` も表示される。画面デザインは `.xaml`、処理は `.xaml.cs` を開いて編集する。

### ソリューション エクスプローラーで見る場所

```
楽フェクター
├─ 00_READ_ME_FIRST
│  └─ EDIT_HERE_FIRST.md         ← この案内
├─ 楽フェクター                   ← メインのWPFアプリ（起動プロジェクト）
│  ├─ App.xaml / App.xaml.cs     ← アプリ開始処理
│  ├─ MainWindow.xaml
│  │  └─ MainWindow.xaml.cs      ← 画面切り替え・メニュー
│  ├─ Views
│  │  ├─ TonePage.xaml
│  │  │  └─ TonePage.xaml.cs     ← 音作り画面
│  │  ├─ TunerPage.xaml
│  │  │  └─ TunerPage.xaml.cs    ← チューナー画面
│  │  ├─ ChordPage.xaml
│  │  │  └─ ChordPage.xaml.cs    ← コード検出画面
│  │  └─ FootswitchPage.xaml
│  │     └─ FootswitchPage.xaml.cs  ← フットスイッチ（割当・保存・操作）
│  ├─ AudioEffects               ← 歪み等の音声エフェクト処理 .cs
│  ├─ ChordDetection             ← コード検出の画面ロジック .cs
│  ├─ Models                     ← 画面で使うモデル .cs
│  └─ Integration                ← 各機能の接続 .cs
├─ DrumEditor                    ← ドラム編集のWPFライブラリ
│  ├─ Views
│  │  └─ DrumEditorView.xaml
│  │     ├─ DrumEditorView.xaml.cs
│  │     ├─ DrumEditorView.Editing.cs
│  │     ├─ DrumEditorView.Polish.cs
│  │     └─ DrumEditorView.Trackpad.cs
│  ├─ Audio                      ← ドラム音声処理 .cs
│  ├─ Files                      ← 保存、MIDI 等 .cs
│  └─ Domain                     ← パターンや編集ロジック .cs
├─ CommonAudio                   ← 共通ASIO入出力 .cs
├─ ChordDetection.Core           ← 音名・コード判定アルゴリズム .cs
└─ ChordDetection.Infrastructure ← Basic Pitch、録音、音源分離 .cs
```

※この図は主な場所だけを示したもので、実際にはその他の `.cs` やリソースもすべて含まれます。

## どのファイルを編集すればよい？

| やりたいこと | 編集するファイル | 
| --- | --- |
| 画面の配置、文字、ボタンを変更 | `src/楽フェクター/Views/○○Page.xaml` |
| 画面内のボタン操作を変更 | 同じ場所の `○○Page.xaml.cs` |
| 左メニューや画面遷移の変更 | `src/楽フェクター/MainWindow.xaml(.cs)` |
| エフェクトの歪み・音色を調整 | `src/楽フェクター/AudioEffects/*.cs` |
| チューナーの検出処理 | `src/楽フェクター/Views/TunerAnalyzer.cs` |
| コード検出の画面 | `src/楽フェクター/Views/ChordPage.xaml(.cs)` |
| コード検出の解析ロジック | `src/ChordDetection.Core/`、`src/ChordDetection.Infrastructure/` |
| ドラムの画面 | `src/DrumEditor/Views/DrumEditorView.xaml(.cs)` |
| ドラム再生・MIDI出力 | `src/DrumEditor/Audio/`、`src/DrumEditor/Files/` |
| 共通のギター入力・出力 | `src/CommonAudio/*.cs` |

## Visual Studioで見えない場合

- **ソリューションエクスプローラーがない** → `Ctrl + Alt + L`。
- **ファイルが見えない** → プロジェクト（例：`楽フェクター`）の左の三角をクリックし、`Views` を展開する。
- **`.xaml.cs` が見えない** → `.xaml` 左の三角をクリックする（デザイナーに紐づくコードは子として表示）。
- **フォルダ表示になっている** → 「フォルダーを開く」ではなく `楽フェクター.sln` を開く。
- **C#のコードを直接開きたい** → ソリューションエクスプローラーでファイルをダブルクリック。`Ctrl + T` の検索でもファイル名を指定可能。

## ビルドについて

Windowsの Visual Studio 2022（.NET デスクトップ開発、.NET 8 SDK）が必要。構成は `Debug / x64`、スタートアッププロジェクトは `楽フェクター`。
この版でも前回の `REPAIR_AND_BUILD.cmd` を利用でき、NuGet復元処理を残しています。
この環境ではWindows上で実際のWPFビルドと実行検証はしていません。ビルドエラーがあれば `restore_and_build_log.txt` を送ってください。

今回の版では **WinForms版のフットスイッチ機能をWPFに追加**し、既存の歪み・AMP・チューナー画面と連携しています。


## フットスイッチ（5機能目）

統合したフットスイッチは `Views/FootswitchPage.xaml`（画面）・`Views/FootswitchPage.xaml.cs`（処理）・`Models/FootSwitchSetting.cs`（データ）、`MainWindow.xaml.cs`（音作りへの連携）で編集できます。詳細は `FOOTSWITCH_INTEGRATION.md` を参照してください。
