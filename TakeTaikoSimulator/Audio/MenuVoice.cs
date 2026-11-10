using ManagedBass;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;

namespace TakeTaikoSimulator.Audio
{
    // メニューで鳴らす、どんちゃんの声（A-Style の Sound フォルダの声）
    //
    //   11/9 たけらい「A-Style のどんちゃんの声を適応したい。使ってないところあるでしょ」
    //   どの声がいつ鳴るかは、TaikøNauts の録画の音と声のファイルを照らし合わせて決めた（AGENTS.md の「Voices」）。
    //
    //   ・声は同時に1つだけ。新しい声を鳴らすと、前の声は止める（カーソルを動かしたら、前の項目の声は切れる）
    //   ・くり返す声（Loop）：鳴り終わったら、すぐにまた頭から鳴らす（TaikøNauts の「演奏ゲーム」「曲を選ぼう」と同じ）
    //     画面から出るとき・何か操作したときは StopLoop で止める
    //   ・PlayThenLoop：1つめの声が鳴り終わってから、2つめの声をくり返す（モード選択に入ったとき「モードを選んでね」→「演奏ゲーム」…）
    //   ・声のファイルが無いスキン（シンプルなど）では何も鳴らない
    //   ・音量は「ボイス」の音量（ConfigManager.Sound.VoiceVolume）
    //   Update は Game が毎フレーム呼ぶ（画面の切り替え中も）。
    public static class MenuVoice
    {
        private sealed class Voice
        {
            public required SoundEffect Sound;
            public required double Seconds;
        }

        private static readonly Dictionary<string, Voice?> cache = new();
        private static int loadedSkin = -1;
        private static int loadedAudio = -1;

        private static Voice? playing;      // 今鳴っている声
        private static double left;         // 今の声の残り秒
        private static string? nextLoop;    // 今の声が終わったら、くり返す声
        private static string? loop;        // くり返している声
        private static string? pending;     // 少し待ってから鳴らす声（PlayAfter）
        private static double pendingLeft;

        /// <summary>声を1回だけ鳴らす（くり返しは止める）</summary>
        public static void Play(string file)
        {
            loop = null;
            nextLoop = null;
            pending = null;
            Start(file);
        }

        /// <summary>seconds 秒たったら声を鳴らす（それまでに別の声を鳴らしたり CancelPending したら鳴らさない）</summary>
        public static void PlayAfter(string file, double seconds)
        {
            pending = file;
            pendingLeft = seconds;
        }

        /// <summary>PlayAfter で待っている声をやめる</summary>
        public static void CancelPending() => pending = null;

        /// <summary>効果音として鳴らす（声とは別。前の声は止めない。音量は効果音の音量）</summary>
        public static void PlaySe(string file)
        {
            Get(file)?.Sound.Play((float)ConfigManager.Sound.GetFinalVolume(ConfigManager.Sound.SeVolume));
        }

        /// <summary>声をくり返す（鳴り終わったらすぐ頭から）</summary>
        public static void Loop(string file)
        {
            nextLoop = null;
            pending = null;
            loop = file;
            Start(file);
        }

        /// <summary>first を鳴らして、鳴り終わったら loopFile をくり返す</summary>
        public static void PlayThenLoop(string first, string loopFile)
        {
            pending = null;
            loop = null;
            nextLoop = loopFile;
            if (!Start(first)) Loop(loopFile);   // 1つめが無いスキンなら、すぐくり返しへ
        }

        /// <summary>くり返しをやめる（今鳴っている声は最後まで鳴らす）</summary>
        public static void StopLoop()
        {
            loop = null;
            nextLoop = null;
        }

        /// <summary>声を全部止める</summary>
        public static void Stop()
        {
            StopLoop();
            pending = null;
            playing?.Sound.Stop();
            playing = null;
            left = 0;
        }

        /// <summary>毎フレーム（Game から）</summary>
        public static void Update(float dt)
        {
            if (pending != null)
            {
                pendingLeft -= dt;
                if (pendingLeft <= 0)
                {
                    string file = pending;
                    pending = null;
                    loop = null;
                    nextLoop = null;
                    Start(file);
                }
            }

            if (playing == null) return;
            left -= dt;
            if (left > 0) return;

            playing = null;
            if (nextLoop != null)
            {
                string file = nextLoop;
                nextLoop = null;
                loop = file;
                Start(file);
            }
            else if (loop != null)
            {
                Start(loop);
            }
        }

        // 声を鳴らす（前の声は止める）。鳴らせたら true
        private static bool Start(string file)
        {
            playing?.Sound.Stop();
            playing = null;
            left = 0;

            Voice? voice = Get(file);
            if (voice == null) return false;
            voice.Sound.Play((float)ConfigManager.Sound.GetFinalVolume(ConfigManager.Sound.VoiceVolume));
            playing = voice;
            left = voice.Seconds;
            return true;
        }

        private static Voice? Get(string file)
        {
            if (loadedSkin != SkinManager.Generation || loadedAudio != AudioManager.Generation)
            {
                // スキンや音の準備が変わったら読み直す（古い音は、音を準備し直したあとはもう使えないので片付けない）
                if (loadedAudio == AudioManager.Generation)
                    foreach (Voice? v in cache.Values) v?.Sound.Dispose();
                cache.Clear();
                playing = null;
                loadedSkin = SkinManager.Generation;
                loadedAudio = AudioManager.Generation;
            }

            if (cache.TryGetValue(file, out Voice? have)) return have;

            Voice? loaded = null;
            string? skin = SkinManager.CurrentPath;
            if (skin != null && AudioManager.IsAvailable && File.Exists(Path.Combine(skin, file)))
            {
                string path = Path.Combine(skin, file);
                SoundEffect? sound = SoundEffect.Load(path, out _, 1);
                if (sound != null) loaded = new Voice { Sound = sound, Seconds = LengthOf(path) };
            }
            cache[file] = loaded;
            return loaded;
        }

        // 音の長さ（秒）。くり返しのタイミングに使う
        private static double LengthOf(string path)
        {
            int stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode);
            if (stream == 0) return 2.0;
            try
            {
                long bytes = Bass.ChannelGetLength(stream);
                double seconds = Bass.ChannelBytes2Seconds(stream, bytes);
                return seconds > 0 ? seconds : 2.0;
            }
            finally
            {
                Bass.StreamFree(stream);
            }
        }
    }
}
