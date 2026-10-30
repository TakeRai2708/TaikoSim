# TaikøNauts 公式ドキュメントの要点（TaikoSim 実装用のメモ）

出典：<https://taikonauts-docs.pages.dev/> の `/docs/` 配下 **全50ページ**（2026-10-30 に全部読んだ）。
ここは「本家がどう作られているか」の**写し**。TaikoSim で実際に測った値は `AGENTS.md` の方に書く。

- 本家は **TJAPlayer3-Develop-ReWrite の精神的後継**。現在βで **Windows のみ**
- `.tja` と **`.osu`**（osu! のビートマップ）の両方を読める
- アカウントシステムは β2026.0408.0 で**廃止済み**

---

## 1. 段位道場（最優先：いま作るところ）

### フォルダ構成

```
📁 [Songs]                     ← GameConfig.json の songPath、または exe と同じ階層
  └ 📁 [DanFolder]
     └ 📁 [DanCategory]
        ├ 📄 dan.def            ← ここから下が段位、という目印
        └ 📁 [DanFiles]         ← 段位1つ＝1フォルダ
           ├ 📄 dan.json         ← 課題曲と合格条件
           ├ 📄 requirements.json ← 解禁条件（任意）
           ├ 📁 ChangeEffect/    ← 曲間演出（任意）
           └ 📄 (.tja / .osu と音源)
```

**OpenTaiko との最大の違い**：OpenTaiko は1つの `.tja` に `#NEXTSONG` で全曲を詰めるが、
TaikøNauts は**課題曲を別々のファイルに置き、`dan.json` でまとめる**。
公式いわく、そうした理由は「複数セットの管理のしやすさ」「条件を別ファイルにして簡約化」「`.tja` 本体を弄らなくて済む」。

### dan.def

```
#TITLE:段位カテゴリ名
#DISABLEDCHEAT:true
```

- `#TITLE` がカテゴリ名。`dan.def` が複数あればゲーム内でカテゴリを選べる
- `#DISABLEDCHEAT:true` で、そのカテゴリの段位は練習向けチート機能が無効になる（省略時は無効化しない）

### dan.json

| キー | 型 | 説明 |
|---|---|---|
| `title` | String | 段位のタイトル名 |
| `danIndex` | Integer | 段位のインデックス番号（並び順） |
| `danPlatePath` | String | 段位プレート画像 |
| `danPanelSidePath` | String | 段位パネルのサイド画像 |
| `danTitlePlatePath` | String | 段位タイトルプレート画像 |
| `danMiniPlatePath` | String | 段位ミニプレート画像 |
| `danMiniPlateText` | String | ミニプレートに出すテキスト |
| `shodanTextPaths` | String[] | 昇段演出の共通テキスト画像（相対パス） |
| `shodanRedTextPaths` | String[] | 赤合格時。未指定なら `shodanTextPaths` |
| `shodanGoldTextPaths` | String[] | 金合格時。未指定なら `shodanTextPaths` |
| `danSongs` | Object[] | 課題曲 |
| `conditionGauge` | Object | ゲージ条件 |
| `conditions` | Object[] | 合格条件 |

`danSongs` の要素：

| キー | 型 | 説明 |
|---|---|---|
| `path` | String | `.tja` / `.osu` の**相対パス**（絶対パス禁止） |
| `difficulty` | Integer | 0=Easy / 1=Normal / 2=Hard / 3=Oni / 4=Edit(裏) |
| `genre` | String | ジャンル名 |
| `isHidden` | Boolean | 段位選択画面で曲名などを伏せるか |

`conditionGauge`：`red` / `gold`（パーセント）。
※ドキュメントの表の見出しは「**合格ゲージ条件**」なのに、本文の説明は「魂ゲージの**初期値**」になっていて**食い違っている**。
たけらいの実ファイルは 初段 `red:97, gold:0`、十段 `red:100, gold:100`。
初段の `gold:0` を「0%未満」と読むと達成不可能なので、**`gold:0` は「金条件なし」の意味だと思われる**（未確認）。

`conditions` の要素：

| キー | 型 | 説明 |
|---|---|---|
| `type` | String | 条件の種類（下表） |
| `threshold` | Object / Object[] | **単一なら全体条件、曲数ぶんの配列なら曲ごとの個別条件** |

