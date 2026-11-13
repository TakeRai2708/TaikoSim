# 曲検索の文字カーソル

`dotnet run --project tools/search-input-check/SearchInputCheck.csproj`

ゲームが使う `UI/TextInputBuffer.cs` を直接コンパイルし、途中への挿入、左右・先頭・末尾移動、前後削除、クリック位置、IME確定文字列、Unicodeの結合文字・絵文字、40文字の上限を確認する。Raylibやウィンドウは不要。

実機では検索を開いて文字を入れ、←→、Home/End、入力欄のクリックで移動し、その場所で入力・Backspace・Deleteを試す。長い文字列でカーソルに横スクロールが追従すること、日本語変換中の←→は変換対象の操作になり、変換確定Enterで検索されないことも確認する。
