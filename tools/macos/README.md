# TakeTaiko の macOS 版を作る

Mac（Hackintosh でもOK）で `TakeTaiko.app` を作る手順です。

## 1. 準備（最初の1回だけ）

1. **.NET 10 SDK** を入れる
   - https://dotnet.microsoft.com/download/dotnet/10.0 から macOS 用のインストーラーをダウンロードして入れる
   - Intel の Mac・Hackintosh は「x64」、Apple シリコン（M1〜M5）は「Arm64」を選ぶ
   - ターミナルで `dotnet --version` と打って、10.0.○ と出ればOK
2. **BASS の Mac 版**を用意する（音を鳴らす部品）
   - https://www.un4seen.com/ から `bass24-osx.zip` と、Add-ons の `bassmix24-osx.zip` をダウンロード
   - 中の `libbass.dylib` と `libbassmix.dylib` を、リポジトリの `TakeTaikoSimulator` フォルダ（`.csproj` がある所）に置く
   - この2つは Git に入れない（自分の Mac でだけ使う）
3. （Native AOT でビルドしたいときだけ）Xcode のコマンドラインツール
   - ターミナルで `xcode-select --install`

## 2. ビルド

リポジトリのいちばん上のフォルダで、ターミナルからこう打つ：

```
./tools/macos/build-app.sh
```

- この Mac に合わせて Intel 用か Apple シリコン用かを自動で選ぶ
- 別の種類を作りたいときは `./tools/macos/build-app.sh arm64`（Apple シリコン用）や `./tools/macos/build-app.sh x64`（Intel 用）
- Native AOT（逆コンパイル対策）でビルドするときは `AOT=1 ./tools/macos/build-app.sh`

できあがりは `work/macos/TakeTaiko.app`。

## 3. 起動

- `TakeTaiko.app` をダブルクリック
- 「開発元を確認できないため開けません」と出たら：
  1. アプリを**右クリック → 開く**
  2. 出てきた画面でもう一回「開く」
  3. それでもだめなら「システム設定 → プライバシーとセキュリティ」の下のほうにある「このまま開く」

## 4. データの場所

- 曲・設定・スキン・ログは **書類/TakeTaiko**（`~/Documents/TakeTaiko`）に作られる
- 曲は `書類/TakeTaiko/Songs` に入れる
- Simple スキンと音色は、最初の起動のときにアプリの中からコピーされる

## 5. Mac 版でできないこと（今のところ）

- 自動更新・インストーラー（Windows だけ）
- 音の出力方式は「BASS（標準）」だけ（DirectSound・WASAPI・ASIO は Windows の仕組み）
- 描画方式は OpenGL だけ（DirectX 11・Vulkan は Windows だけ）

## うまくいかないとき

- **起動してすぐ落ちる**：`書類/TakeTaiko/Logs` の一番新しいフォルダの `System/TaikoSim.log` を見る
- **`libraylib.dylib` が見つからない**：Raylib-cs のパッケージに Mac 用が入っていない可能性がある（未確認）。ログを送ってくれれば対応する
- **Hackintosh で画面がおかしい**：グラボのドライバーが公式じゃないので、ふつうの Mac と違う動きになることがある。ふつうの Mac（MacBook Air など）でも確かめる
