# 通常UI文字のラスタライズ確認

`tools/build-check/Check.csproj` を先にビルドします。

```sh
dotnet run --project tools/font-check/FontCheck.csproj -- /path/to/font.otf /path/to/libraylib.so
```

フォントは日本語を含むTTF/OTFを指定します。TTCは先に1つのOTF/TTFへ取り出してください。
第2引数は実行OS用のRaylib共有ライブラリです。通常の探索場所にあれば省略できます。

ウィンドウは作りません。Raylibの実際のCPUフォント読み込みを使い、表示サイズ以上の
字形解像度、中間の透明度（アンチエイリアス）、測定、改行、大量の漢字のページ分割、
再利用を確認します。1024pxのページ端から文字がはみ出す場合も失敗します。
GPUへの転送・Windowsの実際の表示・IME・Native AOT publishのテストではありません。

設定・初期セットアップ・起動案内などの通常文字はCoverageFont、太い縁付きのゲーム文字は
従来のSDFです。文字サイズを大きくして粗さを隠さず、小さいビットマップの拡大も避けます。
サイズごとのページはフォントに保持し、設定画面を閉じるたびに作り直しません。
