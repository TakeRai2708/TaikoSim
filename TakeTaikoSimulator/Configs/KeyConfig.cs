using Raylib_cs;

namespace TakeTaikoSimulator.Configs
{
    // 太鼓の叩く場所。既存の1Pは0〜3の番号を維持する。
    // 並び順 = キーがかぶったときの優先順位(上ほど優先)
    public enum DrumPart
    {
        LeftKa,    // 左縁
        LeftDon,   // 左面
        RightDon,  // 右面
        RightKa,   // 右縁
        P2LeftKa, P2LeftDon, P2RightDon, P2RightKa,
    }

    // システムキーの種類
    // 増やすときは、ここに1行足して、下の DefaultSystemKeys と GetSystemActionName にも1行ずつ足す
    public enum SystemAction
    {
        Settings,      // 設定を開く
        QuickRestart,  // クイックリスタート
        QuickOption,   // クイックオプション
        Screenshot,    // スクリーンショット(どこでも反応)
        Search,        // 曲を検索(選曲画面)
        Favorite,      // お気に入りに入れる/外す(選曲画面)
        TrainingPause, TrainingPreviousBar, TrainingNextBar, TrainingFaster, TrainingSlower,
    }

    // キー配置の設定
    public class KeyConfig
    {
        public const string FileName = "KeyConfig.json";

        // 戻る/終了キー(固定。変更不可・他の役割にも使えない)
        public const KeyboardKey BackKey = KeyboardKey.Escape;

        // 叩く場所ごとのキー。1か所にいくつでも登録できる(0個もOK)
        public Dictionary<DrumPart, List<KeyboardKey>> DrumKeys { get; set; } = DefaultDrumKeys();

        // システムキー。1役割につき1キーだけ
        // ※システムキーは未割り当てにできない(必ず何かのキーが入る)
        public Dictionary<SystemAction, KeyboardKey> SystemKeys { get; set; } = DefaultSystemKeys();

        // ===== 初期値 =====
        // 1Pは初回セットアップで登録。2Pは C/V/N/M が初期値。
        private static Dictionary<DrumPart, List<KeyboardKey>> DefaultDrumKeys() => new()
        {
            { DrumPart.LeftKa, new() },
            { DrumPart.LeftDon, new() },
            { DrumPart.RightDon, new() },
            { DrumPart.RightKa, new() },
            { DrumPart.P2LeftKa, new() { KeyboardKey.C } }, { DrumPart.P2LeftDon, new() { KeyboardKey.V } },
            { DrumPart.P2RightDon, new() { KeyboardKey.N } }, { DrumPart.P2RightKa, new() { KeyboardKey.M } },
        };

        private static Dictionary<SystemAction, KeyboardKey> DefaultSystemKeys() => new()
        {
            { SystemAction.Settings, KeyboardKey.F1 },
            { SystemAction.QuickRestart, KeyboardKey.F5 },
            { SystemAction.QuickOption, KeyboardKey.F2 },
            { SystemAction.Screenshot, KeyboardKey.F11 }, // F12はVSのデバッグ中に一時停止キーになるので避ける
            { SystemAction.Search, KeyboardKey.F3 },
            { SystemAction.Favorite, KeyboardKey.F4 },
            { SystemAction.TrainingPause, KeyboardKey.Space },
            { SystemAction.TrainingPreviousBar, KeyboardKey.Left },
            { SystemAction.TrainingNextBar, KeyboardKey.Right },
            { SystemAction.TrainingFaster, KeyboardKey.Up },
            { SystemAction.TrainingSlower, KeyboardKey.Down },
        };

        // システムキーがかぶったときに、代わりに使う空きキーの候補
        private static readonly KeyboardKey[] FallbackKeys =
        {
            KeyboardKey.F1, KeyboardKey.F2, KeyboardKey.F3, KeyboardKey.F4,
            KeyboardKey.F5, KeyboardKey.F6, KeyboardKey.F7, KeyboardKey.F8,
            KeyboardKey.F9, KeyboardKey.F10, KeyboardKey.F11, KeyboardKey.F12,
            KeyboardKey.Insert, KeyboardKey.Home, KeyboardKey.End, KeyboardKey.PageUp, KeyboardKey.PageDown,
        };

        // 叩く場所の表示名(日本語)
        public static string GetDrumPartName(DrumPart part) => part switch
        {
            DrumPart.LeftKa => "1P 左縁",
            DrumPart.LeftDon => "1P 左面",
            DrumPart.RightDon => "1P 右面",
            DrumPart.RightKa => "1P 右縁",
            DrumPart.P2LeftKa => "2P 左縁", DrumPart.P2LeftDon => "2P 左面",
            DrumPart.P2RightDon => "2P 右面", DrumPart.P2RightKa => "2P 右縁",
            _ => part.ToString(),
        };

