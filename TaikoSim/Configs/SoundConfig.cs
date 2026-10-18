using TaikoSim.Audio;

namespace TaikoSim.Configs
{
    // 音の設定(音量・出力方式)
    public class SoundConfig
    {
        public const string FileName = "SoundConfig.json";

        // すべて 0〜100
        public int MasterVolume { get; set; } = 100; // 全体(他の全部にかかる)
        public int SeVolume { get; set; } = 100;     // 効果音(ドン・カッなど)
        public int MusicVolume { get; set; } = 100;  // 演奏中の曲
        public int BgmVolume { get; set; } = 100;    // 選曲画面などのBGM
        public int VoiceVolume { get; set; } = 100;  // ボイス

        // ===== 音の出力(設定パネルの［適用］で切り替わる) =====

        // 出力方式(BASS・DirectSound・WASAPI 共有・WASAPI 排他・ASIO)
        public AudioOutputType OutputType { get; set; } = AudioOutputType.Bass;

        // BASS・DirectSound のときのバッファ(ms)。0 で自動。今までと同じ 15
        public int BassBufferMs { get; set; } = 15;

        // WASAPI(共有・排他)のときのバッファ(ms)。0 で自動。初期値は OpenTaiko と同じ 50
        public int WasapiBufferMs { get; set; } = 50;

        // ASIO のときに使う機器の番号(0 から)
        public int AsioDevice { get; set; } = 0;

        // Raylibに渡す実際の音量(0.0〜1.0) = Master × 各音量
        // 例: Master 50, SE 80 → 0.5 × 0.8 = 0.4
        public float GetFinalVolume(int volume)
        {
            return (MasterVolume / 100f) * (volume / 100f);
        }

        // おかしな値を直す
        public void Fix()
        {
            MasterVolume = Math.Clamp(MasterVolume, 0, 100);
            SeVolume = Math.Clamp(SeVolume, 0, 100);
            MusicVolume = Math.Clamp(MusicVolume, 0, 100);
            BgmVolume = Math.Clamp(BgmVolume, 0, 100);
            VoiceVolume = Math.Clamp(VoiceVolume, 0, 100);

            if (!Enum.IsDefined(OutputType)) OutputType = AudioOutputType.Bass;
            BassBufferMs = Math.Clamp(BassBufferMs, AudioManager.BufferMin, AudioManager.BufferMax);
            WasapiBufferMs = Math.Clamp(WasapiBufferMs, AudioManager.BufferMin, AudioManager.BufferMax);
            AsioDevice = Math.Max(0, AsioDevice);
        }
    }
}