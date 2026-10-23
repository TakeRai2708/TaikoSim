using Raylib_cs;
using TaikoSim.Charts;
using TaikoSim.Graphics;

namespace TaikoSim.Scenes
{
    // 譜面分岐の決まりを動かす係(OpenTaiko の CStage演奏画面共通 の分岐まわりと同じ考え方)
    //   ・#BRANCHSTART の「分岐を決める時刻」が来たら、条件を見て 普通・玄人・達人 を決める
    //   ・決めた分岐の音符だけを「ある」ことにする(ほかの分岐の音符は、叩けない・見えない・見逃しにならない)
    //   ・#SECTION で、判定に使う数(良・可・不可・連打・風船)を0に戻す。スコアは戻さない
    //   ・#LEVELHOLD で、そのあとは分岐を固定する
    // 画面には何も描かない(レーンの色は下の BranchLane)
    public sealed class BranchTracker
    {
        // 判定に使う数
        private sealed class Counts
        {
            public int Good, Ok, Bad, Roll, Balloon;

            public void Reset() => Good = Ok = Bad = Roll = Balloon = 0;
        }

        // 分岐の出来事の種類。同じ時刻のときは、この順番で起こす
        //   OpenTaiko：#LEVELHOLD → 分岐を決める → #SECTION の順(CChip の n優先度)
        private enum EventKind
        {
            LevelHold = 0,
            Judge = 1,
            Section = 2,
            Reach = 3, // 分岐する場所に着いた(共通の音符のゲージに使う分岐を切り替える)
        }

        private readonly record struct BranchEventItem(double TimeMs, EventKind Kind, int Order, int Index);

        private Course? course;
        private Func<long> getScore = () => 0;
        private readonly List<BranchEventItem> events = new();
        private int nextEvent;

        // 分岐ごとに決まった結果(#BRANCHSTART の順番。まだ決まっていなければ null)
        private BranchType?[] decided = Array.Empty<BranchType?>();

        private readonly Counts all = new();   // 全部の音符
        private readonly Counts big = new();   // 大音符・大連打・くす玉だけ(条件の1文字目が大文字のとき)

        // 最後に決まった分岐(まだ決まっていない先の分岐の音符も、これで表示する)
        public BranchType Current { get; private set; } = BranchType.Normal;

        // 今の時刻で通っている分岐(分岐する場所に着いたら切り替わる。共通の音符のゲージに使う)
        public BranchType Playing { get; private set; } = BranchType.Normal;

        // #LEVELHOLD で分岐が固定されたか
        public bool LevelHold { get; private set; }

        // 分岐を1回でも決めたか(分岐の文字を出すかどうかに使う予定)
        public bool Started { get; private set; }

        // 分岐が変わったとき(前、後)。レーンの色の切り替えに使う
        public event Action<BranchType, BranchType>? Changed;

        // 次の出来事の時刻(無ければ null)
        public double? NextEventTime => nextEvent < events.Count ? events[nextEvent].TimeMs : null;

        // 最初から(演奏の開始・やり直しのとき)
        public void Reset(Course? course, Func<long> getScore)
        {
            this.course = course;
            this.getScore = getScore;
            events.Clear();
            nextEvent = 0;
            all.Reset();
            big.Reset();
            Current = Playing = BranchType.Normal;
            LevelHold = false;
            Started = false;
            decided = new BranchType?[course?.BranchPoints.Count ?? 0];
            if (course == null) return;

            // 出来事を時刻の順に並べる(同じ時刻なら EventKind の順、その次は書いてある順)
            int order = 0;
            for (int i = 0; i < course.BranchPoints.Count; i++)
            {
                BranchPoint point = course.BranchPoints[i];
                events.Add(new BranchEventItem(point.JudgeTimeMs, EventKind.Judge, order++, i));
                events.Add(new BranchEventItem(point.BranchTimeMs, EventKind.Reach, order++, i));
            }
            foreach (BranchEvent item in course.BranchEvents)
            {
                EventKind kind = item.Type == BranchEventType.Section ? EventKind.Section : EventKind.LevelHold;
                events.Add(new BranchEventItem(item.TimeMs, kind, order++, -1));
            }
            events.Sort((a, b) =>
            {
                int byTime = a.TimeMs.CompareTo(b.TimeMs);
                if (byTime != 0) return byTime;
                int byKind = a.Kind.CompareTo(b.Kind);
                return byKind != 0 ? byKind : a.Order.CompareTo(b.Order);
            });
        }

