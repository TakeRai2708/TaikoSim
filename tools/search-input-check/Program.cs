using System.Globalization;
using TakeTaikoSimulator.UI;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    passed++;
}
TextInputBuffer input = new();
input.Reset("ABCD");
Check(input.Caret == 4, "再度開くと末尾");
input.MoveLeft(); input.MoveLeft();
Check(input.Caret == 2, "左へ移動");
input.Insert("x", 40);
Check(input.Text == "ABxCD" && input.Caret == 3, "途中に挿入して後ろを残す");
input.Backspace();
Check(input.Text == "ABCD" && input.Caret == 2, "カーソル直前を削除");
input.Delete();
Check(input.Text == "ABD" && input.Caret == 2, "カーソル直後を削除");
input.MoveRight();
Check(input.Caret == 3 && !input.MoveRight(), "右端を越えない");
input.MoveHome();
Check(input.Caret == 0 && !input.MoveLeft() && !input.Backspace(), "先頭を越えない");
input.Delete();
Check(input.Text == "BD" && input.Caret == 0, "先頭のDelete");
input.MoveEnd(); input.Insert("曲", 40);
Check(input.Text == "BD曲" && input.Caret == 3, "末尾へ移動して日本語入力");
input.MoveHome(); input.MoveRight(); input.Insert("検索", 40);
Check(input.Text == "B検索D曲" && input.Caret == 3, "途中のIME確定文字列");

input.Reset("A😀か\u3099B");
int[] expected = { 5, 3, 1, 0 };
foreach (int position in expected)
{
    input.MoveLeft();
    Check(input.Caret == position, "絵文字・濁点は一文字で左移動");
}
foreach (int position in new[] { 1, 3, 5, 6 })
{
    input.MoveRight();
    Check(input.Caret == position, "絵文字・濁点は一文字で右移動");
}
input.SetCaret(2);
Check(input.Caret == 1, "サロゲートの間に置かない");
input.Delete();
Check(input.Text == "Aか\u3099B" && input.Caret == 1, "絵文字のDeleteで前後を壊さない");
input.MoveRight(); input.Backspace();
Check(input.Text == "AB" && input.Caret == 1, "結合した濁点も一緒にBackspace");
input.Reset("A👨‍👩‍👧‍👦B");
input.MoveLeft(); input.MoveLeft();
Check(input.Caret == 1, "家族の絵文字を途中で分けない");
input.Delete();
Check(input.Text == "AB", "ZWJ絵文字をまとめてDelete");
input.Reset("🇦X🇧"); input.SetCaret(3); input.Backspace();
Check(input.Text == "🇦🇧" && input.Caret == 0, "Backspace後に結合した国旗の途中に残らない");
input.Reset("🇦X🇧"); input.SetCaret(2); input.Delete();
Check(input.Text == "🇦🇧" && input.Caret == 0, "Delete後に結合した国旗の途中に残らない");

input.Reset(""); input.Insert("😀曲A", 2);
Check(input.Text == "😀曲" && input.Caret == 3, "長さ上限で絵文字を切らない");
input.Reset("か"); input.Insert("\u3099", 1);
Check(input.Text == "か\u3099" && input.Caret == 2, "上限でも結合する濁点は受け付ける");
input.Reset("abc"); input.MoveHome();
Check(!input.Insert("x", 3) && input.Text == "abc" && input.Caret == 0, "上限時に既存の後ろを消さない");
input.Reset(""); input.Insert("a\n\t\rb", 40);
Check(input.Text == "ab", "制御文字は入力しない");

input.Reset("AあB");
float Width(string text) => text.Sum(c => c == 'あ' ? 20f : 10f);
input.MoveToNearest(-100, Width);
Check(input.Caret == 0, "文字の左側をクリック");
input.MoveToNearest(14, Width);
Check(input.Caret == 1, "半角と全角の間をクリック");
input.MoveToNearest(21, Width);
Check(input.Caret == 2, "全角文字の右側をクリック");
input.MoveToNearest(500, Width);
Check(input.Caret == 3, "文字の右側をクリック");
input.Reset("A😀B");
input.MoveToNearest(20, t => StringInfo.ParseCombiningCharacters(t).Length * 10);
Check(input.Caret == 3, "クリックも絵文字の途中に置かない");
input.Reset("");
Check(!input.MoveToNearest(30, Width) && !input.Delete() && !input.Backspace(), "空欄を操作しても壊れない");
input.Insert(new string('あ', 40), 40);
Check(input.Text.Length == 40 && !input.Insert("あ", 40), "検索上限40文字");
Console.WriteLine($"PASS: {passed} search input checks");
