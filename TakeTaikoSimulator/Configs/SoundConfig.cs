using TakeTaikoSimulator.Audio;

namespace TakeTaikoSimulator.Configs
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

        // 遅れの補正(ms)。出力方式ごとに1つずつ
        //   プラス = 曲が遅れて聞こえるとき。曲の位置から、さらにこの分を引く(曲が前に来る)
        //   例：DirectSound で曲が音符より 30ms 遅れて聞こえる → DirectSound を 30 にする
        public int LatencyFixBass { get; set; } = 0;
        public int LatencyFixDirectSound { get; set; } = 0;
        public int LatencyFixWasapiShared { get; set; } = -22;   // たけらいのPC(バッファ10ms)で合った値を初期値にする
        public int LatencyFixWasapiExclusive { get; set; } = 0;
        public int LatencyFixAsio { get; set; } = 0;

        public const int LatencyFixMin = -100;
        public const int LatencyFixMax = 200;

        // その方式の補正値
        public int GetLatencyFix(AudioOutputType type) => type switch
        {
            AudioOutputType.DirectSound => LatencyFixDirectSound,
            AudioOutputType.WasapiShared => LatencyFixWasapiShared,
            AudioOutputType.WasapiExclusive => LatencyFixWasapiExclusive,
            AudioOutputType.Asio => LatencyFixAsio,
            _ => LatencyFixBass,
        };

        // その方式の補正値を変える
        public void SetLatencyFix(AudioOutputType type, int value)
        {
            switch (type)
            {
                case AudioOutputType.DirectSound: LatencyFixDirectSound = value; break;
                case AudioOutputType.WasapiShared: LatencyFixWasapiShared = value; break;
                case AudioOutputType.WasapiExclusive: LatencyFixWasapiExclusive = value; break;
                case AudioOutputType.Asio: LatencyFixAsio = value; break;
                default: LatencyFixBass = value; break;
            }
        }

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
            if (OutputType == AudioOutputType.DirectSound) OutputType = AudioOutputType.Bass; // DirectSound は 11/19 に消した（OpenTaiko に無い）
            BassBufferMs = Math.Clamp(BassBufferMs, AudioManager.BufferMin, AudioManager.BufferMax);
            WasapiBufferMs = Math.Clamp(WasapiBufferMs, AudioManager.BufferMin, AudioManager.BufferMax);
            AsioDevice = Math.Max(0, AsioDevice);
            LatencyFixBass = Math.Clamp(LatencyFixBass, LatencyFixMin, LatencyFixMax);
            LatencyFixDirectSound = Math.Clamp(LatencyFixDirectSound, LatencyFixMin, LatencyFixMax);
            LatencyFixWasapiShared = Math.Clamp(LatencyFixWasapiShared, LatencyFixMin, LatencyFixMax);
            LatencyFixWasapiExclusive = Math.Clamp(LatencyFixWasapiExclusive, LatencyFixMin, LatencyFixMax);
            LatencyFixAsio = Math.Clamp(LatencyFixAsio, LatencyFixMin, LatencyFixMax);
        }
    }
}