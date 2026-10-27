namespace TaikoSim.Scenes
{
    // すべての画面(シーン)の共通ルール
    // 新しい画面を作るときは、これを継承して Draw などを書く
    public abstract class Scene
    {
        // この画面で設定パネルを開けるか(演奏画面では false にする)
        public virtual bool CanOpenSettings => true;

        // キー入力待ち中か(true の間はスクショキーを無視して、キーの登録を優先する)
        public virtual bool IsWaitingKey => false;

        // 画面に入ったとき1回だけ呼ばれる
        public virtual void Enter() { }

        // 画面から出るとき1回だけ呼ばれる
        public virtual void Exit() { }

        // 毎フレーム呼ばれる(入力や動きの処理)
        public virtual void Update(float dt) { }

        // 戻るキー(Esc)が押されたとき
        public virtual void OnBack() { }

        // ファイルやフォルダがウィンドウにドロップされたとき
        public virtual void OnFilesDropped(string[] paths) { }

        // 設定パネルで音量が変わったとき(今鳴っている曲に、すぐ新しい音量をかける)
        //   設定パネルを開いている間は Update が止まるので、ここで直接かけ直す
        public virtual void ApplyVolume() { }

        // 毎フレーム呼ばれる(描画)。座標は1920×1080で考える
        public abstract void Draw();
    }
}