`threshold` の中身は `red` / `gold`。条件タイプによって、その値「**以上**」か「**未満**」かが決まる：

| type | 意味 | 範囲 |
|---|---|---|
| `Great` | 良の数 | **以上** |
| `Good` | 可の数 | **未満** |
| `Miss` | 不可の数 | **未満** |
| `Roll` | 連打数 | **以上** |
| `Hit` | たたけた数 | **以上** |
| `Score` | スコア | **以上** |
| `MaxCombo` | （最大）コンボ数 | **以上** |

→ **以上／未満は JSON に書かれていない。type から決まる。** ここを間違えると合否が逆になる。

たけらいの実ファイルでの使われ方：
- `6 初段`：どの条件も `threshold` が1個 → 3曲合計の全体条件
- `15 十段`：`Good` と `Roll` が3個（＝曲数）→ 曲ごと条件。`Miss` は1個 → 全体条件

### requirements.json（段位の解禁条件・任意）

`dan.json` と同じフォルダ。`path` は**このファイルからの相対パス**。

| type | 必須項目 | 説明 |
|---|---|---|
| `all` | `conditions` | 全部満たしたら解禁（空配列は不可） |
| `any` | `conditions` | 1つ以上で解禁（空配列は不可） |
| `not` | `condition` | 子条件を満たしていなければ解禁 |
| `danClear` | `path`, `clearType` | 指定した段位の合格状況 |
| `songClear` | `path`, `course`, `clearType` | 指定した曲・難易度のクリア状況 |
| `songScore` | `path`, `course`, `score` | スコアが指定値以上（score は0以上） |
| `songRank` | `path`, `course`, `rank` | スコアランクが指定値以上（rank は1〜7） |

- `course`：`Easy` / `Normal` / `Hard` / `Oni` / `Ura`
- `songClear` の `clearType`：`Cleared` / `FullCombo` / `AllPerfect`
- `danClear` の `clearType`：`RedClear` / `GoldClear` / `RedFullCombo` / `GoldFullCombo` / `RedAllPerfect` / `GoldAllPerfect`
- **より高いクリア種別の記録は、より低い条件も満たす**
- ファイルが無い・条件を解決できない場合は**ロックせず**、ゲーム内にエラー通知を出す
- 判定はモード選択時と段位選択画面への移動時。変更したらリストの再読み込みが要る

### ChangeEffect（曲間演出・任意）

`[DanFiles]/ChangeEffect/[EffectName]/Config.json` ＋ `Anime.aup2`。

| 項目 | 型 | 説明 |
|---|---|---|
| `transitionIndex` | Integer | 0 なら1曲目と2曲目の間、1 なら2曲目と3曲目の間 |
| `playDefaultScreen` | Boolean | true＝標準画面、false＝カスタム演出のタイムライン |
| `effectStartMs` | Integer | `Anime.aup2` の開始時刻。標準画面でも aup2 があれば使われる |
| `conditionPanelChangeMs` | Integer | 条件パネルの切り替えアニメ開始 |
| `titleFadeStartMs` | Integer | 次の曲のタイトル表示と画面遷移の開始 |
| `titleFadeInDurationMs` | Integer | タイトル・サブタイトル・Maker のフェードイン時間 |
| `titleDisplayDurationMs` | Integer | それを表示し続ける時間 |
| `previousTitleHideMs` | Integer | 1つ前の曲のタイトルを消す時刻 |
| `nextNoteHitMs` | Integer | 次の曲の1打目を表示する時刻 |

`playDefaultScreen: true` でも使われるのは `effectStartMs` / `conditionPanelChangeMs` / `titleFadeStartMs` の3つだけ。
残り4つはカスタム演出（`false`）専用。

### OpenTaiko 側（参考・TaikoSim では「読めたら読む」おまけ扱い）

OpenTaiko のソースから確定した、TJA ヘッダー方式：
- 入口は `box.def` の `#GENRE:段位道場`
- 1つの `.tja` に `#NEXTSONG タイトル,サブ,ジャンル,音源,ScoreInit,ScoreDiff,Level,Difficulty,タイトル表示` で区切る
  （`Level` 既定10、`Difficulty` 既定3＝おに、`タイトル表示` 既定false）
