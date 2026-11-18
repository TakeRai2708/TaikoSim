using ManagedBass;
using ManagedBass.Asio;
using ManagedBass.Mix;
using ManagedBass.Wasapi;
using TakeTaikoSimulator.Configs;

namespace TakeTaikoSimulator.Audio
{
    // 音の出し方(設定パネルのプルダウンの順番と同じ)
    // ※ 番号は設定ファイルに保存されるので、途中に足したり並びを変えたりしないこと
    public enum AudioOutputType
    {
        Bass = 0,            // BASS におまかせ(Windows の中では WASAPI 共有で鳴る)
        DirectSound = 1,     // 昔の方式。うまく鳴らないときの逃げ道
        WasapiShared = 2,    // WASAPI 共有(ほかのアプリの音も一緒に鳴る)
        WasapiExclusive = 3, // WASAPI 排他(TakeTaikoSimulator が音の出口を独り占めする。遅延が少ない)
        Asio = 4,            // ASIO(専用ドライバ。一番速い)
    }

    // 音の係(BASSの準備と片付け)
    // bass.dll などが無くてもゲームは落ちずに「音なし」で動くようにしている
    //
    // 鳴らし方は2通り
    //   ・BASS / DirectSound … 今まで通り、曲や効果音を BASS に直接鳴らしてもらう
    //   ・WASAPI / ASIO      … 曲をミキサーから渡す。共有では効果音だけ別の共有出力を使える
    //                          準備できないときは、効果音も曲と同じミキサーへ渡す
    public static class AudioManager
    {
        // 音が使えるか(準備に成功したか)
        public static bool IsAvailable { get; private set; } = false;

        // 今の状態(ホーム画面・設定パネルに表示する)
        public static string Status { get; private set; } = "まだ準備していません";

        // 今使っている出し方(設定と違うことがある：選んだ方式が使えなくて BASS にしたときなど)
        public static AudioOutputType CurrentType { get; private set; } = AudioOutputType.Bass;

        // 今のバッファ(ms)。0 なら自動
        public static int CurrentBufferMs { get; private set; } = 0;

        // 今の ASIO 機器の番号
        public static int CurrentAsioDevice { get; private set; } = 0;

        // ミキサー(WASAPI / ASIO のときだけ。0 なら BASS に直接鳴らしてもらう)
        public static int Mixer { get; private set; } = 0;

        // 共有出力では、打音を曲の先読みバッファに並べず、同じ機器の共有出力へ直接渡す。
        // 準備できない場合は、今までどおり曲と同じミキサーを使う。
        public static bool SharedEffectsUseSamples { get; private set; }
        public static double SharedEffectsLatencyEstimateMs { get; private set; }

        // ためた音を足していく間隔(ms)。BASS / DirectSound のとき
        private const int UpdatePeriodMs = 5;

        // 共有の効果音用。機器側の最小単位に合わせて、実際の長さは切り上げられる。
        private const int SharedEffectsBufferMs = 5;

        // BASS がもともと持っているバッファの長さ(ms)。バッファ 0(自動)のときに戻すため、最初に覚えておく
        private static int? defaultDeviceBufferMs = null;

        // バッファの設定の範囲(設定パネルのスライダー)。0 は自動
        public const int BufferMin = 0;
        public const int BufferMax = 100;

        // WASAPI のとき、ミキサー1秒分のバイト数(遅延の計算に使う)
        private static long mixerBytesPerSecond = 0;

        // WASAPI のとき、実際のバッファの長さ(ms)
        private static double wasapiBufferMs = 0;

        // ASIO のとき、1秒あたりのサンプル数
        private static double asioRate = 0;

        // BASS / DirectSound のとき、BASS が測った機器の遅れ(ms)と、実際のバッファ(ms)。測る用の表示に使う
        private static int bassDeviceLatencyMs = 0;
        public static int BassBufferMs { get; private set; } = 0;

        // どこまで準備できたか(片付けで使う)
        private static bool bassInitialized = false;
        private static bool wasapiInitialized = false;
        private static bool asioInitialized = false;

        // WASAPI が音を取りに来るときに呼ばれる関数
        // ※ BASS に渡した関数が途中で消されないように、フィールドに入れて持っておく(OpenTaiko と同じ)
        private static readonly WasapiProcedure wasapiProc = WasapiProc;

