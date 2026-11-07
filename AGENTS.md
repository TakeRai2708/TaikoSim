# AGENTS.md — Handoff guide for AI assistants working on TaikoSim

This file is for any AI (Claude, ChatGPT / Codex, etc.) that picks up work on TaikoSim.
Read all of it before you start.

**Also read `TAIKONAUTS-SPEC.md`.**
That file is a digest of the official TaikøNauts documentation (50 pages): "how the original is built".
The **correct file formats** live there: dan-dojo `dan.json`, Don-chan `Config.json`, `category.def` / `order.def`, `SONGGENREID`, etc.
This file (AGENTS.md) is where we record **values we actually measured** from videos and images for TaikoSim.

> Language note: this file is in English so that AI agents can read it quickly.
> The user (たけらい) is Japanese. **All replies to the user must be in Japanese** (see §2).
> Code comments in the repo are written in plain, easy Japanese — keep that style when you add comments.

---

## 1. Project overview

- **TaikoSim**: a Taiko no Tatsujin simulator. Personal use only, **private. Never publish it.**
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
  dotnet build TaikoSim/TaikoSim.csproj -p:EnableWindowsTargeting=true
  ```
  (needs NuGet for Raylib-cs and ManagedBass)
- Without NuGet access use `tools/build-check/` (see its README.md): `./setup.sh` fetches the Raylib-cs 8.1.0 and ManagedBass sources,
  then `dotnet build Check.csproj` compiles them together with the TaikoSim sources (only the Windows Forms FolderBrowserDialog is replaced by an empty class in `stub.cs`)
- Since visuals can't be run, build mock-ups in Python (Pillow) by compositing skin images and compare them side by side with frames from たけらい's videos
- `bass.dll` etc. are committed to the repo

### Anti-decompilation: Native AOT (compile the exe to machine code so no C# IL ships inside it)
- たけらい wants the exe given to friends built with **Native AOT** (so ILSpy etc. can't read it)
- **A normal `dotnet build` does not do this.** Build the exe from Visual Studio or with:
  ```
  dotnet publish TaikoSim/TaikoSim.csproj -r win-x64 -c Release
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
  (independent flip?): Claude's computer-use screenshots of TaikoSim stayed stale and clicks were judged to land on "the desktop". OBS window capture was fine
- OpenTaiko (Silk.NET + GLFW, rendering through ANGLE; Windows default OpenGL) also has only `WindowState.Normal` (framed) or `WindowState.Fullscreen`
- Fullscreen = `ToggleFullscreen` at the monitor's native size (no mode switch; the 1920×1080 canvas is scaled). The resolution setting applies to window mode only
- A window that doesn't fit the monitor is shrunk (keeping 16:9) and centered. `GameConfig.FullscreenType` is kept only so old config files load; the dropdown is gone
- **Also: computer use's "open application" launched a NEW TaikoSim each time instead of focusing the running one** (several Debug instances piled up). Focus TaikoSim by clicking it

### KeyProbe (Debug builds only, 11/8)
- `Diagnostics/KeyProbe.cs` writes `Logs/KeyProbe_<date>_<time>.txt` next to the exe: Windows key/char/focus messages seen by a WH_GETMESSAGE hook on the main thread,
  Raylib `IsKeyDown` changes for DFJK / ERUI / Enter / Esc / Space / arrows, and focus / foreground-window / fullscreen changes.
- Added because keys sent by Claude's computer use reached TaikøNauts but not TaikoSim; the log shows where they stop

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
TaikoSim/
  Program.cs, Game.cs          … startup, main loop
  Scenes/                      … screens
    ModeSelectScene.cs         … mode select (first screen after boot)
    SongSelectScene.cs         … song select (genre → song → difficulty)
    PlayScene.cs               … play screen (judging, notes, lane)
    PlayHUD.cs                 … top of the play screen (score, gauge, title)
    PlayerEffects.cs           … play effects (fireworks, mini taiko, combo number, sparkles, go-go)
    PlayEnd.cs                 … end-of-song effects (clear, full combo, donderful combo, confetti)
    ResultScene.cs             … result
    SongLoading.cs             … song loading screen (curtain)
    DanSelectScene.cs, DanSelectAStyle.cs        … dan select
    DanPanel.cs, DanPanelAStyle.cs, DanShutter.cs … dan play (condition panel, doors between songs)
    DanLoading.cs              … dan loading screen (dojo picture)
    DanResultScene.cs          … dan result
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

