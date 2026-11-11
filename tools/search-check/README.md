# Search filters

After the source build, run `dotnet run --project tools/search-check/Check.csproj`.

Checks optional text/course/stars/genre conditions, normalization, same-chart course+star matching, Oni/Ura separation, genre fallback and zero-star charts. Does not open the game or test Windows IME.

Entry timing/input-lock regressions live in tools/nameplate-check; scrollable popup rendering/selection checks live in tools/ui-aa-check (Linux EGL/Raylib).
