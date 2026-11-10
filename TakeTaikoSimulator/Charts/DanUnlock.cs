namespace TakeTaikoSimulator.Charts
{
    // 段位の解禁条件（requirements.json）の判定。
    //   ドキュメントの大事な決まり：
    //     「対象ファイルが存在しない、または条件を解決できない場合、その段位はロックされず、
    //       ゲーム内にエラー通知が表示されます」
    //   なので、読めない・見つからないときは **ロックしない側に倒す**（遊べなくして詰ませない）。
    public static class DanUnlock
    {
        /// <summary>解禁条件を使うか（今は使わない＝全部の段位がいつでも遊べる）</summary>
        public const bool UseRequirements = false;

        /// <summary>
        /// その段位を選べるか。
        /// </summary>
        /// <param name="error">条件を解決できなかったときの理由（遊べるが、画面に出す）。無ければ空</param>
        public static bool IsUnlocked(DanCourse course, out string error)
        {
            error = "";
            // 11/5 たけらい「解禁はしなくていい。どの段位もいつでもやりたいに決まってる」→ いつでも遊べる。
            //   requirements.json の読み込みと判定（下）は残してあるので、使いたくなったら UseRequirements を true に
            if (!UseRequirements) return true;
            if (course.Requirement == null) return true;   // requirements.json が無ければいつでも遊べる

            List<string> problems = new();
            bool ok = Evaluate(course.Requirement, problems, 0);
            if (problems.Count > 0)
            {
                error = string.Join("／", problems.Distinct().Take(3));
                return true;   // 解決できない条件があったらロックしない
            }
            return ok;
        }

        /// <summary>条件を満たしているか。解決できない条件に当たったら problems に理由を入れる</summary>
        private static bool Evaluate(DanRequirement req, List<string> problems, int depth)
        {
            if (depth > 16)
            {
                problems.Add("解禁条件が深すぎます");
                return true;
            }
            if (req.Error.Length > 0)
            {
                problems.Add(req.Error);
                return true;
            }

            switch (req.Type)
            {
                case DanRequirementType.All:
                    bool all = true;
                    foreach (DanRequirement child in req.Children)
                        if (!Evaluate(child, problems, depth + 1)) all = false;
                    return all;

                case DanRequirementType.Any:
                    bool any = false;
                    foreach (DanRequirement child in req.Children)
                        if (Evaluate(child, problems, depth + 1)) any = true;
                    return any;

                case DanRequirementType.Not:
                    return req.Children.Count > 0 && !Evaluate(req.Children[0], problems, depth + 1);

                case DanRequirementType.DanClear:
                {
                    if (!Exists(req.FullPath))
                    {
                        problems.Add($"段位が見つかりません：{Show(req.Path)}");
                        return true;
                    }
                    DanClearRecord have = SongHistory.GetDanRecord(SongHistory.KeyFor(req.FullPath));
                    return have.Satisfies(req.DanClear);
                }

                case DanRequirementType.SongClear:
                {
                    if (!Exists(req.FullPath))
                    {
                        problems.Add($"曲が見つかりません：{Show(req.Path)}");
                        return true;
                    }
                    (SongClearType clear, _, _) = SongHistory.GetRecord(SongHistory.KeyFor(req.FullPath), req.Course);
                    // より高いクリア種別の記録は、より低いクリア条件も満たす
                    return clear >= req.SongClear;
                }

                case DanRequirementType.SongScore:
                {
                    if (!Exists(req.FullPath))
                    {
                        problems.Add($"曲が見つかりません：{Show(req.Path)}");
                        return true;
                    }
                    (_, long score, _) = SongHistory.GetRecord(SongHistory.KeyFor(req.FullPath), req.Course);
                    return score >= req.Score;
                }

                case DanRequirementType.SongRank:
                {
                    if (!Exists(req.FullPath))
                    {
                        problems.Add($"曲が見つかりません：{Show(req.Path)}");
                        return true;
                    }
                    (_, _, int rank) = SongHistory.GetRecord(SongHistory.KeyFor(req.FullPath), req.Course);
                    return rank >= req.Rank;
                }

                default:
                    problems.Add("知らない解禁条件です");
                    return true;
            }
        }

        private static bool Exists(string fullPath) => fullPath.Length > 0 && File.Exists(fullPath);

        // 画面に出すときのパス（長いので後ろだけ）
        private static string Show(string path)
        {
            if (path.Length == 0) return "(パスなし)";
            string[] parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length <= 2 ? path : string.Join('/', parts[^2..]);
        }

        /// <summary>
        /// 解禁条件を人が読める文にする（段位選択画面で「あと何をすれば解禁されるか」を出すため）。
        /// </summary>
        public static string Describe(DanRequirement req, int depth = 0)
        {
            if (depth > 8) return "…";
            switch (req.Type)
            {
                case DanRequirementType.All:
                    return string.Join("、かつ ", req.Children.Select(c => Describe(c, depth + 1)));
                case DanRequirementType.Any:
                    return string.Join("、または ", req.Children.Select(c => Describe(c, depth + 1)));
                case DanRequirementType.Not:
                    return req.Children.Count > 0 ? $"{Describe(req.Children[0], depth + 1)} でないこと" : "（条件なし）";
                case DanRequirementType.DanClear:
                    return $"「{Show(req.Path)}」に{(req.DanClear.Gold ? "金" : "赤")}合格{ComboText(req.DanClear.Combo)}";
                case DanRequirementType.SongClear:
                    return $"「{Show(req.Path)}」の{CourseText(req.Course)}を{ClearText(req.SongClear)}";
                case DanRequirementType.SongScore:
                    return $"「{Show(req.Path)}」の{CourseText(req.Course)}で {req.Score} 点以上";
                case DanRequirementType.SongRank:
                    return $"「{Show(req.Path)}」の{CourseText(req.Course)}でランク {req.Rank} 以上";
                default:
                    return "（不明な条件）";
            }
        }

        private static string ComboText(SongClearType combo) => combo switch
        {
            SongClearType.FullCombo => "（フルコンボ）",
            SongClearType.AllPerfect => "（全良）",
            _ => "",
        };

        private static string ClearText(SongClearType clear) => clear switch
        {
            SongClearType.FullCombo => "フルコンボ",
            SongClearType.AllPerfect => "全良",
            _ => "クリア",
        };

        private static string CourseText(CourseType course) => course switch
        {
            CourseType.Easy => "かんたん",
            CourseType.Normal => "ふつう",
            CourseType.Hard => "むずかしい",
            CourseType.Oni => "おに",
            CourseType.Edit => "裏おに",
            _ => course.ToString(),
        };
    }
}
