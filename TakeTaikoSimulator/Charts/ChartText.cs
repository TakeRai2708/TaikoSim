using System.Text;

namespace TakeTaikoSimulator.Charts
{
    // 譜面まわりのテキストファイル(.tja・box.def・dan.def)を読む共通の処理
    public static class ChartText
    {
        /// <summary>
        /// 文字コードを見分けて読む。
        ///   BOM付きUTF-8 → そのまま / UTF-8として正しく読める → UTF-8 / 読めない → Shift-JIS(932)
        /// 改行は \n にそろえる。
        /// </summary>
        public static string Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);

            // BOM付きUTF-8
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Normalize(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
            }

            // UTF-8 として正しく読めればUTF-8、変な文字が出たら Shift-JIS
            try
            {
                return Normalize(new UTF8Encoding(false, true).GetString(bytes));
            }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // Shift-JIS を使えるようにする
                return Normalize(Encoding.GetEncoding(932).GetString(bytes));  // 932 = Shift-JIS
            }
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
    }
}
