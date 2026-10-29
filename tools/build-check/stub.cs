// ビルド確認用(Linux に Windows フォームが無いので、使っている分だけの代わり)
namespace System.Windows.Forms
{
    public enum DialogResult { None, OK, Cancel }
    public sealed class FolderBrowserDialog : System.IDisposable
    {
        public string Description { get; set; } = "";
        public bool UseDescriptionForTitle { get; set; }
        public string InitialDirectory { get; set; } = "";
        public string SelectedPath { get; set; } = "";
        public DialogResult ShowDialog() => DialogResult.Cancel;
        public void Dispose() { }
    }
}
