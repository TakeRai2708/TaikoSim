using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Raylib_cs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.UI;

namespace TakeTaikoSimulator.Scenes;

// フィードバックを書く画面（モード選択の「フィードバック」から）
//   11/26 たけらい「UIダサい」→ ②B：Windows のウィンドウ（UpdateWindow）をやめて、ゲームの中の画面にした
//   見た目は設定画面と同じ（Gui.cs の色・角丸・光）。書いた内容は GitHub の投稿画面（ブラウザ）に渡して、そこで送る
//   Enter：送信画面へ　Shift+Enter：改行　Esc：もどる（書いた内容はゲームを閉じるまで残す）
public sealed class FeedbackScene : Scene
{
    private const int MaxLength = 1400;
    private const string IssueUrl = "https://github.com/TakeRai2708/TaikoSim/issues/new";
    private static string draft = "";

    private static readonly Rectangle Panel = new(360, 170, 1200, 740);
    private static readonly Rectangle Box = new(420, 330, 1080, 380);
    private static readonly Rectangle NoteCard = new(420, 736, 1080, 64);
    private static readonly Rectangle BackButton = new(1080, 830, 190, 56);
    private static readonly Rectangle SendButton = new(1290, 830, 210, 56);
    private const float TextSize = 30, LineHeight = 44, TextPad = 24;

    private int confirmGuard = 2;
    private string error = "";

    public override bool CanOpenSettings => false;
    public override bool IsWaitingKey => true;   // 字を打っている間は、スクショなどのキーを使わない

    public override void Enter()
    {
        while (Raylib.GetCharPressed() != 0) { }
        WindowsIme.TakeResultText();
        Gui.Reset();
    }

    public override void OnBack()
    {
        if (WindowsIme.IsComposing) return;   // 変換中の Esc は変換の取り消し
        SceneManager.Simple(new ModeSelectScene());
    }

    public override void Update(float dt)
    {
        WindowsIme.RequestTextInput(Box, CaretPosition(draft + WindowsIme.CompositionText));
        bool composing = WindowsIme.IsComposing;
        string result = WindowsIme.TakeResultText();
        var typed = new StringBuilder();
        int c;
        while ((c = Raylib.GetCharPressed()) > 0)
            if (Rune.IsValid(c) && !Rune.IsControl(new Rune(c))) typed.Append(new Rune(c).ToString());
        typed.Append(result);
        if (typed.Length > 0) { Append(typed.ToString()); error = ""; }

        if (!composing && (Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && draft.Length > 0)
        {
            int[] positions = StringInfo.ParseCombiningCharacters(draft);
            draft = draft[..positions[^1]];
        }

        if (composing || result.Length > 0) confirmGuard = 2;
        if (confirmGuard > 0) { confirmGuard--; return; }

        bool enter = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter);
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        if (enter && shift) Append("\n");
        else if (enter) Send();
    }

    private static int Count(string text) => StringInfo.ParseCombiningCharacters(text).Length;

