using Raylib_cs;

namespace TaikoSim.Configs
{
    // 太鼓の叩く場所(1P)
    // 並び順 = キーがかぶったときの優先順位(上ほど優先)
    public enum DrumPart
    {
        LeftKa,    // 左縁
        LeftDon,   // 左面
        RightDon,  // 右面
        RightKa,   // 右縁
    }

    // システムキーの種類
    // 増やすときは、ここに1行足して、下の DefaultSystemKeys と GetSystemActionName にも1行ずつ足す
    public enum SystemAction
    {
        Settings,      // 設定を開く
        QuickRestart,  // クイックリスタート
        QuickOption,   // クイックオプション
        Screenshot,    // スクリーンショット(どこでも反応)
    }

    // キー配置の設定
    public class KeyConfig
    {
        public const string FileName = "KeyConfig.json";

        // 戻る/終了キー(固定。変更不可・他の役割にも使えない)
        public const KeyboardKey BackKey = KeyboardKey.Escape;

        // 叩く場所ごとのキー。1か所にいくつでも登録できる
        public Dictionary<DrumPart, List<KeyboardKey>> DrumKeys { get; set; } = DefaultDrumKeys();

        // システムキー。1役割につき1キーだけ。Null は未割り当て
        public Dictionary<SystemAction, KeyboardKey> SystemKeys { get; set; } = DefaultSystemKeys();

        // ===== 初期値 =====
        private static Dictionary<DrumPart, List<KeyboardKey>> DefaultDrumKeys() => new()
        {
            { DrumPart.LeftKa, new() { KeyboardKey.D } },
            { DrumPart.LeftDon, new() { KeyboardKey.F } },
            { DrumPart.RightDon, new() { KeyboardKey.J } },
            { DrumPart.RightKa, new() { KeyboardKey.K } },
        };

        private static Dictionary<SystemAction, KeyboardKey> DefaultSystemKeys() => new()
        {
            { SystemAction.Settings, KeyboardKey.F1 },
            { SystemAction.QuickRestart, KeyboardKey.F5 },
            { SystemAction.QuickOption, KeyboardKey.F2 },
            { SystemAction.Screenshot, KeyboardKey.F11 }, // F12はVSのデバッグ中に一時停止キーになるので避ける
        };

        // 叩く場所の表示名(日本語)
        public static string GetDrumPartName(DrumPart part) => part switch
        {
            DrumPart.LeftKa => "左縁",
            DrumPart.LeftDon => "左面",
            DrumPart.RightDon => "右面",
            DrumPart.RightKa => "右縁",
            _ => part.ToString(),
        };

        // ドン(面)かどうか。false ならカッ(縁)
        public static bool IsDon(DrumPart part) => part == DrumPart.LeftDon || part == DrumPart.RightDon;

        // システムキーの表示名(日本語)
        public static string GetSystemActionName(SystemAction action) => action switch
        {
            SystemAction.Settings => "設定",
            SystemAction.QuickRestart => "クイックリスタート",
            SystemAction.QuickOption => "クイックオプション",
            SystemAction.Screenshot => "スクリーンショット",
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

        public bool TryAddDrumKey(DrumPart part, KeyboardKey key, out string message)
        {
            string name = GetDrumPartName(part);

            if (key == KeyboardKey.Null)
            {
                message = "そのキーは使えません";
                return false;
            }

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

            list.Add(key);
            message = $"{key} を{name}に追加しました";
            return true;
        }

        public bool TrySetSystemKey(SystemAction action, KeyboardKey key, out string message)
        {
            string name = GetSystemActionName(action);

            // Null を渡したら未割り当てにする
            if (key == KeyboardKey.Null)
            {
                SystemKeys[action] = KeyboardKey.Null;
                message = $"{name}を未割り当てにしました";
                return true;
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

            // 最後の1個は外せない(叩けなくなるのを防ぐ)
            if (list.Count <= 1)
            {
                message = $"{name}のキーは最低1個必要です";
                return false;
            }

            list.Remove(key);
            message = $"{key} を{name}から外しました";
            return true;
        }

        // ===== おかしな値を直す(読み込み時に自動で呼ばれる) =====
        public void Fix()
        {
            // 使用済みのキー(Escは最初から使用済み扱い)
            var used = new HashSet<KeyboardKey> { BackKey };

            // 1. 叩くキーを直す(左縁 → 左面 → 右面 → 右縁 の順に優先)
            var defaultDrums = DefaultDrumKeys();
            var fixedDrums = new Dictionary<DrumPart, List<KeyboardKey>>();
            bool hasEmpty = false;

            foreach (DrumPart part in Enum.GetValues<DrumPart>())
            {
                // ファイルに書いてなければ初期値
                List<KeyboardKey> keys = DrumKeys.TryGetValue(part, out List<KeyboardKey>? k) && k != null
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

                if (cleaned.Count == 0) hasEmpty = true;
                fixedDrums[part] = cleaned;
            }

            // 2. キーが0個の場所があったら、4つとも初期値に戻す(叩けなくなるのを防ぐ)
            if (hasEmpty)
            {
                fixedDrums = DefaultDrumKeys();
                used = new HashSet<KeyboardKey> { BackKey };
                foreach (List<KeyboardKey> list in fixedDrums.Values)
                {
                    used.UnionWith(list);
                }
            }
            DrumKeys = fixedDrums;

            // 3. システムキーを直す(叩くキー優先)
            var defaults = DefaultSystemKeys();

            var fixedKeys = new Dictionary<SystemAction, KeyboardKey>();
            foreach (SystemAction action in Enum.GetValues<SystemAction>())
            {
                KeyboardKey defaultKey = defaults.GetValueOrDefault(action, KeyboardKey.Null);

                // ファイルに書いてなければ初期値
                KeyboardKey key = SystemKeys.TryGetValue(action, out KeyboardKey sk) ? sk : defaultKey;

                // かぶってたら初期値に戻す → 初期値もかぶってたら未割り当て
                if (key != KeyboardKey.Null && used.Contains(key))
                {
                    key = defaultKey;
                    if (used.Contains(key))
                    {
                        key = KeyboardKey.Null;
                    }
                }

                if (key != KeyboardKey.Null)
                {
                    used.Add(key);
                }
                fixedKeys[action] = key;
            }
            SystemKeys = fixedKeys;
        }
    }
}