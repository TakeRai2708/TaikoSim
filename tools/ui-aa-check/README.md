# UIのAA描画確認（Linux / Mesa / EGL）

Windowsを起動せずに、実際のRaylib・OpenGLで部品を描いて画素を検証します。
画面サーバーやブラウザーは使いません。Mesa/EGLのsurfaceless OpenGLと、Linux用Raylibが必要です。
EGLはこの確認ツール専用です。ゲーム本体の依存・Windows用ビルド設定には追加していません。

先に `tools/build-check/Check.csproj` をビルドしてください。

```sh
dotnet run --project tools/ui-aa-check/UiAaCheck.csproj -- /path/to/libraylib.so /path/to/font.otf /tmp/ui-aa.png
```

第3引数の画像出力は省略できます。フォントはTTF/OTFを指定します。

単一サンプルの1920×1080描画先で、次を確認します。

- 丸・リングの内外・細いバー・三角・角丸の枠・斜め線の輪郭に中間色がある。
- 従来の丸は同じ描画先では二値の輪郭になる（対照群）。
- リングの穴・半透明色・小数の位置・座標移動・シザークリップを保持する。
- 実際のGuiのON/OFF、スライダー、ボタンと通常フォントを組み合わせて描ける。
- 1280×720へ合成しても形が残る。

GPUから読み戻した画素による18項目の検証です。Mesa llvmpipeではソフトウェア描画です。
WindowsのGPUドライバー、実際の設定画面全体、IME、入力、音声、FPS/メモリ性能、
Native AOT publishを確認した結果ではありません。