        // ===== 起動・作り直し =====

        // 起動時に1回呼ぶ(ウィンドウを作った後)。設定(SoundConfig)の方式で準備する
        // 音を準備し直すたびに 1 増える(前に読み込んだ音が使えなくなったことに気づくため。メニューの音・選曲画面のプレビュー)
        public static int Generation { get; private set; } = 0;

        public static void Init()
        {
            Generation++;
            SoundConfig sound = ConfigManager.Sound;
            AudioOutputType wanted = sound.OutputType;
            int bufferMs = GetBufferMs(sound, wanted);

            // ----- 選んだ方式で準備 -----
            if (TryInit(wanted, bufferMs, sound.AsioDevice, out string error))
            {
                Console.WriteLine($"[音] {Status}");
                return;
            }

            Console.WriteLine($"[音] {GetTypeName(wanted)} が使えませんでした：{error}");
            CleanUp();

            // ----- だめだったら BASS で(今までと同じ鳴らし方) -----
            if (wanted != AudioOutputType.Bass)
            {
                int fallbackBuffer = Math.Clamp(sound.BassBufferMs, BufferMin, BufferMax);
                if (TryInit(AudioOutputType.Bass, fallbackBuffer, 0, out string fallbackError))
                {
                    Status = $"{GetTypeName(wanted)} が使えなかったので BASS で鳴らしています（{error}）";
                    Console.WriteLine($"[音] {Status}");
                    return;
                }
                error = fallbackError;
                CleanUp();
            }

            Status = $"音の準備に失敗しました（{error}）";
            Console.WriteLine($"[音] {Status}");
        }

        // 設定パネルの［適用］で呼ぶ。片付けてから、今の設定で準備し直す
        // ※ 曲や効果音を読み込んでいる画面(演奏中)では呼ばないこと(読み込んだ音が使えなくなる)
        public static void Restart()
        {
            Free();
            Init();
        }

        // 終了時に1回呼ぶ
        public static void Free()
        {
            CleanUp();
            Status = "止めました";
        }

        // ===== 方式ごとの準備 =====

        // 1つの方式で準備してみる。だめなら false と理由
        private static bool TryInit(AudioOutputType type, int bufferMs, int asioDevice, out string error)
        {
            error = "";
            try
            {
                bool ok = type switch
                {
                    AudioOutputType.Bass => InitBass(directSound: false, bufferMs, out error),
                    AudioOutputType.DirectSound => InitBass(directSound: true, bufferMs, out error),
                    AudioOutputType.WasapiShared => InitWasapi(exclusive: false, bufferMs, out error),
                    AudioOutputType.WasapiExclusive => InitWasapi(exclusive: true, bufferMs, out error),
                    AudioOutputType.Asio => InitAsio(asioDevice, out error),
                    _ => InitBass(directSound: false, bufferMs, out error),
                };
                if (!ok) return false;

                IsAvailable = true;
                CurrentType = type;
                CurrentBufferMs = bufferMs;
                CurrentAsioDevice = asioDevice;
                return true;
            }
            catch (DllNotFoundException ex)
            {
                // exe の横に dll が無い(bass.dll・bassmix.dll・basswasapi.dll・bassasio.dll)
                error = $"{GetDllName(ex)} が見つかりません";
                return false;
            }
            catch (BadImageFormatException)
            {
                // 32bit版の dll を置いてしまったとき
                error = "dll の種類が違います（x64 フォルダの方を使ってください）";
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                // dll が古すぎる
                error = "dll のバージョンが古いです（新しいものに入れ替えてください）";
                return false;
            }
        }