        // timeMs までに起きる出来事を、全部起こす
        public void Advance(double timeMs)
        {
            while (nextEvent < events.Count && events[nextEvent].TimeMs <= timeMs)
            {
                BranchEventItem item = events[nextEvent++];
                switch (item.Kind)
                {
                    case EventKind.LevelHold:
                        LevelHold = true;
                        break;
                    case EventKind.Judge:
                        Judge(item.Index);
                        break;
                    case EventKind.Section:
                        all.Reset();
                        big.Reset();
                        break;
                    case EventKind.Reach:
                        Playing = decided[item.Index] ?? Current;
                        break;
                }
            }
        }

        // 分岐を決める
        private void Judge(int index)
        {
            BranchPoint point = course!.BranchPoints[index];
            BranchType before = Current;
            BranchType target = Current;

            // 固定されていなくて、条件があるときだけ決め直す(条件なしなら今の分岐のまま)
            if (!LevelHold && point.Condition.Type != BranchConditionType.None)
            {
                target = Evaluate(point.Condition);
            }

            decided[index] = target;
            Current = target;
            Started = true;

            // この分岐に #LEVELHOLD が書いてあったら、ここから固定
            if (!LevelHold && point.LevelHold[(int)target]) LevelHold = true;

            if (target != before) Changed?.Invoke(before, target);
        }

        // 条件の数を出して、どの分岐か決める(OpenTaiko の tBranchJudge と同じ)
        private BranchType Evaluate(BranchCondition condition)
        {
            double value = GetValue(condition);
            if (condition.Less)
            {
                if (value < condition.Master) return BranchType.Master;
                if (value < condition.Expert) return BranchType.Expert;
                return BranchType.Normal;
            }
            if (value >= condition.Master) return BranchType.Master;
            if (value >= condition.Expert) return BranchType.Expert;
            return BranchType.Normal;
        }

        // 条件に使う数(今の値)
        public double GetValue(BranchCondition condition)
        {
            Counts c = condition.BigOnly ? big : all;
            int total = c.Good + c.Ok + c.Bad;
            return condition.Type switch
            {
                BranchConditionType.Accuracy => total == 0 ? 0 : (c.Good + c.Ok * 0.5) / total * 100.0,
                // ※ OpenTaiko はここが整数の割り算になっていて 0 か 100 にしかならない。こっちは小数で計算する
                BranchConditionType.PercentPerfect => total == 0 ? 0 : (double)c.Good / total * 100.0,
                BranchConditionType.Roll => c.Roll,
                BranchConditionType.BalloonHits => c.Balloon,
                BranchConditionType.Score => getScore(),
                BranchConditionType.JudgeGood => c.Good,
                BranchConditionType.JudgeOk => c.Ok,
                BranchConditionType.JudgeBad => c.Bad,
                _ => 0,
            };
        }

        // ===== 音符が「ある」か =====

        // その分岐の、その #BRANCHSTART の中のものが、今使われているか(共通のものはいつも使う)
        public bool IsActive(BranchType? branch, int branchIndex)
        {
            if (branch == null) return true;
            BranchType used = branchIndex >= 0 && branchIndex < decided.Length && decided[branchIndex] is BranchType d
                ? d : Current;
            return branch == used;
        }

        public bool IsActive(Note note) => IsActive(note.Branch, note.BranchIndex);
        public bool IsActive(BarLine bar) => IsActive(bar.Branch, bar.BranchIndex);
        public bool IsActive(GoGoRange range) => IsActive(range.Branch, range.BranchIndex);

        // ゲージの増え方に使う分岐(分岐の中の音符はその分岐、共通の音符は今通っている分岐)
        public BranchType GaugeBranch(Note note) => note.Branch ?? Playing;

        // ===== 数える =====

        // 普通の音符の判定(良・可・不可。見逃しも不可)
        public void OnJudge(Note note, PlayJudgement judge)
        {
            if (note.Type is not (NoteType.Don or NoteType.Ka or NoteType.BigDon or NoteType.BigKa)) return;
            Add(all, judge);
            if (note.IsBig) Add(big, judge);
        }

        private static void Add(Counts c, PlayJudgement judge)
        {
            switch (judge)
            {
                case PlayJudgement.Good: c.Good++; break;
                case PlayJudgement.Ok: c.Ok++; break;
                default: c.Bad++; break;
            }
        }