- **`#NEXTSONG` は1曲目の前にも必要**（`List_DanSongs.Add` が `#NEXTSONG` の中にしか無い）
- 最大 **9曲**（`cExamMaxSongs`）、曲間 **6200ms**（`msDanNextSongDelay`）
- 条件は `EXAMn:種別,赤,金,範囲` を最大 **7個**（`cMaxExam`）
  - 種別：`g`ゲージ `jp`良 `jg`可 `jb`不可 `s`スコア `r`連打 `h`叩いた数 `c`コンボ `a`精度 `ja`アドリブ `jm`地雷
  - 範囲：`m`以上 / `l`未満
  - `#NEXTSONG` より前＝全体条件、後＝その曲の条件
- **`EXAMGAUGE:` は TJAPlayer3 1.5.x / TJAPlayer4 にしか無く、OpenTaiko は無視する**
  （`StartsWith("EXAM")` → `Substring(4)` が `"GAUGE"` → `int.Parse` で例外 → 握りつぶし）

---

## 2. Chara（どんちゃん・かつちゃん）

置き場所：`[SkinName]/Image/99.Common/Chara/[AssetId]/`
使うキャラは `PlayerData/[Number]/PlayerConfig.json` の **`donchanType`**（0始まりの整数インデックス）で選ぶ。

### モーションフォルダ（連番 `0.png`, `1.png`, …）

演奏：`normal` `10combo` `10combomax`（ゲージ満タン時の10コンボ） `gogostart` `gogo`
`clearin` `clear` `soulin` `combocut` `miss_loop` `failed_loop`
風船：`balloon_breaking`（連打中） `balloon_broke`（成功） `balloon_miss`（失敗）
エントリー：`entry_loop` ／ 選曲：`songselect_loop` `songselect_wait_loop`
リザルト：`result_loop` `result_clear_loop` `result_failed_in` `result_failed_loop`

> A-Style には `dan_select_in` と `failed_down` も入っているが、**ドキュメントには載っていない**。

### モーションビート（コマ送りの仕組み）

公式の言葉：「**1小節を4拍としたとき、各モーションにかかる時間（単位：拍）**」
「ビート数を小さくすると小節当たりのモーションが速くなり、大きくすると遅くなる」
「**楽曲のBPMに合わせてキャラクターが動く**」

→ つまり **アニメ1周ぶん（全コマ）が `motionBeat` 拍で再生される**。BPMが遅い曲ほどヌルヌル、速い曲ほどコマが飛ぶ。

```
コマ番号 = (経過拍数 / motionBeat) の小数部 × コマ数
```

**型は `Double`**（整数ではない。2.5拍などが書ける）。
対象：`normalMotionBeat` `combo10MotionBeat` `combo10MaxMotionBeat` `goGoStartMotionBeat`
**`goGoMotionMotionBeat`**（← `Motion` が2回。本家のキー名がこれ） `clearInMotionBeat` `clearMotionBeat`
`soulInMotionBeat` `comboCutMotionBeat` `missLoopMotionBeat` `failedLoopMotionBeat`
`balloonBrokeMotionBeat` `balloonMissMotionBeat`
ほかに `goGoStartMotionFromBeginning`（Boolean）。

### Config.json のその他

- 座標・倍率の**配列は [1P, 2P]**
- ミリ秒指定（拍ではない）：`entryLoopPlaySideTime` `entryModeLoopTime` `entryModeDecideLoopTime`
  `songSelectLoopTime` `songSelectDecideTime` `songSelectSkipMotionTime` `songSelectWaitLoopTime`
  `songSelectSongStartTime` `resultLoopTime` `resultClearLoopTime` `resultFailedInTime` `resultFailedLoopTime`
  `danSelectLoopTime` `danSelectWaitLoopTime` `danSelectDecideTime` `danSelectSongStartTime`
  `danResultNormalLoopTime` `danResultFailedInTime` `danResultFailedLoopTime`
  `danResultRedInTime` `danResultRedLoopTime` `danResultGoldInTime` `danResultGoldLoopTime`