        // BASS / DirectSound(BASS に直接鳴らしてもらう)
        private static bool InitBass(bool directSound, int bufferMs, out string error)
        {
            // 遅れを小さくする設定(BASS を準備する前に決める)
            //   DeviceBufferLength：スピーカーに送る前にためておく長さ(ms)。小さいほど遅れが減るけど、小さすぎると音がブツブツ切れる
            //   UpdatePeriod：ためた音を足していく間隔(ms)
            // ※ WASAPI / ASIO のあとに作り直したときのために、毎回ちゃんと決め直す
            // ※ 0(自動)のときは、BASS がもともと持っている長さに戻す
            defaultDeviceBufferMs ??= Bass.GetConfig(Configuration.DeviceBufferLength);
            Bass.Configure(Configuration.UpdateThreads, 1);
            Bass.Configure(Configuration.UpdatePeriod, UpdatePeriodMs);
            Bass.Configure(Configuration.DeviceBufferLength, bufferMs > 0 ? Math.Max(bufferMs, UpdatePeriodMs + 1) : defaultDeviceBufferMs.Value);

            // -1 = いつもの出力先(Windowsで「既定」になっているスピーカー)、44100Hz
            // Latency = 遅延を測っておく
            // DirectSound = WASAPI のかわりに DirectSound で鳴らす
            DeviceInitFlags flags = DeviceInitFlags.Latency;
            if (directSound) flags |= DeviceInitFlags.DirectSound;

            if (!Bass.Init(-1, 44100, flags))
            {
                error = Bass.LastError.ToString();
                return false;
            }
            bassInitialized = true;

            // 測る用：BASS が測った機器の遅れ(Latency を付けて準備したので入っている)と、実際のバッファ
            Bass.GetInfo(out var info); // OpenTaiko の CSoundDeviceBASS と同じ呼び方
            bassDeviceLatencyMs = info.Latency;
            BassBufferMs = Bass.GetConfig(Configuration.DeviceBufferLength);

            string name = directSound ? "DirectSound" : "BASS";
            string buffer = bufferMs > 0 ? $"{bufferMs}ms" : "自動";
            Status = $"{name}（バッファ {buffer}）　BASS {Bass.Version}";
            error = "";
            return true;
        }

