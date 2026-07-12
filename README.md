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

## Mapping

The companion **Sticks Mapper** in `Mapper/index.html` provides circular flick, hold, slider, and chord placement with local audio playback, timing-aware snapping, exact-value inspection, and project JSON save/load. Timing authored in lazer's standard editor can be imported from a `.osu` difficulty, including multiple BPM sections, inherited points, effects, and its saved beat divisor. Its **Export .osz** action packages the song and a portable authored map for direct import into an unmodified osu!lazer client.

Authored gameplay data is versioned inside ordinary mode-0 carrier objects. The Sticks converter recognises those markers and reconstructs angles, sides, durations, slider arcs, and repeats exactly; unmarked standard maps continue to use procedural conversion.

Player-replay recording support remains incomplete in this prototype.

## Repository layout

- `osu.Game.Rulesets.Sticks/`: ruleset implementation.
- `osu.Game.Rulesets.Sticks.Tests/`: unit and format-compatibility tests.
- `Mapper/`: standalone authored-map tooling and its documentation.

The project is licensed under the MIT License. See `LICENCE`.