        // 連打・風船を1打叩いた(風船も連打の数に入る。OpenTaiko と同じ)
        public void OnRollHit(Note note)
        {
            bool isBalloon = note.IsBalloon;
            bool isBig = note.Type is NoteType.BigRoll or NoteType.Kusudama;

            all.Roll++;
            if (isBalloon) all.Balloon++;
            if (isBig)
            {
                big.Roll++;
                if (isBalloon) big.Balloon++;
            }
        }

        // ===== 表示用 =====

        public static string GetName(BranchType branch) => branch switch
        {
            BranchType.Normal => "普通譜面",
            BranchType.Expert => "玄人譜面",
            BranchType.Master => "達人譜面",
            _ => branch.ToString(),
        };

        // デバッグ表示の1行(例：「分岐：玄人譜面　良 12 可 3 不可 1 連打 20 風船 0　次 p 80/90 → 今 85.0」)
        public string GetDebugText()
        {
            if (course == null || !course.HasBranch) return "分岐：なし";

            string text = $"分岐：{GetName(Current)}{(LevelHold ? "（固定）" : "")}"
                + $"　良 {all.Good} 可 {all.Ok} 不可 {all.Bad} 連打 {all.Roll} 風船 {all.Balloon}";

            // 次に決める分岐の条件と、今の値
            for (int i = 0; i < decided.Length; i++)
            {
                if (decided[i] != null) continue;
                BranchCondition c = course.BranchPoints[i].Condition;
                if (c.Type == BranchConditionType.None)
                {
                    text += $"　次 {i + 1}個目：条件なし";
                }
                else
                {
                    string range = c.Less ? "未満" : "以上";
                    text += $"　次 {i + 1}個目：{c.Type}{(c.BigOnly ? "(大)" : "")} 玄人{c.Expert:0.##}/達人{c.Master:0.##}{range} → 今 {GetValue(c):0.##}";
                }
                break;
            }
            return text;
        }
    }

    // 分岐したときのレーンの色(玄人は青、達人は紫)。OpenTaiko の DrawBranchBG(AC7〜14風)と同じ動き
    //   ・1段の変化：200ms かけて上の色をふわっと出す(下がるときは消す)
    //   ・2段の変化(普通⇔達人)：まず玄人へ200ms、100ms 止まって、達人へ200ms
    //   ※ TaikøNauts の動画を見てから、2回目で合わせ直す
    public sealed class BranchLane
    {
        private const string ExpertImage = "Image/04.EnsoGame/Lane/Branch_Expert.png";
        private const string MasterImage = "Image/04.EnsoGame/Lane/Branch_Master.png";

        // OpenTaiko のカウンタ：0〜300 を 2ms ずつ(全部で 600ms)
        private const float StepSeconds = 0.002f;
        private const int CounterEnd = 300;

        private BranchType before = BranchType.Normal;
        private BranchType after = BranchType.Normal;
        private float elapsed = 999f;

        public void Reset()
        {
            before = after = BranchType.Normal;
            elapsed = 999f;
        }

        // 分岐が変わったとき(BranchTracker.Changed につなぐ)
        public void OnChanged(BranchType from, BranchType to)
        {
            if (to == after) return;
            before = after; // 切り替えの途中でも、前の行き先から始める(OpenTaiko と同じ)
            after = to;
            elapsed = 0f;
        }

        public void Update(float dt) => elapsed += dt;

        // レーンの上に描く(x, y はレーンの光と同じ場所)
        public void Draw(float x, float y)
        {
            // 普通譜面以外の色がどれくらい出ているか(左が明るいグラデーションの強さに使う)
            float presence;

            int progress = (int)(elapsed / StepSeconds);
            if (progress >= CounterEnd)
            {
                DrawLayer(after, 1f, x, y);
                presence = after == BranchType.Normal ? 0f : 1f;
            }
            else
            {
                int lower = Math.Min((int)before, (int)after);
                int higher = Math.Max((int)before, (int)after);
                bool levelDown = (int)after == lower;

                if (higher - lower < 2)
                {
                    // 1段の変化
                    if (levelDown) progress = Math.Max(0, 100 - progress);
                }
                else
                {
                    // 2段の変化
                    if (levelDown) progress = Math.Max(0, 250 - progress);
                    if (progress >= 150)
                    {
                        progress -= 150;
                        lower = 1;
                    }
                }

                // 加算合成なので、下の色から上の色へ入れ替える(足し算で2色が重ならないように)
                float upper = Math.Min(100, progress) / 100f;
                DrawLayer((BranchType)lower, 1f - upper, x, y);
                DrawLayer((BranchType)(lower + 1), upper, x, y);
                presence = lower == (int)BranchType.Normal ? upper : 1f;
            }

            DrawGradient(presence, x, y);
        }

