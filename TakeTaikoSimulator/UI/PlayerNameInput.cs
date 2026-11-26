using System.Numerics;
using System.Text;
using Raylib_cs;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;

namespace TakeTaikoSimulator.UI;

// 名前入力は通常のドン操作から切り離す。IME確定のEnterで次の画面へ進まない。
public sealed class PlayerNameInput
{
    private static readonly Rectangle Box = new(520, 480, 880, 96);
    public string Value { get; private set; } = "";
    public string Error { get; set; } = "";
    private bool editing;
    private int confirmGuard;
    // 称号でも使う（11/26）：見出し・説明・文字数・空でも決められるか
    private string heading = "プレイヤー名を入力";
    private string note = "24文字まで。あとで着せ替えの「名前・段位」から変えられます";
    private int limit = 24;
    private bool allowEmpty;
    public void Open(string? initialValue = null, string? title = null, string? help = null, int maxLength = 24, bool allowEmpty = false)
    {
        heading = title ?? "プレイヤー名を入力";
        note = help ?? "24文字まで。あとで着せ替えの「名前・段位」から変えられます";
        limit = maxLength;
        this.allowEmpty = allowEmpty;
        editing = initialValue != null;
        Value = initialValue ?? ""; Error = ""; confirmGuard = 2;
        while (Raylib.GetCharPressed() != 0) { }
        WindowsIme.TakeResultText();
    }
    public bool Update()
    {
        WindowsIme.RequestTextInput(Box, new Vector2(Box.X + 25 + FontManager.Measure(Value, FittedSize(Value + WindowsIme.CompositionText)).X, Box.Y + 18));
        bool composing = WindowsIme.IsComposing;
        string result = WindowsIme.TakeResultText();
        int c;
        while ((c = Raylib.GetCharPressed()) > 0)
            if (Rune.IsValid(c) && !Rune.IsControl(new Rune(c))) Value += new Rune(c).ToString();
        Value += result;
        Value = NamePlateConfig.CleanText(Value, limit, trim: false);
        if (!composing && (Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && Value.Length > 0)
        {
            int[] positions = System.Globalization.StringInfo.ParseCombiningCharacters(Value);
            Value = Value[..positions[^1]];
        }
        if (composing || result.Length > 0) confirmGuard = 2;
        if (confirmGuard > 0) { confirmGuard--; return false; }
        return (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)) && (allowEmpty || Value.Trim().Length > 0);
    }
    private static float FittedSize(string text) => 40 * Math.Min(1, 830 / Math.Max(1, FontManager.Measure(text, 40).X));
    public void Draw()
    {
        // 11/12 たけらい「プレイヤー名入力が旧UIになってる」→ 設定画面と同じ見た目（Gui.cs の色・角丸・光）に合わせた
        //   前は紺色の板に水色の字だった
        Raylib.DrawRectangle(0, 0, 1920, 1080, new Color(0, 0, 0, 175));
        Rectangle panel = new(440, 250, 1040, 570);
        SmoothShapes.Rectangle(new Rectangle(panel.X, panel.Y + 8, panel.Width, panel.Height), 24, new Color(0, 0, 0, 90));   // 影
        UiTheme.FillPanel(panel, 24);
        SmoothShapes.Border(panel, 24, 1.5f, Gui.BorderColor);
        FontManager.Draw(heading, 520, 300, 44, Gui.TextColor);
        FontManager.Draw(note, 520, 384, 24, Gui.SubTextColor);
        // 入力の箱：朱色の線と、ひかえめな光（入力中なのが分かるように）
        Gui.Glow(Box, 12, Gui.AccentColor, 0.7f);
        SmoothShapes.Rectangle(Box, 12, Gui.BoxColor);
        SmoothShapes.Border(Box, 12, 2f, Gui.AccentColor);
        string text = Value + WindowsIme.CompositionText;
        float size = FittedSize(text);
        FontManager.Draw(text, Box.X + 25, Box.Y + 18, size, Gui.TextColor);
        // カーソルは 0.5秒ごとに点滅
        if (Raylib.GetTime() % 1.0 < 0.5)
            Raylib.DrawRectangle((int)(Box.X + 25 + FontManager.Measure(text, size).X) + 2, (int)Box.Y + 22, 2, 52, Gui.SakuraColor);
        FontManager.Draw(editing ? "Enter：変更をプレビュー　　Esc：キャンセル" : "Enter：作成　　Esc：データ選択へ", 520, 664, 26, Gui.SubTextColor);
        if (Error.Length > 0) FontManager.Draw(Error, 520, 730, 26, UiTheme.P.Error);
    }
}
