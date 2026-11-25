using System.Numerics;
using Raylib_cs;

namespace TakeTaikoSimulator.UI;

// ★見た目の微調整は、このファイルから。詳しい例はリポジトリ直下の COORDINATE-GUIDE.md。
// 数字を変更 → 保存 → 再ビルド → 起動して確認。設定の「適用」だけでは反映されません。
// 基準は1920×1080。解像度を変えても、ここにはこの基準で数値を書きます。
// Xを増やす＝右へ / Yを増やす＝下へ。左・上へは数値を減らします。
// new(X, Y)＝位置。new(X, Y, 幅, 高さ)＝四角い範囲。特記がなければ左上基準。
// 画像の透明な余白も座標に含まれます。画像の「切り出し範囲」は描画側に残しています。
public static class LayoutAdjustments
{
    // ── 1. ネームプレート共通（プレート内部は338×80基準） ──
    public static class Plate
    {
        public const float Menu1PX = 60, Menu2PX = 1522, MenuY = 920;
        // ↑モード・難易度選択の位置。選曲は呼び出し側でY=900を指定しています。
        // ↓内部の位置なので、プレート全体の位置を変えても一緒に動きます。
        public static readonly Vector2 TitleArtOffset = new(4, 0); // 金色などのタイトル背景。スキンのadjustX/Yに加算（11/14 右はしに 4px すき間があったので右へ）
        public static readonly Rectangle TitleText = new(75, 6, 248, 28); // 上段の称号文字
        public const float TitleTextSize = 26;
        public static readonly Rectangle PlayerBadge = new(0, -1, 82, 80); // 丸い1P/2Pの絵
        public static readonly Rectangle RankBacking = new(12, 34, 131, 37); // 段位の黒背景（11/26 たけらい「段位表示の背景が微妙にずれてる。1pxぐらい下かな」：名前の白い欄より上に 1px 出ていたので、上はしを 1px 下へ(下はしはそのまま)。11/15 称号の背景の下はしの明るい茶色の1行が、すき間に見えていたので、さらに 2px 上から（称号の背景の下はしにかぶせる）。右はし x 143 が名前の白い欄の左はし）
        // 11/14 TaikøNauts のスクショ（モード選択）で測った。プレートの絵は左はし 16px が透明なので、本家のプレートの左上は (51, 920) くらい。そこから
        //   段位の字「七段」の真ん中 x 107・銀の飾りの真ん中 x 108・名前の白い欄 x 142〜326（名前はその真ん中）
        //   （一度、左上を (60,920) とまちがえて 10px 左にずらしたら、段位の字が 1P の丸に隠れて、黒い欄と白い欄の間にすき間ができた）
        public static readonly Rectangle RankOrnament = new(73, 36, 70, 36); // 銀・金・虹の飾り
        public static readonly Rectangle RankFallback = new(73, 37, 76, 34); // スキン素材がないときの段位背景
        public static readonly Rectangle RankText = new(73, 38, 69, 33); // 「七段」などの文字
        public const float RankTextSize = 24;
        public static readonly Rectangle NameWithRank = new(143, 38, 183, 33); // 段位あり：名前を中央に収める範囲（＝名前の白い欄。長い名前は縦はそのまま横だけ縮める）
        public static readonly Rectangle NameWithoutRank = new(76, 37, 249, 33); // 段位なし：名前を中央に収める範囲
        // 名前の大きさ / いちばん小さくするときの大きさ / ふちの太さ(0 = ふちなし)。字の色は TaikøNauts のスクショの濃い字
        //   NameOutlineDark：プレートの絵が無いとき(暗い板に白い字)の黒いふち
        public const float NameSize = 25, NameMinSize = 17, NameOutline = 0, NameOutlineDark = 3;
        public static readonly Raylib_cs.Color NameColor = new(34, 34, 40, 255);
        public const float OtherTextOutline = 2; // 称号・段位・素材なしの1P/2P文字の縁
    }