    private static void Append(string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (Count(draft) >= MaxLength) break;
            draft += rune.ToString();
        }
    }

    private void Send()
    {
        if (string.IsNullOrWhiteSpace(draft)) { error = "内容を書いてください"; return; }
        string url = IssueUrl + "?title=" + Uri.EscapeDataString("TakeTaiko フィードバック") + "&body=" + Uri.EscapeDataString(draft);
        try
        {
            using var browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            SceneManager.Simple(new ModeSelectScene());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            error = "送信画面を開けませんでした。書いた内容は残しています";
        }
    }

    // ===== 描画 =====

    // 箱の幅で折り返した行（改行もそのまま）
    private static List<string> Wrap(string text)
    {
        var lines = new List<string>();
        float max = Box.Width - TextPad * 2;
        foreach (string paragraph in text.Split('\n'))
        {
            var line = new StringBuilder();
            foreach (string ch in TextElements(paragraph))
            {
                if (line.Length > 0 && FontManager.Measure(line + ch, TextSize).X > max)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }
                line.Append(ch);
            }
            lines.Add(line.ToString());
        }
        return lines;
    }

    private static IEnumerable<string> TextElements(string text)
    {
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext()) yield return e.GetTextElement();
    }

    private static int VisibleLines => (int)((Box.Height - TextPad * 2) / LineHeight);

    private static Vector2 CaretPosition(string text)
    {
        List<string> lines = Wrap(text);
        int first = Math.Max(0, lines.Count - VisibleLines);
        int row = lines.Count - 1 - first;
        return new Vector2(Box.X + TextPad + FontManager.Measure(lines[^1], TextSize).X, Box.Y + TextPad + row * LineHeight);
    }

    public override void Draw()
    {
        Raylib.ClearBackground(Gui.BackgroundColor);
        Gui.BeginFrame();

        SmoothShapes.Rectangle(new Rectangle(Panel.X, Panel.Y + 8, Panel.Width, Panel.Height), 24, new Color(0, 0, 0, 90));
        UiTheme.FillPanel(Panel, 24);
        SmoothShapes.Border(Panel, 24, 1.5f, Gui.BorderColor);

        // 見出し：左に手紙の札、右に字数
        Rectangle tile = new(420, 220, 76, 76);
        SmoothShapes.Rectangle(tile, 18, Gui.BoxHoverColor);
        DrawEnvelope(tile);
        FontManager.Draw("フィードバック", 520, 220, 44, Gui.SakuraColor);
        FontManager.Draw("ご意見・不具合を書いてください", 522, 274, 24, Gui.SubTextColor);
        string count = $"{Count(draft)} / {MaxLength}";
        float countWidth = FontManager.Measure(count, 24).X;
        FontManager.Draw(count, Box.X + Box.Width - countWidth, 274, 24, Count(draft) >= MaxLength ? Gui.AccentColor : Gui.SubTextColor);

        // 入力の箱
        Gui.Glow(Box, 14, Gui.AccentColor, 0.6f);
        SmoothShapes.Rectangle(Box, 14, Gui.BoxColor);
        SmoothShapes.Border(Box, 14, 2f, Gui.AccentColor);
        string text = draft + WindowsIme.CompositionText;
        List<string> lines = Wrap(text);
        int first = Math.Max(0, lines.Count - VisibleLines);
        for (int i = first; i < lines.Count; i++)
            FontManager.Draw(lines[i], Box.X + TextPad, Box.Y + TextPad + (i - first) * LineHeight, TextSize, Gui.TextColor);
        if (draft.Length == 0 && WindowsIme.CompositionText.Length == 0)
            FontManager.Draw("例：選曲画面で箱を開けたとき、たまに一瞬ちらつきます", Box.X + TextPad, Box.Y + TextPad, TextSize, Gui.MutedTextColor);
        if (Raylib.GetTime() % 1.0 < 0.5)
        {
            Vector2 caret = CaretPosition(text);
            Raylib.DrawRectangle((int)caret.X + 2, (int)caret.Y + 4, 2, (int)TextSize + 6, Gui.SakuraColor);
        }

        // 送り方のメモ
        SmoothShapes.Rectangle(NoteCard, 12, Gui.BoxColor);
        SmoothShapes.Border(NoteCard, 12, 1f, Gui.BorderColor);
        FontManager.Draw("送信は次の画面（GitHub）で。投稿は公開されます", NoteCard.X + 24, NoteCard.Y + 18, 24, Gui.SubTextColor);

        // 下：操作の説明・エラー・ボタン
        if (error.Length > 0) FontManager.Draw(error, 420, 846, 26, new Color(255, 120, 110, 255));
        else FontManager.Draw("Enter：送信画面へ　Shift+Enter：改行　Esc：もどる", 420, 848, 24, Gui.MutedTextColor);
        if (Gui.Button(BackButton, "もどる")) OnBack();
        if (Gui.PrimaryButton(SendButton, "送信画面へ")) Send();
    }

    // 手紙のしるし（札のまん中）
    private static void DrawEnvelope(Rectangle tile)
    {
        Rectangle body = new(tile.X + 18, tile.Y + 24, tile.Width - 36, tile.Height - 48);
        SmoothShapes.Border(body, 4, 3f, Gui.SakuraColor);
        Vector2 left = new(body.X + 2, body.Y + 2), right = new(body.X + body.Width - 2, body.Y + 2), mid = new(body.X + body.Width / 2, body.Y + body.Height * 0.6f);
        SmoothShapes.Line(left, mid, 3, Gui.SakuraColor);
        SmoothShapes.Line(mid, right, 3, Gui.SakuraColor);
    }
}