- **`songSelectSongStartJumpIndex` / `danSelectSongStartJumpIndex` は「モーションの停止コマ」**（時間ではなくコマ番号）
- 段位まわりの座標は配列ではなく単一：`danSelectX/Y/Scale`、`danResultX/Y/Scale`、`danResultShoudanX/Y`（昇段演出）
- ほかに `playerCustomizeX/Y`（きせかえ画面）

---

## 3. 楽曲システム

### category.def / order.def（TaikoSim 未対応）

- **`category.def`**：置いたフォルダ以下を独立カテゴリにする。`#TITLE` が無ければフォルダ名
- **`order.def`**：`box.def` または `category.def` と同じ階層。書いてある順に並ぶ
  - 曲：`[SongInfo]` ＋ `PATH`（相対パス）。`PATH` 省略時は `TITLE` / `SUBTITLE` で照合
  - フォルダ：`[CategoryInfo]` / `[BoxInfo]` / `[FolderInfo]` ＋ `TITLE` または `GENRE`
  - 未掲載の実在曲をどこに置くかはシステムメニューの「order.def未掲載曲の扱い」（末尾／先頭／未分類）

### SONGGENREID（TaikoSim 未対応）

`.tja` に `SONGGENREID:hogehoge` と書くと、背景・Dancer・Mob・Footer を専用のものに固定できる。
`Config/SongGenreID/{ID}.json` を置けば、タイトル／サブタイトルの完全一致・部分一致で**一括指定**も可能：

```json
{ "title": { "exact": ["..."], "partial": ["..."] },
  "subTitle": { "exact": ["..."], "partial": ["..."] } }
```

- 大文字小文字は区別しない。前後の空白は除去して判定
- 複数ヒットしたら**ランダムに選ぶ**（一括指定側も演出側も）
- 一致しなければ `"default"` 指定の演出からランダム

### 背景映像（TaikoSim 未対応）

`.tja` ヘッダーに `BGIMAGE:image.png`、または `BGMOVIE:movie.mp4` ＋ `MOVIEOFFSET:0.0`（秒、正で遅延）。
表示モードは3つ：**背景ぼかし無し** / **背景ぼかしのみ** / **背景ぼかし＋ミニウィンドウ**
（最後のは**1人プレイ時のみ**。2人プレイ時は「ぼかしのみ」として動く）。
ファイルが無い・無効化時は通常のスキン背景（Upper/Lower）が出る。

### TJA ヘッダー

一般的な TJA 仕様に準拠。独自タグは `SONGGENREID` のみ。
**マイナスBPM等のギミックは、値は読むが表示・スクロールの動作は保証対象外**。

---

## 4. GameConfig.json

```json
{ "skinPath": "Skins/SimpleStyle", "songPath": [""], "isReplacedKeyIn2P": true,
  "autoRollSpeedPerSecond": 60, "boxCloseInterval": 7, "maxSongCount": 3 }
```

| 項目 | 説明 |
|---|---|
| `skinPath` | `Image` フォルダの1つ上（スキン名フォルダ）を指定 |
| `songPath` | 楽曲フォルダ。**複数指定可** |
| `isReplacedKeyIn2P` | false で2P側が2P専用キー割り当てになる |
| `autoRollSpeedPerSecond` | オート時の1秒あたりの連打回数。**本家の既定は 60**（TaikoSim は 20） |
| `boxCloseInterval` | 選曲画面で「閉じる（戻る）」が出る間隔。**既定 7** ← TaikoSim の実装と一致 |
| `maxSongCount` | 曲数カウンターの最大値。プレイ回数の制限はしない、表示専用 |

---

## 5. 操作（TaikoSim とズレているところあり）

共通：`F1` システムメニュー / `F5` コンフィグ再読み込み / `F11` フルスクリーン / `F12` スクリーンショット