    // ── 2. スコアランク・王冠の記録と自己ベスト ──
    public static class Records
    {
        public static readonly Vector2 SummaryOffset = new(0, 0); // 集計パネルと数字をまとめて動かす量
        public static readonly Vector2 BestOffset = new(0, 0); // 自己ベストのパネル・印・数字をまとめて動かす量
        public const float Player2OffsetX = 1548; // 2P側へ移す量
        // 以下の数字は、集計パネルではなく画面の左上が基準（上の移動量は別途加算）。
        // 数字の座標だけは「文字の中心」。Yを3減らすと数字だけ3px上へ動きます。
        public static readonly Vector2 HighestCount = new(300, 346); // 一番上・右端のランクの個数
        public static readonly Vector2[] UpperCounts =
        {
            new(110, 385), // 上段・左
            new(204, 385), // 上段・中央
            new(298, 385), // 上段・右
        };
        public static readonly Vector2[] LowerCounts =
        {
            new(110, 424), // 下段・左
            new(204, 424), // 下段・中央
            new(298, 424), // 下段・右
        };
        public static readonly Vector2[] CrownCounts =
        {
            new(110, 462), // クリア数
            new(204, 462), // フルコンボ数
            new(298, 462), // ドンダフルコンボ数
        }; // 配列は3行のまま、new(...)の数字だけ変えてください。
        public const float CountWidth = 18, CountHeight = 22, CountSpacing = 16, CountMaxWidth = 70;
        public const float BestNumberRight = 291.5f, BestNumberY = 632.5f; // 自己ベストの右端基準X / 数字の中心Y（11/14 録画で測りなおし）
        public const float BestDigitWidth = 24, BestDigitHeight = 28, BestDigitSpacing = 22, BestMaxWidth = 190;
    }

    // ── 3. エントリー画面 ──
    public static class Entry
    {
        public static readonly Rectangle StartRow = new(380, 404, 1172, 190); // 「1人プレイ」の板。クリック範囲も追従
        public const float StartRowSpacing = 190; // 1人プレイ→2人プレイの縦間隔
        public static readonly Rectangle StartMessage1 = new(470, 61, 630, 77); // 1人プレイの「太鼓をたたいて…」。板の左上から
        public static readonly Rectangle StartMessage2 = new(470, 47, 630, 77); // 2人プレイの同じ文字。2枚目の板の左上から
        public const float StartMessageSize = 54, StartMessageOutline = 5;
        public static readonly Rectangle DataPanel = new(249, 295, 1423, 490); // 黄色/緑の大きな板。通信アニメも共通
        public static readonly Rectangle DataCard = new(563, 461, 794, 288); // 中央の赤いデータカード
        public static readonly Rectangle DataTitle = new(628, 515, 320, 52); // 「データ1」
        public static readonly Rectangle DataSubtitle = new(628, 568, 320, 48); // 「登録済みのデータ」
        public static readonly Vector2 PlatePosition = new(620, 626); // カード内のネームプレート
        public static readonly Vector2 SummaryOffset = new(946, 197); // 集計パネルを選曲位置からカード内へ移す量
        public static readonly Rectangle Previous = new(464, 550, 88, 100); // 左矢印。クリック範囲も共通
        public static readonly Rectangle Next = new(1330, 550, 88, 100); // 右矢印。クリック範囲も共通
        public static readonly Vector2 GuideOffset = new(0, 0); // 下の「えらぶ/決定」全体の移動量。決定の当たり判定も追従
    }

    // ── 4. 着せ替え画面 ──
    public static class Dressing
    {
        public static readonly Rectangle List = new(80, 240, 710, 620); // 左の一覧。行・スクロールバー・クリック範囲も追従
        public static readonly Vector2 PreviewTitle = new(890, 245); // 右側の「1P 名前」
        public static readonly Vector2 Character = new(1295, 240); // どんちゃん画像の左上。透明余白を含む
        public static readonly Vector2 CharacterMaxSize = new(720, 370); // 画像が収まる最大の幅/高さ。縦横比は維持
        public static readonly Vector2 PlatePosition = new(1385, 710); // プレビューのネームプレート
    }

    // ── 5. 画面下の「フリープレイ」 ──
    public static class FreePlay
    {
        public static readonly Vector2 Center = new(960, 1052); // 文字の見た目の中心
        public const float Size = 45, Outline = 5; // 文字サイズ / 黒縁の太さ
    }
}
