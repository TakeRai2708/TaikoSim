using TaikoSim.Charts;

// 合否の判定（DanPlay）の境界値テスト。実データの段位を使って、
// 「赤のボーダーちょうど」「1つ足りない」「金のボーダーちょうど」が意図どおりになるか確かめる。
internal static class Tests
{
    private static int passed, failed;

    public static int Run(List<DanCourse> courses)
    {
        passed = failed = 0;
        Console.WriteLine("=== 合否の判定テスト ===");

        DanCourse? shodan = courses.FirstOrDefault(c => c.Title.Contains("初段"));
        DanCourse? judan = courses.FirstOrDefault(c => c.Title.Contains("十段"));

        if (shodan != null) TestShodan(shodan);
        else Console.WriteLine("  （初段が見つからないので、全体条件のテストは飛ばす）");

        if (judan != null) TestJudan(judan);
        else Console.WriteLine("  （十段が見つからないので、曲ごと条件のテストは飛ばす）");

        TestRangeRules();
        if (shodan != null) TestDisplayed(shodan);

        Console.WriteLine();
        Console.WriteLine($"→ 成功 {passed} / 失敗 {failed}");
        return failed == 0 ? 0 : 1;
    }

    // 初段：全体条件（良 814/862 以上、不可 36/0 未満、連打 141/158 以上、ゲージ 97/0）
    private static void TestShodan(DanCourse c)
    {
        Console.WriteLine($"-- {c.Title}（全体条件）--");

        // ぴったり赤のボーダー
        Check("赤ちょうど → 赤合格", Play(c, 97, (814, 35, 141)), DanResultType.Red);
        // 良が1つ足りない
        Check("良が1足りない → 不合格", Play(c, 97, (813, 35, 141)), DanResultType.Failed);
        // 不可がボーダーちょうど（36 未満なので 36 はアウト）
        Check("不可36（未満なのでアウト） → 不合格", Play(c, 97, (814, 36, 141)), DanResultType.Failed);
        Check("不可35 → 赤合格", Play(c, 97, (814, 35, 141)), DanResultType.Red);
        // 連打が1つ足りない
        Check("連打が1足りない → 不合格", Play(c, 97, (814, 35, 140)), DanResultType.Failed);
        // ゲージが足りない
        Check("ゲージ96 → 不合格", Play(c, 96, (814, 35, 141)), DanResultType.Failed);
        Check("ゲージ97ちょうど → 赤合格", Play(c, 97, (814, 35, 141)), DanResultType.Red);
        // 金：良862以上・連打158以上。不可とゲージは gold:0 なので金の足を引っぱらない
        Check("金のボーダー到達 → 金合格", Play(c, 97, (862, 35, 158)), DanResultType.Gold);
        Check("良だけ金に1足りない → 赤合格", Play(c, 97, (861, 35, 158)), DanResultType.Red);
        Check("不可が多めでも赤を割らなければ金（不可はgold:0）", Play(c, 97, (862, 35, 158)), DanResultType.Gold);
    }

    // 十段：可と連打が曲ごと、不可が全体、ゲージ 100/100
    private static void TestJudan(DanCourse c)
    {
        Console.WriteLine($"-- {c.Title}（曲ごと条件）--");

        // 可：1曲目 18/13 未満、2曲目 25/20 未満、3曲目 30/2 未満
        // 連打：1曲目 7/8、2曲目 46/51、3曲目 80/93 以上
        // 不可：全体 8/4 未満
        var red = new[] { (17, 2, 7), (24, 2, 46), (29, 2, 80) };   // (可, 不可, 連打)
        Check("各曲が赤ちょうど → 赤合格", PlayPerSong(c, 100, red), DanResultType.Red);

        var bad2 = new[] { (17, 2, 7), (25, 2, 46), (29, 2, 80) };  // 2曲目の可が25（25未満なのでアウト）
        Check("2曲目の可だけ割る → 不合格", PlayPerSong(c, 100, bad2), DanResultType.Failed);

        var badRoll3 = new[] { (17, 2, 7), (24, 2, 46), (29, 2, 79) }; // 3曲目の連打が1足りない
        Check("3曲目の連打だけ足りない → 不合格", PlayPerSong(c, 100, badRoll3), DanResultType.Failed);

        // 不可は全体で8未満。各曲3ずつ＝9でアウト
        var miss9 = new[] { (17, 3, 7), (24, 3, 46), (29, 3, 80) };
        Check("不可が合計9（全体8未満なのでアウト） → 不合格", PlayPerSong(c, 100, miss9), DanResultType.Failed);
        var miss7 = new[] { (17, 3, 7), (24, 2, 46), (29, 2, 80) };
        Check("不可が合計7 → 赤合格", PlayPerSong(c, 100, miss7), DanResultType.Red);

        // 金：可 13/20/2 未満、連打 8/51/93 以上、不可 全体4未満、ゲージ100
        var gold = new[] { (12, 1, 8), (19, 1, 51), (1, 1, 93) };
        Check("全部金のボーダー → 金合格", PlayPerSong(c, 100, gold), DanResultType.Gold);
        var goldMiss1 = new[] { (12, 1, 8), (19, 1, 51), (1, 2, 93) };  // 不可 合計4（4未満なのでアウト）
        Check("不可が合計4（金は4未満） → 赤合格", PlayPerSong(c, 100, goldMiss1), DanResultType.Red);
    }