        // ドン(面)かどうか。false ならカッ(縁)
        public static bool IsDon(DrumPart part) => part == DrumPart.LeftDon || part == DrumPart.RightDon || part == DrumPart.P2LeftDon || part == DrumPart.P2RightDon;

        // キーが1個も無い叩く場所の一覧
        // ※プロパティにするとJSONに保存されてしまうので、メソッドにしている
        public List<DrumPart> GetEmptyParts()
        {
            var result = new List<DrumPart>();
            foreach (DrumPart part in Enum.GetValues<DrumPart>().Take(4))
            {
                if (!DrumKeys.TryGetValue(part, out List<KeyboardKey>? list) || list.Count == 0)
                {
                    result.Add(part);
                }
            }
            return result;
        }

        // 初回起動セットアップが必要か(叩くキーが1個も無い場所がある)
        public bool NeedsSetup()
        {
            return GetEmptyParts().Count > 0;
        }

        // システムキーの表示名(日本語)
        public static string GetSystemActionName(SystemAction action) => action switch
        {
            SystemAction.Settings => "設定",
            SystemAction.QuickRestart => "クイックリスタート",
            SystemAction.QuickOption => "クイックオプション",
            SystemAction.Screenshot => "スクリーンショット",
            SystemAction.Search => "検索",
            SystemAction.Favorite => "お気に入り",
            SystemAction.TrainingPause => "特訓：一時停止／再開",
            SystemAction.TrainingPreviousBar => "特訓：前の小節",
            SystemAction.TrainingNextBar => "特訓：次の小節",
            SystemAction.TrainingFaster => "特訓：速度を上げる",
            SystemAction.TrainingSlower => "特訓：速度を下げる",
            _ => action.ToString(),
        };

        // ===== 調べる =====

        // そのキーを使っている役割の名前を返す。誰も使っていなければ null
        public string? FindOwner(KeyboardKey key)
        {
            if (key == BackKey) return "戻る(固定)";

            foreach (var pair in DrumKeys)
            {
                if (pair.Value.Contains(key)) return GetDrumPartName(pair.Key);
            }

            foreach (var pair in SystemKeys)
            {
                if (pair.Value == key) return GetSystemActionName(pair.Key);
            }
            return null;
        }

        // ===== 登録する(設定画面で使う) =====

        // 1か所に入れられるキーの数（11/26 たけらい「4つまでにしよう。F1も4つまでで」）
        //   前の版で5つ以上入れていた人のキーは消さない（足せなくなるだけ）
        public const int MaxKeysPerPart = 4;

        public bool TryAddDrumKey(DrumPart part, KeyboardKey key, out string message)
        {
            string name = GetDrumPartName(part);

            if (key == KeyboardKey.Null)
            {
                message = "そのキーは使えません";
                return false;
            }

            // 2P は設定できない（見えない）ので、1P に入れるキーが 2P の初期値とかぶったら 2P の方から外す
            if (part < DrumPart.P2LeftKa)
                foreach (DrumPart p2 in Enum.GetValues<DrumPart>().Skip(4))
                    DrumKeys.GetValueOrDefault(p2)?.Remove(key);

            string? owner = FindOwner(key);
            if (owner != null)
            {
                message = $"そのキーは{owner}で割り当てられています";
                return false;
            }

            if (!DrumKeys.TryGetValue(part, out List<KeyboardKey>? list))
            {
                list = new List<KeyboardKey>();
                DrumKeys[part] = list;
            }
            if (list.Contains(key))
            {
                message = $"{key} はもう{name}に入っています";
                return false;
            }
            if (list.Count >= MaxKeysPerPart)
            {
                message = $"{name}のキーは {MaxKeysPerPart} つまでです（いらないキーを外してから追加してください）";
                return false;
            }

            list.Add(key);
            message = $"{key} を{name}に追加しました";
            return true;
        }

        // 変更に失敗しても、前のキーを消さない。
        public bool TryReplaceDrumKey(DrumPart part, KeyboardKey key, out string message)
        {
            if (DrumKeys.TryGetValue(part, out var old) && old.Contains(key))
            {
                DrumKeys[part] = new() { key };
                message = $"{GetDrumPartName(part)}を{key}にしました";
                return true;
            }
            if (!TryAddDrumKey(part, key, out message)) return false;
            DrumKeys[part] = new() { key };
            return true;
        }

