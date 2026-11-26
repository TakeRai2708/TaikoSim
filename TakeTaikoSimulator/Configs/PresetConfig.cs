using Raylib_cs;
using TakeTaikoSimulator.Audio;

namespace TakeTaikoSimulator.Configs
{
    // プリセット（Config/Presets.json）
    //   11/26 たけらい「音オフセットとかキー配置のプリセットを作ろう。3つまで登録可能に」「キー配置と音出力系の二つで」
    //   ・キー配置のプリセット 3つ：叩くキー（1P・2P）
    //   ・音の出力のプリセット 3つ：出力方式・バッファ・ASIO 機器・音オフセット・譜面オフセット
    //   ・どちらも名前をつけられる（空なら「プリセット1」など）。切り替えは設定（F1）の「音」「キー」の一番上
    //   ・使い方：今選んでいるプリセットに、自動で保存する（設定を保存するたびに ConfigManager.SaveAll が今の値を写す）
    //     → 出力やオフセットを変えても、別のプリセットに切り替えれば元の値に戻せる
    //   ・PC 共通（プレイヤーデータごとではない）。ファイルが無いときは、3つとも今の値で作る
    public sealed class KeyPreset
    {
        public string Name { get; set; } = "";
        public Dictionary<DrumPart, List<KeyboardKey>> DrumKeys { get; set; } = new();
    }

    public sealed class AudioPreset
    {
        public string Name { get; set; } = "";
        public AudioOutputType OutputType { get; set; } = AudioOutputType.Bass;
        public int BassBufferMs { get; set; } = 15;
        public int WasapiBufferMs { get; set; } = 50;
        public int AsioDevice { get; set; } = 0;
        public int AudioOffsetMs { get; set; } = 0;
        public int ChartOffsetMs { get; set; } = 0;
    }

    public sealed class PresetConfig
    {
        public const string FileName = "Presets.json";
        public const int Count = 3;
        public const int NameLimit = 12;

        public int KeyActive { get; set; } = 0;
        public int AudioActive { get; set; } = 0;
        public List<KeyPreset> Keys { get; set; } = new();
        public List<AudioPreset> Audio { get; set; } = new();

        // 3つにそろえる。足りない分は、今の値で作る
        public void Fix(GameConfig game, SoundConfig sound, KeyConfig key)
        {
            while (Keys.Count < Count) { var p = new KeyPreset(); CopyKeys(key.DrumKeys, p.DrumKeys); Keys.Add(p); }
            while (Audio.Count < Count) { var p = new AudioPreset(); CaptureAudio(p, game, sound); Audio.Add(p); }
            if (Keys.Count > Count) Keys.RemoveRange(Count, Keys.Count - Count);
            if (Audio.Count > Count) Audio.RemoveRange(Count, Audio.Count - Count);
            KeyActive = Math.Clamp(KeyActive, 0, Count - 1);
            AudioActive = Math.Clamp(AudioActive, 0, Count - 1);
            foreach (var p in Keys)
            {
                p.Name = NamePlateConfig.CleanText(p.Name, NameLimit);
                p.DrumKeys ??= new();
                if (p.DrumKeys.Count == 0) CopyKeys(key.DrumKeys, p.DrumKeys);
            }
            foreach (var p in Audio)
            {
                p.Name = NamePlateConfig.CleanText(p.Name, NameLimit);
                p.AudioOffsetMs = Math.Clamp(p.AudioOffsetMs, GameConfig.OffsetMin, GameConfig.OffsetMax);
                p.ChartOffsetMs = Math.Clamp(p.ChartOffsetMs, GameConfig.OffsetMin, GameConfig.OffsetMax);
            }
        }

        public static string Label(string name, int index) => name.Length > 0 ? name : $"プリセット{index + 1}";
        public string[] KeyLabels() => Keys.Select((p, i) => Label(p.Name, i)).ToArray();
        public string[] AudioLabels() => Audio.Select((p, i) => Label(p.Name, i)).ToArray();

        // 今の値を、今選んでいるプリセットに写す（設定を保存するたび）
        public void Capture(GameConfig game, SoundConfig sound, KeyConfig key)
        {
            CaptureKeys(KeyActive, key);
            CaptureAudio(Audio[AudioActive], game, sound);
        }
        public void CaptureKeys(int index, KeyConfig key) => CopyKeys(key.DrumKeys, Keys[index].DrumKeys);
        public static void CaptureAudio(AudioPreset p, GameConfig game, SoundConfig sound)
        {
            p.OutputType = sound.OutputType;
            p.BassBufferMs = sound.BassBufferMs;
            p.WasapiBufferMs = sound.WasapiBufferMs;
            p.AsioDevice = sound.AsioDevice;
            p.AudioOffsetMs = game.AudioOffsetMs;
            p.ChartOffsetMs = game.ChartOffsetMs;
        }

        // プリセットの値を設定に入れる
        public void LoadKeys(int index, KeyConfig key) { CopyKeys(Keys[index].DrumKeys, key.DrumKeys); key.Fix(); }
        public void LoadAudio(int index, GameConfig game, SoundConfig sound)
        {
            AudioPreset p = Audio[index];
            sound.OutputType = p.OutputType;
            sound.BassBufferMs = p.BassBufferMs;
            sound.WasapiBufferMs = p.WasapiBufferMs;
            sound.AsioDevice = p.AsioDevice;
            game.AudioOffsetMs = p.AudioOffsetMs;
            game.ChartOffsetMs = p.ChartOffsetMs;
            sound.Fix();
            game.Fix();
        }

        private static void CopyKeys(Dictionary<DrumPart, List<KeyboardKey>> from, Dictionary<DrumPart, List<KeyboardKey>> to)
        {
            to.Clear();
            foreach (var (part, keys) in from) to[part] = new List<KeyboardKey>(keys);
        }
    }
}
