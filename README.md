# Sticks for osu!lazer

Sticks is a standalone external ruleset prototype for dual-analogue controllers. It requires no osu!lazer patches.

- Left stick: blue outer ring.
- Right stick: red inner ring.
- Flick notes require returning to neutral, then crossing outward at the target angle near the hit time.
- Circular sliders require continuously following the displayed angular path with the assigned stick.
- Directional holds require acquiring an angle and sustaining it; blue duration rails extend outward and red duration rails extend toward the centre.
- Standard circles convert to flicks. Standard sliders and other duration objects convert to generated circular slider patterns.
- Source hold notes and spinners convert to directional holds.
- Autoplay drives both analogue sticks at the current render update rate, including neutral flick preparation and continuous slider/reversal tracking. Interpolated replay positions go directly to the playfield without synthesising thousands of logged framework joystick events; physical controllers still use lazer's normal joystick path.

Approach Rate is a persistent player preference rather than beatmap difficulty. Set it under **Settings → Rulesets → Sticks**, or use lazer's standard decrease/increase scroll-speed bindings (F3/F4 by default) during gameplay. The default is AR 5 / 850 ms, and the selected value is reused across maps without requiring a mod.

The converter treats the two sticks as separate resources: simultaneous notes split across them, notes during a slider prefer the free stick, and ordinary notes form short hand phrases rather than naïvely alternating every object. Slider arcs are quantised from rhythm duration and source gesture direction; they do not trace Tau's polar slider representation.

## Build and install

Requirements: .NET 8 SDK and an osu!lazer installation compatible with ruleset API 2026.702.1.

```powershell
dotnet restore .\osu.Game.Rulesets.Sticks.sln
dotnet test .\osu.Game.Rulesets.Sticks.sln -c Release
```

Copy the resulting DLL into the `rulesets` directory inside lazer's data folder, then restart lazer:

```text
osu.Game.Rulesets.Sticks\bin\Release\net8.0\osu.Game.Rulesets.Sticks.dll
```

Use **Settings → Open osu! folder** if you do not know where lazer's data folder is.

## In-client editor

Sticks has a native circular composer inside osu!lazer. To start from an imported song:

1. Select an osu!standard difficulty for the song.
2. Open **Settings → Rulesets → Sticks**.
3. Choose **Create blank Sticks difficulty** to keep the song, metadata, and timing but map from scratch, or **Create editable converted Sticks difficulty** to use the converter as a starting point.
4. The new database-backed Sticks difficulty opens directly in lazer's editor.

Use the left editor toolbox (or number keys) to select Flick, Hold, or Slider:

- Flick: click the blue outer lane for the left stick or red inner lane for the right stick.
- Hold: press on a lane, drag radially in the displayed duration direction, and release.
- Slider: press on a lane, trace the circular arc, and release.
- Selected hold/slider: Ctrl + wheel changes duration by the active beat snap.
- Selected slider: Shift + wheel changes the repeat count.
- Drag selected objects around the ring to change their angle. A single object may also cross between the two stick lanes; grouped selections preserve their angular pattern.

Normal editor save, Ctrl+S, undo/redo, dirty-state warnings, timing, setup, timeline, test-play, copy, and paste remain available. Same-time objects only replace an existing object on the same stick, so opposite-stick chords can be authored normally.

lazer's stock **Create new difficulty** path attempts to encode an external ruleset before its composer exists. Use the two Sticks settings actions above for the initial difficulty; once created, it behaves like a normal editable difficulty.

Authored gameplay data is versioned inside ordinary mode-0 carrier objects. The Sticks converter reconstructs angles, sides, durations, slider arcs, and repeats exactly when the difficulty is reopened or shared; unmarked standard maps continue to use procedural conversion. This provides editor persistence without patching osu!lazer.

The earlier companion **Sticks Mapper** remains available in `Mapper/index.html` as an optional portable authoring tool. It supports local audio, timing import, project JSON, and `.osz` export, but the in-client editor is now the primary mapping workflow.

Player-replay recording support remains incomplete in this prototype.

## Repository layout

- `osu.Game.Rulesets.Sticks/`: ruleset implementation.
- `osu.Game.Rulesets.Sticks.Tests/`: unit and format-compatibility tests.
- `Mapper/`: standalone authored-map tooling and its documentation.

The project is licensed under the MIT License. See `LICENCE`.
