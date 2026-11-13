# AGENTS.md — Handoff guide for AI assistants working on TakeTaikoSimulator

This file is for any AI (Claude, ChatGPT / Codex, etc.) that picks up work on TakeTaikoSimulator.
Read all of it before you start.

**Also read `KNOWN-ISSUES.txt` before working; update it when investigation or verification changes.**

**Also read `TAIKONAUTS-SPEC.md`.**
That file is a digest of the official TaikøNauts documentation (50 pages): "how the original is built".
The **correct file formats** live there: dan-dojo `dan.json`, Don-chan `Config.json`, `category.def` / `order.def`, `SONGGENREID`, etc.
This file (AGENTS.md) is where we record **values we actually measured** from videos and images for TakeTaikoSimulator.

> Language note: this file is in English so that AI agents can read it quickly.
> The user (たけらい) is Japanese. **All replies to the user must be in Japanese** (see §2).
> Code comments in the repo are written in plain, easy Japanese — keep that style when you add comments.

---

## 1. Project overview

- **TakeTaikoSimulator**: a Taiko no Tatsujin simulator. Personal use only, **private. Never publish it.**
- Author: たけらい (Japanese IT vocational-school student, Windows 11)
- Stack
  - C# / .NET 10 (`net10.0-windows`)
  - Raylib-cs 8.1.0 (rendering)
  - ManagedBass 3.1.1 (+ Mix / Wasapi / Asio) (audio)
- Charts are `.tja` (same format as OpenTaiko / TJAPlayer3)
- All screen coordinates assume **1920×1080** (`Graphics/Screen.cs` scales to the window)