---

## 6. Skin rules

- Skins live in `Skins/<skin name>/`. The folder structure is **the same as TaikøNauts**
  - `Image/02.SongSelect/` (song select), `Image/03.SongLoading/` (song loading screen, `Scenes/SongLoading.cs`; see "Song loading screen" in §9)
  - `Image/04.EnsoGame/` (play), `Image/05.Result/` (result)
  - `Image/01.Entry/` (mode select; this is TaikøNauts' real name. Simple still uses `Image/01.ModeSelect/`)
    - Inside: `Bar/Bar/<n>.png` (bars), `Bar/Title/<n>.png` (titles), `Bar/Overlay.png`, `Bar/Selected.png`, `Bar/Pop/<n>.png`,
      `Bar/1POnly.png`, `Background.png`, `Header.png`, `Background_Fireworks.png`.
      **The doubled `Bar/` is not a mistake** (たけらい checked the real thing)
  - `Font/default.otf` (font for song titles and menus)
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
- Output overwrites `TaikoSim/Skins/TaikoSim-Simple/` directly
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

### Mode select (`Scenes/ModeSelectScene.cs`; A-Style `Image/01.Entry/`)
- The list **does not wrap** (stops at the first and last item)
- Bars are spaced **50 px** further apart vertically than song select. When opening they spread **23 px** up and down each
- Bar images are 980×160. When open, only the base is stretched; the title (`Title/<n>.png`) is placed on top at native size
- Only indices 0 ("演奏ゲーム") and 1 ("段位道場") are used.
  A-Style's 2 and 3 are "Player settings" and "AI battle", which differ from TaikoSim's items, so they're unused
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

### Song-select text (`Scenes/SongSelectScene.cs` `DrawHeader` / `DrawEmpty`)
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
  - Position arrays are [1P, 2P]. TaikoSim is single player, so [0] is used
- **Loading them as-is doesn't fit in memory**: 1.87 MB per frame → `normal` (471 frames) alone is 883 MB; the whole play screen is 4.7 GB
- Even trimmed + scaled to 0.81 + every other frame, the 13 play animations are **627 MB of GPU memory** → too much. So (11/7):
  - `Graphics/CharaCache.cs` `CharaBaker`: after every skin (re)load, a **background thread bakes** each animation into
    `Cache/Chara/<skin>/<chara>/<anim>/<n>.png` + `<anim>.json` (trim to the union of all kept frames' alpha box, scale, keep every 2nd frame).
    First run takes about a minute; animations become usable one by one (`CharaBaker.IsReady`). Meta is written last, so an interrupted bake is redone
  - `Graphics/CharaStream.cs`: **one texture per animation**. A worker thread decodes the PNGs for the current frame and the next 6
    (plus the first 4 frames of every animation, kept permanently so one-shots start instantly); the main thread only `UpdateTexture`s (≈300 KB).
    A frame that isn't decoded yet keeps showing the previous one
  - `Image` operations (LoadImage/ImageCrop/ImageResize/ExportImage/LoadImageFromMemory) are CPU-only and run on worker threads. Textures only on the main thread
- Play screen (`Scenes/PlayChara.cs`, 11/7) — port of OpenTaiko `CActImplCharacter` with TaikøNauts motion names:
  - Position: the 820×599 frame's top-left at `ensoGameX/Y[0]` = (-83, -111), scale 0.81. **Checked on video b (11/5) at 26 s: mean pixel diff 2.3** (match)
  - Drawn after the background, before the lane (the lane's top border hides the feet, as in the video). Balloon motions at (213, -8) ×0.95, drawn after the balloon
  - One loop = `motionBeat` beats at the current BPM. `balloon_breaking` has no beat key → plays at the source 60 fps
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
    - `failed_down` was not seen (the song was cleared). TaikoSim plays it when the song ends without clearing (dan: 不合格) — still a guess
- **In dan play Don-chan never uses the clear/max motions** (11/8, 十段 on auto `2026-11-08 03-23-19.mp4`: gauge 100 % rainbow, still `normal`; 10 combo = `10combo`). `PlayChara.Load(isDan)`
- Menu screens (`Graphics/MenuChara.cs`, 11/8) — time-based loops (`…LoopTime` ms per cycle), all checked by template matching on the 11/8 recordings (error ≈ 0):
  - Mode select and song select: `songselect_loop` (236 frames / 1000 ms) at (−185, 436) ×1, drawn behind the bars. Idle never switches to `songselect_wait_loop` (that is the 2-player "waiting" motion)
  - Dan select: `songselect_loop` at (`danSelectX/Y`) = (−190, 445) ×1, behind the panel, after the intro
  - Result: `result_loop` (117 / 500 ms) at (−70, 538) ×0.75 from the fade-in; when the balloon appears (`ClearBalloonTime`) a clear switches to `result_clear_loop` (156 / 700 ms; seen). Fail → `result_failed_in` (200 ms) → `result_failed_loop` (500 ms) (not seen yet)
  - Not done: the jump when a song is decided (`songSelectSongStartJumpIndex` 66), `dan_select_in`, `entry_loop` (unused on mode select), dan result
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
- Dan select: `Scenes/DanSelectScene.cs` (choose the plate with ←→ / ka, category with ↑↓. Decide → confirm popup)
- During play: `Scenes/DanPanel.cs`. `PlayScene` calls `DrawBottom()` (lower half) right after the background and `DrawTop()` after the title.
  The current song's stats go into `DanPlay` every frame via `PlayScene.SyncDanLive()`. The bottom-left scoreboard is hidden during dan
  - With 4+ conditions, pages of 3 switch every 4 s (unknown how the original handles it)
  - Bar colors follow **the same rules as OpenTaiko's `Dan_Cert.UpdateReachStatus`** (`DanPlay.Reach()` → `DanReach`; see the 10/31 九段 video section)
- Result: `Scenes/DanResultScene.cs`. ① per-song breakdown (3.6 s) → ② summary (gauge fills over 1 s, conditions appear 0.3 s apart, stamp)
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
- **Between songs** (`Scenes/DanShutter.cs`; door timings from OpenTaiko `Dan_Cert.cs`): doors close in 1.0 s → stay closed 2.3 s (next song swapped in at 0.8 s) →
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
- End of the last song: on fail, a light-blue 「不合格」 on the lane (small characters on both sides; TaikoSim shows text only) → fade → result. Pass not seen yet
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

#### Dan loading screen (11/5; `Scenes/DanLoading.cs`)
Measured on the 11/5 六段 video (`2026-11-05_09-32-24.mp4`, 17.8–23.6 s). Replaces the song-loading curtain after 「挑戦する」
- Dan select fades out 0.47 s → black 0.6 s → dojo picture fades in 0.47 s → shown 3.2 s (longer if loading is slow; at least 0.3 s after loading) → fades out 0.53 s → play screen fades in 0.5 s (doors closed)
- A-Style: `07.DanLoading/Background.png` (1920×1300) scrolls up at **73.5 px/s** from the start of the fade-in and stops at 220 px (bottom edge). Dan plate (`danPlatePath`) at top right, about x1638–1783, top y162
- Simple: dark gradient, 「段位道場」 and the dan name, plus the plate. Nothing copied from the original picture
- F2 retry still uses the normal song-loading curtain (5 s of dojo picture on every retry felt too long; ask たけらい if he wants otherwise)

#### Dan select drawn with A-Style images (11/1; `Scenes/DanSelectAStyle.cs`)
Positions found by template-matching the images onto frames 34 s (初段) and 50 s (confirm) of video 01-44-20 (OpenCV matchTemplate). Skins without the images keep the code-drawn look
- Plates `MiniPlate.png` (six 80×168 colors: kyu / 初–五段 / 六–十段 / 玄人–超人 / 達人 = gold / 外伝): top-left (190 + danIndex×68 − 8, 26).
  **Empty slots also show the name, with color ×0.5** (the original's 五級–一級 and 玄人–外伝 are exactly half brightness). Names in black 36 px; first character centered 100 px below the plate top, 37 px per character
  - Selected plate: 22 px lower with `Plate_Effect.png` (white edge + 29% white inside) on top. Faint `Plate_Arrow.png` on both sides (−85 / +29 px from the plate center, +47 px from the top; right one mirrored)
  - Passed dans get `Stamp.png` (six 90×76; order is a guess: (combo type − 1)×2 + 1 if gold)
- Panel `Panel.png` at (346,192) (shadow bottom-right; 合格条件 tab and frame included); side patterns `Panel_Side.png` (six 40×806) at (364,215) and (1800,215, mirrored)
- Song bars `SongBar.png` (three 1380×110) at (412, 231 + 110×row). Difficulty mark `Song_CourseSymbol.png` (five 78×52) at (529, 245 + 110×row);
  ★ level center (567, 309); title from x640, center y283, white text with black outline
- Soul gauge box `GaugeCondition_Bar.png` (434,611); condition boxes `Condition_Bar.png` (751, 608 + 132×row);
  contents `Condition_Content.png` (50 px each: coin / 1st / 2nd / 3rd / white bar): coin row (768, 654 + 132×row), white bar (775, 690 + 132×row), per-song columns 331 px apart.
  「814 以上」 right edge x1085, center y679 (soul gauge: right 735, y693). **The gold value is not shown** (the original doesn't either)
- Confirm popup `Challenge_Check/Panel_Base.png` (blue box with the question baked in) and `Panel.png` (buttons), both 1368×488 at native size at (276,296).
  Three buttons: wrench (play options), 挑戦する, 挑戦しない; centers (613.5 / 858.5 / 1196.5, 581.5).
  Cursor `Select_Effect.png` (seven 350×150: large = yellow frame / glow / white frame / white fill; small = yellow frame / glow / white frame). Appearance: dims over 0.2 s → box from 0.75× over 0.13 s → buttons 0.2 s later
- Plate area: `PlateX 145, PlateY 205, W 230, H 485`, fitted with `DanPanel.FitPlate`
- Mini plates always show the slot's standard name (五級…外伝, `DanSelectAStyle.SlotName`), not the dan.json title (11/7: 2026段位道場's 玄人/名人 titles start with 「2026段位道場」)
- No control hints and no category name at the bottom of dan select (たけらい, 11/7)

#### Dan play drawn with A-Style images (11/1; `Scenes/DanPanelAStyle.cs` and others)
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
  - Doors start closing ~2.2 s after the last note (2.25 s and ~2.2 s) — same as TaikøNauts and TaikoSim
  - Doors slam almost shut in ~0.1 s, then close the last gap over ~0.9 s; open in ~0.17 s (TaikoSim keeps the OpenTaiko/TaikøNauts 1.0 s / 0.27 s; たけらい chose not to change it)
  - The next song's title is already behind the doors and visible the moment they start opening (TaikoSim: `DanShutter` draws the title under the doors from the closed phase, 11/6)
  - Next song's 0 s after the previous #END: 8.06 s and 6.98 s (TaikøNauts 10.71/10.84 s). Not constant; maybe the arcade uses the audio length. TaikoSim keeps 10.78 s
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

#### Dan result drawn with skin images (11/5; `Scenes/DanResultAStyle.cs`)
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
    `PlaySpeed` 480×40 (12 frames: 0.5–0.9 / empty / 1.1–1.5 / 2.0). **TaikoSim lacks those features, so they're unused for now**
- `Speed.png` frames 1–14 **exactly match** TaikoSim's `HiSpeedSteps` minus `0.5` and `1.0`.
  No badge at ≤ 1.0×. Values not on a step (typed directly) use the nearest step's badge
- Position: **left panel, right of the difficulty mark** (top-left (170, 353), native 40×40; more badges go to the right).
  Measured on the 10/31 dan video (with AUTO). It used to be guessed under the song title
  - Order/direction with 2+ badges is unverified (we draw speed → AUTO to the right)
- Skins without images (Simple etc.) draw a yellow rounded tag with text (`AUTO`, `×1.5`) instead

### Favorite key
- TaikoSim uses **F4**; TaikøNauts uses F8. **Kept at F4 at たけらい's request** (not matching the original)

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
  - Boot loading → mode select: 0.3 s out → 0.5 s in (`AfterBoot`; TaikøNauts shows an IC-card screen here, so TaikoSim chose these values)
  - Dan select → dan play: `DanLoading` (see above)
  - The current scene's Update is paused while fading out and while black
- Donderful combo: display timing, confetti position (525,162 / right side mirrored at 1367) and speed (30 frames/s) match TaikøNauts
- Text under the notes (SENotes, 口唱歌): ドン notes become ドン / ド / コ and カッ notes カッ / カ depending on the spacing to the 3 notes before and after
  (`Charts/SeNotes.cs`, a port of OpenTaiko `CTja.tSenotes_Core_V2`; decided at load time into `Note.SeKind`; per branch when the chart branches).
  Spacing is in 16th notes, computed from ms × BPM / 15000 (TaikoSim notes have no beat position), so comparisons allow ±0.02.
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
    Within each cycle: dark until 0.27 s → up in 0.06 s → down over 0.17 s. OpenTaiko uses 532 ms (365/448/531 ms), which TaikoSim used before.
    The recording was only ~10–15 fps after 104 s, so the in-cycle shape is approximate; the 0.5 s period is reliable
  - No permanent glow on the tip cell (the skin glow added 10/20 was removed 11/6; `SkinManager.GetGaugeGlowCell` is now unused)
- Soul gauge rainbow: `Gauge/Rainbow.png` 24 frames (3 sets × 8 frames for norma 80/70/60%); each frame is crossfaded into the next
- BASS hit-sound lag: HitSoundLagMs = buffer + update period / 2 (applied to judging only)

---

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

- Song loading screen (`Scenes/SongLoading.cs`; between choosing a difficulty and play; called via `SceneManager.ToPlayWithLoading`)
  - Measured frame by frame on the TaikøNauts video (10/29 A.mp4, 82.4–87.9 s):
    0.4 s wait after deciding → the curtain (left and right halves of `Background_Fade.png`) closes in 0.48 s (constant speed) →
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
  - TaikoSim's own sets (Electro / Clap / WoodBlock) are made by `hitsounds()` in `tools/skin-gen/sounds.py` into `TaikoSim/HitSounds/` (copied next to the exe at build)
- Play options panel (`UI/PlayOptionsPanel.cs`): drawn with A-Style `02.SongSelect/Option/` images. Controls match OpenTaiko's `CActPlayOption`
  - ka / ←→ change the value; don / Enter go to the next item and close after the last (don is ignored until fully open); ↑↓ move between items; number keys type a value; Esc / F2 close
  - Speed (HS) steps match OpenTaiko (0.5, 1.0–2.0 by 0.1, 2.5–4.0 by 0.5). Auto roll: 0,5,8,10,12,15,18,20,25,30,40,50,60,80,100; accuracy in 5% steps (TaikoSim's choice)
  - Look measured on the TaikøNauts video (10/18 `2026-10-18_00-27-01.mp4`, 50–58 s):
    panel at native size at (0, 465) (sized for 8 items; grows/shrinks 61 px per item to match TaikoSim's item count) / top-left 1P mark 54 px (36, 28), 「演奏オプション」 32 px (105, center 55) /
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
  Main suspects with default settings: (1) the judging clock is 17.5 ms ahead of the visuals (nowMs − (AudioOffsetMs − HitSoundLagMs)),
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
- TaikoSim's dependency: **540 `Raylib.*` calls, 35 `Rlgl.*` calls, 38 of 64 files**,
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
  - Checked on TaikoSim itself (11/8 `2026-11-08 04-40-58.mp4`): difficulty → song matches (frame/open 0.45–0.53 s after the switch)
  - 「とじる」/「ランダム」 keep the open genre's background (TaikoSim used to switch to background 0 = rainbow). During the 「とじる」 flip the bar that widens back from the thin line is the destination genre's color (TaikøNauts showed J-POP's light blue)
- Phase 2 of A-Style song select: search, options panel, Oni→Ura transition animation (`Oni_Transition.png`), personal best, crowns
- Older leftovers: logs for the exclusive-fullscreen crash, checking skin coordinates

---

## 10. Things to watch out for

- There are about 3000 songs. **Preload** text and images so scrolling never stalls (`FontManager.PreloadSkinMain` etc.)
- Files are UTF-8. **Keep the BOM on files that have one** (e.g. Python `utf-8-sig` when editing)
- Code comments are in Japanese, in plain, easy words (same style as the existing code)
- Never commit other people's skins or song files
- Raylib scissor mode can't be nested — re-scissor per element (see `DanShutter.Clip`)
