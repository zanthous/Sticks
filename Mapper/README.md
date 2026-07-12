# Sticks Mapper

A dependency-free browser mapper for authoring native Sticks patterns without modifying osu!lazer.

## Launch on Windows

The quickest option is to open the repository's `Mapper` directory and double-click `index.html`.

```text
Mapper\index.html
```

If your browser restricts local files, open PowerShell in this folder and run:

```powershell
py -m http.server 8765
start http://localhost:8765
```

No packages, build step, or internet access are required.

## Basic workflow

1. Choose **Load audio** and select the song.
2. For a pre-timed song, choose **Import .osu timing** and select a standard `.osu` difficulty made in lazer. The mapper imports every valid timing row and the editor's beat divisor. Otherwise, enter a manual BPM and first-beat offset.
3. Seek with the song bar, timeline, arrow keys, mouse wheel, or snap buttons.
4. Choose Flick, Hold, or Slider. The outer blue ring is the left stick and the inner red ring is the right stick.
5. Click to place flicks and holds. Drag around a ring to place a slider and define its direction/arc.
6. Select objects in the playfield, timeline, or object list. Edit exact values in the inspector.
7. Use **Save project** frequently. The `.sticks.json` file stores mapping data but not the audio.
8. Use **Export .osz** to package the current project, generated mode-0 `.osu`, and selected audio for import into osu!lazer.

When reopening work, load the project JSON first and then reselect its audio file. Loading a project deliberately clears any previously selected song so the wrong audio cannot be packaged by accident.

## Reusing lazer's timing editor

The recommended timing workflow is:

1. Create or open an osu!standard difficulty in lazer.
2. Set its BPM changes, meter, inherited timing, sample settings, volume and effects with lazer's timing tools.
3. Choose **File → Export → Guest difficulty (.osu)**, then select that file with **Import .osu timing** in Sticks Mapper.
4. Author Sticks objects and export the finished `.osz`.

Imported uninherited timing points control snapping, timeline beat markers and beat-based default object lengths in each song section. Inherited rows are preserved for export in their original order and precision. The current imported source and active BPM are shown above the timing controls. While imported timing is active, manual BPM/offset fields are disabled; **Use manual timing** clears the import and starts from its first BPM section.

Projects save the complete imported timing data in the `.sticks.json` file. Manual and imported timing use the same project format.

Keyboard shortcuts:

- `Space`: play/pause
- `Left` / `Right`: seek one snap
- `1`: flick tool
- `2`: hold tool
- `3`: slider tool
- `4` or `V`: select tool
- `Delete`: remove selected object

## Portable `.osu` representation

The exporter writes a standard mode-0 beatmap tagged `sticks-v1`. This keeps the archive importable by an unmodified osu!lazer client; the Sticks ruleset performs the authored conversion. Imported `[TimingPoints]` rows are written back verbatim; manual projects receive one generated uninherited timing point.

- Polar centre: `(256, 192)`
- Left stick: radius `160` (outer / blue)
- Right stick: radius `105` (inner / red)
- Flicks use mode-0 hit-circle carriers. Holds and sliders use spinner carriers so stock lazer can see their duration for map length/statistics. Gameplay properties remain losslessly stored in the custom sample filename.
- Flick: `sticks-v1~f~l|r~angle.wav`
- Hold: `sticks-v1~h~l|r~angle~duration.wav`
- Circular slider: `sticks-v1~s~l|r~angle~duration~arcAngle~repeatCount.wav`

The custom sample field always uses `0:0:0:100:<marker>`. The Sticks ruleset removes this marker when it reconstructs the authored gameplay object.

## Export boundary

All archive-specific work is isolated in `exportMapPackage()` in `mapper.js`. It returns `{ blob, filename }` and currently produces an uncompressed, standards-compliant ZIP/`.osz` using a local CRC32 implementation. Future format revisions can replace that function without coupling the mapper UI to archive generation.
