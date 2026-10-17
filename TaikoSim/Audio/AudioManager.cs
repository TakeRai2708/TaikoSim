using ManagedBass;

namespace TaikoSim.Audio
{
    // 音の係(BASSの準備と片付け)
    // bass.dll が無くてもゲームは落ちずに「音なし」で動くようにしている
    public static class AudioManager
    {
        // 音が使えるか(BASSの準備に成功したか)
        public static bool IsAvailable { get; private set; } = false;

        // 今の状態(ホーム画面に表示する)
        public static string Status { get; private set; } = "まだ準備していません";

        // 遅れを小さくする設定(OpenTaiko の BASS の設定を参考)
        private const int DeviceBufferMs = 15;
        private const int UpdatePeriodMs = 5;

        // 起動時に1回呼ぶ(ウィンドウを作った後)
        public static void Init()
        {
            try
            {
                // 遅れを小さくする設定(BASS を準備する前に決める)
                //   DeviceBufferLength：スピーカーに送る前にためておく長さ(ms)。小さいほど遅れが減るけど、小さすぎると音がブツブツ切れる
                //   UpdatePeriod：ためた音を足していく間隔(ms)
                Bass.Configure(Configuration.DeviceBufferLength, DeviceBufferMs);
                Bass.Configure(Configuration.UpdatePeriod, UpdatePeriodMs);

                // -1 = いつもの出力先(Windowsで「既定」になっているスピーカー)、44100Hz
                // Latency = 遅延を測っておく(あとで使うかもしれないので)
                if (!Bass.Init(-1, 44100, DeviceInitFlags.Latency))
                {
                    Status = $"音の準備に失敗しました（{Bass.LastError}）";
                    return;
                }

                IsAvailable = true;
                Status = $"BASS {Bass.Version}";
            }
            catch (DllNotFoundException)
            {
                // exe の横に bass.dll が無い
                Status = "bass.dll が見つかりません（音は鳴りません）";
            }
            catch (BadImageFormatException)
            {
                // 32bit版の bass.dll を置いてしまったとき
                Status = "bass.dll の種類が違います（x64 フォルダの方を使ってください）";
            }
        }

        // 終了時に1回呼ぶ
        public static void Free()
        {
            if (!IsAvailable) return;
            Bass.Free();
            IsAvailable = false;
        }
    }
}