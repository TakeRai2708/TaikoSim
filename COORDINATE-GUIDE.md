# 座標を自分で微調整する

編集するファイル：`TakeTaikoSimulator/UI/LayoutAdjustments.cs`

Visual Studioのソリューションエクスプローラーから **UI → LayoutAdjustments.cs** を開いてください。最近調整しているネームプレート、記録の数字、エントリー、着せ替え、フリープレイの値をこのファイルにまとめています。全ゲーム画面の座標を移したわけではありません。

数値を変更したら **保存 → 再ビルド → 起動して確認**。ゲーム内の「適用」やF5では、このC#ファイルの変更は反映されません。

## 数字の読み方

- 画面の基準は1920×1080。実際に1280×720で動かしていても、設定する数値は1920×1080基準です。
- **Xを増やすと右、減らすと左。Yを増やすと下、減らすと上**へ動きます。
- `new(X, Y)` は位置。`new(X, Y, 幅, 高さ)` は四角い範囲です。
- 画像は左上、記録の数字とフリープレイは文字の中心が基準です。名前・称号・段位の文字は指定した四角の中央に収まります。
- `Offset` は「今の位置からどれだけ動かすか」。`new(0, -3)` なら3px上へ移動します。
- プレート内部の座標は338×80のプレートの左上が基準です。拡大率0.5の場所では、内部の2px調整が画面上では1pxになります。
- 画像の透明な余白も位置に含まれます。絵だけが動かないように見えるときは、その余白も確認してください。

## 目的から探す

| 動かしたいもの | ファイル内の項目 | 調整する値 |
|---|---|---|
| 名前が低い／高い | `Plate.NameWithRank` / `NameWithoutRank` | 2番目のY。段位あり／なしの両方を調整 |
| 名前の大きさ・黒縁 | `Plate.NameSize` / `NameOutline` | 文字サイズ／縁の太さ |
| 上段の称号文字 | `Plate.TitleText` | 四角の位置・大きさ |
| 金色などのタイトル背景 | `Plate.TitleArtOffset` | X・Y。スキンの`PlateConfig.json`の`adjustX/Y`に加算 |
| 丸い1P・2Pの絵 | `Plate.PlayerBadge` | X・Y。画像の切り出し位置は変えない |
| 段位の黒背景 | `Plate.RankBacking` | X・Y |
| 段位の銀・金・虹の飾り | `Plate.RankOrnament` | X・Y |
| 段位の文字 | `Plate.RankText` | X・Y |
| モード画面のプレート全体 | `Plate.Menu1PX` / `Menu2PX` / `MenuY` | 各プレイヤーのX・共通Y。クリック範囲も追従 |
| 集計パネル全体 | `Records.SummaryOffset` | パネル・数字・素材なし時の印を一緒に移動 |
| 一番上のランク数 | `Records.HighestCount` | 数字の中心X・Y |
| 上段・下段のランク数 | `Records.UpperCounts` / `LowerCounts` | 左・中央・右の各`new(X, Y)` |
| 王冠の記録の数字 | `Records.CrownCounts` | 順にクリア・フルコンボ・ドンダフルコンボ |
| 自己ベスト全体 | `Records.BestOffset` | パネル・印・数字を一緒に移動 |
| 自己ベストの数字だけ | `Records.BestNumberRight` / `BestNumberY` | 右端基準X／数字の中心Y |
| 太鼓をたたいてスタート | `Entry.StartMessage1` / `StartMessage2` | それぞれの板の左上からのX・Y |
| 1人・2人プレイの板 | `Entry.StartRow` / `StartRowSpacing` | 1枚目の位置・2枚目までの縦間隔。クリック範囲も追従 |
| データ選択の大きな板 | `Entry.DataPanel` | 通信アニメの板も同じ位置 |
| 中央のデータカード | `Entry.DataCard` | カード画像の位置。中の文字・プレートは別項目 |
| データ1・登録済みの文字 | `Entry.DataTitle` / `DataSubtitle` | 各文字の四角 |
| データカード内のプレート | `Entry.PlatePosition` | プレート全体のX・Y |
| データカード内の集計 | `Entry.SummaryOffset` | 選曲時の集計位置からの移動量 |
| データ切替の左右矢印 | `Entry.Previous` / `Next` | 表示・クリックの同じ四角 |
| 下のえらぶ・決定の案内 | `Entry.GuideOffset` | 全体の移動量。決定のクリック範囲も追従 |
| 着せ替えの左の一覧 | `Dressing.List` | X・Y。行・クリック範囲・スクロールバーも追従 |
| 着せ替えのどんちゃん | `Dressing.Character` | 画像の左上X・Y |
| 着せ替えのどんちゃんの大きさ | `Dressing.CharacterMaxSize` | 画像全体の最大幅・高さ。縦横比を維持 |
| 着せ替えのプレート | `Dressing.PlatePosition` | プレート全体のX・Y |
| フリープレイ | `FreePlay.Center` / `Size` / `Outline` | 文字の中心／大きさ／黒縁 |

## そのまま使える例

**名前を3px上げる**：段位あり・なしのYをどちらも`37`から`34`へ。

```csharp
public static readonly Rectangle NameWithRank = new(151, 34, 174, 33);
public static readonly Rectangle NameWithoutRank = new(76, 34, 249, 33);
```

**クリア数だけ2px上げる**：`CrownCounts`の最初のYを`462`から`460`へ。

```csharp
new(110, 460), // クリア数
```

**タイトル背景だけ2px上げる**：

```csharp
public static readonly Vector2 TitleArtOffset = new(0, -2);
```

**着せ替えのどんちゃんを20px左へ**：`Character`のXを`1295`から`1275`へ。

```csharp
public static readonly Vector2 Character = new(1275, 240);
```

## 調整するときの注意

まずはX・Yだけを2～5pxずつ変えると見比べやすいです。名前の枠の幅を変えても、長い名前は今までどおり横だけ縮小します。

記録の各配列は3項目のままにしてください。着せ替えの一覧は9行固定なので、最初は`List`のX・Yだけを調整してください。幅・高さを詰める場合は、行や文字が収まっているか確認が必要です。

選曲画面のプレートYは、既存の呼び出し`NamePlate.DrawPlayers(900, ...)`が優先されます。演奏・リザルト・段位選択などのプレート全体の位置も、今回は呼び出し側の指定を維持しています。`Plate`内の文字・背景・1P/2Pの調整は、これらの画面にも共通で反映されます。

タイトル背景はAup2・静止画像の両方に対応します。素材がない場合の仮の青背景には`TitleArtOffset`を適用しません。スキンの画像を切り出す数値は、うっかり別の絵が出ないよう元の描画処理に残しています。

ゲーム設定・記録・プレイヤーデータのファイルは変更しません。調整値を戻したいときは、このC#ファイルの変更をGitで戻して再ビルドしてください。
