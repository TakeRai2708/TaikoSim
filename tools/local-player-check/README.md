# Local 2P test

This is a playable test implementation, not a reference-matched A-Style 2P layout.
The two complete ordinary play screens are displayed side by side at half scale.
Linux verification: source build passed (0 errors, 253 existing warnings); all 48
record/local-session checks passed. Rendering, sound and physical input were not run.

## Automated checks

After building `tools/build-check/Check.csproj`, run:

```sh
dotnet run --project tools/record-check/RecordCheck.csproj
```

This covers record/profile isolation, exclusive crowns, entry order and options
restoration, including exceptions, and old/new key files. It does not simulate gameplay.

## Windows test steps

1. Open initial setup with a fresh test Config directory. The keys page should show
   1P and 2P columns. 2P defaults are left Ka **C**, left Don **V**, right Don **N**,
   right Ka **M**. Change a 2P key, cancel with Esc, then try a duplicate 1P key:
   the old binding must remain. Changes can also be made under F1 > キー.
   Existing 1P bindings take priority if any conflict with the new defaults.
2. At entry, press 1P Don once: only 1P joins. Press 1P Don again: count is fixed,
   fade plays, choose a data slot, then mode select. Repeat with only 2P Don and
   verify menu selection and solo performance use the 2P bindings.
3. At entry, press 1P Don, then 2P Don. Both should join, without advancing.
   Either side's next Don fixes the count. Select each side's data in turn.
   The second side must not be able to reuse the first side's slot. After both
   confirmations, mode select opens. Simultaneous first Don should also join both.
4. Choose normal play and the same song. Each side can select its own difficulty
   (including Ura where present) with its own Ka keys and confirm with Don.
   Open each side's options row to set different speed/auto/tone. Only one options
   panel is open at a time. Tab changes arrow/Enter/F2 focus. Other-side Don must
   not operate the open panel. Both confirmations are required to launch.
5. Check that each side hits only its own notes. The mini taiko, judgments,
   fireworks, roll count/fan and balloon must still draw inside each viewport.
   Test actual key rollover by pressing keys on both sides simultaneously.
   There should be one song playback, with both note streams sharing its position.
6. F5 (or the configured quick-restart key) restarts both sides. Test a chart with a
   negative first bar. Esc aborts both without saving score/crown results.
7. Finish a song with different difficulties, then check each profile's best score,
   crown and rank. Auto must not update that side's best record. With different
   chart end times, the earlier finisher must wait for the later side, without
   stopping music or blacking out the other side.
8. Verify solo normal play, training and dan still work. Training/dan are currently
   blocked when two participants have joined. Separate play options remain for
   the entry session only; they do not replace the shared GameConfig.json.

Windows Native AOT publishing also remains unverified here.

## Record display and mirrored menu positions

- Under Settings > General > Record display, choose Easy / Normal / Hard / Oni /
  Oni + Ura and apply. The count panel should change immediately and retain the
  choice after restart. Hovering difficulty must not change the chosen count group.
  Individual song records remain separate; combined counts include both charts.
- Join both sides with different data. In song selection, the left and right panels
  must show each side's own counts and personal best, with upright text/digits.
  The 2P difficulty screen now also shows per-course crowns/ranks and side panels.
- Mode and song selection should have a character for each joined side; 2P is on
  the right, facing inward. Check 2P-only entry too. Skin-supplied 2P coordinates
  take priority; a single shared coordinate falls back to mirroring across 1920px.
- First save to a fresh data slot must create PlayerData/2 or /3 automatically.

The current automated total is 64 checks. Windows visual checks above are pending.

## Menu BGM (current automated total: 82)

1. With A-Style active, entry and mode selection should play
   `Sound/BGM/Entry.ogg`. During player-data selection and the transition to mode
   selection, the song must continue from the same position.
2. On the genre list, `Sound/BGM/SongSelect.ogg` should repeat at about 4.06 seconds
   (the short supplied clip includes a gap before the next repetition).
3. A song preview should replace this BGM. In difficulty selection, move the cursor
   onto **Back**: preview stops and SongSelect.ogg restarts. Move back onto a
   difficulty: the preview returns. Repeat on the song list's Close/Back row.
4. During preview loading and the transition into 2P difficulty or performance,
   there should be no brief burst of menu BGM. If a preview cannot load, BGM should
   remain available. 2P difficulty now also previews the shared selected song.
5. Adjust Master/BGM separately from Music/Voice; test F1 settings, a skin switch,
   audio-output restart and game exit. Other scenes' own music must not overlap
   the shared menu music. Missing BGM files must not cause errors or repeated loads.

Controller tests use fake streams and do not substitute for these native audio checks.
