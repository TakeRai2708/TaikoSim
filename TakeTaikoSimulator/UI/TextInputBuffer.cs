using System.Globalization;
using System.Text;

namespace TakeTaikoSimulator.UI;

// 入力中の文字と、文字の間にあるカーソル。絵文字・濁点の途中では分けない。
internal sealed class TextInputBuffer
{
    public string Text { get; private set; } = "";
    public int Caret { get; private set; }
    private int[]? boundaries;

    private int[] Boundaries => boundaries ??= StringInfo.ParseCombiningCharacters(Text).Append(Text.Length).ToArray();

    public void Reset(string text)
    {
        Text = text;
        Caret = text.Length;
        boundaries = null;
    }

    public bool SetCaret(int position)
    {
        position = Math.Clamp(position, 0, Text.Length);
        int next = 0;
        foreach (int boundary in Boundaries)
        {
            if (boundary > position) break;
            next = boundary;
        }
        if (Caret == next) return false;
        Caret = next;
        return true;
    }

    public bool MoveLeft()
    {
        int previous = 0;
        foreach (int boundary in Boundaries)
        {
            if (boundary >= Caret) break;
            previous = boundary;
        }
        return SetCaret(previous);
    }

    public bool MoveRight()
    {
        foreach (int boundary in Boundaries)
            if (boundary > Caret) return SetCaret(boundary);
        return false;
    }

    public bool MoveHome() => SetCaret(0);
    public bool MoveEnd() => SetCaret(Text.Length);

    public bool Insert(string text, int maxElements)
    {
        bool changed = false;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (Rune.IsControl(rune)) continue;
            string part = rune.ToString();
            string candidate = Text.Insert(Caret, part);
            if (StringInfo.ParseCombiningCharacters(candidate).Length > maxElements) break;
            Text = candidate;
            Caret += part.Length;
            boundaries = null;
            changed = true;
        }
        // 入れた字が後ろの濁点などと一つになったときも、その字の後ろへ置く。
        if (changed)
            foreach (int boundary in Boundaries)
                if (boundary >= Caret) { Caret = boundary; break; }
        return changed;
    }

    public bool Backspace()
    {
        if (Caret == 0) return false;
        int end = Caret;
        MoveLeft();
        Text = Text.Remove(Caret, end - Caret);
        boundaries = null;
        SetCaret(Caret);
        return true;
    }

    public bool Delete()
    {
        if (Caret == Text.Length) return false;
        int end = Boundaries.First(p => p > Caret);
        Text = Text.Remove(Caret, end - Caret);
        boundaries = null;
        SetCaret(Caret);
        return true;
    }

    // マウスを押した場所から、文字の間でいちばん近い位置を選ぶ。
    public bool MoveToNearest(float x, Func<string, float> measure)
    {
        int previous = 0;
        float previousX = 0;
        foreach (int boundary in Boundaries)
        {
            if (boundary == 0) continue;
            float nextX = measure(Text[..boundary]);
            if (x <= (previousX + nextX) / 2f) return SetCaret(previous);
            previous = boundary;
            previousX = nextX;
        }
        return MoveEnd();
    }
}
