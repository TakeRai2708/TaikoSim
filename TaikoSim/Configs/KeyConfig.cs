using Raylib_cs;

namespace TaikoSim.Configs
{
    // システムキーの種類
    // 増やすときは、ここに1行足して、下の DefaultSystemKeys と GetSystemActionName にも1行ずつ足す
    public enum SystemAction
    {
        Settings,      // 設定を開く
        QuickRestart,  // クイックリスタート
        QuickOption,   // クイックオプション
    }

    // キー配置の設定
    public class KeyConfig
    {
        public const string FileName = "KeyConfig.json";

        // 戻る/終了キー(固定。変更不可・他の役割にも使えない)
        public const KeyboardKey BackKey = KeyboardKey.Escape;

        // ドン(面)のキー。いくつでも登録できる
        public List<KeyboardKey> Don { get; set; } = DefaultDon();

        // カッ(縁)のキー。いくつでも登録できる
        public List<KeyboardKey> Ka { get; set; } = DefaultKa();

        // システムキー。1役割につき1キーだけ。Null は未割り当て
        public Dictionary<SystemAction, KeyboardKey> SystemKeys { get; set; } = DefaultSystemKeys();

        // ===== 初期値 =====
        private static List<KeyboardKey> DefaultDon() => new() { KeyboardKey.F, KeyboardKey.J };
        private static List<KeyboardKey> DefaultKa() => new() { KeyboardKey.D, KeyboardKey.K };

        private static Dictionary<SystemAction, KeyboardKey> DefaultSystemKeys() => new()
        {
            { SystemAction.Settings, KeyboardKey.F1 },
            { SystemAction.QuickRestart, KeyboardKey.F5 },
            { SystemAction.QuickOption, KeyboardKey.F2 },
        };

        // システムキーの表示名(日本語)
        public static string GetSystemActionName(SystemAction action) => action switch
        {
            SystemAction.Settings => "設定",
            SystemAction.QuickRestart => "クイックリスタート",
            SystemAction.QuickOption => "クイックオプション",
            _ => action.ToString(),
        };

        // ===== 調べる =====

        // そのキーを使っている役割の名前を返す。誰も使っていなければ null
        public string? FindOwner(KeyboardKey key)
        {
            if (key == BackKey) return "戻る(固定)";
            if (Don.Contains(key)) return "ドン";
            if (Ka.Contains(key)) return "カッ";

            foreach (var pair in SystemKeys)
            {
                if (pair.Value == key) return GetSystemActionName(pair.Key);
            }
            return null;
        }

        // ===== 登録する(設定画面で使う) =====

        public bool TryAddDon(KeyboardKey key, out string message) => TryAddPlayKey(Don, "ドン", key, out message);
        public bool TryAddKa(KeyboardKey key, out string message) => TryAddPlayKey(Ka, "カッ", key, out message);

        private bool TryAddPlayKey(List<KeyboardKey> list, string name, KeyboardKey key, out string message)
        {
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

        public bool TryRemoveDon(KeyboardKey key, out string message) => TryRemovePlayKey(Don, "ドン", key, out message);
        public bool TryRemoveKa(KeyboardKey key, out string message) => TryRemovePlayKey(Ka, "カッ", key, out message);

        private static bool TryRemovePlayKey(List<KeyboardKey> list, string name, KeyboardKey key, out string message)
        {
            if (!list.Contains(key))
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
            // 1. ドン・カッから「使えないキー(Null, Esc)」と重複を消す
            //    ドンとカッでかぶってたらドン優先
            Don = Don.Where(k => k != KeyboardKey.Null && k != BackKey).Distinct().ToList();
            Ka = Ka.Where(k => k != KeyboardKey.Null && k != BackKey && !Don.Contains(k)).Distinct().ToList();

            // 2. どっちかが空になったら、両方初期値に戻す(叩けなくなるのを防ぐ)
            if (Don.Count == 0 || Ka.Count == 0)
            {
                Don = DefaultDon();
                Ka = DefaultKa();
            }

            // 3. システムキーを直す(ドン・カッ優先)
            var defaults = DefaultSystemKeys();
            var used = new HashSet<KeyboardKey>(Don);
            used.UnionWith(Ka);
            used.Add(BackKey);

            var fixedKeys = new Dictionary<SystemAction, KeyboardKey>();
            foreach (SystemAction action in Enum.GetValues<SystemAction>())
            {
                KeyboardKey defaultKey = defaults.GetValueOrDefault(action, KeyboardKey.Null);

                // ファイルに書いてなければ初期値
                KeyboardKey key = SystemKeys.TryGetValue(action, out KeyboardKey k) ? k : defaultKey;

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