| 画面 | キー |
|---|---|
| デモ | 面（ドン）でエントリー画面へ |
| 選曲 | `ESC` デモへ / `F2` 曲リスト再生成 / `F3` 検索 / **`F8` お気に入り** / `F9` マウス操作切り替え |
| 演奏 | `ESC` ポーズ / `F2` クイックリトライ / `F3` クイックバック（選曲へ） |
| リザルト | `F2` クイックリトライ（直接演奏画面へジャンプ） |
| 段位選択 | `ESC` デモへ / `F2` 段位楽曲リスト再生成 |
| 段位演奏 | `ESC` 段位選択へ / `F2` クイックリトライ / `F3` 強制終了 |

マウス操作ON時：ホイール＝1曲、**Shift+ホイール＝4曲**、**Ctrl+ホイール＝8曲**、左クリック＝決定。

> **TaikoSim との差**：お気に入りは TaikoSim が `F4`、本家は **`F8`**。
> TaikoSim の「カッ連打で7曲スキップ」は `boxCloseInterval` とは別物。本家のマウスは 4/8 曲。

---

## 6. システムメニュー（F1）の構成

`Image/99.Common/SystemMenu/` のアイコン8枚と一対一で対応している。

- **一般設定（Common）**：FPS表示 / 起動時の選曲カテゴリ（次回起動から） / order.def未掲載曲の扱い /
  段位課題曲フォルダに外伝も含める / プレイ後にNewマークを削除 / Newマークを全部削除 /
  譜面読み込み（**簡易再読み込み**＝キャッシュ利用、**完全再読み込み**＝キャッシュ無視）
- **ウィンドウ設定（Window）**：フルスクリーン / 解像度（1920x1080・1600x900・1366x768・1280x720・1024x600）/
  目標フレームレート（0〜240、0で無制限）/ 垂直同期
  → 低遅延のおすすめは「目標フレームレート 0・垂直同期 OFF」
- **サウンド設定（Sound）**：Master / SE / Music / BGM / Voice（各0〜100）、
  再生方式（DirectSound / WASAPI Shared / WASAPI Exclusive / ASIO）、バッファサイズ（5〜200）
  → 変更後は「設定を適用」が要る。演奏画面の曲音声はリトライするまで鳴らない
- **プレイ設定（Play）**：全体オフセット[ms]（-200〜200、正で曲が遅く）/ 全体オフセット[px]（-15〜15、正で判定が左寄り）/
  入力遅延[ms]（0〜100）/ 判定枠残像（ヒット時に1フレームだけ残像）/ 背景設定一式
- **キー設定（Key）**：**キー入力プリセット**（作成・リネーム・切り替え）、
  1P/2P の 左面・右面・左縁・右縁、選曲画面（お気に入り・マウス操作）、演奏画面（クイックリトライ・クイックバック）
- **スキン設定（Skin）**：フォントのプリロード（起動は遅くなるが選曲のプチフリーズ低減、再起動後に適用）/
  キャラクター読み込み（無効で起動が速くなる、再起動後に適用）/ 新リザルト演出
- **電源（Power）**：サイドバー下部。クリックで終了

---

## 7. SkinConfig.json

```json
{ "skinName":"", "skinVersion":"", "skinDescription":"",
  "mainFontPath":"", "detailFontPath":"", "nameplateEnglishFontPath":"",
  "nameplateEnglishLetterSpacing":0, "mainFontOffsetX":0, "mainFontOffsetY":0,
  "detailFontOffsetX":0, "detailFontOffsetY":0,
  "entryBGMIntroTimeMs":0, "songSelectBGMIntroTimeMs":0,
  "resultBGMIntroTimeMs":0, "playerCustomizeBGMIntroTimeMs":0,
  "danBackgroundInEndFrame":208 }
```

- フォントは**スキンの `Font` フォルダからの相対パス**。無ければ `Default.ttf` にフォールバックし、
  それも無ければエラーで起動できない
- `mainFontPath`＝曲名など、`detailFontPath`＝ジャンル説明などの補足、`nameplateEnglishFontPath`＝ネームプレートの英数字
- **`*BGMIntroTimeMs`＝BGMが最後まで鳴ったときに戻るループ開始位置（ミリ秒）**（TaikoSim 未対応）
- `danBackgroundInEndFrame`＝段位選択画面の背景アニメを止めるフレーム番号

---

## 8. 演奏画面のスキン（Background / Dancer / Mob / Footer）

### Background（`Image/04.EnsoGame/Background/`）

