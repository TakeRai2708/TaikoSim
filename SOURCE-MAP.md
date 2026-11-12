# どの画面のソースがどこにあるか

Visual Studioでは **TakeTaikoSimulator → Scenes → 画面名** の順に開きます。
画面の本体と、その画面に関係する描画・演出のファイルを同じフォルダに置いています。

| 画面・処理 | フォルダ | 主なファイル・役割 |
|---|---|---|
| エントリー | [Scenes/Entry](TakeTaikoSimulator/Scenes/Entry) | `EntryScene.cs`：人数確定、データ選択、初回の名前入力、データ読み込み演出 |
| モード選択 | [Scenes/ModeSelect](TakeTaikoSimulator/Scenes/ModeSelect) | `ModeSelectScene.cs`：モード一覧、カーソル、モード決定・登場演出 |
| 曲・難易度選択 | [Scenes/SongSelect](TakeTaikoSimulator/Scenes/SongSelect) | `SongSelectScene.cs`：ジャンル・曲一覧、検索、難易度選択。`LocalDifficultyScene.cs`：2Pの難易度選択。`SongLoading.cs`：曲開始前の幕 |
| 演奏 | [Scenes/Play](TakeTaikoSimulator/Scenes/Play) | `PlayScene.cs`：ノーツ・判定・入力・同期。`PlayScene.Local.cs` / `LocalPlayScene.cs`：2P。その他のファイルは下表 |
| 特訓 | [Scenes/Training](TakeTaikoSimulator/Scenes/Training) | `TrainingScene.cs`：一時停止・再開など。`TrainingTimeline.cs`：小節・時刻。`TrainingScrollMotion.cs`：小節移動のイージング |
| 通常リザルト | [Scenes/Result](TakeTaikoSimulator/Scenes/Result) | `ResultScene.cs`：結果表示・自己ベスト更新の帯 |
| 段位道場 | [Scenes/Dan](TakeTaikoSimulator/Scenes/Dan) | `DanSelect*`：段位選択。`DanPanel*`：演奏中の条件。`DanShutter.cs`：曲間。`DanLoading.cs`：開始演出。`DanResult*`：結果。`AStyle`はスキン描画部分 |
| 着せ替え | [Scenes/Dressing](TakeTaikoSimulator/Scenes/Dressing) | `DressingScene.cs`：名前・獲得済み段位・どんちゃん・ぷちキャラ・プレートの選択と適用 |
| 起動・初回セットアップ | [Scenes/Startup](TakeTaikoSimulator/Scenes/Startup) | `LoadScene.cs`：起動時の読み込み。`SetupScene.cs`：初回設定 |
| クレジット | [Scenes/Credits](TakeTaikoSimulator/Scenes/Credits) | `CreditsScene.cs` |

## 演奏フォルダの中

| ファイル | 直したい内容 |
|---|---|
| `PlayScene.cs` | 演奏全体、ノーツ、判定、入力、曲の同期 |
| `PlayHUD.cs` | 演奏画面のスコア・ゲージ・曲名など |
| `PlayerEffects.cs` | ミニ太鼓、判定の花火、連打・コンボなどの演出 |
| `PlayEnd.cs` | 曲終了時のクリア・フルコンボ・ドンダフルコンボなどの演出 |
| `PlayChara.cs` | 演奏中のどんちゃんの動作 |
| `PlayBranch.cs` | 譜面分岐 |
| `PlayStats.cs` / `PlayScoreboard.cs` / `PlayScoreRank.cs` | ノーツ数・スコア・ランクなどの集計・計算 |
| `PlayScene.Local.cs` / `LocalPlayScene.cs` | 2P演奏に関する部分 |

特訓や段位も演奏用の処理を再利用しています。例えば、特訓のノーツ表示を追うときは`Training/`に加えて`Play/`を見る場合があります。

## 複数画面から使うもの

| 探したいもの | 場所 |
|---|---|
| 座標・名前の大きさ・縁の微調整 | [UI/LayoutAdjustments.cs](TakeTaikoSimulator/UI/LayoutAdjustments.cs)。手順は[座標調整ガイド](COORDINATE-GUIDE.md) |
| ネームプレートの共通描画 | [Graphics/NamePlate.cs](TakeTaikoSimulator/Graphics/NamePlate.cs) |
| スコアランク・王冠の記録パネル、自己ベスト表示 | [UI/SongRecordPanel.cs](TakeTaikoSimulator/UI/SongRecordPanel.cs) |
| モード・選曲で共通の斜めの一覧 | [UI/BarList.cs](TakeTaikoSimulator/UI/BarList.cs) |
| 演奏オプションのパネル | [UI/PlayOptionsPanel.cs](TakeTaikoSimulator/UI/PlayOptionsPanel.cs) |
| 名前入力 | [UI/PlayerNameInput.cs](TakeTaikoSimulator/UI/PlayerNameInput.cs)。エントリーと着せ替えから利用 |
| F1・全画面の設定 | [Overlays/SettingsOverlay.cs](TakeTaikoSimulator/Overlays/SettingsOverlay.cs) |
| 設定の保存、プレイヤーデータ | `Configs/` |
| 曲・譜面・記録の読み書き | `Charts/` |
| 音声・BGM | `Audio/` |
| スキン・フォント・共通の画像処理 | `Graphics/` |

`Scenes/`直下の`Scene.cs`は画面の共通ルール、`SceneManager.cs`は画面遷移、`LocalSession.cs`は参加者・プレイヤーデータ・2Pオプションの管理です。

今回の整理では型名と名前空間`TakeTaikoSimulator.Scenes`を維持しています。ファイルを移動しただけで、呼び出し側の`using`を変える必要はありません。新しい画面専用の補助ファイルも、その画面のフォルダに置いてください。