    // 画面に出す数：「未満」系は残り回数、「以上」系はそのまま
    private static void TestDisplayed(DanCourse c)
    {
        Console.WriteLine("-- 画面に出す数（未満は残り） --");
        DanExam? miss = c.Exams.FirstOrDefault(e => e.Type == DanExamType.Miss);   // 36 未満
        DanExam? great = c.Exams.FirstOrDefault(e => e.Type == DanExamType.Great); // 814 以上

        if (miss != null)
        {
            DanPlay p0 = Play(c, 100, (0, 0, 0));
            Check2($"不可0のとき 残り{miss.For(0).Red}と出る", p0.Displayed(miss) == miss.For(0).Red, true);
            Check2("そのときバーは満タン", Math.Abs(p0.Progress(miss) - 1f) < 0.001f, true);

            DanPlay p10 = Play(c, 100, (0, 10, 0));
            Check2($"不可10のとき 残り{miss.For(0).Red - 10}と出る", p10.Displayed(miss) == miss.For(0).Red - 10, true);

            DanPlay pOver = Play(c, 100, (0, 999, 0));
            Check2("使い切ってもマイナスにならない", pOver.Displayed(miss) == 0, true);
            Check2("そのときバーは空", Math.Abs(pOver.Progress(miss)) < 0.001f, true);
        }

        if (great != null)
        {
            DanPlay p = Play(c, 100, (100, 0, 0));
            Check2("良100のとき そのまま100と出る", p.Displayed(great) == 100, true);
            Check2("バーは 100/814", Math.Abs(p.Progress(great) - 100f / great.For(0).Red) < 0.001f, true);
        }
    }

    // 以上／未満のルールそのもの
    private static void TestRangeRules()
    {
        Console.WriteLine("-- 以上／未満のルール --");
        Check2("以上：10 >= 10", DanPlay.Satisfies(10, 10, false), true);
        Check2("以上：9 >= 10 ではない", DanPlay.Satisfies(9, 10, false), false);
        Check2("未満：9 < 10", DanPlay.Satisfies(9, 10, true), true);
        Check2("未満：10 < 10 ではない", DanPlay.Satisfies(10, 10, true), false);
        Check2("Good は未満", DanExamTypes.IsLess(DanExamType.Good), true);
        Check2("Miss は未満", DanExamTypes.IsLess(DanExamType.Miss), true);
        Check2("Great は以上", DanExamTypes.IsLess(DanExamType.Great), false);
        Check2("Roll は以上", DanExamTypes.IsLess(DanExamType.Roll), false);
        Check2("Hit は以上", DanExamTypes.IsLess(DanExamType.Hit), false);
        Check2("Score は以上", DanExamTypes.IsLess(DanExamType.Score), false);
        Check2("MaxCombo は以上", DanExamTypes.IsLess(DanExamType.MaxCombo), false);
    }

    // 全体条件のテスト用：3曲ぶんを均等に割り振らず、1曲目に全部入れる（合計が同じなら結果も同じはず）
    private static DanPlay Play(DanCourse c, double gauge, (int Great, int Miss, int Roll) total)
    {
        DanPlay p = new(c) { Gauge = gauge, SongIndex = c.Songs.Count - 1 };
        p.Tallies[0].Great = total.Great;
        p.Tallies[0].Miss = total.Miss;
        p.Tallies[0].Roll = total.Roll;
        return p;
    }

    private static DanPlay PlayPerSong(DanCourse c, double gauge, (int Good, int Miss, int Roll)[] perSong)
    {
        DanPlay p = new(c) { Gauge = gauge, SongIndex = c.Songs.Count - 1 };
        for (int i = 0; i < perSong.Length && i < p.Tallies.Length; i++)
        {
            p.Tallies[i].Good = perSong[i].Good;
            p.Tallies[i].Miss = perSong[i].Miss;
            p.Tallies[i].Roll = perSong[i].Roll;
        }
        return p;
    }

    private static void Check(string name, DanPlay play, DanResultType expected)
    {
        DanResultType got = play.Overall();
        Report(name, got == expected, $"{expected} を期待したが {got}");
    }

    private static void Check2(string name, bool got, bool expected) =>
        Report(name, got == expected, $"{expected} を期待したが {got}");

    private static void Report(string name, bool ok, string detail)
    {
        if (ok) { passed++; Console.WriteLine($"  ○ {name}"); }
        else { failed++; Console.WriteLine($"  × {name} … {detail}"); }
    }
}