### What to match (important)
1. **Behavior / rules**: OpenTaiko first, then TJAPlayer3 (OpenTaiko's source is the primary reference)
2. **Visuals**: the **A-Style** skin of TaikøNauts (an OpenTaiko fork)
   - たけらい records TaikøNauts videos and sends them. We step through them frame by frame and measure coordinates and timings
   - Measured values are in code comments and in §8 of this file

---

## 2. How to talk to たけらい and how to work (mandatory)

- **Casual Japanese like a friend** (net slang OK, plenty of emoji). Equal footing; when he jokes, do a tsukkomi
- **Never be vague. Explain in detail with concrete numbers**
- If you don't know, say 「**分からない**」 plainly. When guessing, start the sentence with 「**推測なんだけど**」
- **Don't build things right away.** First explain how you understood the request and what you plan to do; offer choices as A/B/C when a decision is needed
- If the prompt is ambiguous, ask before building
- When delivering code, give **both the diff (修正差分) and the full modified code (修正後の全コード)** (attach large files and show the diff)
- Do not use `<details>` (collapsible) tags — they show up as raw text and are hard to read
- Don't give instructions like "copy-paste one block at a time" where block boundaries are unclear
- End every reply with exactly these two lines:
  - `📋 タブ名：「（今の話題の名前）」`
  - `💾 Ctrl+Shift+S で保存してね`

---

## 3. Git rules

- Japanese commit messages; summarize what changed as bullet points
- Work on branches (e.g. `codex/xxx`; earlier ones were `claude/xxx`)
- **Merge into master only when たけらい says 「マージして」**
- **Push only when たけらい says 「上げて」** (explain first and get an OK)
  - In practice (since 10/31) たけらい has had Claude commit directly on master and push, then sync `claude/aot`:
    `git branch -f claude/aot master && git push -f origin claude/aot`
- Remote branches are deleted by たけらい in Visual Studio
- The GitHub repo moved to `https://github.com/TakeRai2708/TaikoSim.git` (pushes to the old URL still redirect)

### Branch status (as of 2026-11-05)
- **master**: everything is merged (up to the dan-dojo work of 11/5)
- **claude/aot**: kept identical to master
- All other `claude/` / `codex/` branches are merged (safe to delete)

---

## 4. Building

- Runs on たけらい's PC (Windows, Visual Studio). **The AI side usually cannot run the game and look at the screen**,
  so be honest: "it compiles, but I haven't checked the visuals"
- To compile-check on Linux:
  ```
  dotnet build TakeTaikoSimulator/TakeTaikoSimulator.csproj -p:EnableWindowsTargeting=true
  ```
  (needs NuGet for Raylib-cs and ManagedBass)
- Without NuGet access use `tools/build-check/` (see its README.md): `./setup.sh` fetches the Raylib-cs 8.1.0 and ManagedBass sources,
  then `dotnet build Check.csproj` compiles them together with the TakeTaikoSimulator sources (only the Windows Forms FolderBrowserDialog is replaced by an empty class in `stub.cs`)
- Since visuals can't be run, build mock-ups in Python (Pillow) by compositing skin images and compare them side by side with frames from たけらい's videos
- `bass.dll` etc. are committed to the repo

### Anti-decompilation: Native AOT (compile the exe to machine code so no C# IL ships inside it)
- たけらい wants the exe given to friends built with **Native AOT** (so ILSpy etc. can't read it)
- **A normal `dotnet build` does not do this.** Build the exe from Visual Studio or with:
  ```
  dotnet publish TakeTaikoSimulator/TakeTaikoSimulator.csproj -r win-x64 -c Release
  ```
  - Visual Studio needs the **"Desktop development with C++"** workload (AOT uses the C++ toolchain)
- What was changed to make AOT work (`claude/aot`):
  - **JSON**: no reflection (unavailable under AOT); uses **source generation** (`Configs/AppJsonContext.cs`).
    Reading/writing settings, history and song cache passes the per-type `AppJsonContext.Default.XXX`. When you add a type that is saved as JSON, add `[JsonSerializable(typeof(…))]` there
  - **Folder picker**: no Windows Forms (unavailable under AOT); calls Win32 directly (`Platform/FolderPicker.cs`, shell32 `SHBrowseForFolder`)
  - csproj: removed `UseWindowsForms`; added `PublishAot`, `IsAotCompatible`, `JsonSerializerIsReflectionEnabledByDefault=false`
  - `JsonDocument.Parse` (HitSounds, SkinPreview, SkinManager, PlayBackground) doesn't use reflection, so it stays
- **AOT publish can't be tested on the AI side (Linux)** (needs the Windows toolchain). With `IsAotCompatible` in `tools/build-check`
  you can statically check on Linux that our own code raises no AOT warnings (IL / SYSLIB). (CA1420 is a by-ref/array P/Invoke notice that also appears in existing code such as WindowsIME; AOT supports runtime marshalling, so it's fine)
- **Not yet verified**: whether the Raylib-cs 8.1.0 and ManagedBass NuGet packages work under AOT (たけらい must publish and check on his PC).
  If publish emits IL warnings or fails, those library types may need to be preserved with `[DynamicDependency]` etc.

### Window modes (11/8): only "framed window" or "real fullscreen", like OpenTaiko
- The self-made borderless (undecorated window exactly the monitor size) was removed. Windows seemed to treat it as a fullscreen game
  (independent flip?): Claude's computer-use screenshots of TakeTaikoSimulator stayed stale and clicks were judged to land on "the desktop". OBS window capture was fine
- OpenTaiko (Silk.NET + GLFW, rendering through ANGLE; Windows default OpenGL) also has only `WindowState.Normal` (framed) or `WindowState.Fullscreen`
- Fullscreen = `ToggleFullscreen` at the monitor's native size (no mode switch; the 1920×1080 canvas is scaled). The resolution setting applies to window mode only
- A window that doesn't fit the monitor is shrunk (keeping 16:9) and centered. `GameConfig.FullscreenType` is kept only so old config files load; the dropdown is gone
- **Also: computer use's "open application" launched a NEW TakeTaikoSimulator each time instead of focusing the running one** (several Debug instances piled up). Focus TakeTaikoSimulator by clicking it

### KeyProbe (Debug builds only, 11/8)
- `Diagnostics/KeyProbe.cs` writes `Logs/KeyProbe_<date>_<time>.txt` next to the exe: Windows key/char/focus messages seen by a WH_GETMESSAGE hook on the main thread,
  Raylib `IsKeyDown` changes for DFJK / ERUI / Enter / Esc / Space / arrows, and focus / foreground-window / fullscreen changes.
- Added because keys sent by Claude's computer use reached TaikøNauts but not TakeTaikoSimulator; the log shows where they stop

### Debug log (CSV) — for investigating slowness, input, audio drift
- Turn on via Settings "一般 → デバッグ → デバッグ記録（CSV）" or the song-select option 「デバッグ記録(CSV)」
- While on, three CSVs are written to `Logs/Debug_<date>_<time>/` (on a background thread so the game doesn't stall; the 10 newest folders are kept)
  - `frames.csv`: **one row per frame**. Frame interval, work time (CPU busy time), time per section
    (song list, scene update, scene draw, settings panel, FPS display, canvas submit, screenshot, compositing, present/wait;
    during play also background, lane, gauge, notes, effects, panel text, ending; plus "text" across everything),
    FPS, number of text draws, text pages, texture count, memory, bytes allocated this frame, GC counts, song-vs-screen drift, output latency, BASS CPU
  - `events.csv`: events. Scene changes (and their prep time), heavy frames (> 20 ms, top 3 sections),
    texture and font-page loads, song loads, skin, GC, display settings, and **every log line shown on the black console**
  - `input.csv`: input during play (same columns as the old "input diagnostics CSV")
- "重さの内訳を画面に出す" shows "average / worst frame" per second at the top right (for recording a video to share)
- Times are CPU-side. Real GPU cost shows up lumped into "canvas submit" and "present/wait"
- To add a measured section: add it to `PerfSection` in `Diagnostics/DebugLog.cs` and wrap with `DebugLog.Begin()` → `DebugLog.End(section, t)`

---

## 5. Folder layout (main parts)

```
TakeTaikoSimulator/
  Program.cs, Game.cs          … startup, main loop
  Scenes/                      … screens
    Scene.cs, SceneManager.cs, LocalSession.cs … shared scene lifecycle / local player session
    Entry/                    … EntryScene: joining, data selection, name entry, data-load animation
    ModeSelect/               … ModeSelectScene: modes and entry-to-mode intro
    SongSelect/               … SongSelectScene, LocalDifficultyScene, SongLoading
    Play/                     … PlayScene + Local, LocalPlayScene, HUD, effects, ending, character, branch, score helpers
    Training/                 … TrainingScene, TrainingTimeline, TrainingScrollMotion
    Result/                   … ResultScene
    Dan/                      … DanSelectScene/AStyle, DanPanel/AStyle, DanLoading, DanShutter, DanResultScene/AStyle
    Dressing/                 … DressingScene
    Startup/                  … LoadScene, SetupScene
    Credits/                  … CreditsScene
  UI/
    BarList.cs                 … the diagonal "bars" motion shared by mode select and song select
    MenuDraw.cs                … drawing helpers (bold text with round outline, etc.)
    MenuSkin.cs                … draws skin images (returns false if missing → code draws instead)
    PlayOptionsPanel.cs        … play options panel (bottom left of song select)
  Graphics/
    SkinManager.cs             … loads skin images
    FontManager.cs, DynamicFont.cs … Japanese fonts (missing glyphs added on demand)
    Aup2.cs                    … AviUtl2 project (.aup2) player
  Diagnostics/DebugLog.cs      … debug CSV log and timing breakdown
  Audio/                       … BASS, preview, menu sounds
  Charts/                      … .tja loading, song list, history, dan data
  Skins/TaikoSim-Simple/       … our own skin (tracked in Git)
tools/skin-gen/                … Python that generates the Simple skin's images and sounds (see §7)
tools/dan-check/               … runs the dan parser on a real dan folder (+ boundary tests)
tools/build-check/             … compile check without NuGet
```

Japanese navigation guide: [SOURCE-MAP.md](SOURCE-MAP.md). Keep scene-specific files in the matching folder above.
Shared UI/graphics/configuration stay in their existing shared folders. Folder grouping does not change the `TakeTaikoSimulator.Scenes` namespace.

---

## 6. Skin rules

- Skins live in `Skins/<skin name>/`. The folder structure is **the same as TaikøNauts**
  - `Image/02.SongSelect/` (song select), `Image/03.SongLoading/` (song loading screen, `Scenes/SongSelect/SongLoading.cs`; see "Song loading screen" in §9)
  - `Image/04.EnsoGame/` (play), `Image/05.Result/` (result)
  - `Image/01.Entry/` (mode select; this is TaikøNauts' real name. Simple still uses `Image/01.ModeSelect/`)
    - Inside: `Bar/Bar/<n>.png` (bars), `Bar/Title/<n>.png` (titles), `Bar/Overlay.png`, `Bar/Selected.png`, `Bar/Pop/<n>.png`,
      `Bar/1POnly.png`, `Background.png`, `Header.png`, `Background_Fireworks.png`.
      **The doubled `Bar/` is not a mistake** (たけらい checked the real thing)
  - `Font/default.otf` (font for song titles and menus)
  - Fonts can be chosen in the skin's `SkinConfig.json` (11/11 たけらい chose this over a settings-screen option):
    `"mainFont"` (song titles, nameplates) and `"systemFont"` (settings, notes, every other text).
    The value is a path from the skin folder (`"Font/MyFont.otf"`); a bare file name also searches `Font/` and `Fonts/`.
    Paths outside the skin folder are ignored. Missing or unreadable → the old rules (`Font/default.*`, `Assets/Fonts`)
- When an image is missing, the program draws it itself (the game must work without a skin)
- In tests, `SkinManager.SystemSkin = true` treats every image as missing

### Simple skin (`TaikoSim-Simple`)
- All images and sounds are **our own work** (generated by Python in `tools/skin-gen/`)
- Song-select images use **the same names and layouts as A-Style** (so the code can use the same positions)
  - e.g. `02.SongSelect/Difficulty_Panel.png` (tag on the opened song bar), `Difficulty_Select/Difficulty_Panel.png` (difficulty cards)
- **Never commit A-Style images** (someone else's skin). Use them only as shape references
- たけらい wants Simple to stay **simple and original**: don't copy A-Style's flashy animations, characters or text effects

### Where A-Style (TaikøNauts) images are read
- The "TaikøNauts skin" part of `SongSelectScene.cs`
- Bar number comes from `#BOXTYPE` in box.def, background from `#BGTYPE`. If absent, derived from the genre name
  (J-POP→1, Anime→3, Vocaloid→4, Children/Variety→0, Classic→7, Game Music→5, Namco Original→6, Recently played→9, Favorites→10)

---

## 7. `tools/skin-gen/` (Python that generates the Simple skin)

```
pip install pillow numpy
cd tools/skin-gen
python menu.py        # mode select and song select images
python loading.py     # song loading screen (the 5 images in 03.SongLoading)
python gogo_fire.py   # go-go fire (Lane/GoGoSoul.png)
python gogo.py        # combo sparkles (MiniTaiko/Effect.png)
python endres.py      # end-of-song effects, result
python play.py        # play screen (notes, lane, digits...; regenerates everything, careful)
python sounds.py      # sounds (needs ffmpeg)
python dan.py         # dan dojo images (20 dan plates)
```
- Output overwrites `TakeTaikoSimulator/Skins/TaikoSim-Simple/` directly
  (set `TAIKOSIM_SKIN_OUT` to write elsewhere)
- **Fonts**: originally drawn with Noto Sans CJK **Black** and **Bold**.
  If missing, `Font/default.otf` (Noto Sans CJK JP Bold) is used instead (slightly thinner).
  Put `NotoSansCJK-Black.ttc` in `tools/skin-gen/fonts/` or point `TAIKOSIM_FONT_BLACK` / `TAIKOSIM_FONT_BOLD` at the files
- **Gauge bars** (`gauge` in `play.py`) are the only exception: they reuse A-Style's image shapes and repaint the colors.
  Without the A-Style `04.EnsoGame` path in `TAIKOSIM_ASTYLE_ENSO` the gauge is not regenerated (current images are kept)
  - Rainbow.png: row 72 of each 77-px frame (bottom edge of the cells) has dark shading in A-Style; it is only painted black if the row above is also dark.
    Otherwise a jagged edge appears under the cells (fixed 11/5)
- **Hit sounds** (`sounds.py`): `loud()` raises them to A-Style's loudness — peak 50 ms RMS **don −4.0 dB / ka −8.0 dB** —
  with a tanh soft limiter (ceiling −1 dB) so they don't clip. Used for the skin's `Sound/Taiko/0.Taiko/` (`save_taiko()`)
  and for the Electro / Clap / WoodBlock hit-sound sets (`hitsounds()`). Before 11/5 Simple was 5–9 dB quieter
- To regenerate just one part, call a single function, e.g. `python -c "import menu; menu.tn_cards()"`

---

## 8. Measured values (from TaikøNauts videos; also in code comments)

### Song select (`UI/BarList.cs`)
- Bars are laid out diagonally: top-left = (495 + 40r + 10·sign(r), 478 + 135r + 120·sign(r)) (r = distance from the selected bar). Bar size 932×124
- The cursor moves one step every **0.15 s** (constant speed). Bars stay small while it moves
- After it stops: yellow frame at **0.33 s** → at **0.43 s** the selected bar opens over **0.1 s** (y 385–695) (mode select is the same)
- **The preview song starts together with the bar opening, from the beginning, at normal volume** (no fade)
- Frame of the opened bar: while idle **solid yellow (253,253,0)**, about 20 px thick (equal on all sides).
  On decide, for 0.53 s it alternates between a light color (white 56.5% over yellow = pale yellow (253,253,144)) and yellow every 0.15 s (light part 0.11 s), then white.
  (The 10/27 note saying "pale yellow while idle" was wrong — that measured the "全○曲" balloon.)
  A-Style `Bar/Selected.png`: the filled shape is at 18,22, 972×166, corner radius ≈48 px → cut exactly to that shape and 9-slice with 50 px corners
- The opened bar's decoration (`Bar/Decoration.png`) is **not stretched**. Same size as on the closed bar (938×133), attached on top of the opened bar
- Background (`Background/<n>.png`) scrolls left at **96 px/s** (no vertical motion)
- Opened genre bar:
  - Genre name: same place and size as on the closed bar (top of the bar)
  - The bright inner panel is only below the genre-name band (113 px from the top to 22 px from the bottom; fully open = y 498–673).
    When the frame appears the closed bar's inner panel disappears, and it grows downward as the bar opens (song bars use the whole bar as before)
  - Characters fade in over 0.13 s from when the frame appears
  - "全 ○ 曲" uses the balloon `Maker_Balloon.png` (870×122) at native size at (528, 291). Text centered at (435, 58) in the balloon. Appears 0.07 s after the bar is fully open
  - Characters on both sides `Box_Pop/<n>.png` (1344×486, 0–13) at native size at (288, 271) (A-Style only; Simple has none)
- Decide → frame blinks (0.53 s) → list disappears (0.75–0.92 s) → difficulty panel appears (1.25–1.58 s) → 1P marker (1.7 s)
- **Choosing a genre** (`SongSelectScene` Flip…; t = 0 at the decide moment): 0–0.53 s frame blinks → white until 0.63 s →
  from here (seconds since FlipStart = 0.63 s, re-measured on the 10/30 video): 0–0.067 the bar pops larger (×1.21 wide, ×1.14 tall) and the frame disappears / 0–0.12 genre name, characters and balloon disappear /
  0.11–0.22 bars above slide up and bars below slide down by 150 px and vanish / 0.26–0.41 the bar squashes horizontally (933→794→556→355→204→0 px) / 0.41–0.60 stays a thin line /
  0.60–0.77 widens back at nearly constant speed (empty inside) / 0.77 contents become the first song's bar (open shape) / 0.77–0.90 contents and neighbor bars fade in / yellow frame 0.33 s after 0.77
  (The 10/29 A.mp4 paused 0.13 s mid-widen and then snapped; that looked like a hitch, so use the 10/30 video)
- "とじる" (close) every 7 songs (same as OpenTaiko). The opened genre's slot contains "とじる + songs + random"
- For songs with Ura Oni, the "おに" tag on the opened song bar swaps with Ura every **1.5 s** (0.25 s crossfade)
- Difficulty panel: frame 352..1562 × 62..808, song-title box 388..1528 × 97..307
- Back, options and cards are drawn **at native image size**: back (383, 406) and options (514, 406), both 114×112;
  cards x = 647 / 862 / 1077 / 1292 (215 px apart), y = 406, 196×354 (squashing them misaligns the stars and digits)
- Difficulty card (inside the 196×354 image): star center (77.5, 263.5), number center (108, 264), star-row dots x = 30, 45, …, 165, y = 293
- Tag on the opened song bar (inside 176×86): number center (146, 43)

### Mode select (`Scenes/ModeSelect/ModeSelectScene.cs`; A-Style `Image/01.Entry/`)
- The list **does not wrap** (stops at the first and last item)
- Bars are spaced **50 px** further apart vertically than song select. When opening they spread **23 px** up and down each
- Bar images are 980×160. When open, only the base is stretched; the title (`Title/<n>.png`) is placed on top at native size
- Only indices 0 ("演奏ゲーム") and 1 ("段位道場") are used.
  A-Style's 2 and 3 are "Player settings" and "AI battle", which differ from TakeTaikoSimulator's items, so they're unused
- Fireworks (`Background_Fireworks.png`, two 300×300 frames): one cycle per 3.2 s. They swell and fade over 1.4 s
- "段位道場" shows `1POnly.png`; "演奏ゲーム" shows the same character as song select (`Bar/Pop/0.png` at (288, 271) native size)
- The "フリープレイ" at the bottom is **text only**, 48 px, centered in (730, 1012, 460, 56) (it used to be 30 px, too small)
- 段位道場 description (same as the original): 「3曲連続して演奏しながら、\nお題の合格を目指そう!\n合格すると腕前が認定されるよ!」 (34 px, line height 55)

#### Not implemented yet: entry screen (the very first "insert coin" screen)
`CreditBar.png` belongs to the **entry screen**, not mode select. Values measured for when we build it:
- `CreditBar.png` is 1172×380 with 2 frames. Top = "1人プレイ", bottom = "2人プレイ"
- Inside one frame (1172×190): bar body y21–178; baked-in text "1人プレイ" x148–380, y73–125
- Placed bottom-center at native size it fits (x = 374, y = 884; body y905–1062, shadow to y1073)
  - **y = 884 was not measured on real hardware** (no screenshot/video; just chosen so the shadow fits on screen)

### Song-select text (`Scenes/SongSelect/SongSelectScene.cs` `DrawHeader` / `DrawEmpty`)
- The top-left "演奏ゲーム" / "ジャンルをえらぶ" is **not drawn**: it overlapped skin images (removed 10/30).
  The bars themselves show which genre you're in. The top-right "F3：検索" and "○曲" stay
- When there are no songs, the folder path uses pale yellow (255, 236, 160) because light grey (`Gui.SubTextColor`) was unreadable

### Don-chan / Katsu-chan (`Image/99.Common/Chara/`; `Graphics/CharaConfig.cs`, `CharaCache.cs`, `CharaStream.cs`)
In A-Style they look like **sideways taiko drums** (Don-chan = red face, light-blue body; Katsu-chan = the reverse).
- Assets: `01.DonChan` / `02.KatsuChan`, **23 animations, 4456 frames each**. One frame is **820×599**; 435 MB total
- `Config.json` contains **all** positions, scales, loop times (ms) and motion beat counts. Keys are camelCase
  - Play: `ensoGameX [-83,-83]` `ensoGameY [-111,690]` `ensoGameNormalScale 0.81`
  - Song select: `songSelectX [-185,1300]` `songSelectY [436,436]` `songSelectLoopTime 1000` `songSelectWaitLoopTime 560`
  - Beats: `normalMotionBeat 4` `combo10MotionBeat 2` `goGoMotionMotionBeat 4` (**note the doubled "Motion" in the key name**)
  - Position arrays are [1P, 2P]. TakeTaikoSimulator is single player, so [0] is used
- **Loading them as-is doesn't fit in memory**: 1.87 MB per frame → `normal` (471 frames) alone is 883 MB; the whole play screen is 4.7 GB
- Even trimmed + scaled to 0.81 + every other frame, the 13 play animations are **627 MB of GPU memory** → too much. So (11/7):
  - `Graphics/CharaCache.cs` `CharaBaker`: after every skin (re)load, a **background thread bakes** each animation into
    `Cache/Chara/<skin>/<chara>/<anim>/<n>.png` + `<anim>.json` (trim to the union of all kept frames' alpha box, scale, keep every 2nd frame).
    First run takes about a minute; animations become usable one by one (`CharaBaker.IsReady`). Meta is written last, so an interrupted bake is redone
  - `Graphics/CharaStream.cs`: **one texture per animation**. A worker thread decodes the PNGs for the current frame and the next 6
    (plus the first 4 frames of every animation, kept permanently so one-shots start instantly); the main thread only `UpdateTexture`s (≈300 KB).
    A frame that isn't decoded yet keeps showing the previous one
  - `Image` operations (LoadImage/ImageCrop/ImageResize/ExportImage/LoadImageFromMemory) are CPU-only and run on worker threads. Textures only on the main thread
- Play screen (`Scenes/Play/PlayChara.cs`, 11/7) — port of OpenTaiko `CActImplCharacter` with TaikøNauts motion names:
  - Position: the 820×599 frame's top-left at `ensoGameX/Y[0]` = (-83, -111), scale 0.81. **Checked on video b (11/5) at 26 s: mean pixel diff 2.3** (match)
  - Drawn after the background, before the lane (the lane's top border hides the feet, as in the video). Balloon motions at (213, -8) ×0.95, drawn after the balloon
  - One loop = `motionBeat` beats at the current BPM. `balloon_breaking` (11/9, as OpenTaiko): **restarts from frame 0 on every balloon hit** (`PlayChara.OnBalloonHit`), reaches the last frame in 0.2 s and holds it (no loop). Before, it looped every 0.65 s regardless of hits (たけらい 「叩くたびに左右に動く」 → A)
  - Loop choice (every frame, counter kept): balloon → `failed_loop` (after a failed song end) → `gogo` → misses (`failed_loop` from the 6th 不可 in a row, else `miss_loop`; a 良/可 ends it) → `clear` (norma or max) → `normal`
  - One-shots (counter from 0, back to the loop at the end): every 10 combo outside GoGo `10combo` (`10combomax` when maxed),
    GoGo start `gogostart`, becoming clear `clearin`, becoming max `soulin`, balloon popped `balloon_broke` / ended unpopped (with hits) `balloon_miss`
  - **Checked on a TaikøNauts recording made by Claude via computer use** (11/8, `2026-11-08 00-39-05.mp4`, 「1 2 3 〜恋がはじまる〜」 かんたん, played by たけらい with misses;
    Don-chan frames classified by masked template matching against every animation frame, judge counts read from the on-screen counter):
    - First 不可 (even at combo 0) → `miss_loop` at once. 6th 不可 in a row → `failed_loop` (3 of 3 cases exactly the 6th). No `failed_down` in between
    - **`combocut` is the recovery motion**: the first 良/可 after misses (from `miss_loop` or `failed_loop`) plays `combocut` once (2 beats), then `normal`. 4 of 4 cases.
      Same as OpenTaiko's `Return` (not played during GoGo). (On 11/8 we first guessed "when a combo breaks" — wrong)
    - During GoGo, misses don't change the motion (`gogo` stays)
    - Gauge becoming clear and 10 combo on the same hit → `10combo` wins (OpenTaiko updates the gauge before the combo milestone). `PlayChara.OnJudge` takes the gauge state for this
    - `clearin` also plays during GoGo, then back to `gogo`
    - Balloon: `balloon_breaking` at (212, −8) ×0.95 (match within 1–2 px). `balloon_broke` at the same place, 1 beat. **The balloon image is drawn over Don-chan's mouth** → Don-chan is drawn before the balloon
    - `failed_down` was not seen (the song was cleared). TakeTaikoSimulator plays it when the song ends without clearing (dan: 不合格) — still a guess
- **In dan play Don-chan never uses the clear/max motions** (11/8, 十段 on auto `2026-11-08 03-23-19.mp4`: gauge 100 % rainbow, still `normal`; 10 combo = `10combo`). `PlayChara.Load(isDan)`
- Menu screens (`Graphics/MenuChara.cs`, 11/8) — time-based loops (`…LoopTime` ms per cycle), all checked by template matching on the 11/8 recordings (error ≈ 0):
  - Mode select and song select: `songselect_loop` (236 frames / 1000 ms) at (−185, 436) ×1, drawn behind the bars. Idle never switches to `songselect_wait_loop` (that is the 2-player "waiting" motion)
  - Dan select: `songselect_loop` at (`danSelectX/Y`) = (−190, 445) ×1, **in front of** the panel, after the intro (11/8: たけらい 「どんちゃんが隠れてる」; in the original Don's rim overlaps the panel's left frame)
  - Result: `result_loop` (117 / 500 ms) at (−70, 538) ×0.75 from the fade-in; when the balloon appears (`ClearBalloonTime`) a clear switches to `result_clear_loop` (156 / 700 ms; seen). Fail → `result_failed_in` (200 ms) → `result_failed_loop` (500 ms) (not seen yet)
  - Song decided (11/8, `2026-11-08 00-39-05.mp4` 141.53 s): `10combo` from frame 0 to source frame 66 over 360 ms, then **held** on frame 66
    (`songSelectSongStartTime` / `songSelectSongStartJumpIndex`; the held pose matched 10combo:66 with error 0.003). `MenuChara.PlayHold`, `CharaBaker.SongStartAnim`.
    10combo is baked at max(play scale, song-select scale) = 1.0. `Scene.UpdateBlocked` keeps Don moving while `SongLoading` blocks input
  - Mode decide (seen, not done): 段位道場 → Don does a flip (looks like `dan_select_in` ~70–200) about 1 s after the decide, then holds an arms-out pose; 演奏ゲーム → Don just keeps `songselect_loop`
  - Not done: `dan_select_in`, `entry_loop` (unused on mode select), dan result
  - Baked by `CharaBaker` too (`songselect_loop` first, then play, then result; `BakeScale`)
- **The Simple skin has no Don-chan** (we can't redistribute the images; with no images nothing is drawn)

### Dan dojo (`Charts/Dan.cs`, `DanJson.cs`, `DanList.cs`)
**The correct file format is fully described in "1. Dan dojo" of `TAIKONAUTS-SPEC.md`.** Only implementation notes here.
- The format **matches TaikøNauts** (`dan.def` + `dan.json`), so たけらい's existing dans work as-is.
  OpenTaiko's TJA-header style (`#NEXTSONG` + `EXAM`) is a nice-to-have (not implemented yet)
- **Only the pass/fail logic follows OpenTaiko** (red pass / gold pass, handling of "以上" (at least) / "未満" (less than))
- Pitfalls:
  - **dan.json does not say "以上" or "未満". It's decided by the condition type** (`DanExamTypes.IsLess()`).
    Only `Good` (可) and `Miss` (不可) are "未満". Get it wrong and pass/fail flips entirely
  - `threshold` is **a whole-course condition if it's a single object, a per-song condition if it's an array with one entry per song**
  - **For "未満" types, `gold: 0` means "no gold condition"** (less than 0 is impossible).
    Same for `conditionGauge` `gold:0`. たけらい's 初段 has `不可 red:36 / gold:0` and gauge `red:97 / gold:0`
  - `conditionGauge` is contradictory in the docs (table heading "pass gauge condition", body text "initial value").
    Judging from real files we treat it as **the pass border**
  - A border of 0 (e.g. 「連打数 0 以上」 = no condition for that song) shows **an empty bar with "0"**, not a full rainbow
    (`DanPlay.NoCondition`; `Progress` returns 0 and `Reach` returns Low). Confirmed on the 11/5 六段 video, both in play and on the result
- Clear records were added to `SongHistory` (`Records` = per song + difficulty, `DanRecords` = per dan.json).
  Needed for `songClear` / `songScore` / `songRank` / `danClear` in `requirements.json`.
  Keys are "path relative to the Songs folder" as before (`SongHistory.KeyFor()`)
- Screens and flow:
  - Mode select 「段位道場」 → `DanSelectScene` (if there are multiple dan.def, choose the category in a popup first)
  - Decide → `DanLoading` (dojo loading screen) → `PlayScene(DanCourse)`. **All songs are played in one PlayScene** (changed 10/31; before, each song was a new PlayScene with its own loading screen)
    - End of a song (`UpdateDanEnd` → `DanSongEnded`): if there's a next song, `DanShutter` (doors) closes; 0.8 s into the closed state `OnDanSwap` loads the next .tja and audio
    - **Soul gauge, score and combo carry over across songs.** The gauge gain per hit is based on **the total note count of all songs** (`PlayStats.Reset(course, dan.TotalNotes())`;
      note counts are stored in `DanSong.NoteCounts` by `DanList.FillSongInfo`)
    - No per-song clear / full-combo banners. At the end of the last song (or `AlreadyFailed()`) the lane shows 「不合格」 etc. (`DanEndBanner`) → fade → `DanResultScene`
    - Even when a "未満" condition is used up, that song is played to the end (agreed with たけらい; the original does the same)
  - Finally `DanResultScene` → ○/× per condition and pass/fail; on pass, the `shodanTextPaths` image
  - On pass `SongHistory.SaveDanRecord` (affects unlocking the next dan). **Not recorded when autoplayed (`DanPlay.AutoPlayed`) or quit midway**
  - Categories with `#DISABLEDCHEAT:true` disable auto play and auto roll
  - Keys (same as TaikøNauts): Esc = back to dan select (counts as fail) / **F2 or quick restart = restart from song 1** / **F3 = force end (fail, go to result)**.
    The F3 debug display is unavailable during dan
  - During dan, the norma is the dan's pass gauge (the gauge marker shows at the border; pass/fail is decided by `DanPlay`)
- `tools/dan-check` runs the parser on a real dan folder to inspect it (see its README)
  - With `--test` it also runs **boundary tests for pass/fail** (initially 28 cases from the real 初段 and 十段 data; 48 now including the ReachStatus tests. No CI — run by hand)
  - Verified on たけらい's "10 段位道場": category names, danIndex order, difficulty→CourseType, relative path resolution,
    十段's "可 and 連打 per song / 不可 whole course" detection, 以上/未満 assignment
  - **Unlock conditions are turned off** (`DanUnlock.UseRequirements = false`, 11/5): たけらい wants every dan playable at any time. Parsing/evaluation code is kept
  - **Not yet tested with real data**: `requirements.json` and `ChangeEffect` (not in たけらい's folder)
  - **`DanList` folder scanning itself is unverified** (needs Raylib, so it can't be in dan-check)

#### Original's look (measured from the 10/31 video `2026-10-31_01-44-20.mp4`; 1920×1080, 60 fps, 9 min)
**Rebuilt to this look on 10/31** (`DanSelectScene`, `DanPanel`, `DanResultScene`).
- Dan select: `Scenes/Dan/DanSelectScene.cs` (choose the plate with ←→ / ka, category with ↑↓. Decide → confirm popup)
- During play: `Scenes/Dan/DanPanel.cs`. `PlayScene` calls `DrawBottom()` (lower half) right after the background and `DrawTop()` after the title.
  The current song's stats go into `DanPlay` every frame via `PlayScene.SyncDanLive()`. The bottom-left scoreboard is hidden during dan
  - With 4+ conditions, pages of 3 switch every 4 s (unknown how the original handles it)
  - Bar colors follow **the same rules as OpenTaiko's `Dan_Cert.UpdateReachStatus`** (`DanPlay.Reach()` → `DanReach`; see the 10/31 九段 video section)
- Result: `Scenes/Dan/DanResultScene.cs`. ① per-song breakdown (3.6 s) → ② summary (gauge fills over 1 s, conditions appear 0.3 s apart, stamp)
- **This video also revealed the real position of the play-option badge (AUTO)**: left panel, right of the difficulty mark, 40×40 native, top-left (170, 353)

**Dan select screen (original)**
- **A single row of dan plates** at the top (not diagonal bars). **68 px wide, 20 plates from x = 190**, y 38–190
  - Order: 五級 四級 三級 二級 一級 / 初段 二段 三段 四段 五段 / 六段 七段 八段 九段 十段 / 玄人 名人 超人 達人 / 外伝
  - Colors: kyu = brown, 初段–五段 = teal, 六段–十段 = red, 玄人–達人 = grey, 外伝 = dark green
  - **Only the selected plate is slightly larger** (75 px wide, y 48–219). This fixed the `danIndex` order
    (**初段 = 5, 十段 = 14**, matching たけらい's dan.json)
- On the left, a **tall dan plate sign** (`danPlatePath`; e.g. 「七段 / SEVENTH DAN」)
- A **large cream panel** in the center (red frame, roughly x365–1845, y215–1030)
  - Three song rows at **y = 240 / 350 / 460 (110 px apart), 90 px tall**
    - **1st/2nd/3rd colored balls** on the left (x425–510, about 80 px; green, light blue, pink)
    - To the right: difficulty crown + ★, then the song title
  - Below: a brown 「合格条件」 tab and the condition boxes
    - Soul gauge alone on the left, the others stacked on the right
    - **Per-song conditions show 1st/2nd/3rd side by side** (colors match the balls)
    - Each condition has an **empty progress bar** below it (fills during play)
- Don-chan and the 1P name plate at the bottom left
- **Deciding opens a confirm popup 「この段位に挑戦するのか？」** (挑戦する / 挑戦しない)

**Dan play screen (original)**
- **Only the top half is the play area** (lane squeezed to y285–530). The bottom half is always the condition panel
- Top right: song title + **「1曲目 / 3曲」**
- **A 「魂ゲージ 99%以上」 label** above the soul gauge
- A small dan-only counter at the top left (良 / 可 / 不可 / 連打数)
- Condition panel (y540–1080, full width): each row = brown tab (e.g. 可の数) + coin + 「285 未満」 + large bar
  - **Dan plate at the right edge**
  - Slots for songs not yet played are shown as grey placeholders
- ★ **"未満" bars show "remaining count" and decrease** (`可の数 285未満` starts at 285 with a full bar;
  each 可 lowers the number and shrinks the bar). "以上" bars grow from 0.
  Same as OpenTaiko's `GetDisplayedAmount()` (`Range != Less ? Amount : Value[0] - Amount`). **Don't invert it**

**Dan result (original)**
- Dan plate + Don-chan + 1P name plate on the left
- Large gold-framed panel: blue score box / 良・可・不可 (left column) / 連打数・最大コンボ数・叩けた数 (right column)
- Condition bars below. **Satisfied conditions become a rainbow gradient** (pink during play)
- The soul-gauge bar has a 「99 以上」 marker and a 「魂」 flame icon at the end

#### Findings from the 10/31 九段 video (`2026-10-31_03-04-03.mp4`; たけらい failed on 不可)
- **Between songs** (`Scenes/Dan/DanShutter.cs`; door timings from OpenTaiko `Dan_Cert.cs`): doors close in 1.0 s → stay closed 2.3 s (next song swapped in at 0.8 s) →
  open in 0.27 s → title on the lane (center y388, 52 px) and artist (y447, 30 px) for 2.9 s (fades out over 0.5 s from 2.0 s)
  - The doors cover the lane x498–1920, y288–476 (the 「ドン・カッ」 strip below stays visible). Two halves
  - Simple draws its own **fusuma** (sliding paper doors: wood frame 9 px, washi paper with faint lines and two thin rails, round brass pull near the seam, 6 px wood-colored seam).
    No Don-chan face (たけらい: "no Don-chan, make it look like fusuma")
  - Sounds: A-Style `Sound/DanGame/ScreenIn.ogg` (close), `ScreenOut.ogg` (open)
  - **Time never stops** (OpenTaiko's `#NEXTSONG` model; changed 11/5). Back-calculated from the 11/5 六段 video and the .tja files:
    - Doors start closing **2.2 s after the previous song's last note** (song 2: 2.25 s, song 3: 2.23 s)
    - The next song's 0 s (start of its audio) is **10.78 s after the previous song's #END** (song 2: 10.71 s, song 3: 10.84 s). OpenTaiko uses 6.2 + 2.5 = 8.7 s
      (`PlayScene.OnDanSwap`: `nowMs = oldNow - danPrevEndMs - DanNextSongAfterEndMs`)
    - Song 1: doors start opening 1.71 s after the play screen starts fading in; the song's 0 s is at 6.69 s
    - "Seconds from doors fully open to the next song" varies per song (6.10 s, 5.48 s) — don't use that as the reference.
      This is probably why the arcade has different gaps per dan
- **Score and the top-left counter (良・可・不可・連打) carry over across songs** (end of song 1: 良1088 → song 2 continues at 1140)
- **Bar colors** (sampled pixels): pink (254,167,190) / yellow (254,252,41) / red (247,104,50) / empty (61,61,61) / rainbow (digits also look rainbow)
  - "未満": broken = empty bar, digit 0, red 「達成失敗」 in the middle / ≤ 4 remaining or < 20% = blinking red (fades to grey and back) / < 30% = red /
    otherwise gold still safe = pink, only gold broken = yellow (九段 不可: 7 left = pink, 5 = yellow, 3 and 2 = blinking red)
  - "以上": gold also met = rainbow / red met = pink (blinks whiter and faster the closer to gold) / ≥ 95% = blinking yellow / ≥ 50% = yellow / below = yellow (guess; OpenTaiko uses a red image)
  - On breaking: A-Style `Sound/DanGame/Disqualify.ogg` (fallback: OpenTaiko `Sounds/Dan/Failed.ogg`)
- Per-song conditions: small slots on the right show finished songs' tags (1st) + that song's mini bar (same colors as the large bar) + number
- The tag under the song title alternates between 「N曲目 / M曲」 and the genre (`genre` in `dan.json`) **about every 2 s**
- The left panel's difficulty mark is the 「段位道場」 mark (red cloth bundle). Code-drawn if `MiniTaiko/CourseSymbol_Dan.png` is missing
- End of the last song: on fail, a light-blue 「不合格」 on the lane (small characters on both sides; TakeTaikoSimulator shows text only) → fade → result. Pass not seen yet
- Result per-song page: songs not reached show a grey plate with 「???」 and 「未到達」
- Dan-select plates sit at **fixed places x = 190 + danIndex × 68** (20 slots; missing dans show faint plates)

#### aup2 (AviUtl2 project) playback and the dan intro (10/31)
- TaikøNauts A-Style makes its dan animations as **`Anime.aup2` (AviUtl2 project file; plain text)**.
  `Graphics/Aup2.cs` loads and draws it (supported: same as chapter 11 of `TAIKONAUTS-SPEC.md` — standard draw X/Y/center/Z-rotation/scale/opacity/blend mode,
  scale filter, group control X/Y, two-point 「直線移動」 and 「直線移動(時間制御)」)
  - (0,0) is the center of the scene. The point "image center + 中心X/Y" goes to (W/2 + X, H/2 + Y); scale and rotation pivot there
  - Image paths are sometimes absolute paths from the author's PC, so **take only the file name and load from the aup2's folder**
  - **Verified**: drawing frame 65 in Python with the same rules matched the original video (27.8 s) exactly
  - Guess: bezier y values of time-controlled moves seem stored upside down, so we use `1 - y` (unverified)
  - 透明度 (transparency) 0 = opaque. 60 fps. Blend modes 加算 (add) / 乗算 (multiply) supported
- A-Style dan images: `Image/06.DanSelect/` (select), `07.DanLoading/`, `08.DanGame/` (play), `09.DanResult/` (result),
  `Image/01.Entry/DanCategorySelect/` (Popup.png, Cursor.png). Sounds: `Sound/DanSelect/`, `DanGame/`, `DanResult/`, `Entry/DaniDojo.ogg`, `BGM/DanSelect.ogg`, `BGM/DanResult.ogg`
- **Mode select → dan select** (10/31 video 01-44-20, 17–31 s):
  - After choosing 段位道場, a balloon 「◀ category ▶」 appears over the bar (`Popup.png` 870×122 native at (525,258)); the rest is dimmed. ←→ chooses the dan.def category, don decides (`Sound/Entry/DaniDojo.ogg`)
  - Bar flashes → dark over 0.5 s → black 0.4 s → appears over 0.3 s (`SceneManager.ToDanSelectFromMode`)
  - Dan select plays `06.DanSelect/Background/Anime.aup2` (245 frames) as-is (doors → 「段」「位」「道」「場」 shrink in from 1.75× → doors open → dojo), together with `Sound/DanSelect/Intro.ogg`
  - Frame 197: `Header.png`; frames 221–263: plates appear left to right; frames 263–275: panel and plate slide in from the right. Input and BGM start after that
  - Switching dans: the old panel slides out sideways (0.1 s), the new one slides in from the other side (0.1 s)
  - Coming back from a dan: no intro; start from the dojo picture (last frame of the aup2)
  - **Simple (no aup2)**: no doors or text effects. Plates wipe in from the left (0.7 s) and the panel slides in (0.2 s), starting 0.2 s into the fade-in (≈1.1 s total; `SimpleLeadFrames`)

#### Dan loading screen (11/5; `Scenes/Dan/DanLoading.cs`)
Measured on the 11/5 六段 video (`2026-11-05_09-32-24.mp4`, 17.8–23.6 s). Replaces the song-loading curtain after 「挑戦する」
- Dan select fades out 0.47 s → black 0.6 s → dojo picture fades in 0.47 s → shown 3.2 s (longer if loading is slow; at least 0.3 s after loading) → fades out 0.53 s → play screen fades in 0.5 s (doors closed)
- A-Style: `07.DanLoading/Background.png` (1920×1300) scrolls up at **73.5 px/s** from the start of the fade-in and stops at 220 px (bottom edge). Dan plate at top right at native size: `Dan_Plate.png` 190×371 with top-left (1620, 140) (11/8: たけらい 「段プレートが小さい」; the old 150×320 box made it 0.79×)
- Simple: dark gradient, 「段位道場」 and the dan name, plus the plate. Nothing copied from the original picture
- F2 retry still uses the normal song-loading curtain (5 s of dojo picture on every retry felt too long; ask たけらい if he wants otherwise)

#### Dan select drawn with A-Style images (11/1; `Scenes/Dan/DanSelectAStyle.cs`)
Positions found by template-matching the images onto frames 34 s (初段) and 50 s (confirm) of video 01-44-20 (OpenCV matchTemplate). Skins without the images keep the code-drawn look
- Plates `MiniPlate.png` (six 80×168 colors: kyu / 初–五段 / 六–十段 / 玄人–超人 / 達人 = gold / 外伝): top-left (190 + danIndex×68 − 8, 26).
  **Empty slots also show the name, with color ×0.5** (the original's 五級–一級 and 玄人–外伝 are exactly half brightness). Names in black 39 px; first character centered 88.5 px below the plate top, 40.5 px per character (11/8 remeasured: たけらい 「文字が下すぎ」; before 36 px / 100 / 37 was 11 px too low)
  - Selected plate: 22 px lower with `Plate_Effect.png` (white edge + 29% white inside) on top. `Plate_Arrow.png` on both sides (image left edge −81 / +22 px from the plate center, +48 px from the top; right one mirrored). Alpha jumps to 100 % and fades linearly to 0 over 1 s, repeating; restarts when the cursor moves (11/8, video 01-44-20 at 33.4–34.3 s)
  - Passed dans get `Stamp.png` (six 90×76; order is a guess: (combo type − 1)×2 + 1 if gold)
- Panel `Panel.png` at (346,192) (shadow bottom-right; 合格条件 tab and frame included); side patterns `Panel_Side.png` (six 40×806) at (364,215) and (1800,215, mirrored)
- Song bars `SongBar.png` (three 1380×110) at (412, 231 + 110×row). Difficulty mark `Song_CourseSymbol.png` (five 78×52) at (529, 245 + 110×row);
  ★ level center (567, 309); title from x640, center y283, white text with black outline
- Soul gauge box `GaugeCondition_Bar.png` (434,611); condition boxes `Condition_Bar.png` (751, 608 + 132×row);
  contents `Condition_Content.png` (50 px each: coin / 1st / 2nd / 3rd / white bar): coin row (768, 654 + 132×row), white bar (775, 690 + 132×row), per-song columns 331 px apart.
  「814 以上」 right edge x1085, center y679 (soul gauge: right 735, y693). **The gold value is not shown** (the original doesn't either)
- Confirm popup `Challenge_Check/Panel_Base.png` (blue box with the question baked in) and `Panel.png` (buttons), both 1368×488 at native size at (276,296).
  Three buttons: wrench (play options), 挑戦する, 挑戦しない; cursor centers (615 / 855.5 / 1193.5, 580) (11/8: たけらい 「カーソルがずれてる」; old values were 4 px right / 2 px low).
  Cursor `Select_Effect.png` (seven 350×150: large = yellow frame / glow / white frame / white fill; small = yellow frame / glow / white frame). Appearance: dims over 0.2 s → box from 0.75× over 0.13 s → buttons 0.2 s later
- Plate area: `PlateX 145, PlateY 205, W 230, H 485`, fitted with `DanPanel.FitPlate`
- Mini plates always show the slot's standard name (五級…外伝, `DanSelectAStyle.SlotName`), not the dan.json title (11/7: 2026段位道場's 玄人/名人 titles start with 「2026段位道場」)
- No control hints and no category name at the bottom of dan select (たけらい, 11/7)

#### Dan play drawn with A-Style images (11/1; `Scenes/Dan/DanPanelAStyle.cs` and others)
Measured by overlaying images on frames of the 九段 video (03-04-03 at 20 s, 250 s, 164 s, 311 s). Rows are 159 px apart
- Lower-half background `ConditionPanel/Background.png` (0,540); row background `Base.png` (with tab) (123, 579); label text center (305, 598)
- `Context.png` (220×65: coin / 1st / 2nd / 3rd) (135, 633); 「120 未満」 right edge x478, center y664
- Gauge frame `Large_Gauge_Base.png` (two 1008×120: frame, dark inner band; per-song conditions use `Small_Gauge_Base.png` 684) at (491, 591).
  Fill at (+18,+16) from the frame: white `Large_Gauge.png` 972×86 tinted by state / rainbow `Large_Gauge_Rainbow.png` (16 frames at 30 fps; `Small_` has 8)
- Digits `Large_Gauge_Number.png` (80×100; top row white, bottom row rainbow) at (+21, −8) from the frame, 56 px apart. Broken: grey, plus `Disqualify_Text.png` at (+18,+17)
- Other songs: small frame `Mini_Gauge_Base.png` (two 254×50) at (1236, frame y+10; second +47), fill (+11,+9) 232×30, `Mini_Context.png` (−36,+5),
  digits `Mini_Gauge_Number.png` (30×38) at (+10,−1), 20 px apart; unplayed songs show `Mini_Gauge_Position.png` at (1200, same y)
- Soul gauge tag `Gauge/Condition_Plate.png` (tab and red arrow included) (1192,163); marker `Gauge/Condition_Stopper.png` at border x−21, y230
  (`PlayHUD.NormaBorderX(norma) = GaugeX + 12 + (norma/2 − 1) × CellWidth`)
- Left panel `MiniTaiko/Background.png` (0,276); 「段位道場」 mark `MiniTaiko/CourseSymbol.png` (27,351)
- Doors `Screen.png` (1422×195): fully closed at (498,288). Left and right halves slide in from outside
- Top background: `Background/0–5/Anime.aup2` layered in index order, each looping forever (`Background/Anime.aup2` is the author's preview — unused).
  **These aup2 scenes are 1920×276** (`video.width/height` in `[scene.0]`). (0,0) is that scene's center, so `Aup2` reads the size per file
- Final text `End/Text.png` (three 800×150: gold pass, red pass, fail; the gold/red order is a guess) at native size at (809,309), popping 1.3 → 1
  - **Then, if passed with no 不可 across the whole dan, the lane switches to the normal full-combo banner (any 可 → フルコンボ, none → ドンダフルコンボ) 1.0 s later** (`PlayScene.DanComboAfterSeconds`; uses `PlayEnd` and its sound; the pass text is hidden from then on; fade-out waits for `PlayEnd.IsFinished`).
    Seen on the 11/6 arcade 六段 video: 402.0 s 「合格」 → 403.0 s white flash → 「ドンダフルコンボ!!」 until ~409 s. Whether it also appears after a fail is unknown (only implemented for passes)
- Arcade 六段 video (11/6, `2026-11-06_09-10-55.mp4`; 0–80 s barely plays and later parts stall now and then, so long spans run 1–10 % long):
  - Doors start closing ~2.2 s after the last note (2.25 s and ~2.2 s) — same as TaikøNauts and TakeTaikoSimulator
  - Doors slam almost shut in ~0.1 s, then close the last gap over ~0.9 s; open in ~0.17 s (TakeTaikoSimulator keeps the OpenTaiko/TaikøNauts 1.0 s / 0.27 s; たけらい chose not to change it)
  - The next song's title is already behind the doors and visible the moment they start opening (TakeTaikoSimulator: `DanShutter` draws the title under the doors from the closed phase, 11/6)
  - Next song's 0 s after the previous #END: 8.06 s and 6.98 s (TaikøNauts 10.71/10.84 s). Not constant; maybe the arcade uses the audio length. TakeTaikoSimulator keeps 10.78 s
  - After the result, the arcade shows a separate full-screen 「六段合格」 celebration (~4.5 s). Not implemented
- **Dan plates come from the skin** (`Graphics/DanPlates.cs`, 11/5): `Image/06.DanSelect/DanPlate/{danIndex}.png` (五級 = 0 … 初段 = 5 … 六段 = 10 … 外伝 = 19),
  falling back to the dan folder's `danPlatePath` (so custom dans still show one). たけらい: a plate from the song folder shows in every skin, which felt wrong.
  - Loaded with mipmaps + Trilinear filtering (before, plates were loaded with the default point filter and looked jagged when stretched)
  - A-Style's `Dan_Plate.png` is only 190×371 but shown at 230×485. On たけらい's PC we put 2× (380×742) versions made with Real-ESRGAN
    (`realesrgan-x4plus-anime` via the `ncnn` pip package on CPU: ×4, alpha also through the model, then Lanczos down to ×2)
    **Use `num_threads=1`**: with 2 threads ncnn sometimes returned corrupted output (black blobs / glitch lines in 4 of 10 plates).
    Check each result by shrinking it back and comparing with the original into
    `TaikoNauts-latest\Skins\A-Style\Image\06.DanSelect\DanPlate\`. **Not in the repo** (someone else's art). The dan folders' `Dan_Plate.png` / dan.json are untouched
  - Simple has its own 20 plates from `tools/skin-gen/dan.py` (tier-colored board, vertical name, English below)
- **Simple now ships dan-screen images with the A-Style names, sizes and frame layouts** (`dan_screens()` in `tools/skin-gen/dan.py`, 11/5),
  so `DanSelectAStyle` / `DanPanelAStyle` draw Simple too. Only sizes and inner part positions (tabs, frames, bars) follow A-Style; the art is Simple's flat look
  (cream boards, tan tabs, dark outlines). 06.DanSelect: MiniPlate, Plate_Effect, Plate_Arrow, Stamp, Panel, Panel_Side, SongBar, Song_CourseSymbol,
  Condition_Bar, GaugeCondition_Bar, Condition_Content, Challenge_Check/{Panel_Base, Panel, Select_Effect}.
  08.DanGame: ConditionPanel/* (Background, Base, Context, Mini_Context, {Large,Small,Mini}_Gauge(_Base/_Rainbow/_Number), Disqualify_Text, Mini_Gauge_Position),
  Gauge/{Condition_Plate, Condition_Stopper}, MiniTaiko/CourseSymbol, Screen (fusuma), End/Text. Not made: 06 Header, 07.DanLoading (code-drawn), 08 MiniTaiko/Background. 09.DanResult: see below (no stamp aup2)
- Dan screens draw text with a THINNER outline: `MenuDraw.UseOutlineRatio(MenuDraw.DanOutlineRatio)` = 0.08× size (0.16 elsewhere). The lane song title between songs (DanShutter) uses `DanTitleOutlineRatio` = 0.1.
  Measured 11/5 in video b as distance from the white glyph to the outer edge of the black outline: need text 「346 未満」 ≈ 0.06× font size, bar digits ≈ 0.055×, lane title 「MATSURI D/A」 ≈ 0.095×. (An earlier 0.22 was a mismeasurement; the user said it was far too thick.)
  Simple dan images in tools/skin-gen/dan.py follow the same idea: PIL stroke ≈ 0.055–0.08× font size (Large digits size 92 stroke 5, Mini 34 stroke 2, End text 110 stroke 8).
- Code-drawn panel (no A-Style): plate `PlateX 1560, PlateY 560, W 230, H 485`; top-left counter (良・可・不可・連打数) box (28, 98) 212×128,
  labels 21 px, numbers 27 px (enlarged 11/5; たけらい said the numbers were too small)

- **Not built yet**:
  - `ChangeEffect` (between-song effect) is **parsed only**. The doors above (same as OpenTaiko) are shown instead.
    The original uses 8 millisecond parameters + `Anime.aup2` (see `TAIKONAUTS-SPEC.md`)
  - (done 11/5) Dan result with skin images — see "Dan result drawn with skin images" below

#### Dan result drawn with skin images (11/5; `Scenes/Dan/DanResultAStyle.cs`)
Measured on the 11/5 六段 video (`2026-11-05_09-32-24.mp4`, 423.9–445 s) by template-matching the `09.DanResult` images onto frames. Used when the skin has `09.DanResult/` (A-Style and Simple); otherwise `DanResultScene` code-draws as before.
t = 0 when the result fades in (fade 0.07 s).
- Background `Background.png` (0,0). Plate (same box as before) appears at 0.33 s over 0.2 s
- **Per-song breakdown**: wood `Song/Panel_Background.png` (340,88). Song rows `Song/Panel_Base.png` (1440×256) at (380, 148 + 276·i), sliding in from the right at 0.78 + 0.425·i s:
  offset = 1494·(1 − easeOutCubic(t/0.117)) + 40·clamp(1 − (t − 0.083)/0.267) (fast to ~40 px in 7 frames, then a slow 40 px creep, done at 0.35 s)
  - Inside a row: `SongStage.png` (80×80 ×3) at (+48,+47); course mark `06.DanSelect/Song_CourseSymbol.png` at (+147,+47); level number centered (+201,+110) (the ★ is in Panel_Base);
    title x +252, center y +88, 44 px white with dark outline; counts `Score_Number.png` (28×40 black digits) right-aligned at +443 / +760 / +1077 / +1395, y +166.
    Songs not reached: 「???」, no counts, `NotReached.png` on the right (position is a guess)
- **Summary** starts at 6.93 s: `Overall/Panel.png` slides in from the right (346,88) with an overshoot (keyframes in `PanelSlide`: −18 px at 0.18 s, +8 px at 0.27 s, settled at 0.37 s)
  - 0.97 s: `Overall/JudgementPanel.png` (345,92) fades in 0.23 s. Score text centered (680,222) ~92 px (font; no image matched). Stats `Score_Number` right edges 1261 / 1715, y 157 + 62·row
  - Rows `Overall/Base.png` (1400×160) at x 400: gauge y 352, conditions y 506 + 154·k (max 3 rows). Tab label center (+187,+19); `08.../Context.png` at (+20,+60)
  - Soul gauge: `Overall/Gauge_Base.png` frame 0 at (520,384). Cells/rainbow/soul reuse the play gauge images (`04.EnsoGame/Gauge/`) with the play-gauge origin at (550,385)
    (red cells = Gauge_Light (0,33,21,33) at y+45; rainbow = bottom 33 px of the play Rainbow frame; Soul_Text at +1067; Soul_Anime at (+1039,−115)).
    Fill starts 1.55 s after the summary, **40 % per second**. When done: 「100%」 `Overall/Gauge_Number.png` bottom (orange) row at (764,357), digits 34 px apart, % 47 px after the last digit.
    Border marker `08.../Gauge/Condition_Stopper.png` centered at 562 + 1050·norma/100 − 4
  - Condition rows start 1.1 s after the gauge is done; each fades in 0.2 s, counts from 0 over 1.65 s (skipped for 「未満」 with nothing used), then the number pops.
    Next row 1.15 s after the last pop. Whole-course rows: need text right +365 / center +87; `Large_Gauge_Base` at (+381,+19); digits at (+397,+7).
    Per-song rows: columns 465 px apart; bar (+120,+65) 330×50 code-drawn; digits = `Large_Gauge_Number` **rainbow row at half size (40×50), 25 px apart** (always that row in the video); numbers pop 0.7 s apart per song
  - Stamp 0.53 s after the last pop: `Stamp/{n}/Anime.aup2` played as-is (1920×1080 scene; loops from `_loopStartFrame` 99).
    n: 0 fail, 1 red, 2 gold, 3 red full combo, 4 gold full combo, 5 red donderful, 6 gold donderful (the video: red pass + donderful → 5, rainbow ring).
    Skins without the aup2 (Simple) get the code-drawn stamp centered at (196,556)
- `DanPlay.DisplayedFor / ProgressFor / ReachFor` take an amount so the counting-up bars use the same colors as play
- Simple's `09.DanResult` images come from `dan_result()` in `tools/skin-gen/dan.py` (same names, sizes and inner layout as A-Style)

### Play-option badges (`UI/OptionBadges.cs`; A-Style `Image/99.Common/Option_Icon/`)
- Images are **40×40 frames laid out horizontally**. **Frame 0 is empty (= off)**; content starts at frame 1
  - `Auto.png` 80×40 (2 frames) / `Speed.png` 600×40 (15 frames: empty / ×1.1 1.2 1.3 1.4 1.5 1.6 1.7 1.8 1.9 2.0 2.5 3.0 3.5 4.0)
  - `Abekobe` `Doron` `HiSpeedLock` `Adjust` are 80×40 (2 frames), `Random` 120×40 (3 frames),
    `PlaySpeed` 480×40 (12 frames: 0.5–0.9 / empty / 1.1–1.5 / 2.0). **TakeTaikoSimulator lacks those features, so they're unused for now**
- `Speed.png` frames 1–14 **exactly match** TakeTaikoSimulator's `HiSpeedSteps` minus `0.5` and `1.0`.
  No badge at ≤ 1.0×. Values not on a step (typed directly) use the nearest step's badge
- Position: **left panel, right of the difficulty mark** (top-left (170, 353), native 40×40; more badges go to the right).
  Measured on the 10/31 dan video (with AUTO). It used to be guessed under the song title
  - Order/direction with 2+ badges is unverified (we draw speed → AUTO to the right)
- Skins without images (Simple etc.) draw a yellow rounded tag with text (`AUTO`, `×1.5`) instead

### Favorite key
- TakeTaikoSimulator uses **F4**; TaikøNauts uses F8. **Kept at F4 at たけらい's request** (not matching the original)

### Play screen
- Judge circle center: (620, 389) for notes/judging. The judge-frame image (`Notes/JudgementFrame.png`, 195×195) is drawn with its top-left at **(522, 291)**
  (11/7: best pixel match against the TaikøNauts 六段 video at 26 s; before it was 2 px right and 1 px low)
- The score's dark rounded backing `MiniTaiko/Background_Score.png` is drawn on top of the panel in dan mode too (the dan panel `08.DanGame/MiniTaiko/Background.png` has no backing; 11/7)
- Balloon (`Notes/Normal/*/10.png`, 390×195): shape measured pixel by pixel from A-Style 0/10.png. Center y = 95.5
  - Head circle radius 52 (black 4 px outside → cream rim 9 px → orange (255,120,0)). Center (96, 95.5)
  - Neck band flat at x146–164 (height 24). Same orange as the head; 2 px black on its right edge separates it from the body
  - Body x165–297 in (255,80,0). Height: 20 at x171–184 (waist) → 42 at 244–256 (max) → 0 at 297
  - A-Style has eyes and a mouth; **Simple has no face** (shape only)
- Simple's go-go fire (11/5; たけらい asked to match A-Style's tongue shapes): A-Style's GoGoSoul tongues are thin, wave in an S 2–3 times with small lobes,
  have **sharp tips**, rise up-right from a slight hump on the ball's upper right, 2–4 per frame with big per-frame changes (flicker), and leaf-shaped
  fragments (pointed at both ends) sometimes break off. Simple follows that structure with its own drawing: nearly round ball (r 70 in the image = ~63 px on screen at 0.9×, i.e. between the judge frame's
  small circle r≈50 and big circle r≈77; slight wobble) + small hump, tongues leaning diagonally up-right (heading −62°…−34°),
  3–4 tongues per frame from a seeded RNG (seed 100+frame), occasional side branch, 0–2 leaf fragments, only a 2.2 px blur to smooth joints (tips stay sharp),
  flat light orange (255,146,60, α112). `frame()` in `tools/skin-gen/gogo_fire.py`
- Go-go fire (`Lane/GoGoSoul.png`, 480×600 per frame × 8): fireball center (243,297) placed on the judge circle at **0.9×**, additive blend.
  At start uses OpenTaiko's scale curve (0.8→2.5→…→1.0). 0.05 s per frame. **Drawn BEFORE the soul gauge, so the gauge covers it** (`PlayerEffects.DrawFireLayer`; 11/6 たけらい saw this on the arcade cabinet. It used to be drawn after the gauge)
- Combo sparkles (`MiniTaiko/Effect.png`): **from 50 combo; the stars are always pure white** (silver/gold applies to the digits).
  Digits: silver at 50–99, **gold from 100** (11/5 六段 video: 99 silver → 100 gold). Stars stay white on both.
  The code draws a copy of the star with its color forced to white (alpha kept) with additive blending, so A-Style's yellow arms no longer look gold on gold digits.
  A star with dark outline pixels (Simple: 40×40 white 4-point star with a grey edge, `gogo.py`) is drawn as-is with normal alpha blending
  (Simple's light silver digits swallowed an additive white star)
  Measured frame by frame on a TaikøNauts video (gold 133 combo). One cycle = 16 ms × 36 steps:
  - One star per digit at the same time, **center (steps 2–9) → right (10–17) → left (18–25)**. Each lasts 8 steps (0.13 s), swelling and shrinking (sin)
  - Horizontal: center = digit center, right +17 px, left −11 px (for a 66 px wide digit; scale with width)
  - Vertical: all three start 53 px above the digit bottom (for a 78 px tall digit), rising 1.05 px per step
  - **Additive blend** (A-Style stars are white in the middle with yellow arms; additive makes them glow white over bright digits)
  - Whitening + tinting was wrong (tried 10/27, looked blurry). OpenTaiko's formula verbatim is also wrong (right and left come out 12–15 px too low)
- Scene transitions (`SceneManager`): fade out → hold black → fade in, with TaikøNauts timings
  - Mode select → song select: 0.5 → 1.05 → 0.3 s (`ToSongSelectFromMode`)
  - Song select → mode select: 0.5 → 0.8 → 0.65 s (`ToModeFromSongSelect`)
  - Mode select → dan select: 0.5 → 0.4 → 0.3 s (`ToDanSelectFromMode`)
  - Result → next: the result fades itself over 0.5 s, 1.05 s black → 0.5 s fade in (`AfterResult`)
  - Boot loading → mode select: 0.3 s out → 0.5 s in (`AfterBoot`; TaikøNauts shows an IC-card screen here, so TakeTaikoSimulator chose these values)
  - Dan select → dan play: `DanLoading` (see above)
  - The current scene's Update is paused while fading out and while black
- Donderful combo: display timing, confetti position (525,162 / right side mirrored at 1367) and speed (30 frames/s) match TaikøNauts
- Text under the notes (SENotes, 口唱歌): ドン notes become ドン / ド / コ and カッ notes カッ / カ depending on the spacing to the 3 notes before and after
  (`Charts/SeNotes.cs`, a port of OpenTaiko `CTja.tSenotes_Core_V2`; decided at load time into `Note.SeKind`; per branch when the chart branches).
  Spacing is in 16th notes, computed from ms × BPM / 15000 (TakeTaikoSimulator notes have no beat position), so comparisons allow ±0.02.
  Checked against the 11/6 arcade 六段 video: 時空庁 around 140 s 「ド カッ カ ド ドン ドン カッ」 and MATSURI around 190 s 「ド カッ カ カッ カ ドン ド カ ド」 both match.
  Image rows in A-Style / Simple `SENotes.png`: 0 ドン, 1 ド, 2 コ, 3 カ, 4 カッ (OpenTaiko's own image has カッ/カ the other way round)
- Judge text above the judge circle (良・可・不可) is shown **only when the player hit the note** (`PlayScene.SetJudge`, `delta != null`).
  A note that scrolls past unhit still counts as 不可 (combo break, gauge down) but shows no text (11/6 たけらい; OpenTaiko also skips `actJudgeString.Start` for `ENoteJudge.Miss`).
  Windows: 良 ±25 ms, 可 ±75 ms, 不可 ±108 ms (hits outside 108 ms do nothing)
- Soul gauge cell lighting (`PlayHUD.DrawGauge`; measured 11/6 frame by frame on たけらい's arcade recording `2026-11-06_09-19-28.mp4`):
  - A red cell (before the border) that was just added turns orange: blend 50 % of (255,140,34) over the cell at 0.05 s (base (248,54,12) → (251,98,23)), hold to 0.12 s, linear back to red by 0.48 s.
    Only that cell; neighbours and separators don't change (median of 35 additions). Drawn with a whitened mask of the Gauge_Light red cell (darker separator pixels get less)
  - Yellow cells (border and after) do **not** flash when added
  - After clearing, the whole red section flashes yellow (peak (248,248,9)) **every 0.500 s counted from the moment of clear** (38 flashes fit 0.501 s; the song's bar was 0.983 s, so it is not beat-synced).
    Within each cycle: dark until 0.27 s → up in 0.06 s → down over 0.17 s. OpenTaiko uses 532 ms (365/448/531 ms), which TakeTaikoSimulator used before.
    The recording was only ~10–15 fps after 104 s, so the in-cycle shape is approximate; the 0.5 s period is reliable
  - No permanent glow on the tip cell (the skin glow added 10/20 was removed 11/6; `SkinManager.GetGaugeGlowCell` is now unused)
- Soul gauge rainbow: `Gauge/Rainbow.png` 24 frames (3 sets × 8 frames for norma 80/70/60%); each frame is crossfaded into the next
- BASS hit-sound lag: HitSoundLagMs = buffer + update period / 2. **Not applied to judging since 11/8** (only shown in F3). With たけらい's 40 ms buffer it made judging 42.5 ms ahead of the visuals: hitting when the note is in the frame counted as late (たけらい: 「音でたたけば取れるけど、判定枠を見ると取れない」). OpenTaiko / TaikøNauts judge on the song clock only and don't compensate hit-sound lag

---

### Don-chan voices and menu sounds (11/9, `Audio/MenuVoice.cs`)
Found by cross-correlating every A-Style `Sound/*.ogg` against the audio of TaikøNauts recordings
(`2026-11-08 00-39-05 / 03-14-18`, `2026-11-09 02-11-13 … 02-25-14`; match 0.65–1.00).
- `MenuVoice`: one voice at a time (a new voice cuts the old one), `Loop` repeats right after the end, `PlayThenLoop`, `PlayAfter(delay)`, `PlaySe` (SE volume, does not cut the voice). `Game` calls `MenuVoice.Update` every frame. Missing files → silent (Simple skin)
- Mode select: enter → `Entry/SelectMode` then **loop** the hovered item's voice (`Entry/EnsoGame` every 2.31 s, `Entry/DaniDojo` every 4.35 s, forever while idle); cursor move → switch immediately; dan category popup → stop the loop; decide → `Entry/ModeSelected`. (Before 11/9 DaniDojo was played on decide — wrong)
- Song select: enter → **loop** `SongSelect/SelectStart` (3.9 s) until the first input; genre bar hovered → `SongSelect/Genre/{BarType}.ogg` 0.95 s later (J-POP 1, アニメ 3, ボーカロイド 4; どうよう/バラエティー are 0 → silent, same in TaikøNauts), also after 「とじる」; song decided (difficulty panel) → `DecideSong` 0.5 s later; difficulty decided → `DecideDifficulty` at once; 「とじる」 → `Cancel.ogg` (SE)
- Dan select: `DanSelect/Intro` at 0 → `Plate.ogg` (SE) at 3.43 s → `DanSelectIn` voice at 4.33 s (0.5 s when returning without intro); confirm popup → `Select.ogg`; 挑戦する → `Decide.ogg`
- Dan result (A-Style only): `Intro.ogg` 0.3 s, `BGM/DanResult.ogg` loop from 1.9 s, `PanelIn` at the summary, `Gauge` at gauge start, `ConditionGaugeCountUp` at each counting row, `ClearGold`/`ClearRed`/`Failed` at the stamp (all matched within 0.1 s on the 名人 gold pass). Skipping plays only the stamp sound. TaikøNauts waits for a key before the summary; TakeTaikoSimulator still advances by itself
- Dan result, after a pass: in TaikøNauts a second Don-chan with a name plate slides up from below (rank-up / 昇格 animation). たけらい 11/9: the A-Style assets or TaikøNauts' code for it look broken → **do not copy it**
- Training pause → `EnsoGame/Pause.ogg` (TaikøNauts plays it when the pause menu opens)
- Not used yet: `Entry/DataLoadSuccess`, `SelectPlaySide`, `Join`, `Start` (entry screens TakeTaikoSimulator doesn't have), `Result/BestScore_1P` (auto-play results are not saved, so it could not be recorded), `DanSelect/Stamp`, `DanResult/SongResultPlateIn`, `BGM/SongSelect.ogg` (loops on the genre list every 4.06 s in TaikøNauts; TakeTaikoSimulator plays previews instead), `BGM/Entry.ogg`

## 9. Work in progress and what's next

### In master but not yet confirmed by たけらい on his PC
- Text uses an **SDF font** (`claude/sdf-font`). Fixes blurry small text and heavy outlines
  - Render glyphs at 96 px → compute distance to the edge → shrink to 48 px (`DynamicFont.BaseSize`) and pack into 2048×2048 pages. Distance range ±12 px (`Spread`)
  - At draw time a shader makes only ±0.5 px around the edge semi-transparent → crisp at any size
  - Outlines are just the glyph drawn fattened (outline → glyph, 2 draws; previously 25 offset draws). Corners are rounded.
    Maximum thickness `DynamicFont.MaxOutline(size)` (about 0.24× the text size with Spread 12)
  - Outlined text: `FontManager.DrawSkinMainOutlined` / `DrawOutlined` (`MenuDraw.OutlinedAt` uses them)
  - Distance computation is ~0.5 ms per glyph (one slow cloud CPU core). Use all CPU cores when adding many glyphs
- Outline thickness: `MenuDraw.OutlineRatio` = **0.16 × text size** (was 0.11; TaikøNauts subtitles have ~1.5× thicker outlines and thin outlines made white text look faint).
  For that, SDF `Spread` went 8 → 12
- Simple's song-select images were remade with the A-Style layout (digit images, ★, cards)

- Song loading screen (`Scenes/SongSelect/SongLoading.cs`; between choosing a difficulty and play; called via `SceneManager.ToPlayWithLoading`)
  - Measured frame by frame on the TaikøNauts video (10/29 A.mp4, 82.4–87.9 s):
    wait after deciding: **2.53 s when Don is shown** (11/8 remeasured: decide 141.53 → curtain 144.07 in `2026-11-08 00-39-05.mp4`; 10/29 video 80.27 → 83.03; the old 0.4 s was measured from a later event), 0.4 s for skins without Don → the curtain (left and right halves of `Background_Fade.png`) closes in 0.48 s (constant speed) →
    hint 0.15 s, Don and Katsu drop in from the outer top 0.3 s, plate widens over 0.28 s starting 0.02 s later → when fully wide the title appears instantly →
    3.83 s after the curtain closed (if loading is long, 0.3 s after it finishes) → disappears over 0.29 s (plate squashes vertically, characters fly up) → curtain opens in 0.48 s
  - The song is loaded (`PlayScene.Enter`) once the title is shown and the picture is static (a hitch during loading doesn't change the look)
  - The play screen runs from when the curtain starts opening (blocked before that by `SceneManager.IsBlocking`)
  - Plate at 0.9×, centered (960, 400). Title 62 px, white with 6 px black outline, center y = 390. Subtitle 34 px, dark grey, y = 478
  - TaikøNauts' hint image is named `Background_Light..png` (two dots). If `Background_Light.png` is missing, that one is read
  - Simple made by `tools/skin-gen/loading.py`. **No attempt to reproduce the original; minimal only** (たけらい's wish): black-to-dark-grey background and curtain (thin grey line at the gap edge), red and blue circles with thin white rims, a title plate. No sparkles, no hints

- Donderful-combo confetti (`PlayEnd.DrawConfetti`)
  - `Confetti.png` (520×368 cells, 10 across × 5 down) is **read column-wise** (0–4 = first column top to bottom). In A-Style, vertically adjacent cells continue the shape; horizontally they don't
  - Drawn **behind** the fan and text (TaikøNauts: only paper flying above the fan is visible). Cell top-left: left (560, 200), right (1332, 200) (right mirrored), from 0.85 s after the big text (frame 67)
  - Simple's `Confetti.png` was also rebuilt in column order (`confetti()` in `tools/skin-gen/endres.py`; bursts and falls)
- On choosing a song: frame turns white over 0.53 s → **disappears at 0.65 s** → list fades out 0.70–0.93 s (A.mp4 from 69.15 s, frame by frame)

- Settings screen motion (`SettingsOverlay`, `Gui`; modeled on transitions.dev, reui.io, shadcn/ui. たけらい chose: slider plan A, slide in from the right, everything included)
- 11/10 Motion Prompt (https://github.com/showheyohtaki/motion-prompt, MIT; listed in THIRD-PARTY-NOTICES) — たけらい let Claude choose, liked #054:
  - Settings explanations → **#054 tooltip**: `HelpTip(text)` puts a blue "?" ring after the previous row's label; hovering shows a #26282c bubble (0.1 s wait → 0.2 s easeOutCubic in, rises 6 px; leave: 0.1 s → 0.2 s linear out). Status lines (今の出力 etc.) stay inline with `NoteRow`
  - Settings category switch → **#057 tabs**: band slides old→new in 0.25 s easeInOutCubic; content appears after 0.1 s over 0.2 s rising 8 px
  - Setup → **#068 step indicator**: 5 circles joined by a line (done = blue + check, current = blue + number, later = ring); the blue line grows 0.3 s easeInOutCubic; the 「1 / 5」 text is gone
  - Boot (`LoadScene.cs`, class `LoadingScene`) → **#089 preloader**: logo, big %, 640×8 bar centered; % follows real progress (skin 50 % + songs 50 %); when done the whole screen lifts up in 0.55 s (`LoadingScene.DrawLift` from `SceneManager.DrawFade`) while the next scene fades in over 0.6 s
  - Open: full screen slides from the right, the F1 panel from the left, 0.3 s (ease-out quint). Close: 0.22 s (ease-in cubic). Input is ignored while moving
  - Sliding uses `Rlgl.PushMatrix` + `Translatef` (`BeginScissorMode` clipping isn't affected by Translatef, so add the offset manually)
  - Categories: the blue bar on the left glides to the new spot. Content on the right starts 18 px off and returns over 0.22 s while a background-colored overlay fades out
  - `Gui.Approach(key, target, speed)`: per-widget values ease toward a target (button color, toggle knob, slider). Keys unused for 1 s are dropped
  - Slider: thin 6 px bar + white round knob (radius 10 normal, 12 hover, 13 dragging, soft shadow). Blue glow when selected/dragging. A value bubble above while hovered. A marker at 0 for ranges with negatives

- Song-select skip (OpenTaiko `CStageSongSelect`: LeftChange/RightChange during scrolling calls `tカーソルスキップ` 7 times with `Skip.ogg`)
  - Pressing ka or ↑↓ again while the cursor is still moving (before the previous 0.15 s move finishes) jumps 7 songs. Held ↑↓ repeat and the mouse wheel don't skip
  - For **0.5 s** after a skip, ka and ↑↓ are ignored (TaikøNauts video 10/30: mashing played the skip sound exactly every 0.5 s, with no ka sound or motion in between)
  - Sound `Sound/SongSelect/Skip.ogg` (A-Style has it; Simple's is made by `skip()` in `sounds.py`; fallback is ka)
- Hit sounds (`Audio/HitSounds.cs`): `HitSounds/<name>/` next to the exe (dong.ogg or don.ogg, ka.ogg, HitSounds.json). Same layout as OpenTaiko's `Global/HitSounds`, so those can be dropped in
  - The first entry is "skin" (the skin's `Sound/Taiko/0.Taiko/`). The choice is `GameConfig.HitSound` (folder name). In the play options 「音色」, ka → plays don, then ka 0.15 s later
  - TakeTaikoSimulator's own sets (Electro / Clap / WoodBlock) are made by `hitsounds()` in `tools/skin-gen/sounds.py` into `TakeTaikoSimulator/HitSounds/` (copied next to the exe at build)
- Play options panel (`UI/PlayOptionsPanel.cs`): drawn with A-Style `02.SongSelect/Option/` images. Controls match OpenTaiko's `CActPlayOption`
  - ka / ←→ change the value; don / Enter go to the next item and close after the last (don is ignored until fully open); ↑↓ move between items; number keys type a value; Esc / F2 close
  - Speed (HS) steps match OpenTaiko (0.5, 1.0–2.0 by 0.1, 2.5–4.0 by 0.5). Auto roll: 0,5,8,10,12,15,18,20,25,30,40,50,60,80,100; accuracy in 5% steps (TakeTaikoSimulator's choice)
  - Look measured on the TaikøNauts video (10/18 `2026-10-18_00-27-01.mp4`, 50–58 s):
    panel at native size at (0, 465) (sized for 8 items; grows/shrinks 61 px per item to match TakeTaikoSimulator's item count) / top-left 1P mark 54 px (36, 28), 「演奏オプション」 32 px (105, center 55) /
    item bands from (32, 94) in the panel, 61 px apart; normally half-white overlay, selected band (254,255,0) / names white with black outline 26 px /
    values in a white box (band (178, 6)), dark text with white outline; the selected band has no box and shows ◀ value ▶ (arrows at band (186, 10) and (326, 10)) / ON/OFF are 「する」「しない」
  - Motion (same video): open = rises from the bottom at 3000 px/s, overshoots 60 px, settles in 0.07 s. Close = rises 54 px in 0.04 s, returns in 0.04 s, then drops at 3100 px/s
  - Text uses the skin's title font (`Font/default.*`) for now. TaikøNauts can apparently set a system font in `SkinConfig.json` (support it once we learn its contents)
  - Open sound `Sound/SongSelect/Option.ogg` (fallback: don). Simple's images: `option_images()` in `menu.py`
  - Items (10/30): auto play, speed, auto roll, hit sound, same note pattern every time, judge panel. Auto accuracy (良/可/不可) moved to Settings "演奏", debug log to Settings "一般"
- Oni ⇔ Ura Oni: on Oni, press right (ka / → re-press) **10 times** to switch (like the real new cabinet; たけらい's wish). Moving left resets the count; Tab switches immediately
  - OpenTaiko switches with one press (`nスイッチカウント` exists but nothing ever decrements it; unchanged since the first commit)
  - Sound `Sound/SongSelect/UraFlip.ogg` (A-Style has it; Simple's from `ura_flip()` in `sounds.py`; fallback is don)
- Skin preview in settings (`UI/SkinPreview.cs`): reads directly from the selected skin folder (visible before applying). Judge circle, don, ka, big don, big ka, roll, balloon
- Slider dragging: `Gui.BeginFrame` cancels a drag only when the mouse button is really released (the screen behind F1 also calls BeginFrame, which cancelled drags every frame)

- Dan dojo items from たけらい's 11/5 Simple-skin playthrough (all in master, **not yet checked on his PC**):
  gauge jaggies fixed, 「0 以上」 empty bar, louder hit sounds, larger top-left counter, `DanLoading`, Simple dan-select intro, fusuma doors,
  between-song timing from the .tja back-calculation
  - Still open: **text outline thickness** — たけらい said outlines differ from the original; ask which texts before changing anything

### Known issues (unconfirmed, waiting for data)
- AUDIO-001 (hits feel off): Codex's `codex/known-issues` branch has `KNOWN-ISSUES.txt`. **Not merged** — waiting for たけらい's config and CSV data.
  Main suspects with default settings: (1) ~~the judging clock is 17.5 ms ahead of the visuals~~ fixed 11/8 (HitSoundLagMs removed from judging),
  (2) in BASS mode the song position doesn't subtract device latency (WASAPI/ASIO do)

### What's next (discussed with たけらい)
- **Order (10/30): dan dojo → training mode → small fixes → online battle**

#### Planned: private room-based online battle (agreed with たけらい, 2026-11-05)

Status: wishlist / agreed user flow only. **Not implemented by this documentation change.**
Scope: a small friends-only room system, initially for two players. No public-service-scale matchmaking or account/ranking system is planned.

Confirmed flow and requirements:
- [ ] The host creates a room and receives an entry code.
- [ ] The guest enters that code to join the room.
- [ ] The room owner presses "Play" to move both players to song selection.
- [ ] The host chooses and confirms the song.
- [ ] **Start checking/transferring the song when the host confirms that song, not when a cursor merely highlights it, and not after both players finish choosing difficulties.**
- [ ] If the guest lacks the matching song/chart data, automatically download the necessary files from the host. Transfer the TJA (including its difficulties), audio, and other files needed by that song.
- [ ] Once the song is available, each player chooses their own difficulty and play options.
- [ ] Start play only after both players confirm their choices and finish loading; coordinate a shared start/countdown.
- [ ] Continue through normal play and results, adapted for the room.
- [ ] When the owner leaves the room, close that room for the guest as well and return them with an explanation.

Implementation decisions to settle when work starts (not additional agreed features):
- Room server/transport and how song files travel between the players.
- Content hashes to distinguish different files with the same title and avoid unnecessary downloads.
- Storage/reuse of received songs, progress/cancel UI, incomplete-transfer cleanup, and validation before loading.
- Disconnect/timeout handling, ready cancellation, and the post-result/replay flow.
- Keep local audio and judging independent of network delays; communicate room state/start coordination and the data needed for results.


#### Planned: full-featured mobile support and 120 Hz (2026-11-05)

Status: wishlist / requirements only. **Not implemented by this documentation change.**
Target platforms: Android and iOS.

Requirements:
- [ ] Preserve the desktop feature set rather than building a reduced mobile edition: normal play, dan dojo, skins, chart import, settings and play options, plus training and room-based online battle as those features are implemented.
- [ ] Adapt navigation and play to touch input, including simultaneous touches, while retaining each mode's behavior. Mobile layout and platform-specific controls may differ from Windows.
- [ ] Offer a 120 Hz / 120 fps display target when supported by the device and operating system. Request the appropriate display mode and verify actual refresh rate and frame pacing on real devices.
- [ ] Fall back gracefully to an available refresh rate when 120 Hz cannot be used; do not assume that a 120 Hz panel guarantees sustained 120 fps.
- [ ] Keep chart/audio clocks, judging, input timestamps and animation timing independent of rendering FPS. A higher display rate must not alter judging rules or force the 60 Hz compatibility/60-hit options.
- [ ] Keep the agreed online-room flow on mobile, including automatic song transfer on host song confirmation (never merely on highlighting a song).

Implementation work to investigate before choosing a framework:
- Separate shared game/chart/judging logic from Windows-specific input, file picking, audio and window/display code.
- Assess reusable rendering/skin support and Android/iOS audio backends; do not promise a direct build of the current Windows target.
- Design touch regions and layouts for landscape screens, display cutouts and safe areas.
- Test audio latency, multitouch, background/resume, file import, memory use and sustained high-refresh performance on real phones.
- Treat training and online battle as planned features, not already implemented desktop functionality.

#### Make rendering lighter, reduce input latency, fix the exclusive-fullscreen crash (たけらい's wish, 10/30)
たけらい suggested "let's implement DirectX and Vulkan". Asked about the goal, it was
**"run lighter and more stable", "reduce input latency", "fix crashes in fullscreen"**.
The API itself isn't the goal, so **do not jump to a different graphics API**. Follow this order.

Facts first:
- **Raylib is OpenGL-only.** The official FAQ says "built on top of OpenGL API ... there are currently no plans to
  support any other graphics APIs"
- `rlgl` only abstracts **OpenGL versions** (1.1 / 2.1 / 3.3 / 4.3 / ES2 / ES3). No bridge to other APIs
- Vulkan "could be added if really required" (implying the abstraction layer is a big job). DirectX isn't mentioned at all
- TakeTaikoSimulator's dependency: **540 `Raylib.*` calls, 35 `Rlgl.*` calls, 38 of 64 files**,
  plus **two custom GLSL shaders** (`DynamicFont` SDF font, `SkinManager` roll heat). Switching means rewriting them as HLSL / SPIR-V → **effectively a full rewrite**

Order:
1. **Measure (zero code changes)**: turn on Settings "一般 → デバッグ → デバッグ記録（CSV）", play one heavy song,
   and read `frames.csv` and `events.csv`. The numbers show whether it's drawing, GC, BASS or vsync waits.
   **Don't decide the cause before measuring**
2. **Fix the cheap things**: target FPS 0 + vsync OFF (TaikøNauts' official low-latency recommendation; already in settings),
   the exclusive-fullscreen crash (`Graphics/Screen.cs` `ApplyWindowMode`; since 11/8 fullscreen keeps the monitor's native mode — check whether the crash is gone),
   reduce per-frame allocations if GC is the cause
3. **Only if still not enough, the backend**: the top candidate is **ANGLE** (OpenGL ES → D3D11 translation layer, used by Chrome).
   Its strength is that it **may run on D3D11 with no code changes**.
   Moving to Silk.NET / Veldrid, or the community fork `rlvk` (Raylib in Vulkan), comes after that
- "Back" motions (11/8, `2026-11-08 03-14-18.mp4`):
  - Difficulty → song: the difficulty panel vanishes at once (no fade); the song bar is closed, yellow frame at 0.43 s, opens from 0.45 s (`BarList.CollapseAfterDifficulty`)
  - 「とじる」 → genre list: the same flip animation as opening a genre (timings within 0.05 s) → `flipOpen = CloseGenreNow`
  - Song select → mode select (Esc): 0.5 s out, ~0.77 s black, then in — matches `ToModeFromSongSelect`
  - Esc inside a genre still uses the old 0.18+0.18 s crossfade (not recorded)
  - Checked on TakeTaikoSimulator itself (11/8 `2026-11-08 04-40-58.mp4`): difficulty → song matches (frame/open 0.45–0.53 s after the switch)
  - 「とじる」/「ランダム」 keep the open genre's background (TakeTaikoSimulator used to switch to background 0 = rainbow). During the 「とじる」 flip the bar that widens back from the thin line is the destination genre's color (TaikøNauts showed J-POP's light blue)
- Phase 2 of A-Style song select: search, options panel, Oni→Ura transition animation (`Oni_Transition.png`), personal best, crowns
- Older leftovers: logs for the exclusive-fullscreen crash, checking skin coordinates

---

## 10. Things to watch out for

- **Text quality:** never create small bitmap text and enlarge it for settings, setup, boot loading or other UI. Keep the intended logical font size; rasterize at that size or higher, retain grayscale antialiasing and measure with the same metrics used to draw. Do not enlarge UI labels to hide poor raster quality. SDF is still appropriate for outlined game text, but it is not a guarantee that tiny stored glyphs preserve large-size detail. Check actual ink bounds and outline padding when fitting text inside plates. A whole-canvas upscale beyond 1920x1080 is separate from glyph rasterization; native high-DPI verification is still required.
- **UI curves:** round switches, slider thumbs/tracks, help rings, borders and tooltips use Graphics/SmoothShapes.cs. A larger polygon segment count or a window MSAA flag does not antialias the single-sample1920x1080 render texture. Preserve the derivative-based edge coverage, transparent ring interiors, alpha, clipping and slide transforms. Keep ordinary UI text at its intended size. Additional references and retrieval limits are in UI-REFERENCES.md.
- There are about 3000 songs. **Preload** text and images so scrolling never stalls (`FontManager.PreloadSkinMain` etc.)
- Files are UTF-8. **Keep the BOM on files that have one** (e.g. Python `utf-8-sig` when editing)
- Code comments are in Japanese, in plain, easy words (same style as the existing code)
- Never commit other people's skins or song files
- Raylib scissor mode can't be nested — re-scissor per element (see `DanShutter.Clip`)


### Training mode (updated 2026-11-09)
- Training base was merged; normal play rendering is shared. Controls are configurable. Resume plays from the preceding measure; the first measure uses the normal OFFSET/pre-roll.
- User wording is 一時停止 (not ポーズ).
- Paused navigation: 0.24s ease-out cubic, first repeat after 0.42s then every 0.30s; held 1.5s accelerates to 0.10s repeat/motion. These are implementation choices, not measured reference timings.
- Paused hit review shows hit notes and small labels immediately above them. Only actual hits are labeled; early OK is −可 and late OK is +可. History survives browsing, resets on resuming/restarting.
- Left OK row is split into early / total / late. Millisecond delta readout removed from training UI.
- Current enhancement has NOT been compiled or run: execution service fails before starting commands (invalid directory). Prior 41-test result applies to the preceding implementation only.

### Song best records (Codex, 2026-11-09)
- Normal manual play saves per TJA path + difficulty at FinishPlay, using the existing SongHistory.json Records and AOT source generation. Auto, training, aborted play and dan course runs do not enter this normal-play update.
- Score, clear state and rank improve independently. Equal/lower results never downgrade records.
- Each chart belongs to exactly one best clear category: Cleared, FullCombo or AllPerfect (DondaFullCombo). GetSummary derives mutually exclusive counts from best records; promotions replace the old category. Highest score rank is similarly counted only once.
- Difficulty selection shows score/state and global clear-state totals. Result shows a static new-best notice (no new animation).
- Three player profiles, entry screen, 2P and play-count tracking are separate subsequent work. Existing records stay in the current single SongHistory.json.
- Build/runtime are NOT verified for this change: command launch fails with invalid-directory errors before the compiler starts. Record regression checks are supplied in tools/record-check.


### Song-select record display (Codex, 2026-11-09)
- Replaced the provisional right-side text list with `UI/SongRecordPanel.cs`, using the supplied A-Style `02.SongSelect` images. Never commit those reference images.
- Measured against `2026-10-29 09-28-06.mp4` at 60/70/78 s: summary sheet at (16,306), 340x200, five rows in `ScorePanel.png`; rank-count centers (110/204/298,388/428), highest rank (300,348), crown counts at y=470. Oni and Ura share the sheet's final row and combine both independent chart records. Other difficulties have separate totals.
- Best-score panel is left above Don, compact while browsing. It shows the saved score and difficulty/rank symbol; detailed good/ok/bad/roll breakdown is not stored or fabricated here.
- Difficulty-card records use `Difficulty_Select/Crown.png` (4 horizontal cells, first blank; clear/FC/DFC) and `ScoreRank.png` (7 horizontal cells); top-left/top-right placements. Native gray empty crowns are already drawn in A-Style's card image.
- Summary caches invalidate after saving. Highest crown/rank categories remain mutually exclusive. No result-animation changes.
- Validation: Linux source compile succeeded (0 errors, 253 existing dependency/platform warnings), and 16 record checks passed, including per-difficulty totals and cache invalidation after promotion. Windows rendering/audio and Native AOT publish still need the user's PC.

### Entry / three record profiles (Codex, 2026-11-10)
- `Scenes/Entry/EntryScene.cs` is now the destination after boot/setup: 1-player start, then choose one of three data slots with left/right ka or arrow keys; don/Enter confirms. The list stops at its ends. Esc closes data selection, then exits from the start screen.
- Reference inspected: `TaikoNauts_エントリー画面_1109.mp4` (57.03 s). At 25 s the dimmed festival background has the yellow data-selection panel and one centered red profile card with arrows. Use A-Style `01.Entry/Background.png`, `Header.png`, `CreditBar.png` (2 rows), `DataLoad_Panel.png` (2 rows), `DataLoad_Text.png` (3 rows), `PlayerData_Panel.png`, `Cursor_Left/Right.png`. Reference assets remain local; never commit them.
- Data-panel destination (248,292,1423,490), profile-card destination (563,461,794,288); summary uses the existing selection panel at (962,503). These were composed against the video frame; game rendering is not verified. The attract/title jukebox, network communication, play-side assignment and simultaneous 2P are not implemented by this change.
- Slot 1 keeps `Config/SongHistory.json` in place (no migration or overwrite). Slots 2/3 use `Config/PlayerData/2|3/SongHistory.json`; they are written when records/favorites/recent changes are saved. Best score, crown/rank, dan records, recent and favorites are isolated; display names currently default to プレイヤー1–3. Shared game/input settings remain shared. No play-count feature was added.
- SelectProfile validates/reads before changing the active slot, then invalidates all summary caches. Preview reads three slots once on entry and never changes the active player. Unreadable data blocks confirmation instead of overwriting the file. Song-select lists reset on profile selection so another player's favorites do not remain visible.
- Mode select shows the active profile and a change button (F5 also returns to the data selector). Restart always starts through entry; slot selection is not silently bypassed.
- Linux source compile: 0 errors, 253 existing dependency/platform warnings. All 25 record checks passed, including profile isolation, preview, unreadable-data rejection, favorites/recent reload and legacy slot-1 preservation. Windows visual/input/audio verification and Native AOT publish remain pending.


## Local two-player test (11/10)

- User-approved entry flow: the first Don on either physical side joins that side. A first Don on the other side joins it without advancing. A second Don from an already joined side fixes the player count, plays a 0.5s fade, then opens profile selection for each participant in physical-side order. After all profiles are confirmed, go to mode select. Never move profile selection before count confirmation. Reject using the same profile twice.
- `LocalSession` separates physical 1P/2P from the three history slots; the first joining side controls shared menus. 2P-only solo entry is supported. Existing drum enum values 0–3 remain unchanged; new 2P keys are 4–7. User-set defaults are left Ka=C, left Don=V, right Don=N, right Ka=M. Missing entries in older files get these defaults, but existing 1P keys take priority if they overlap. F1 settings exposes all eight key parts. First-run setup still requires only 1P's four parts, but displays/editable key rows for both sides in two columns. Clicking Change starts replacement; Esc cancels without deleting the old key. Completion also shows both sides. Deliberately unassigned 2P parts do not force startup setup; keys cannot overlap across sides or system actions.
- Normal song selection stays shared. With two participants, `LocalDifficultyScene` provides independent difficulty/readiness and the existing six-item play-options panel for each side. Left/right Ka selects, Don confirms. Tab changes the arrow/Enter/F2 keyboard focus. Only one options panel opens at a time; both players must confirm difficulty before starting. Opening options or moving a cursor cancels that side's readiness. Esc first closes options, then cancels readiness, then returns to shared song selection.
- Two-player options last for the entry session and are not written over shared `GameConfig.json`. `LocalSession.WithOptions` temporarily applies only the six per-player play options and restores them in `finally`; common offsets and other settings remain live. This is synchronous main-thread scope, never await or run background work inside it.
- `LocalPlayScene` owns two ordinary `PlayScene` engines. Only physical 1P's engine loads music, and the other reads its synchronized clock. Both use the same input timestamp anchor; Raylib's consumable key queue is read once and fanned out, with each engine filtering its own bindings. Judging, branches, rolls/balloons, score, mini taiko and effects are reused. Quick restart restarts both engines at the earliest first bar minus 3s. A finished side waits while the other continues; music stops only when both finish or the whole scene exits. Esc abort does not save results.
- This is deliberately a **test layout**: two complete ordinary play screens scaled uniformly to 960×540, side by side. Background scissors are translated/scaled to match each viewport. No mirrored text or reference-perfect two-lane A-Style layout is claimed. Combined results are system text for now. On completion, each non-auto result updates only its selected profile; auto does not update score/crown. Recent songs are recorded separately. 2P dan and training are blocked in mode selection; existing solo modes remain available.
- `tools/record-check` now also checks entry order, 2P-only participation, simultaneous joining, scoped/nested options, exception restoration, live common offsets, old key-file migration, key conflicts and JSON round trips. Native game rendering, physical keyboard rollover, shared music synchronization/audio, 2P effects and Windows Native AOT publishing still need Windows verification. Do not describe the source checks as successful gameplay testing.
- Current validation: Linux source compile 0 errors / 253 existing warnings; **48 checks passed**. Windows manual checks are in `tools/local-player-check/README.md` (not executed here). The earlier 11/9 entry-only notes above describe the historical single-player implementation and are superseded by this section.

### Difficulty-group display and 2P menu placement (11/10 follow-up)

- Settings > General > Record display now selects **Easy / Normal / Hard / Oni only / Oni + Ura** (`GameConfig.RecordGroup`, default combined for old files). This changes the summary display; existing per-file/per-course records stay separate. The five groups use rows 0–4 of `ScorePanel.png`. Difficulty-card hover does not change this selected summary group. While browsing, the best-score panel follows the configured group (combined defaults to the last Oni/Ura choice); hovering an actual difficulty shows that course's best. If a song lacks the preferred course, best-score display falls back to its hardest available course and shows that course symbol.
- `SongHistory.GetSummary(RecordGroup, profile)` caches by profile and group. Profile reads for non-active-side rendering are cached, never call SelectProfile from Draw. Selecting/reloading a profile or saving a score invalidates that profile's group summaries. Existing CourseType summary overload remains for compatibility with older callers; menu display uses the explicit RecordGroup overload.
- Normal selection and the 2P difficulty scene show participating sides' own summary and best-score panels. 1P stays left; 2P panel coordinates shift +1548px (the panel rectangles land symmetrically at the right edge), keeping all text and digits upright. Two-player difficulty columns moved inward to leave the side panels clear; each course row also shows that profile's crown/rank.
- Mode select, song select and 2P difficulty select draw only joined sides' menu characters. Use separate 2P positions in skin Config.json when supplied; otherwise mirror the 1P position using the original animation image width. 2P artwork faces inward. Mirroring uses SourceWidth and trim offsets, rather than just flipping the cropped cell. The same animation stream is shared by both draws.
- Fixed first save to a new data slot: create the parent of that slot's history file before the temporary file write. The previous generic Config-only directory creation was insufficient for PlayerData/2 or /3.
- Linux source compile: 0 errors / 253 existing warnings. **64 checks pass**, including all five groups, Oni-only vs combined, profile-independent reads, cache refresh after save, initial save without a profile directory, and settings JSON round trips. Windows rendering/input/audio and Native AOT publish remain unverified here.

### Entry/mode and song-select BGM (11/10)

- `Audio/MenuBgm.cs` owns one menu stream. `Scene.MenuBgmFile/Muted/LoopMs` declares what each scene needs; Game updates it after scene input every frame, including settings and blocked transitions, then shuts it down before freeing audio. EntryScene and ModeSelectScene both request `Sound/BGM/Entry.ogg`, so the same stream/position continues across their transition. No per-scene Exit disposes this shared BGM. Other scenes default to no shared menu BGM so it does not overlap their music.
- Waveform evidence from `TaikoNauts_エントリー画面_1109.mp4` and the previously supplied `BGM.zip`: video 26s matches Entry.ogg 9.0028s, 47s matches 29.9827s, 53s (mode select) matches 35.9828s, 55s matches 37.9828s. Mono normalized correlation is ~0.98 without voice; stereo difference (L-R) suppresses centered voice and gives 0.907 at 53s and 0.930 at 55s. This identifies continuous Entry BGM, not a new start at mode select. Analysis: 8kHz, 150–3300Hz bandpass, 2s windows. Do not describe correlation as a percentage confidence score.
- Song selection requests `Sound/BGM/SongSelect.ogg`. On genres and Back/Close rows it plays; selecting the difficulty panel's Back button explicitly removes the preview request. Returning to a difficulty resumes its preview. While a preview is requested/loading or playing, stop the BGM (keep its stream loaded); on returning to BGM restart it at zero. A failed preview load releases this suppression so it does not leave permanent silence.
- The existing recorded reference cadence for SongSelect.ogg is ~4.06s (§8 voices). The supplied audio is ~3.079s long, so the shared player leaves the remaining gap rather than continuously looping the short sound. Entry uses the stream's native full-track loop. No new video cadence measurement was possible in this follow-up; 4.06s is retained from the earlier handoff measurement.
- Two-player difficulty selection now has its own song preview and the same BGM fallback; entering it or starting play must not briefly resume the menu BGM. Starting play stops/disposes the preview; shared BGM remains suppressed throughout SongLoading. Volume uses Master × BGM, previews use Master × Music, and voice/SE remain separate.
- Skin or audio generation changes cause a reload. Never dispose a handle from the old audio generation after output restart, as its ID may have been reused. Missing music is silent and attempted only once per file/skin/audio generation. A-Style audio assets remain private/local and are not committed; skins without these files work silently.
- `BGM.zip` actually includes BGM plus most of the Sound tree. In that supplied copy `EnsoGame/Combo/2P/` is empty; do not request nonexistent separate 2P combo voices without checking that first. The older §8 "BGM Entry/SongSelect not used" statement is superseded by this section.
- Linux source compile 0 errors / 253 existing warnings. **82 checks passed**: actual BGM controller exercised with fake streams for continuity, source changes, stop/restart on Back, loop cadence, live volume, generation invalidation, no assets/retry loops, plus preview loading/cancel/failure state. Real Windows playback, mixing balance, native looping and device switching remain unverified.

### Back/cancel sound consistency (11/10)

- `MenuSounds.Cancel()` loads `Sound/Cancel.ogg` and uses SE volume. A skin without it falls back to the ordinary Ka sound. It follows the same skin/audio-generation reload and safe disposal as the other menu effects.
- Song-list Close confirmation keeps its existing Don plus cancel effect. Esc from the song list to genres, genres to mode select, and difficulty Back/ESC now use the same cancel effect (difficulty previously played only Ka). Both button confirmation and Esc route through CloseDifficulty so the cancel effect is not doubled.
- Two-player difficulty Esc now plays cancel when clearing readiness or returning to song select; closing the options popup retains its separate behavior. No new sounds were added to menu music or preview cursor movement.
- Compile check and the existing 82 checks cover source/integration regressions; native audio playback of this change still needs Windows verification.

### Play-options opening click (11/10)

- Fixed the panel opening and immediately closing from one mouse click. SongSelectScene and LocalDifficultyScene may call Open while drawing a clicked options row, then call PlayOptionsPanel.Draw later in the same frame. Raylib's IsMouseButtonPressed remains true, and the opening panel is still below the screen, so its old outside-click handler called Close immediately.
- Mouse interaction in PlayOptionsPanel.Draw now requires Opened, matching the existing Don gate. The initial click and clicks while the panel rises/settles cannot edit values or trigger outside-close. Once settled, controls and outside-click close work normally; Esc/F2 close is unchanged. This applies to both normal and 2P options panels. Native mouse verification remains pending.


### Branch integration (2026-11-10, user explicitly requested master push)
- Checked all 20 remote branches against master 8cdae106. The only unmerged heads were codex/local-two-player-test (cbd057c5), codex/training-review-scroll (438b0205), and codex/known-issues (ff442a29). All three are integrated with their history preserved; all other branch heads were already ancestors of master or identical. This supersedes the historical branch-status section above.
- Preserved latest master's settings tooltips/category motion, setup stepper and boot preloader. Source changes merged automatically; resolved AGENTS conflicts by keeping the latest English guide and all newer handoff sections.
- Integrated source build: 0 errors / 253 existing warnings. Record/local-player/BGM checks: 82 passed. Training checks: 50 passed. These are compile/headless checks, not native gameplay testing.
- The training enhancement's earlier no-build note is historical: it now compiles and its scroll/hold checks pass. Windows visuals, keyboard rollover and audio remain unverified.
- KNOWN-ISSUES.txt contains historical audio hypotheses from 2026-11-05; they are not newly verified findings against today's sources. Its TEST-001 records the existing dan requirement-test failure, reproduced before these merges and after the rename.

### Official product rename (2026-11-10)
- Official app/window/assembly/root namespace: TakeTaikoSimulator. Open TakeTaikoSimulator.slnx; source/project: TakeTaikoSimulator/TakeTaikoSimulator.csproj; executable: TakeTaikoSimulator.exe. All source/script/project references follow this path. No GitHub Actions workflows existed in the repository at the time of this change.
- Keep BaseOutputPath pointing to legacy TaikoSim/bin/. Runtime data is relative to AppContext.BaseDirectory, so this preserves existing output-folder data when configuration/RID match. Do not rename Config files, profile paths, cache formats, TaikoSim-Simple, TAIKOSIM_* variables, or TaikoSim_ log/screenshot prefixes just to remove legacy branding. Updating a distribution must retain its existing data folders.
- Renaming itself does not change gameplay/audio/judging; earlier feature changes belong to the separate integration commit. Only the long settings-sidebar product label is fitted within its original width. External DLL names, packages and third-party license bodies are unchanged.
- Production Windows-target win-x64 build passed with the original PublishAot/IsAotCompatible=true and JSON reflection disabled: 0 errors, one existing CS0162 in DanUnlock. A managed PE apphost is produced with the new name. This is NOT a Native AOT publish or a Windows launch test. Those require the user's Windows machine.
- Before/after source builds and 82 record/local-player/BGM + 50 training checks passed. tools/rename-check checks 22 generated-JSON/path/assembly/type compatibility cases. Existing dan --requirements-test fails identically on original master and renamed sources; do not claim every repository test passes.
- GitHub repository rename is still pending: the connected GitHub tools expose no rename endpoint, and the cloud browser is signed out (private Settings inaccessible). Actual repo URL remains https://github.com/TakeRai2708/TaikoSim . An administrator can rename it to TakeTaikoSimulator in Settings > General > Repository name. Keep it private. Do not claim the rename already happened or replace working links with a nonexistent URL.


### Name plates and dressing (2026-11-10, codex/nameplates-and-dressing)
- User requested: ask for a name when first using a data slot; allow config editing; treat rank/isRankGold/rankType as dan state with no manual in-game editor; update on passing dan; add mode-select Dressing for Don-chan, PuchiChara and name-plate choice.
- New Config/PlayerData/1|2|3/NamePlateConfig.json and PlayerConfig.json. Score/history locations are unchanged, especially slot 1 at Config/SongHistory.json. Name plate uses exact camelCase properties, AOT AppJsonContext source generation and extension data to retain unknown properties. PlayerConfig retains legacy numeric choices and adds stable folder IDs. No external TaikøNauts binary-profile migration.
- Entry asks for a name only when the name config is absent, after player count is fixed and that side selects a slot. Existing history is retained. Name input has a 24-grapheme limit, IME composition and an Enter guard. This needs real Windows IME testing. Reenter data selection/restart to reload external JSON edits. Malformed config is not overwritten automatically.
- Config rankType: silver 0 / gold 1 / rainbow 2, confirmed against the upstream ConfigEditor field definitions; isRankGold is separately gold dan completion. A valid non-auto, non-quit passed dan updates the selected profile: higher DanIndex replaces rank/state; equal rank improves flags/type; lower rank never downgrades. FullCombo => 1, AllPerfect => 2. Numeric 6段 and kanji 六段 compare equally. Gaiden (index19) has no ordering and does not replace ordinary rank. Names, titles and plate choice survive promotion. No new rank-up animation.
- Rendering uses Image/99.Common/NamePlate/NamePlate_Base.png (338x560), NamePlate_Rank_Gold.png, Plates/<id>/Base.png or Anime.aup2, plus PlateConfig adjustX/Y. Static text + existing Aup2 renderer, system fallback if missing. Both physical sides display their own selected history-slot identity; text remains upright. Current placements: menus y900, play (0,450)x0.96 within each existing viewport, normal/dan results and dan select (16,950). These are initial integration placements, NOT all measured against reference screens. Rank text treatment still needs native visual comparison.
- Dressing is full screen with 3 categories, participating-side buttons/Tab, bounded list navigation and a visible scrollbar. Row selection is draft preview; Apply/Don/Enter persists. Back with changes opens save/discard confirmation with top-left X, Esc dismisses only that confirmation. No rank editing controls. A save error remains on screen and can be retried; separate config files/profiles are saved sequentially, so a failure may leave an explicitly reported partial save.
- CharaBaker now resolves each profile's selected character, shares identical source/cache jobs, and checks a per-worker CancellationToken so restarting cannot revive the old cancelled job. Metadata/stream format is unchanged. MenuChara has separate streams for different participants, and local PlayScene.Load captures the side's Config/Cache via the existing LocalSession options scope. Applying plate-only changes does not restart the baker. Native multi-character rendering and memory performance are not verified.
- PuchiChara supports both documented folder roots, horizontal Chara.png and charaImageCount/charaSizeWidth/Height, per-scene positions/scales. Basic 4fps loop is a provisional implementation, NOT reference-matched motion. The supplied 09.zip has no PuchiChara assets; runtime Puchi appearance, balloon-specific positioning and animation timing remain unverified. Do not claim them complete/reference matched.
- Assets were found autonomously in the previously supplied private 09.zip (279 NamePlate files, 2 full Don/Katsu folders) and TaikoNauts.zip (one 147.05-second, 1920x1080/60fps play video). Only name-plate assets were extracted locally. They are never committed. The user's Windows Documents/taikonauts-latest is inaccessible from this Linux task; do not claim it was read.
- Validation guide: tools/nameplate-check/README.md. 47 new profile/JSON/selection/promotion/character-assignment checks, existing 82 record/local-session/BGM and 50 training checks passed. Windows-target build keeps production AOT flags and JSON reflection disabled; Linux source compile also passes. Actual Windows launch, IME, visuals/audio, Native AOT publish and real Puchi assets remain pending. The existing dan requirements-test failure remains outside this change.
- This new feature branch is for review; do not merge it into master until the user requests that merge. Earlier merge authorization covered the previous rename/integration task.


### Entry confirmation bounds fix (2026-11-10)
- User reported an unhandled IndexOutOfRangeException after the name-plate feature. Found and reproduced the entry-confirmation path without native rendering: Decide pre-incremented dataPlayerIndex to LocalSession.Count after the last participant, while the old EntryScene still draws during the fade. The new NamePlate.Draw read Players[dataPlayerIndex] without the title's clamp. Solo 1P also fails.
- Fixed the state transition: advance dataPlayerIndex only if another participant remains. Keep the last valid participant while leaving/fading. Do not suppress the exception globally or change history/settings.
- tools/nameplate-check now invokes the actual private Decide via reflection in the test process only; covers 1P-only, 2P-only, both joining orders, profile assignment, valid fade-time plate participant, and Esc while leaving. It does not start the game or call native Draw. The regression reproduces IndexOutOfRangeException on b642d0a before the fix.
- Name-plate/profile/cosmetic checks increase from 47 to 67 (20 entry-flow assertions). Windows visual verification remains pending.


### Entry appearance and explicit confirmation (2026-11-10)
- User clarified: moving the data cursor must only select; only a subsequent Don/Enter/Decide begins name entry for a missing name config. Existing code already opened the name input in Decide, but now navigation consumes the frame and a selectionChanged guard also blocks same-frame mouse confirmation. Added an uncreated-data preview caption, without creating files or changing the active profile on browsing.
- Compared the actual previously supplied TaikoNauts_エントリー画面_1109.mp4: 20s = native two CreditBar rows, 24s = data panel/card and guide. Matched the static left portion of CreditBar at x380,y404 with scale1; second row is y594. Source dimensions1172x380, two190px rows. Weighted normalized correlation ~0.9992 (not a probability/confidence percentage). PlayerData_Panel static border matches x563,y461 (~0.9997); DataLoad_Panel corner matches x249,y295 (~0.9916).
- Replaced the provisional red/blue participation blocks with those skin bars and contextual participation text. Physical Don membership/count confirmation remains the user-approved flow, preceding data choice. Clicking a count bar joins on first click and confirms on a second click. One mouse event cannot operate both rows. Do not add the reference's later play-side chooser: it conflicts with the agreed physical Don assignment.
- Data panel uses yellow/green cells for physical1P/2P. Data card/summary use existing native layout. Name plate moves to (620,626); slot number and registered/new captions occupy the top-left. No fabricated last-play timestamp: current history does not store a true last-play time.
- Small control-guide Aup2 at (772.5,806), scale0.75, with the existing choose/confirm labels. Only its right-hand confirm hitbox calls Decide; arrows only move. Missing skin/guide uses drawn fallback. Guide images preload on entry/skin generation and unload on Exit.
- Footer uses native-style Free Play. Header stays above data-screen dimming; name input is a navy dialog over the entry background. Entry fireworks reuse the existing mode-select formula; timing is not reference-verified. No attract/title jukebox, network/IC service, timer behavior or new play-side-selection feature was implemented.
- Tests: 79 nameplate/profile/cosmetic/rank/entry checks, including browsing without creation, same-frame confirm suppression, delayed explicit confirmation, mouse count selection and transition to data selection. Windows visual/IME verification still pending. All reference PNGs/video remain outside the repo.
- User subsequently requested removing the bottom-left entry keyboard-help footer (F1/settings, Esc/back). Do not restore that text; the key handlers, Free Play and the native choose/confirm guide remain.

### Plate alignment, sharp UI text and earned rank choices (2026-11-10)
- Latest user clarification supersedes the earlier config-only rank editor rule: Dressing may select previously earned dan ranks/states. Arbitrary rank strings/gold/colors remain available only by directly editing NamePlateConfig.json. Config values do NOT grant earned choices. Manual dan passes still update the equipped rank through the existing promotion rules.
- Dressing adds a Name/Rank category. Name input starts from the current name, retains the 24-grapheme/IME Enter guard, and edits a draft. Rank choices are bounded by that profile's actual DanRecords: red/gold completion and silver/gold/rainbow through the earned maximum, plus no rank. Name/rank/plate/character changes are persisted only by Apply (or save-and-return); unsaved-back confirmation includes these edits. Titles and unknown config fields are retained.
- New successful DanRecords store DanIndex (default -1 for old records), so choices survive removal/movement of the course. Old records resolve danIndex from their existing dan.json only; missing/invalid old course files do not invent an earned rank. Profiles are queried without switching the active history. Failed, auto and aborted courses remain excluded. Gaiden19 is not a normal rank. For the same rank, historical records retain independent best gold/combo flags, as before.
- Successful Dressing Apply shows an icon-free blue notification, "適用しました": 0.15s fade/rise by6px, visible for2s total, final0.2s fade. This is a local design choice, not a reference-measured or copied Motion Prompt effect. It does not block input. Failure hides success feedback and keeps dirty state for retry.
- Mode-select Esc returns to Entry without a back/ka sound. Existing category-popup/decision cancellation stays local and silent. Removed the unrelated dark profile-button backing and overlaid F5 label; F5 and plate clicks still open data selection. Menu plates use mirrored x60/1522, mode/default y920 (338x80), compared visually with the supplied entry recording at52s; not a pixel-exact runtime match. Explicit placements in play/results remain unchanged. Plate text fits actual glyph ink +2px outline/padding, including long names, title and rank; badge text remains upright for2P.
- Entry credit-row message is aligned by visible ink to the native left label (center at rowY+99.5). Shared Free Play keeps48px text but uses a3px outline instead of0.16*size (7.68px); visible center y1052 leaves bottom padding. Existing entry help-footer removal is preserved.
- Settings/setup/boot regular text now uses CoverageFont: Raylib grayscale glyphs at >= requested size, cached in size buckets and1024px atlas pages. Original antialias coverage is retained (no binary SDF conversion); Measure/Draw share advances. Default system font sizes are unchanged. Centered GUI labels now fit and align by actual glyph ink rather than an oversized logical line box. Thick outlined skin/game text continues to use DynamicFont's SDF renderer. Cached regular draws avoid missing-glyph allocations, reuse pages, upload only changed rows and free textures with the font. Text-page counts and CPU glyph-preparation events remain in debug logs.
- Validation: production Windows-target build preserves AOT settings (0 errors, one existing CS0162 warning); Linux source build passes (0 errors, 253 existing warnings). Nameplate/profile/earned-rank checks:101; record/local-player/BGM:82; training:50; real CPU font/raster/atlas checks:863. tools/font-check tests real Raylib CPU rasterization with a local Noto CJK OTF, including antialias alpha, >=display-size rasterization, multiline metrics, atlas packing and reuse; no GPU drawing. Windows visuals/IME, actual window scaling, performance and Native AOT publish remain unverified. Do not describe CPU checks as a Windows display test.

### Settings component AA and additional UI references (2026-11-10)
- User reported aliased switches, volume slider and help/question marks; supplied a7-site UI-reference text file. UI-REFERENCES.md records exact URLs, which official pages were read, retrieval limits and the modest component changes. Retain the approved navy/blue layout and round knobs. Buttons/input/value bubbles now share6px radii; slider focus is a thin blue ring and its value bubble uses actual-ink text centering. Settings/explanation contents, hit targets, keyboard/mouse control and save semantics are unchanged.
- SmoothShapes uses one quad per shape and an original GLSL330 signed-distance shader. fwidth controls a roughly one-render-pixel edge, with original fill opacity retained. Rounded rectangles/circles, transparent rings/inside borders, rounded lines and tooltip triangles share the renderer. Gui, both SettingsOverlay layouts and Setup use it. No game-wide supersampling/MSAA setting, larger UI text, textures of small enlarged shapes, copied third-party components or new package dependencies. Shader preloads in Screen.Init and unloads before graphics shutdown. Failed shader compilation logs once and uses native shape fallbacks.
- New verification method: Linux libEGL/Mesa surfaceless OpenGL can run the real Raylib/rlgl renderer without a window or X server. tools/ui-aa-check initializes an isolated context, draws actual SmoothShapes and Gui controls into the same single-sample render texture and reads pixels back. This is a rendering test, not a full game launch. No Windows BASS/IME/input APIs or production backend changes are involved. Test code's reflection only assigns a font in the test process, not in the game/AOT binary.
- Mesa llvmpipe18 rendering checks passed: old aliased circle as control; fractional edge colors; ring holes;6px track; triangle; partial opacity; matching translated pixels; clipping; thin borders; rotated line; subpixel position; actual ON/OFF/volume thumbs; help/regular-font integration;1280x720 compositing. OpenGL4.5/GLSL4.50 successfully compiled the GLSL330 shader. Windows GPU-driver appearance, real full-screen overlays and performance still need native verification; do not claim the user's Windows workspace recovered.
- Windows-target build:0 errors/one existing CS0162; Linux source build:0 errors/253 existing warnings. Nameplate/profile regression:101; record/local-player/BGM:82; training:50. In this sandbox, parallel dotnet-run shared compiler processes hit ReleaseMutex/host failures; rerun sequentially with DOTNET_CLI_USE_MSBUILD_SERVER=0 and -p:UseSharedCompilation=false. These environment retries are distinct from source/compiler failures.
- User then explicitly requested no new branch and a push to master. Master8e91bcc8 is the ancestor of nameplate/dressing head b4c7967 (ahead5, behind0), so fast-forward master through that work and this AA commit, retaining all history. This supersedes the earlier review-only/keep-off-master note for this feature. Do not create another branch or force-push unrelated refs.

### Native-resolution text rendering (2026-11-11)
- User reported poor small system text, especially after lowering resolution. Supplied screenshot shows a1920x1080 setting fitted to an actual1742x980 client area. Root architectural issue: text first rasterized into size buckets, shrunk into a fixed1080p canvas, then the entire canvas resampled again. AA presence alone did not establish readability.
- Screen keeps1920x1080 logical coordinates and existing input conversions, but now allocates the render texture at GetRenderWidth/Height, uses a Camera2D logical-to-framebuffer transform and composites at framebuffer pixel density. Reallocate only on actual size changes. No larger UI font sizes or layout overhaul. A native-resolution screenshot now matches the current framebuffer dimensions, retaining old filenames/folders. Higher-resolution render targets consume proportionally more GPU memory.
- All game/UI scissor calls go through Screen.BeginScissorMode/EndScissorMode so logical clip rectangles scale into framebuffer pixels, including existing slide offsets and local2P viewports. Non-canvas test rendering passes through unchanged. Black bars are restored after drawing even when scenes call ClearBackground. Keep mouse and IME coordinates in logical window space; do not multiply them a second time.
- CoverageFont rasterizes at ceil(logical font size * actual framebuffer scale), without the former buckets. Its glyph quads/advances use one source texel per framebuffer pixel; baseline origins snap to framebuffer pixels. Layout and draw use the same metrics. Direct-to-window notifications use scale1 after EndCanvas. Keep original grayscale antialiasing rather than disabling AA. At most24 raster-size caches are retained using LRU; flush pending draws before disposing an evicted texture. SDF outlined text uses derivative-based edge width under the canvas transform.
- tools/resolution-check uses the real offscreen EGL/Raylib pipeline and passed40 checks at1080p/720p/540p/1742x980/4:3, including return resize, native glyph sizing, pixel alignment, correct clipping, black bars and state reset. Production Windows-target build passed with original AOT flags:0 errors/one existing CS0162. Linux source build:0 errors/253 existing warnings. Windows actual readability, DPI/resize behavior, audio/IME and Native AOT publish still require the user's machine.


- Regression checks after this change: nameplate/profile101, record/local-player/BGM82, training50, CPU font/raster/atlas978. Font checks now exercise14 exact raster sizes rather than9 buckets.

### Entry/plate/dressing alignment follow-up (2026-11-11)
- User supplied four screenshots. CreditBar.png in the existing private01.Entry.zip has baked black-label bounds y73..125 in cell0 and y59..111 in cell1 (measured in x140..410/y45..140). Right-hand entry text now centers at cell-relative99.5/85.5, correcting the second-row14px mismatch; participation flow is unchanged.
- Shared Free Play changes48->45px and outline3->5px at the same center. Shared name text moves6logical px upward (area y43->37); titles/ranks/config/data remain unchanged.
- Dressing preview uses screenshot-indicated orange/yellow areas: Don image origin(1295,240), existing scale, assuming the supplied A-Style820x599 transparent frame; plate origin(1385,710), native scale1 instead of1.8. Other skins with different transparent margins may need further tuning. These are user-screenshot adjustments, not new reference-video measurements.
- Dressing resets inherited GUI click clipping and snapshots the visible list top. It defers one row selection until after list drawing. Old in-loop Select changed top and could process one click repeatedly (20-item algorithm reproduction: row7 selected7,10,13,16,18; fixed:7 only). Windows mouse/resize verification remains pending; this does not prove every reported click-coordinate symptom resolved.
- Remaining: the reported1P badge right-edge crop is not fixed. The original NamePlate_Base.png is absent from this task filesystem; do not guess new atlas cell dimensions from a screenshot. Request/retrieve that original sheet before changing the crop.
- Validation: Linux source build0errors/253existing warnings;101profile/nameplate checks;40offscreen resolution checks. Production Windows-target build was attempted but blocked by missing restored runtime pack/ILLink.Tasks10.0.14 in this environment. No csproj/AOT settings changed. Windows game/IME/audio and Native AOT publish were not tested.

## Discord Rich Presence (11/11)
- `Platform/DiscordPresence.cs`. No library: writes JSON frames to the `discord-ipc-0..9` named pipe on a background thread (Utf8JsonWriter, AOT-safe).
- Each scene overrides `Scene.Presence` (details = line 1, state = line 2, end = "remaining" countdown). Sent only when it changes, at least 2 s apart.
- たけらい: the result screen shows no score rank. Settings: 一般 → Discord (`DiscordPresence`, `DiscordHideSongTitle`).
- `ApplicationId` is empty until たけらい creates the Discord app; the art asset is named `logo` (never A-Style art).

### Name plate name fitting and rank backing (2026-11-12)
- User supplied a small reference showing a dark rank cell left of the name. Shared plate rendering now paints a black76x35 backing at(73,36) only when Rank is nonempty. Existing NamePlate_Rank_Gold.png overlays that cell for gold passes when present; missing gold art falls back to black. RankType text colors/earned-state rules are unchanged. This is a drawn backing, not newly extracted official rank art.
- Name text uses fixed30px skin-font glyphs, centered by visible ink in y37..70. A separate horizontal-only matrix scale fits ink plus2px outline/padding into the available width. Vertical scale remains1 for both ranked and unranked names. Ranked names use x151,width174; unranked names use x76,width249 and center in that entire name band. Existing titles and badge crop are unchanged.
- Linux source build passes with0errors/253existing warnings;101profile/nameplate/cosmetic/rank checks pass. These checks do not verify actual Windows plate appearance, GPU drivers or Native AOT publish. Original NamePlate_Base.png crop issue remains pending. No skin art, settings/profile format, rank unlock logic or scene placement changes.

### Real name-plate rank art and badge crop (2026-11-12)
- User corrected the drawn rank backing: original art exists. Recovered the previously supplied private09.zip (451260654bytes) and inspected actual99.Common/NamePlate/NamePlate_Base.png (338x560) and NamePlate_Rank_Gold.png (63x24). Reference assets remain untracked. The latter is a gold color gradient, not a full rank backing; do not stretch it over the whole rank cell.
- Actual alpha components in NamePlate_Base.png: black rank backing(12,241,131,34); silver/gold/rainbow ornaments(72,323+82*type,70,36). Draw at(12,40) and(73,36), native size. RankType selects the ornament; IsRankGold controls text gold independently. Gold text remains a flat gold fill, not the63x24 gradient masked into glyphs. Missing/short sheets retain the drawn black fallback. No unlock/config changes; no rank still centers the name and uses no rank art.
- Actual badges have72px visible alpha atx9/91/173,y83, proving82px horizontal spacing. Corrected1P/2P source rectangles to x0/164,y80,w82,h80 and draw after rank backing so the badge is not covered. This resolves the previously unverified wrong80px crop; Windows visual confirmation is still pending.
- Linux source build0errors/253existing warnings;101profile/nameplate checks. Also ran the actual NamePlate.Draw in EGL/Raylib using the recovered original sheet and a local Noto test font, and visually inspected silver/gold/rainbow/no-rank2P outputs. This is offscreen Linux drawing, not Windows game launch, the actual skin font, IME/audio or Native AOT publish. Do not commit the private art or inspection images. Earlier notes saying original sheet unavailable are superseded by this recovery.

### Entry screenshot alignment correction (2026-11-12)
- User reported remaining record/plate offsets in image(20261111-154228).png,1744x1013 with1742x980 client. Re-measured visible centers rather than claiming the preceding adjustment fixed them. Top digit/icon y525.5/523.5, first rank y554.5/558.5, second y590.5/594, crown y627.5/629.5 (physical screenshot pixels; antialias/mask estimates). At980/1080 scale, adjust summary centers348->346,380->385,420->424,460->462. X positions, counts and digit atlas handling stay unchanged. Applies to the shared summary, including Entry's(946,197) translation.
- Plate art moves+1x,+2y relative to PlateConfig adjustX/Y (both Aup2 and static Base). Badge destination x-8->0,y-1 retained, matching the shared sheet's own embedded left margin rather than subtracting it again. Black rank backing y40->37, ornament(73,36)->(72,35), using unchanged native source rectangles. Fallback black area follows the adjusted band. Name25px/outline4, text fitting, rank selection and data formats remain unchanged.
- Ran actual NamePlate.Draw with original00/Anime.aup2, silver/gold/rainbow and no-rank2P in Linux EGL and inspected the output. This now includes real gold title art; the earlier missing-art/fallback-blue inspection did not test title alignment. Source build0errors/253existing warnings, profile101 and record/local-player82 checks pass. Screenshot measurements and Linux rendering still do not establish Windows/actual-skin-font pixel-exact matching. Source02.SongSelect art is unavailable here, so summary icon centers were measured from the user's screenshot, not from that atlas. Private art/inspection files remain untracked.

### Title-art upper edge correction (2026-11-12)
- Latest screenshot image(20261111-160240).png still shows a thin white gap above the gold title. The preceding+1x/+2y correction was wrong for the original art and is now removed for both Aup2 and static Base. Use PlateConfig adjustX/Y directly, without extra displacement or enlargement. Other plate/badge/rank/count placements are unchanged.
- Verified original pixel rows atx200: common base y4 black/y5gray/y6white;00/Base.png y4black/y5dark gold/y6gold. Native origins align. The added2px downward shift had exposed the white base at the top. Actual338x41 title size is sufficient.
- Linux source build0errors/253existing warnings; real00/Aup2 offscreen rendering inspected again. Actual output at(230,36) (plate origin30,30 + pixel200,6) is gold(253,237,140), not white. Windows and actual skin-font display remain pending; no reference art is committed. This supersedes the earlier art+1,+2 note.

### Entry data-load animation, mode voice timing and filtered song search (2026-11-12)
- Explicit user request: inspect the entry reference at0.1-second intervals. Extracted24.0..38.4s at10fps from the previously supplied TaikoNauts_エントリー画面_1109.mp4 (1920x1080/60fps); inspected32.7..37.9s frame by frame. At approximately32.9s the profile starts fading;33.2s absent;33.3..33.4s green panel vertically opens;34.9s still green;35.0..35.1s fades;35.2..35.3s success panel vertically opens;37.1..37.3s disappears. Sampling gives approximately0.1s precision, not exact60fps event times. Native1423x490 DataLoad_Panel rows0/1 and DataLoad_Text rows0/1/2 align at(249,295), including the green/success panels (white-border comparison at quarter-size matched approximately248,296).
- Entry now validates/selects the profile before starting a4.4s cosmetic local-load sequence:0..0.3 old card/guide fade;0.3..0.5 green vertical opening;green until2.0 then fade through2.2;2.2..2.4 success opening;hold through4.2 then fade through4.4. Success voice at2.4. This is NOT real network communication. Windows close/quit remains available. Esc/reconfirm/settings are blocked during the sequence. Each participant completes it before the next participant chooses data; no extra play-side chooser was added. Keep a valid participant index during all draws/transitions. Success completion runs once; no repeated confirmation sound at automatic completion. Missing skin images use simple drawn local-load messages.
- Mode cursor movement stops the preceding voice and waits until BarList.Expand>0 before looping the hovered EnsoGame/DaniDojo voice. A rapid move cancels the obsolete request. Existing entry SelectMode -> item loop and category/decide behavior are retained. Actual filenames Entry/EnsoGame.ogg, DaniDojo.ogg and DataLoadSuccess.ogg confirmed inside the supplied BGM.zip. No new voices are invented for modes without art/audio; native audible playback remains untested.
- Song search is now a navy/blue centered modal with text, optional course, optional exact star level and optional genre, clear/back/X/search actions, live result count and bounded popups (8visible rows + wheel + scrollbar). Text alone, filters alone and all-unspecified searches work. Oni/Ura are separate; difficulty+stars must match the SAME chart. Genre uses category.Genre, falling back to category.Title. Levels include0..10 plus actual custom levels. Cancel restores the previous query and filters; Enter submits after pending dropdown results are applied. IME conversion Enter/Backspace are guarded; actual Windows IME remains untested. Cache results by query/filter values AND SongList.Categories identity; no JSON/settings/history format change. Reset filters on profile switch.
- Gui.Dropdown gains optional maxVisibleRows; other callers retain their existing default. Search requests8. Scroll bounds and popup selected-row indexing are covered in the real EGL/Raylib test. Popup selection is mouse/wheel; this does not claim keyboard arrow navigation through popup candidates. Search key binding is unchanged; removed permanent search/count/footer labels stay removed.
- Validation: Linux source build0errors/253existing warnings; search filters19, profile/entry125, record/local-player82, real offscreen UI22 checks passed. Additionally rendered the actual search modal and4communication phases using original entry panel/text images and a local Noto test font; visually inspected outputs. Test reflection stays in test programs. No reference video/art/screenshots are committed. Windows launch/audio/IME/real pointer/DPI interaction, performance and Native AOT publish remain pending. tools/search-check/README.md explains the filter checks; existing nameplate-check covers entry sequence locks and participant order.

### User-supplied application icon (2026-11-12)
- User chose9B16AEC0-03A6-42C9-A38A-C540048C2BDB.png as the TTS application icon. Kept the complete square artwork (1254x1254) and converted it without redesign/cropping: Assets/Branding/AppIcon.ico has16/24/32/48/64/128/256px frames; AppIcon.png is256x256. This is user-supplied app branding, not borrowed skin art. Do not attribute authorship/licensing beyond that instruction.
- Production csproj ApplicationIcon embeds the ICO for Windows Explorer/exe/shortcuts. PNG is EmbeddedResource with fixed logical name TakeTaikoSimulator.AppIcon.png. Platform.AppIcon.Apply reads it through typeof(AppIcon).Assembly.GetManifestResourceStream, decodes via Raylib.LoadImageFromMemory and calls SetWindowIcon immediately after InitWindow; image is unloaded after setting. No loose icon file dependency, working-directory change, settings/profile/skin changes or new packages. Existing Native AOT flags remain intact.
- Source build-check includes the same ApplicationIcon/PNG resource so compilation validates the ICO and resource packaging. Its ApplicationIcon uses MSBuildThisFileDirectory explicitly because SrcDir is declared later in the property group. After correcting that check-only path, Linux source build passed0errors/253existing warnings. Actual production Windows-target/AOT publish is not verified.
- Validated generated ICO frames with Pillow; parsed the compiled Check DLL PE resource directory and confirmed7RT_ICON entries plus one7-frame RT_GROUP_ICON with all intended dimensions. Verified the exact164800-byte PNG and its resource name exist in that same compiled assembly. The later runtime resource-decode retry could not run after the older SDK/native runtime workspace disappeared; do not describe that retry as passed. Actual Windows title-bar/taskbar/Explorer appearance, runtime SetWindowIcon and Native AOT publish remain pending.


### Approved transparent TakeTaiko icon replacement (2026-11-12)
- User replaced the previous icon choice with59EA809C-8932-4440-BB35-8CF31D6693F5.png, clarified clipping on all four sides and requested transparency outside the circular emblem. Generated a repaired transparent preview and waited; the user explicitly approved that preview before applying. The selected image is the approved edited preview, not a claim of pixel-identical reproduction of the original attachment.
- Replaced only Assets/Branding/AppIcon.png (256x256 RGBA) and AppIcon.ico (16/24/32/48/64/128/256px), using the same existing resource names and project/runtime wiring. Circular artwork and TakeTaiko lettering match the approved preview; transparent exterior and padding are retained. No gameplay, settings, data paths, skins, source code, build properties or AOT flags changed.
- Validated both formats with Pillow: all7ICO sizes decode, corners remain transparent in every frame, and the256px ICO frame equals the PNG exactly. Checked existing production/check csproj icon and embedded-resource references. The older SDK workspace is no longer present, so no rebuild was run for this asset replacement. Windows Explorer/title-bar/taskbar appearance, runtime SetWindowIcon and Native AOT publish remain unverified. Earlier build/compiled-resource checks refer to the previous icon, not this replacement.
## Song select measurements from the 11/11 recording (2026-11-11 23-44-20.mp4 / 23-46-22.mp4, TaikøNauts latest, A-Style)
- Top-left taiko guide = `99.Common/Control_Guide/Large/Anime.aup2` drawn at (0,0), native size, plain 653-frame loop from scene enter
  (does not react to input). Vanishes 0.77 s after a song is decided; not shown while choosing a difficulty.
- Song decided at t=0: blink 0–0.55 (4 cycles), list fades 0.77–1.00, difficulty panel fades in 1.25–1.77 (linear), 1P marker at 1.97 (0.05 s fade).
- Difficulty cursor: jumps instantly; only the 1P marker dips 8.6 px and returns within 0.117 s.
  Deciding a course: cursor frame yellow⇔white ×4 in 0.567 s (white = 3rd frame of Cursor_Effect.png), yellow until 0.65, gone by 0.767; curtain 2.7 s later.
- Positions found by overlaying the skin images on the video in 0.5 px steps: card number centre (111.5, 263), dot-row star (31 + 15n, 292),
  bar chip number centre (147.5, 43.5). Missing courses: card and digit "0" at 50 % brightness (no 「なし」 text).
- Esc from the song list uses the same blink → flip as deciding 「とじる」 (TaikøNauts' own Esc behaviour was not recorded).
- Best-score update (result): at RevealTime a gold ribbon replaces the 「スコア」 label (x 75–497, y 277–318) with a black pill "+gain↑";
  rises 11 px for 0.13 s then lands by 0.20; text 「ベストスコア更新！」 0.23–0.28; yellow flash 0.32–0.47; white diagonal shine 0.50–0.78.
  `BestScore_1P.ogg` plays together with ScoreDon. Not shown on a first play (no previous record). Drawn by code — the A-Style image name is unknown.
  ChatGPT's dark info box on the right was removed (not in TaikøNauts).
- クリア失敗 (play end) and the failed result matched the existing implementation (PlayEnd.cs / ResultScene.cs); nothing changed there.
- Entry → mode select: no black fade. `ModeSelectScene(intro: true)`: selected bar + nameplate at 0.07 s, Don-chan slides in 0.33–0.53,
  top-left guide fades in 0.47–0.62, other bars slide in from outside 0.55–0.67. The mode select also shows the Large control guide.
  TaikøNauts has a 「どちらの太鼓でつかいますか」 step (ひだり／つかわない／みぎ) before this; TakeTaikoSimulator does not.
- Seen but not implemented: after a result TaikøNauts returns to the genre list and flips the genre open again; other mode bars flash pale ~1.1 s after the intro.



### User-editable coordinate constants (2026-11-12)
- User wants to make small position adjustments personally to reduce AI/token usage. Added UI/LayoutAdjustments.cs with Japanese section labels and per-item comments for shared name-plate internals, menu plate positions, record counts/best-score offsets, Entry panels/messages/arrows/guide and Dressing previews/list, plus Free Play. COORDINATE-GUIDE.md is the Japanese purpose-to-field map and before/after examples. It explicitly says edit/save/rebuild/restart; these are C# constants/readonly values, not runtime JSON or F5 reload settings. Values use1920x1080 logical coordinates, and comments distinguish origins, centers, local plate coordinates and offsets.
- Initial values preserve the current layout. Existing name horizontal-fit, skin PlateConfig adjustX/Y, atlas source rectangles and all animations/input/save/history/AOT rules remain. TitleArtOffset adds to both static/Aup2 title origins; missing-art blue fallback is unchanged. Records SummaryOffset/BestOffset move whole groups; per-number centers remain separately adjustable. Entry start rows/arrows and Dressing list rows use the same rectangles for draw/click; GuideOffset also moves its confirmation hitbox. No extra screen/settings controls or new dependencies. Scope is these frequently adjusted elements, not every game coordinate; song-select's explicit plateY900 and other scene-specific caller placements are documented and retained.
- Reviewed diffs against master d80703b26c05c56810c6a17a2fe0a83cc2bcaa60, including Claude's newer entry/mode/result work. Static audit resolved57Layout member references, compared30default position tuples,3record-number rows and the Entry/Dressing relative placement calculations against the former values. This is NOT compilation or a render/input test. The former .NET SDK/dependency workspace remains missing; direct SDK-download attempt timed out. No build, regression executable, Windows launch/visual/input check or Native AOT publish was performed for this refactor. Do not describe the earlier0-error builds as validation of these files.


### Scene folders and shorter star-filter label (2026-11-12)
- User requested the star-filter default label be simply「すべて」and related scene source files be grouped. Moved32files under Scenes/Play, SongSelect, Entry, ModeSelect, Dan, Training, Result, Dressing, Startup and Credits. Only Scene.cs, SceneManager.cs and LocalSession.cs remain at the root. SOURCE-MAP.md is the Japanese navigation guide; README links it and the existing coordinate guide. Updated explicit source paths and this file's main folder map. Shared UI/Graphics/Configs remain shared.
- Retained filenames, types, partial-class identities, the TakeTaikoSimulator.Scenes namespace and all gameplay/data/skin/AOT behavior. Only C# text changes are the two default star-filter labels in Scenes/SongSelect/SongSelectScene.cs. Other difficulty/genre labels and actual star filters are unchanged. Existing SDK implicit inclusion and build-check's SrcDir**/*.cs already include nested files; no project edits are needed.
- Static audit against master9095ac3: all120tracked C# files byte-identical after reversing those two label edits;32moves,104production C# sources remain included,13project/solution files unchanged, no stale moved-source path references,18SOURCE-MAP links resolve. These are static checks, not a successful build. dotnet build tools/build-check/Check.csproj --no-restore was attempted and failed because dotnet is absent (exit127). Windows launch/input/audio/rendering and Native AOT publish remain unverified. Earlier successful builds do not validate this reorganization.


### Final approved Windows mascot icon (2026-11-12)
- User approved the final face-led icon after drawing the rope's desired curve on a markup image: preview exec-0857ed3d-d41a-4a07-a9c4-a2d6a8727bc9.png. The red-white rope now drops from its crown loop and curves down/right before tucking into the hood fold. Replaced only Assets/Branding/AppIcon.png and AppIcon.ico; earlier unapproved face-icon candidates were not pushed. This supersedes the complete circular logo's use as a Windows icon.
- PNG256x256; ICO16/24/32/48/64/128/256px. Pillow decoded all7frames, checked transparent corners, and verified the256px ICO frame equals the PNG. Existing executable/embedded PNG/SetWindowIcon wiring remains unchanged, as do all C# sources, project settings, Native AOT flags and game/data behavior. No rebuild was run because the SDK remains absent. Windows actual small-icon appearance/runtime SetWindowIcon and Native AOT publish remain unverified.
- The user proposed the complete circular TakeTaiko logo for the center of startup loading. That screen placement is not implemented by this icon-only change. The approved full circular artwork remains available from this chat; its former application-icon assets remain in master history before this replacement.


### One-second song-list movement lock after skipping (2026-11-12)
- User requested1second of ignored movement after a song-list skip. SongSelectScene.SkipInterval changes0.5->1.0s. The movement gate now covers fresh Ka/arrows, arrow auto-repeat and wheel, and other-item mouse selection also observes the lock. Inputs during the lock are discarded, not queued. Existing selected-item click/Don/Enter confirmation remains available. SkipCount7, label-skipping/wrapping and the existing moving-cursor trigger (BarList.Shown != CurrentPos) are unchanged; no new fixed150ms key-timestamp threshold or same-direction requirement was added.
- Reviewed the three changed regions against master845e30b. The constant and all listed selection-input gates use the existing countdown; no project/AOT/data/skin changes. SDK is still absent, so this small source change is not compiled or runtime-tested here. Windows interaction and Native AOT publish remain unverified. The earlier0.5s reference-video measurement is historical;1s is the user's chosen behavior.


### Same-direction triple-tap skipping (2026-11-12)
- User found the moving-cursor skip too sensitive, requested same-side repetition for skip direction, and reverted post-skip lock to0.5s. Used the previously proposed0.25s total first-to-third window as announced. Their earlier0.025s suggestion was flagged as25ms and was not silently used. Fresh Ka/up/down presses now use SongSelectSkipGesture and Raylib.GetTime(), not the animation position. First2presses move normally; the third same-direction press within250ms moves7items. Opposite direction begins a new count (normal reverse movement remains). Arrow presses behave like Ka; repeat/wheel/simultaneous opposing directions reset the count and do not trigger skipping.
- A small queue retains recent timestamps, allowing any rolling last3same-direction presses within250ms; firing resets it. Modal/transition/scan/lock states and mouse repositioning reset it. Lock covers list movement via Ka/arrows/repeat/wheel/other-item clicks; ignored inputs are not queued. Don/Enter/selected-item confirmation still work. Seven-item movement, wrapping, label exclusion and data/AOT settings are unchanged.
- Added dependency-free tools/selection-skip-check compiling the actual helper, with41sequence/boundary assertions. SDK is absent: these C# checks, full build and Windows runtime were NOT executed here. Reviewed input/reset/mouse gates against master2b0bd0a. Do not report tests passed or reference-matched timing without running them.


### First-side double-tap with ignored opposite input (2026-11-12)
- User clarified before the attempted correction was pushed: left->right->right quickly should produce ONLY the initial one-item left move, no skip and no right moves. It must NOT skip in the first direction merely because an opposite second input arrived. Final rule: hold the initial side for250ms; ignore opposite presses without changing/extending the first timestamp; same-side second press within the window skips7items in that side. After timeout, either side becomes a new ordinary first press. Left->right->left counts two left presses; right is ignored. Register now returns (Direction, Skip), with Direction0 meaning fully ignored, and the scene honors that result. The earlier same-direction3-tap description is superseded.
- Skip navigation excludes Kind.Back and Kind.Label from counted items and landing positions, while ordinary one-step navigation still selects close. Genre/random items retain their existing behavior. Each step scan is bounded by list.Count and returns without moving/sound if nothing qualifies. Post-skip movement lock stays0.5s. Existing modal/repeat/wheel/mouse resets and decision inputs remain. User deferred animation changes; BarList is untouched.
- Updated tools/selection-skip-check with29sequence/boundary/reset assertions against the actual helper. Checks explicitly include left-right-right and its reverse producing only the initial ordinary move, ignored opposite presses not extending the window, and same-side double taps. SDK is absent, so these C# checks, build, scene close-item navigation and Windows/AOT behavior are NOT executed here. Static diff review against74023f9 only. Discard the earlier unpushed misinterpretation.


### Close-to-close skip from an initial close item (2026-11-12)
- User added a starting-position exception: a skip started while the cursor is on「とじる」should land on the next「とじる」in that direction. Capture CurrentPos/Kind.Back BEFORE the first ordinary one-step move. On the second same-side tap, use that original position to search for the next Back item, bounded by one full list traversal, wrap included. A single Back item can wrap to itself. Exactly one skip sound on landing; existing0.5s lock remains.
- Starting from a song retains the prior seven counted-item skip excluding Back/Label. First-side input ownership,250ms double-tap condition, modal/input resets and all BarList animation code remain unchanged. README adds explicit native navigation cases. Static source/diff review only; SDK is still absent, so build, gesture executable, actual close-to-close UI/input/audio and Windows/AOT verification are unexecuted.

## Own UI look (11/12, たけらい approved the mock)
- Palette follows the mascot icon, toned down for legibility (Gui.cs): near-black with a wine tint for background/panels,
  crimson `AccentColor` (226,58,94) only for the selected thing and primary buttons, `SakuraColor` for sub-headers and highlights.
  Do not bring back the navy/blue palette. Use the Gui colour constants instead of hard-coded colours in own screens.
- Glow: `Gui.Glow` / `Gui.GlowCircle` on hovered buttons, the active slider knob, open dropdowns, focused inputs. One knob: `Gui.GlowStrength`.
- Every rounded shape in own UI goes through `SmoothShapes` (anti-aliased). No `DrawRectangleRounded` / `DrawRectangleLinesEx` for visible boxes.
- The setting 「?」 help tips stay. Dressing stays a separate screen with the same look. Song search uses the same panel style.
- Loading screen: the round logo (`Assets/Branding/Logo.png`, embedded) centred with a subtle pulsing glow; % and bar below it.
- A Linux raylib binary download for `tools/ui-aa-check` was blocked by the safety classifier, so none of this was rendered here.
- Simple skin / code-drawn shapes: every `Raylib.DrawRectangleRounded`, `DrawRectangleRoundedLinesEx`, `DrawCircle(V)`, full `DrawRing`,
  `DrawTriangle`, `DrawLineEx` call outside SmoothShapes.cs was replaced by the same-signature `SmoothShapes` wrapper (≈1 px edge AA, no blur).
  Use the wrappers for new code. たけらい wants moderate AA, not soft/blurry edges.
- Bars drawn without the A-Style bar art (`BarList.DrawBody`) now get a soft drop shadow and a darker rim band inside the outline,
  because たけらい found the Simple skin bars flat next to A-Style. Unverified on screen.

## Pause during play (11/12) — `Scenes/Play/PlayPause.cs`
- Esc opens a pause menu in normal play and dan (not in training, 2-player, or after the end sequence started).
  Normal: つづける / 最初からやりなおす / 曲選択にもどる. Dan: つづける / やめる only (たけらい's decision).
- つづける counts 3-2-1 before the clock and song resume (たけらい: 「俺は優しいから」). The song is stopped and restarted by UpdateSongSync.
- Look: skin art if the skin has it, otherwise own UI. The A-Style pause art names are still unknown, so every skin uses the own UI for now.
- Settings rows are grouped into cards (`BeginCard`/`EndCard` in SettingsOverlay.cs); `Gui.Segmented` replaces a dropdown automatically
  when it has 2–4 short options (no current setting qualifies).


## Entry flow (11/12, たけらい)
- Flow: yellow start bars (Don of either key set) → 「どちらの太鼓で」 side select (ひだり／つかわない／みぎ, default つかわない, uses A-Style `01.Entry/Playside_*.png`; own UI when missing) → data select → 通信中 → 完了 → mode select.
- Side and key set are separate: `LocalSession.KeySets[side]`. `InputManager.IsDrumPressed(part, side)` maps through it; `IsKeySetPressed`/`IsKeySetDonPressed` read a key set directly (for people not joined yet).
- Data select/communication lives in `Scenes/Entry/PlayerJoin.cs` and is reused as an overlay: the 2nd player joins from mode select or song select (list only, not difficulty select, not training) with a Don of the unused key set, and takes the free side. `LocalSession.Join` is called only when 完了 ends.
- Solo player on みぎ looks like 2P in menus; the play screen keeps the 1P layout (PlayScene draws the nameplate/puchi with side 0).
- Leaving 2-player = going back to the entry screen (it resets the session).
- An unregistered data slot shows only 「新しく遊ぶ」 (no name, score rank, or crown summary).

## Per-player settings, new-data setup, data reset (11/12, たけらい)
- `Configs/PlayerSettings.cs`: `Config/PlayerData/<slot>/PlayerSettings.json` holds the personal settings (HiSpeed, HitSound, auto options, ShowScoreboard, RecordGroup, 5 volumes). Drum keys, window, skin, input mode, debug, Discord, offsets and audio output stay shared (たけらい: 「全員共通でいったん行こうか」 for keys).
- The rest of the game still reads `ConfigManager.Game/Sound`. `LocalSession.Join(side, keySet, profile)` calls `PlayerSettings.Activate` for the controller (copies the file into the live config); `ConfigManager.SaveAll/SaveSound` also write the active player's file (`SaveActive`). The 2nd player only gets their own play options in `LocalSession.Options[side]`; volumes follow the controller. `LocalSession.Reset` deactivates.
- `PlayerSettings.Migrate()` at startup copies the current shared settings into every named slot that has no file yet.
- New data: after the name input, `PlayerJoin` shows 「はじめの設定」 (speed, hit sound, master volume) before 通信中, also when joining from mode/song select.
- Reset: Settings → 一般 → プレイヤーデータ. Pick a slot, then 全部初期化 or 選んで初期化 (checkbox list, `PlayerDataParts`), then a confirm dialog. `PlayerDataReset.Run` + `SongHistory.ClearProfile`. If the slot is in use (or the entry screen is open) the game returns to the entry screen.


### Discord application ID configured (2026-11-12)
- User supplied Application ID1570361696688799856 from their Discord application. Set Platform/DiscordPresence.cs ApplicationId to that exact string, enabling the existing IPC worker when the shared DiscordPresence setting is ON. No token/client secret or new permissions are used; protocol, scene activity content, background connection logic and AOT configuration are unchanged.
- Existing large-image key is logo; upload the desired image under that key in the application's Rich Presence assets to show it. Desktop Discord must be running and activity sharing enabled. Source diff confirms the exact ID string is the only runtime-code change. SDK/build, actual Windows IPC connection/activity display and Native AOT publish remain unverified; do not claim connected merely because the ID is configured.

## Play options added (11/12, たけらい)
- New options in `GameConfig` (per player data via `PlayerSettingsFile`, and in the `LocalSession` per-side tuple): `Doron`, `Abekobe`, `Random` (`NoteRandom` Off/Kimagure/Detarame), `PlaySpeed`, `ComboVoice`.
- `Charts/NoteOptions.cs`: Abekobe flips every Don⇔Ka; Kimagure 21 %, Detarame 51 % per note (OpenTaiko `CTja.tRandomizeTaikoChips`), then `SeNotes.Assign` again. Applied in `PlayScene.LoadChart`.
- Doron: `DrawNote` is skipped, only the SE text scrolls. ComboVoice off: combo voices are not played (nothing else).
- PlaySpeed: only the values that have an A-Style icon (`GameConfig.PlaySpeeds`: 0.5–1.5 by 0.1, and 2.0). Uses `Song.SetPlaybackRate` (pitch changes too) and the training-mode `PlaybackRate` clock; judge windows are multiplied by the speed so they stay ±25/75/108 ms in real time. Forced to 1.0 in dan, 2-player and training.
- Records are saved as usual whatever options are on (たけらい: may change later).
- `PlayOptionsPanel`: 8 rows per page, more rows go to page 2 (「1/2」 at top right, PageUp/PageDown, Don walks through). `Kind.Choice` and `Kind.Disabled` (演奏スキップ is shown greyed out; 音符位置調整 is not shown).
- `OptionBadges.DrawGrid`: order はやさ → ドロン → あべこべ → ランダム → AUTO → 演奏速度, packed. Play screen: 3 per row from (170, 353). Song select: one row under each nameplate.
- Scoreboard (演奏情報表示) moved from the bottom-left corner to (8, 540) under the nameplate, 1.1×.

## "Not recorded" notice for auto play (11/12, たけらい)
- `UI/InfoBar.cs` draws A-Style `04.EnsoGame/Information_Bar.png` (940×110) at (998, 119), shrunk 19 px in width, with 「特定の演奏オプションが使用されたため、今回のプレイは記録されません。」 (white, black outline, ~23 px).
  Measured on `2026-10-29_09-28-06.mp4` (auto play): appears instantly at 87.48 s, gone by 94.15 s (≈6.6 s, 0.05 s fade).
- Shown: play screen for 6.6 s when auto (not training / 2-player); ResultScene the whole time when `PlayResultData.AutoPlayed`; DanResultScene at (998, 950) when `dan.AutoPlayed`; PlayOptionsPanel for 4 s right of the panel when オートプレイ is switched to する.
- TaikøNauts' result screen showed no such bar in that recording (only the option icons on the black tab); the result bar is our own addition at たけらい's request.

## 11/12 polish (たけらい)
- Menu BGM loop: `SkinConfig.json` keys `entryBGMIntroTimeMs` / `songSelectBGMIntroTimeMs` (TaikøNauts names; A-Style 2500 / 1000) → after the first pass the BGM jumps back to the intro time instead of 0 (`MenuBgmPlayer.Update(..., introMs, loopEndMs)`). Own optional keys `entryBGMLoopEndMs` / `songSelectBGMLoopEndMs` (default = file end). Waveform notes: Entry.ogg is 89.15 s, music ends 88.56 s, phrase grid 3.75 s from 2.5 s (a grid-exact loop end would be 88750); SongSelect.ogg is 3.079 s with beats at 1.0/1.5/2.05/2.55 (bar-exact end ≈ 3000). Without the keys the old behaviour stays (`Scene.MenuBgmLoopMs`).
- `DynamicFont` SDF shader takes 4 samples per pixel (rotated grid) so small text is not jagged.
- `BarList.DrawRimLight`: top highlight / bottom shade line just inside the black rim of code-drawn bars (seen on A-Style `01.Entry/Bar/Bar/0.png`).
- `Gui.Notice(text)`: the notice banner (mode select / dan select / song select toasts), top centre, Gui palette.
- `PlayerNameInput.Draw` restyled with the Gui palette. Dressing back plays `MenuSounds.Cancel`. Mode select decide plays the Don-chan song-start jump (`PlayHold(SongStartAnim…)`), restored with `CharaBackToLoop`.

## Entry order + guest (11/13, たけらい)
- Entry flow is now: yellow bars (Don of key set 0/1) → data select → 通信中 → 完了 → 「どちらの太鼓で」 (with Don-chan `songselect_loop` at (735,225)×0.55 and the nameplate at (800,578)×0.95, measured on `2026-11-11 23-40-44.mp4` 104.5 s; Don-chan position is an estimate) → mode select. The player first joins the side equal to the key set, then `LocalSession.MoveSide` moves them to the chosen side. つかわない / Esc → back to the yellow bars with a full reset.
- Data select has 4 entries: データ1–3 then ゲスト. Empty slots show 「プレイヤーデータを新規作成します。」 + an empty white pill; guest shows 「ゲストモードでプレイします。」 + 「どんちゃん」 (pill at (804,626) 320×72, text ~31 px at y 540, from たけらい's screenshots).
- Guest = slot 0 (`PlayerDataStore.GuestSlot`): name どんちゃん, default appearance, no files. Everything saved during play (nameplate/appearance/dan rank via `PlayerDataStore.Save`, settings via `PlayerSettings`, records via `SongHistory`) is kept in memory only and dropped on `Reload` / `Deactivate` (entry screen). `SongHistory.IsGuest` → the 「記録されません」 bar in play, result and dan result. `CharaBaker`/`NamePlate.Preload` include slot 0. `PlayerSettings.ActiveSlot` is now -1 when nobody is active (0 is the guest).
- `BarList.DrawBody` no longer draws `DrawRimLight` over the skin's bar images (caused a dark line on the right edge); it stays for code-drawn bars.

## 11/13 batch 2 (たけらい)
- New player data: name input → `SetupScene(slot, next)` (ようこそ → 音量 → キー → 完了; no 画面 page) → back to the caller via `PlayerJoin.Pending`/`Resume` (entry: continues to 通信中 → どちらの太鼓; mode select / song select: continues the overlay join). The old in-panel 「はじめの設定」 is gone. Play options of a new data start from defaults (`PlayerSettings.ForNewData`); for a mid-join the first player's volume is restored after the setup.
- Note faces (`PlayScene.UpdateNoteFace`): combo < 10 → `Notes/Normal/0`; 10–99 → `1` (open mouth) in the second half of each beat; ≥100 → `2` (angry) in the second half; first half always `0`. Beat phase = time since the last active bar line / (60000/bar BPM). Rolls/balloons/big notes use the same folder. (Before: folder 1 was used all the time.)
- Mode select Don-chan idle = `entry_loop` at `entryModeLoopTime` (500 ms; measured period 0.5 s on `2026-11-11 23-40-44.mp4` 108–114 s), falling back to `songselect_loop`. `entry_loop` is baked too.
- Don-chan menu fps: `CharaBaker.FrameStepFor` thins timed menu loops to ≤60 frames/s (songselect_loop was 118 decoded PNGs/s and the stream couldn't keep up); `CharaStream.Ahead` 6 → 10. Caches re-bake automatically (FrameStep changed).

## 11/13 batch 3 (たけらい)
- Skin detail font: `FontManager.ReloadSkinDetailFont` reads `detailFontPath` (TaikøNauts key) or `Font/Default2.ttf|otf`; `DrawDetail`/`MeasureDetail` fall back to the system font. `mainFontPath` is also accepted for the main font. Used for the result best-score gain so far.
- Result vs `2026-11-11 23-46-22.mp4` 90 s (same song as たけらい's screenshot): soul gauge scale 0.69 → 0.70 at GaugeX 79 (left end was 8 px off), 「魂」 grey (`Soul_Text.png` row 0) unless max on the result only, gauge drawn with the panel (not at RevealTime), nameplate (16,950) → (37,932) (the dark shadow on its right was the panel showing through). Difficulty tab, judge labels, Don-chan matched (0 px).
- Play screen: `CharaBaker.Paused` stops background baking while PlayScene is active (suspected GC hitches → hits felt simultaneous).
- Song counter (`UI/SongCounter.cs`): 「N曲目 / 3曲」. `LocalSession.SongsPlayed` counts songs started since entry (not dan/training, not restarts); number wraps every 3 (TaikøNauts 1 play = 3 songs; we stay free play). Song select plate (1612,232) shows the next song, result plate (1631,2) the current one: `99.Common/SongCountSystem/Plate.png` + `Number_Large` at (+48,+19) right-aligned to +84, `Number_Small` at (+188,+29). Play screen: `04.EnsoGame/Genre/Song.png` (count) and `Genre/Bar.png` (genre name) at (1584,99), alternating every 2 s with 0.2 s cross-fade, count first (measured on `2026-11-11 23-46-22.mp4`). Genre = song's category Genre or Title (none for 「その他」).
- Song titles in the play screen, the loading screen and the difficulty view use the skin detail font (`MenuDraw.OutlinedDetail`) with a pure black outline (play 6 px, loading 7 px, difficulty view 6/4 px); loading subtitle is black without outline; open-bar subtitle black outline. List bar titles keep the main font. Measured on `2026-11-11 23-46-22.mp4` 2 s (play) and `23-44-20.mp4` 106 s (loading) / 80 s (difficulty).
- Result best score (`ResultScene.DrawBestRecordImages`): A-Style `05.Result/BestScore_Plate.png` at (71,272) (lifted with the existing timeline), `BestScore_Number.png` 15×18 digits every 14 px right-aligned to plate+394 at plate+14, 「ベストスコア更新!」 24 px white/black centred at plate+(142,23.5), `BestScore_Effect.png` flash centred on the plate, `BestScore_SlashEffect.png` sweep clipped to the plate. Code-drawn ribbon stays as the fallback. Measured on `2026-11-13_09-41-48.mp4` 110 s.
- Branch popup (`PlayBranch.DrawPopup`): measured on `2026-11-13_09-41-48.mp4`/`09-43-58.mp4` 71 s per 1/60 s: centre-anchored scale 1.0→1.2 with fade-in in 4 frames, hold 1.2 to 8 frames, back to 1.0 at 13 frames, shown until 0.9 s, fade out in 5 frames (no scale). Position (265,181) confirmed.
- Song-list box open / back from difficulty in `2026-11-13_09-37-49.mp4` (61.6 s / 80.5 s) match the existing BarList timings (open 0.43 s after stop, ~0.1 s; back: instant swap, open after ~0.43 s). That recording freezes in places, so the genre flip keeps the 10/30 measurement.
- Code-drawn bars (no skin image): imitate A-Style `02.SongSelect/Bar/1.png`: black 7 px ink, 4.5 px light band (top, left, right upper half), 4.5 px darker band (bottom, right lower half), outer radius 30. No shadow on them (the image bars have none); the Simple-skin (Bar_Base) bars keep a 2-layer smooth shadow instead of the old 5 stacked rectangles.
- Play-screen hitches: `PlayScene.PreloadPlayImages` (called in Enter) loads every PNG in Notes/Normal/0–2, Popup, Effects, Lane, Gauge, MiniTaiko, Genre, Scoreboard, End via `SkinManager.PreloadFolder` (non-recursive, same keys as GetTexture). Before this they loaded on first use mid-song (combo 10/100 faces, first roll popup, GoGo, rainbow, best/branch popups). `GetNoteTexture(int)` uses precomputed path strings; `TryGetDrumPart` no longer allocates (no Enum.GetValues/LINQ per hit).
- Mode-select Don-chan idle: play-screen `normal` looped at `normalMotionBeat` × 500 ms (measured on `2026-11-13_09-37-49.mp4` 23.6–39 s: closed-mouth smile, one nod every 0.5 s, no jump). Falls back to songselect_loop. TN also drops Don-chan in from the upper left at 23.45–23.65 s (not implemented).
