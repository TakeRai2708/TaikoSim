# ビルド確認（Linux・クラウドの AI 用）

TaikoSim は Windows 用（`net10.0-windows`・Raylib と BASS の dll）なので、Linux では動かせない。
NuGet につながらない環境でも **コンパイルが通るかだけ** 確かめるための道具。

```
cd tools/build-check
./setup.sh                 # 最初の1回だけ：Raylib-cs 8.1.0 と ManagedBass のソースを .deps/ に取ってくる
dotnet build Check.csproj  # TaikoSim のソースといっしょに1つのプロジェクトでコンパイルする
```

- .NET 10 SDK が要る（`dotnet --version` が 10.x）
- `stub.cs`：Linux に無い Windows フォームの `FolderBrowserDialog` の代わり（中身は空）
- `.deps/`・`.obj/`・`.bin/` は Git に入れない（`.gitignore`）
- 画面や音は確かめられない。「ビルドは通った・見た目はまだ確かめていない」と正直に伝える
- 見た目を確かめたいときは、Python（Pillow）でスキンの画像を重ねた「見本の画像」を作って、たけらいの動画のコマと並べて見比べてきた

## AOT（逆コンパイル対策）の確認
`Check.csproj` に `IsAotCompatible` を入れてあるので、ビルドすると自作コードに AOT の問題があれば警告が出る。
```
dotnet build Check.csproj 2>&1 | grep -E "warning (IL|SYSLIB)" | grep /TaikoSim/ | grep -v /.deps/
```
- 何も出なければ、自作コードは AOT 的にOK（JSON のソース生成・P/Invoke など）
- `CA1420`（by-ref・配列の P/Invoke）は、ここが `net10.0`（Windows専用TFMでない）で、実行時マーシャリングの注意。既存の WindowsIME などでも出る。AOT は対応しているので問題ない（NoWarn にしてある）
- ここでできるのはコンパイルと静的チェックまで。**AOT の publish 本体（exe 生成）は Windows が要る**ので、たけらいの PC で：
  `dotnet publish TaikoSim/TaikoSim.csproj -r win-x64 -c Release`
