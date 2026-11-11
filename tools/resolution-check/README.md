# 実解像度への描画確認

`tools/build-check/Check.csproj`をビルドしてから実行します。LinuxのMesa/EGLとLinux用Raylibが必要です。

```sh
dotnet run --project tools/resolution-check/ResolutionCheck.csproj -- /path/to/libraylib.so /path/to/font.otf
```

Windowsも画面サーバーも使わず、実際のScreen.BeginCanvas/EndCanvas・フォント・シェーダーを実行します。
内部の解像度指定入口をテストからだけ呼び、実際のウィンドウの代わりに描画先のサイズを渡します。
1920×1080、1280×720、960×540、1742×980、1200×900、720pへ戻すリサイズを確認します。

文字生成サイズ、画素への整列、描画先サイズ、論理座標の切り取り、画面外の通知への倍率リセット、
黒帯を、GPUからの画素読戻しを含む40項目で検証します。
ゲームの判定、Windowsの入力・音声・IME、実際のモニターでの可読性やNative AOT publishのテストではありません。