**Upper（上背景）** と **Lower（下背景）** の2層。フォルダ構造が違う。

- Upper：`Upper/[ThemeName]/` の下に状態フォルダ。`Config.json` は `songGenreID` のみ
  - 予約フォルダ名：`Normal` `Clear` `Normal_1P` `Normal_2P` `Clear_1P` `Clear_2P`
  - サイド指定のフォルダが無ければ共通フォルダが使われる
- Lower：先に `Normal` / `Clear` で分かれ、その下に `[ThemeName]/`
  - `Config.json`：`bgInEndFrame`（登場演出が終わるコマ。**0ならIn演出なし**）/ `songGenreID` / `useFooter`
- どちらも `[N]`（0始まり）が**レイヤー**で、0から順に描かれる。中身は `Anime.aup2`

### Dancer（`Image/04.EnsoGame/Dancer/`）

演奏画面の下で踊るやつ。`[DancerName]/MainConfig.json` ＋ `[N]/Config.json`。
`[N]` は「個体」で、中に `In` / `Loop` / `Out` フォルダ（連番画像 or `Anime.aup2`）。

`MainConfig.json`：

| 項目 | 説明 |
|---|---|
| `dancerShowCount` | 合計何体出すか |
| `dancerTypeLock` | 各個体の種類固定。**-1 でランダム**、0以上でフォルダ番号に固定 |
| `dancerGaugePercent` | 各個体が登場するゲージの％地点（例 `[0,0,0,40,80]`） |
| `forceDrawBeforeCurtain` | 読み込まれるが、**現行コードでは描画タイミングに反映されていない**（公式がそう書いている） |
| `songGenreID` | ジャンル固定 |

`Config.json`（個体ごと）：
- `inMotionMeasureCount` / `loopMotionMeasureCount` / `outMotionMeasureCount`（**単位は小節数**、Float）
- `inMotionBasePoint` / `loopMotionBasePoint` / `outMotionBasePoint`（BasePoint）
- `dancerInX/Y`・`dancerLoopX/Y`・`dancerOutX/Y`（**要素数は `dancerShowCount` と一致させる**）
- `useSameImageForInOut`（true なら `Out` フォルダ不要）
- `useSameMotionForInOut`（true なら**退場は入場の逆順再生**）
- `dancerInMotionIndex` / `dancerLoopMotionIndex` / `dancerOutMotionIndex`（使う画像の番号順）

### Mob（`Image/04.EnsoGame/Mob/`）

**ゲージが満タンになったときに出る演出**。`[FolderName]/Config.json` ＋ `Anime.aup2`。
In / Loop / End を**1つの aup2 にまとめて**作り、全体を **2:2:1** の比率にする。

- `mobInEndFrame`：In が終わるコマ番号
- `mobLoopEndFrame`：Loop が終わるコマ番号
- 残りが自動的に退場扱い
- 例：全200コマなら In 0〜79、Loop 80〜159、End 160〜199

### Footer（`Image/04.EnsoGame/Footer/`）

`[FolderName]/Config.json` ＋ 画像1枚。フォルダ単位でランダム、または `songGenreID` で固定。
- `x` / `y` / `songGenreID` / `referencePoint`（BasePoint）
- **`y` が 0 だと自動的に画面最下部に固定される**（`x` はそのまま）。上端に出したいなら 0 以外にする
- フォルダ内に複数画像があると**ファイルパス順で一番上**が使われる → `Image.png` に固定するのが推奨

---

## 9. PuchiChara / NamePlate

### PuchiChara

置き場所は**ドキュメント内で食い違っている**：
FolderStructure では `Image/99.Common/PuchiChara/[AssetId]/`、Config.json のページでは `Image/99.Common/Cosmetic/PuchiChara/[AssetId]/`。

- `Chara.png` 1枚に、全フレームを**横に連結**して入れる（全フレーム同じ `charaSizeWidth` × `charaSizeHeight`）
- `charaImageCount` が枚数
- 各画面の座標・倍率（`entryX/Y`・`songSelectX/Y`・`ensoGameX/Y`・`ensoGameBalloonX/Y`・`resultX/Y`・`danSelectX/Y`・`danResultX/Y` ほか）
- 使うものは `PlayerConfig.json` の `puchiCharaType`（整数インデックス）

