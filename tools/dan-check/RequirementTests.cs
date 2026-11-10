using TakeTaikoSimulator.Charts;

// requirements.json に不正な条件が混じる場合も、遊べる状態とエラー通知を両立する。
internal static class RequirementTests
{
    public static int Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "TaikoSim-DanRequirement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string song = Path.Combine(folder, "song.tja");
            File.WriteAllText(song, "");
            SongHistory.SaveRecord(song, CourseType.Oni, SongClearType.None, 50, 3);

            Check(folder, "{\"type\":\"all\",\"conditions\":[{\"type\":\"songScore\",\"path\":\"song.tja\",\"course\":\"Oni\",\"score\":100},{\"type\":\"songClear\",\"path\":\"missing.tja\",\"course\":\"Oni\",\"clearType\":\"Cleared\"}]}", true, true,
                "all: 先に未達条件があっても、後ろのファイル欠落を通知");
            Check(folder, "{\"type\":\"any\",\"conditions\":[{\"type\":\"songScore\",\"path\":\"song.tja\",\"course\":\"Oni\",\"score\":40},{\"type\":\"unknown\"}]}", true, true,
                "any: 先に達成条件があっても、後ろの不正条件を通知");
            Check(folder, "{\"type\":\"all\",\"conditions\":[{\"type\":\"songScore\",\"path\":\"song.tja\",\"course\":\"Oni\",\"score\":100},null]}", true, true,
                "不正な子条件を読み飛ばさない");
            Check(folder, "{\"type\":\"songScore\",\"path\":\"song.tja\",\"course\":\"Oni\",\"score\":-1}", true, true,
                "負のスコアはエラー");
            Check(folder, "{\"type\":\"songRank\",\"path\":\"song.tja\",\"course\":\"Oni\",\"rank\":8}", true, true,
                "範囲外のランクはエラー");
            Check(folder, "{\"type\":\"songScore\",\"path\":\"song.tja\",\"course\":\"Mystery\",\"score\":100}", true, true,
                "未知の難易度をおに扱いにしない");
            Check(folder, "null", true, true, "不正なルートはエラー");
            Check(folder, "{\"type\":\"songScore\",\"path\":\"song.tja\",\"course\":\"Oni\",\"score\":100}", false, false,
                "正常な未達条件はロック");
            Check(folder, "{\"type\":\"songRank\",\"path\":\"song.tja\",\"course\":\"Oni\",\"rank\":3}", true, false,
                "正常な達成条件は解禁");
            Console.WriteLine("解禁条件: 9ケース OK");
            return 0;
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void Check(string folder, string json, bool unlocked, bool error, string name)
    {
        string path = Path.Combine(folder, DanJson.RequirementsName);
        File.WriteAllText(path, json);
        DanCourse course = new() { Requirement = DanJson.ReadRequirements(path) };
        bool actual = DanUnlock.IsUnlocked(course, out string message);
        if (actual != unlocked || (message.Length > 0) != error)
            throw new Exception($"{name}: 解禁={actual}, エラー=「{message}」");
        Console.WriteLine($"OK: {name}");
    }
}
