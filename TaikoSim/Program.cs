namespace TaikoSim
{
    internal class Program
    {
        // Windowsの「フォルダの選択」画面を使うのに必要
        [STAThread]
        static void Main(string[] args)
        {
            // ゲームを起動する(中身は Game.cs)
            new Game().Run();
        }
    }
}