### NamePlate

`Image/99.Common/NamePlate/Plates/[ID]/` に `Base.png` または `Anime.aup2`（どちらか必須）＋ `PlateConfig.json`。

`PlayerData/[Number]/NamePlateConfig.json`：

| 項目 | 説明 |
|---|---|
| `name` | 表示名。初期値「プレイヤー」 |
| `title` | 名前の上の称号。空なら非表示 |
| `rank` | 段位テキスト |
| `isRankGold` | 段位を金色にするか |
| `namePlateId` | **プレートフォルダ名**（旧仕様の `namePlateType`（整数）は使わない） |
| `rankType` | 段位部分の表示種類。通常 0〜2 |

公式段位道場の開催期間中は、`rank` / `isRankGold` / `rankType` は公式段位に揃えられる。

---

## 10. PlayerData

```
📁 PlayerData
  └ 📁 [Number]
     ├ 📄 PlayerData.dat        ← スコアや段位の記録（バイナリ）
     ├ 📄 PlayerConfig.json
     └ 📄 NamePlateConfig.json
```

`PlayerConfig.json`：

| 項目 | 説明 |
|---|---|
| `isShowScore` | 選曲画面などでスコアパネルを出すか |
| `isSaveScore` | 演奏結果を保存するか |
| `donchanType` | 使う Chara のインデックス（0始まり） |
| `isUsePuchiChara` / `puchiCharaType` | ぷちキャラ |
| `songSelectScorePanelIndex` | スコアパネルに出す難易度。0=Easy 1=Normal 2=Hard 3=Oni **4=Oni/Edit** |

JSON が無ければ自動生成される。

---

## 11. .aup2 サポート（AviUtl のプロジェクトファイル）

各モーションフォルダに `Anime.aup2` を置くと、連番画像より**優先して**使われる。

対応している標準描画プロパティ：`X / Y`、`中心X / 中心Y`、`Z軸回転`、`拡大率`、`透明度`、
`合成モード`（**通常・加算・乗算のみ**）。

| カテゴリ | 対応 |
|---|---|
| グループ制御 | **X / Y のみ** |
| リサイズ | X / Y |
| クリッピング | 上下左右 |
| 反転 | 上下・左右 |
| 音声ファイル | **デモ画面のみ**再生可能 |

時間制御（イージング）は**2点間（開始点と終了点）のもののみ**。

---

## 12. コンフィグ共通のデータ型

`String` / `Integer` / `Float` / `Boolean` / `T[]`。

**`BasePoint`（描画基準位置、0〜8）** ← スキンの各所で使われる：

| 値 | 位置 | 値 | 位置 | 値 | 位置 |
|---|---|---|---|---|---|
| 0 | 左上 | 1 | 中心上 | 2 | 右上 |
| 3 | 左中心 | 4 | 中心 | 5 | 右中心 |
| 6 | 左下 | 7 | 中心下 | 8 | 右下 |

---

## 13. TaikoSim に無い機能（将来やるなら、のリスト）

本家にあって TaikoSim に無いもの。優先度は付けていない。

- **段位道場**（いま着手中）
- `category.def` / `order.def`（カテゴリと並び順の定義）
- `SONGGENREID`（曲ごとに背景・Dancer・Mob・Footer を固定）
- `BGIMAGE` / `BGMOVIE` / `MOVIEOFFSET`（背景画像・背景動画とぼかし3モード）
- Dancer（踊るキャラ）、Mob（ゲージ満タン演出）、Footer、PuchiChara、NamePlate
- `.osu` ビートマップの読み込み
- `.aup2`（AviUtl プロジェクト）をアニメーションとして読む仕組み
- SkinConfig の `mainFontPath` / `detailFontPath` などのフォント指定とオフセット
- BGM のループ開始位置（`*BGMIntroTimeMs`）
- キー入力プリセット（作成・リネーム・切り替え）
- 音符位置オフセット（px 単位）、入力遅延補正、判定枠残像
- プレイヤーデータ（複数プレイヤー、スコア保存、きせかえ）
- 自動アップデート、オンラインランキング
