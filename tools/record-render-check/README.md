# Record digit rendering check

Run after building tools/build-check/Check.csproj:

```sh
dotnet run --project tools/record-render-check/Check.csproj -- /absolute/path/to/libraylib.so
```

Uses the existing ui-aa-check EGL helper and a synthetic transparent digit atlas. Ten checks cover context setup, asymmetric transparent margins, visible-ink centering, specified height, multiple digits, width limits and reuse of the cached bounds. It invokes the real SongRecordPanel number renderer through reflection only in the test process.

Requires Linux libEGL/Mesa and Raylib. No A-Style assets, Windows game, input, audio or Native AOT publish verification. The actual skin panel/icon alignment still needs the user's Windows screenshot.