        // ----- TaikøNauts の動画(達人譜面)を測って合わせた値 -----
        //   レーンの色 ＝ 元のレーン ＋ 分岐の画像 × 強さ(加算合成) ＋ 明るさの差
        //   強さは左(判定枠側)が少し弱く、右が少し強い。明るさの差は左が明るく、右が暗い。
        //   どちらも、だいたいまっすぐ変わっていたので、レーンの左はしと右はしの値を決めて、間はなめらかにつなぐ
        //   ※ 何の画像をどう重ねているのかは分からないので、見た目だけを似せている
        private const float StrengthLeft = 0.29f;   // レーンの左はし(x=494)の強さ
        private const float StrengthRight = 0.40f;  // レーンの右はし(x=1920)の強さ
        private const float BrightLeft = 42f;       // 左はしで足す明るさ(0〜255)
        private const float BrightZeroX = 1510f;    // 明るさの差が0になる x
        private const float DarkRight = 0.14f;      // 右はしで暗くする割合(黒を14%重ねる)
        private const int StripCount = 62;          // 強さを変えるために、画像を縦に何本に分けて描くか
        private const float BandTop = 12f;          // 分岐の画像の色が付いているところ(上から12px、高さ195px)
        private const float BandHeight = 195f;

        // 分岐の画像を、加算合成で描く(alpha は切り替えの途中の濃さ 0〜1)
        // 普通譜面はレーンそのままなので何も描かない
        private static void DrawLayer(BranchType branch, float alpha, float x, float y)
        {
            string? image = branch switch
            {
                BranchType.Expert => ExpertImage,
                BranchType.Master => MasterImage,
                _ => null,
            };
            if (image == null || alpha <= 0f) return;
            if (SkinManager.GetTexture(image) is not Texture2D texture) return;

            Raylib.BeginBlendMode(BlendMode.Additive);
            float stripWidth = texture.Width / (float)StripCount;
            float laneRight = x + texture.Width;
            for (int i = 0; i < StripCount; i++)
            {
                float left = i * stripWidth;
                float centerX = x + left + stripWidth / 2f;
                float t = Math.Clamp((centerX - x) / (laneRight - x), 0f, 1f);
                float strength = StrengthLeft + (StrengthRight - StrengthLeft) * t;
                Rectangle source = new Rectangle(left, 0, stripWidth, texture.Height);
                Rectangle dest = new Rectangle(x + left, y, stripWidth, texture.Height);
                Raylib.DrawTexturePro(texture, source, dest, System.Numerics.Vector2.Zero, 0f,
                    Raylib.ColorAlpha(Color.White, Math.Clamp(strength * alpha, 0f, 1f)));
            }
            Raylib.EndBlendMode();
        }

        // 左が明るく、右が暗いグラデーション(分岐しているときだけ。presence は 0〜1)
        private static void DrawGradient(float presence, float x, float y)
        {
            if (presence <= 0f) return;
            int top = (int)(y + BandTop);
            int height = (int)BandHeight;
            float laneRight = Screen.Width;

            // 左側：白っぽい光を足す(加算合成)
            byte bright = (byte)Math.Clamp(BrightLeft * presence, 0f, 255f);
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawRectangleGradientH((int)x, top, (int)(BrightZeroX - x), height,
                new Color(bright, bright, bright, (byte)255), new Color((byte)0, (byte)0, (byte)0, (byte)255));
            Raylib.EndBlendMode();

            // 右側：黒を少し重ねて暗くする(ふつうの合成)
            byte dark = (byte)Math.Clamp(DarkRight * presence * 255f, 0f, 255f);
            Raylib.DrawRectangleGradientH((int)BrightZeroX, top, (int)(laneRight - BrightZeroX), height,
                new Color((byte)0, (byte)0, (byte)0, (byte)0), new Color((byte)0, (byte)0, (byte)0, dark));
        }
    }
}