        // WASAPI 共有 / 排他(OpenTaiko の CSoundDeviceWASAPI と同じ流れ)
        private static bool InitWasapi(bool exclusive, int bufferMs, out string error)
        {
            bool directEffectsReady = false;
            if (!exclusive)
            {
                defaultDeviceBufferMs ??= Bass.GetConfig(Configuration.DeviceBufferLength);
                Bass.Configure(Configuration.UpdatePeriod, 1);
                Bass.Configure(Configuration.UpdateThreads, 1);
                Bass.Configure(Configuration.DeviceBufferLength, SharedEffectsBufferMs);
                // BASSの「Default」は機器IDを持たない別名なので、対応する実機を選ぶ。
                int effectsDeviceIndex = FindSharedEffectsDevice();
                directEffectsReady = effectsDeviceIndex >= 0 && Bass.Init(effectsDeviceIndex, 44100, DeviceInitFlags.Latency);
                if (directEffectsReady)
                {
                    bassInitialized = true;
                    Bass.GetInfo(out var effectsInfo);
                    SharedEffectsLatencyEstimateMs = effectsInfo.Latency;
                }
            }

            if (!directEffectsReady)
            {
                // 従来経路ではWASAPIが音を取りに来るので、BASS自身の更新は止める。
                Bass.Configure(Configuration.UpdatePeriod, 0);
                Bass.Configure(Configuration.UpdateThreads, 0);

                // 0 = 「音を出さない機器」で BASS を準備(音を出すのは BASSWASAPI の係)
                if (!Bass.Init(0, 44100, DeviceInitFlags.Default))
                {
                    error = $"BASS の準備（{Bass.LastError}）";
                    return false;
                }
                bassInitialized = true;
            }

            // -1 = いつもの出力先。周波数とチャンネル数は 0 = 機器に合わせる
            WasapiInitFlags flags = WasapiInitFlags.AutoFormat | (exclusive ? WasapiInitFlags.Exclusive : WasapiInitFlags.Shared);
            // 排他は、機器が次の音を必要としたときに渡す。
            // 従来の周期指定なしでは、5msを希望しても20ms刻みで取りに来る機器があった。
            bool eventDriven = exclusive;
            if (eventDriven) flags |= WasapiInitFlags.EventDriven;
            bool initialized = BassWasapi.Init(-1, 0, 0, flags, bufferMs / 1000f, 0, wasapiProc, IntPtr.Zero);
            // イベント方式に対応していない機器だけ、従来の排他方式へ戻す。
            if (!initialized && eventDriven && Bass.LastError == Errors.NotAvailable)
            {
                eventDriven = false;
                flags &= ~WasapiInitFlags.EventDriven;
                initialized = BassWasapi.Init(-1, 0, 0, flags, bufferMs / 1000f, 0, wasapiProc, IntPtr.Zero);
            }
            if (!initialized)
            {
                Errors code = Bass.LastError;
                error = code == Errors.Busy ? "ほかのアプリが独り占めしています（Busy）" : code.ToString();
                return false;
            }
            wasapiInitialized = true;

            BassWasapi.GetInfo(out WasapiInfo info);

            // 既定の機器の対応が違う場合、曲と打音が別の機器に出ないよう従来経路を使う。
            SharedEffectsUseSamples = directEffectsReady
                && Bass.GetDeviceInfo(Bass.CurrentDevice, out var effectsDevice)
                && BassWasapi.GetDeviceInfo(BassWasapi.CurrentDevice, out var musicDevice)
                && string.Equals(effectsDevice.Driver, musicDevice.ID, StringComparison.OrdinalIgnoreCase);

            // WASAPI と同じ形のミキサー(Decode = 自分では鳴らさない。WASAPI に渡すだけ)
            int mixer = BassMix.CreateMixerStream(info.Frequency, info.Channels, BassFlags.MixerNonStop | BassFlags.Float | BassFlags.Decode);
            if (mixer == 0)
            {
                error = $"ミキサーを作れませんでした（{Bass.LastError}）";
                return false;
            }
            Mixer = mixer;
            mixerBytesPerSecond = (long)info.Frequency * info.Channels * sizeof(float);

            if (!BassWasapi.Start())
            {
                error = $"WASAPI を開始できませんでした（{Bass.LastError}）";
                return false;
            }

            // BASSWASAPIのバッファ長はfloat32換算。機器が16bitでも、2byteで割らない。
            // 排他のイベント方式では、この長さのバッファを2つ交互に使う。
            double actualMs = mixerBytesPerSecond > 0 ? info.BufferLength * 1000.0 / mixerBytesPerSecond : 0;
            wasapiBufferMs = actualMs * (eventDriven ? 2 : 1);

            string name = exclusive ? "WASAPI 排他" : "WASAPI 共有";
            string wantedText = bufferMs > 0 ? $"{bufferMs}ms" : "自動";
            string buffers = eventDriven ? $"{actualMs:0.#}ms×2" : $"{actualMs:0.#}ms";
            Status = $"{name}（バッファ {buffers}／希望 {wantedText}・{info.Frequency}Hz）";
            if (SharedEffectsUseSamples) Status = $"{name}（曲 {actualMs:0.#}ms／打音は別共有）";
            error = "";
            return true;
        }