        public bool TrySetSystemKey(SystemAction action, KeyboardKey key, out string message)
        {
            string name = GetSystemActionName(action);

            // システムキーは未割り当てにできない
            if (key == KeyboardKey.Null)
            {
                message = "システムキーは未割り当てにできません";
                return false;
            }

            // 今と同じキーなら何もしない
            if (SystemKeys.TryGetValue(action, out KeyboardKey current) && current == key)
            {
                message = $"{name}はすでに{key}です";
                return true;
            }

            string? owner = FindOwner(key);
            if (owner != null)
            {
                message = $"そのキーは{owner}で割り当てられています";
                return false;
            }

            SystemKeys[action] = key;
            message = $"{name}を{key}にしました";
            return true;
        }

        // ===== 外す(設定画面で使う) =====

        public bool TryRemoveDrumKey(DrumPart part, KeyboardKey key, out string message)
        {
            string name = GetDrumPartName(part);

            if (!DrumKeys.TryGetValue(part, out List<KeyboardKey>? list) || !list.Contains(key))
            {
                message = $"{key} は{name}に割り当てられていません";
                return false;
            }

            list.Remove(key);

            // 0個になったら知らせる(0個でもOK。キーを別の場所に移すときに一度外せるように)
            message = list.Count == 0
                ? $"{key} を{name}から外しました（{name}のキーが無くなりました）"
                : $"{key} を{name}から外しました";
            return true;
        }

        // ===== リセット =====

        // システムキーを初期値に戻す(叩くキーとかぶっていたら空いてるキーに逃がす)
        public void ResetSystemKeys()
        {
            SystemKeys = DefaultSystemKeys();
            Fix();
        }

        // 叩く場所のキーを全部消す(セットアップのやり直し用)
        public void ClearDrumKeys(DrumPart part)
        {
            DrumKeys[part] = new List<KeyboardKey>();
        }

        // ===== おかしな値を直す(読み込み時に自動で呼ばれる) =====
        public void Fix()
        {
            // 使用済みのキー(Escは最初から使用済み扱い)
            var used = new HashSet<KeyboardKey> { BackKey };

            // 1. 叩くキーを直す(左縁 → 左面 → 右面 → 右縁 の順に優先)
            var defaultDrums = DefaultDrumKeys();
            var fixedDrums = new Dictionary<DrumPart, List<KeyboardKey>>();

            foreach (DrumPart part in Enum.GetValues<DrumPart>())
            {
                // ファイルに書いてなければ初期値(空)
                //   2P は設定できなくした（11/26 たけらい）ので、いつも初期値（C・V・N・M）。1P とかぶったキーは下で捨てる
                List<KeyboardKey> keys = part >= DrumPart.P2LeftKa ? defaultDrums[part]
                    : DrumKeys.TryGetValue(part, out List<KeyboardKey>? k) && k != null
                    ? k
                    : defaultDrums[part];

                var cleaned = new List<KeyboardKey>();
                foreach (KeyboardKey key in keys)
                {
                    // Null、Esc、もう使われてるキーは捨てる
                    if (key == KeyboardKey.Null || used.Contains(key)) continue;

                    cleaned.Add(key);
                    used.Add(key);
                }

                fixedDrums[part] = cleaned;
            }

            // 2. キーが0個の場所があっても、ここでは直さない(起動時にセットアップ画面が出る)
            DrumKeys = fixedDrums;

            // 3. システムキーを直す(叩くキー優先)
            var defaults = DefaultSystemKeys();

            var fixedKeys = new Dictionary<SystemAction, KeyboardKey>();
            foreach (SystemAction action in Enum.GetValues<SystemAction>())
            {
                KeyboardKey defaultKey = defaults.GetValueOrDefault(action, KeyboardKey.Null);

                // ファイルに書いてなければ初期値
                KeyboardKey key = SystemKeys.TryGetValue(action, out KeyboardKey sk) ? sk : defaultKey;

                // 未割り当て or かぶってたら初期値に戻す
                if (key == KeyboardKey.Null || used.Contains(key))
                {
                    key = defaultKey;
                }

                // 初期値もかぶってたら、空いてるキーを自動で割り当てる
                if (key == KeyboardKey.Null || used.Contains(key))
                {
                    key = FindFreeKey(used);
                }

                used.Add(key);
                fixedKeys[action] = key;
            }
            SystemKeys = fixedKeys;
        }

        // 使われていないキーを候補から探す
        private static KeyboardKey FindFreeKey(HashSet<KeyboardKey> used)
        {
            foreach (KeyboardKey key in FallbackKeys)
            {
                if (!used.Contains(key)) return key;
            }
            return KeyboardKey.Null; // 候補が全部埋まっていることは普通ありえない
        }
    }
}