        private static int FindSharedEffectsDevice()
        {
            string? id = null;
            for (int i = 0; BassWasapi.GetDeviceInfo(i, out var device); i++)
                if (device.IsEnabled && device.IsDefault && !device.IsInput)
                {
                    id = device.ID;
                    break;
                }
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; Bass.GetDeviceInfo(i, out var device); i++)
                if (device.IsEnabled && string.Equals(device.Driver, id, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        // ASIO
        private static bool InitAsio(int device, out string error)
        {
            Bass.Configure(Configuration.UpdatePeriod, 0);
            Bass.Configure(Configuration.UpdateThreads, 0);

            if (!Bass.Init(0, 44100, DeviceInitFlags.Default))
            {
                error = $"BASS の準備（{Bass.LastError}）";
                return false;
            }
            bassInitialized = true;

            // Thread = ASIO の処理を専用の流れで動かす(BASSASIO の説明書のおすすめ)
            if (!BassAsio.Init(device, AsioInitFlags.Thread))
            {
                Errors code = BassAsio.LastError;
                error = code == Errors.Device ? "ASIO の機器が見つかりません" : code.ToString();
                return false;
            }
            asioInitialized = true;

            asioRate = BassAsio.Rate;
            int mixer = BassMix.CreateMixerStream((int)asioRate, 2, BassFlags.MixerNonStop | BassFlags.Float | BassFlags.Decode);
            if (mixer == 0)
            {
                error = $"ミキサーを作れませんでした（{Bass.LastError}）";
                return false;
            }
            Mixer = mixer;

            // 出力の1チャンネル目にミキサーをつなぐ(Join = 2チャンネル目も一緒に)
            if (!BassAsio.ChannelEnableBass(false, 0, mixer, true))
            {
                error = $"ASIO の出力にミキサーをつなげませんでした（{BassAsio.LastError}）";
                return false;
            }

            // 0 = バッファの長さは機器の設定のまま
            if (!BassAsio.Start(0, 0))
            {
                error = $"ASIO を開始できませんでした（{BassAsio.LastError}）";
                return false;
            }

            string deviceName = BassAsio.GetDeviceInfo(device, out AsioDeviceInfo info) ? info.Name : $"機器 {device}";
            Status = $"ASIO：{deviceName}（遅延 {OutputLatencyMs:0.#}ms・{asioRate:0}Hz）";
            error = "";
            return true;
        }

        // 片付け(途中まで準備したものも、全部片付ける)
        private static void CleanUp()
        {
            try
            {
                if (wasapiInitialized)
                {
                    BassWasapi.Stop(true);
                    BassWasapi.Free();
                }
                if (asioInitialized)
                {
                    BassAsio.Stop();
                    BassAsio.Free();
                }
                if (Mixer != 0) Bass.StreamFree(Mixer);
                if (bassInitialized) Bass.Free();
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is BadImageFormatException || ex is EntryPointNotFoundException)
            {
                // 準備の途中で dll が無かったとき。片付けるものも無いので何もしない
            }

            wasapiInitialized = false;
            asioInitialized = false;
            bassInitialized = false;
            SharedEffectsUseSamples = false;
            SharedEffectsLatencyEstimateMs = 0;
            Mixer = 0;
            mixerBytesPerSecond = 0;
            wasapiBufferMs = 0;
            asioRate = 0;
            bassDeviceLatencyMs = 0;
            BassBufferMs = 0;
            IsAvailable = false;
        }

        // ===== 遅延 =====

        // ミキサーから出した音が、実際に聞こえるまでの時間(ms)。WASAPI / ASIO のときだけ。BASS のときは 0
        // 曲の再生位置を「今聞こえている位置」にするために使う
        public static double OutputLatencyMs
        {
            get
            {
                if (Mixer == 0) return 0;
                try
                {
                    if (wasapiInitialized && mixerBytesPerSecond > 0)
                    {
                        // WASAPI の中にたまっていて、まだ鳴っていない音の量(OpenTaiko と同じ計算)
                        int queued = BassWasapi.GetData(IntPtr.Zero, (int)DataFlags.Available);
                        if (queued >= 0) return queued * 1000.0 / mixerBytesPerSecond;

                        // 量が取れなかったときは、だいたいバッファの半分たまっていることにする
                        return wasapiBufferMs / 2;
                    }
                    if (asioInitialized && asioRate > 0)
                    {
                        // ASIO の機器が教えてくれる出力の遅延(サンプル数)
                        int samples = BassAsio.GetLatency(false);
                        return samples > 0 ? samples * 1000.0 / asioRate : 0;
                    }
                }
                catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
                {
                }
                return 0;
            }
        }

        // BASS / DirectSound のとき、叩いた音が曲より遅れて聞こえる量の目安(ms)。F3 の表示だけに使う
        //   11/8 から判定には使わない（PlayScene.AudioOffsetMs。見た目と判定がずれて、判定枠を見て叩くと合わなかった）
        //   新しく鳴らす効果音は、BASS がためてある音(バッファ)の後ろに入るので、曲より遅れて聞こえる
        //   (テスト譜面のオート録画では、ドンが曲のカチッより約 35 ms 遅れていた)
        //   実際に曲に合わせて叩いたときのズレは平均 -17.6 ms(早い)だったので、
        //   まずは「バッファ + 足し込む間隔の半分」(バッファ 15 ms なら 17.5 ms)だけ早める
        //   WASAPI / ASIO は曲と効果音が同じミキサーを通るので 0
        public static double HitSoundLagMs =>
            Mixer == 0 && bassInitialized && BassBufferMs > 0 ? BassBufferMs + UpdatePeriodMs / 2.0 : 0;

        // 設定の「遅れの補正」(ms)。今動いている出力方式の値。曲の位置からさらに引く
        //   11/18 たけらい：DirectSound だと BASS が測った遅れより実際の遅れが大きく、曲が音符より遅れて聞こえた。
        //   どの方式でも同じ感覚で叩けるように、方式ごとに手で合わせる値を持たせた
        public static double LatencyFixMs => IsAvailable ? ConfigManager.Sound.GetLatencyFix(CurrentType) : 0;

        // 測る用：今の方式で、音が実際に聞こえるまでの遅れの目安(ms)
        //   BASS / DirectSound … BassOutputLatencyMs（機器の遅れ＋バッファ。曲の位置から引いている）
        //   WASAPI / ASIO      … OutputLatencyMs と同じ(曲の位置から引いている)
        public static double MeasuredLatencyMs => Mixer != 0 ? OutputLatencyMs : BassOutputLatencyMs;

        // BASS / DirectSound のとき、曲が「送り出した位置」から「実際に聞こえる」までの遅れ(ms)
        //   11/14 たけらい「曲聞いてたたくと可の数やばい。判定見ながらだと曲ずれてる気がする」：
        //   BASS のときだけ、曲の位置から遅れを引いていなかった（WASAPI / ASIO は引いている）。
        //   そのため音符が判定枠に来る時刻より、曲が聞こえるのが遅れていて、耳で叩くと遅い（可）になっていた。
        //   OpenTaiko の BASS モードと同じく「機器の遅れ(Info.Latency) ＋ バッファ」を引く
        public static double BassOutputLatencyMs =>
            Mixer == 0 && bassInitialized ? bassDeviceLatencyMs + Math.Max(0, BassBufferMs) : 0;

        // WASAPI が「次の音をちょうだい」と言ってきたときに呼ばれる(別の流れから呼ばれる)
        // ミキサーで混ぜた音を、そのまま渡す
        private static int WasapiProc(IntPtr buffer, int length, IntPtr user)
        {
            int mixer = Mixer;
            if (mixer == 0) return 0;
            int got = Bass.ChannelGetData(mixer, buffer, length);
            return got < 0 ? 0 : got;
        }

        // ===== 設定パネル用 =====

        // 方式の名前
        public static string GetTypeName(AudioOutputType type) => type switch
        {
            AudioOutputType.Bass => "BASS",
            AudioOutputType.DirectSound => "DirectSound",
            AudioOutputType.WasapiShared => "WASAPI 共有",
            AudioOutputType.WasapiExclusive => "WASAPI 排他",
            AudioOutputType.Asio => "ASIO",
            _ => type.ToString(),
        };

        // その方式で使うバッファ(ms)。BASS と DirectSound は同じ値、WASAPI は共有・排他で同じ値(OpenTaiko と同じ)
        public static int GetBufferMs(SoundConfig sound, AudioOutputType type) => type switch
        {
            AudioOutputType.Bass or AudioOutputType.DirectSound => Math.Clamp(sound.BassBufferMs, BufferMin, BufferMax),
            AudioOutputType.WasapiShared or AudioOutputType.WasapiExclusive => Math.Clamp(sound.WasapiBufferMs, BufferMin, BufferMax),
            _ => 0, // ASIO は機器の設定のまま
        };

        // ASIO の機器の名前の一覧(bassasio.dll が無ければ空)
        // ※ 調べるのに少し時間がかかることがあるので、1回調べたら覚えておく
        private static List<string>? asioDevices = null;
        public static List<string> GetAsioDeviceNames()
        {
            if (asioDevices != null) return asioDevices;

            asioDevices = new List<string>();
            try
            {
                for (int i = 0; BassAsio.GetDeviceInfo(i, out AsioDeviceInfo info); i++)
                {
                    asioDevices.Add(info.Name);
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is BadImageFormatException || ex is EntryPointNotFoundException)
            {
                // bassasio.dll が無い
            }
            return asioDevices;
        }

        // DllNotFoundException の文から、どの dll が無いのか取り出す(分からなければまとめて書く)
        private static string GetDllName(DllNotFoundException ex)
        {
            foreach (string name in new[] { "bassmix", "basswasapi", "bassasio", "bass" })
            {
                if (ex.Message.Contains($"'{name}'", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains($"{name}.dll", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{name}.dll";
                }
            }
            return "bass の dll";
        }
    }
}
