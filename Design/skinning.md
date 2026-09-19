# Skinning Sticks

These assets customise the centre-out playfield and notes in gameplay and the editor.

Import [Sticks Minimal.osk](<skin-example/Sticks Minimal.osk>) into osu!lazer, then
select **Sticks Minimal** in the skin settings. Enable **Show cursor trails** under
**Settings → Rulesets → Sticks** to see its trail artwork. The skin works in gameplay
and the editor, including placement previews.

To make your own, put the PNGs below beside a `skin.ini`, zip the files at the root
of the archive, and rename the archive from `.zip` to `.osk`. Every image is optional:
missing Sticks assets retain the normal Sticks appearance. Existing skins without
these assets continue to work. The usual beatmap skin and colour settings still
control whether a map's skin takes precedence.

## Images

Names below omit `.png`. `@2x.png` versions are supported: double the pixel dimensions
while keeping the same artwork layout. A transparent image deliberately hides its
element; omitting the image restores the default.

| Filename | Logical size and layout | Appearance |
| --- | --- | --- |
| `sticks-playfield` | 640 × 640; centre (320, 320), judgement circle radius 230 | Replaces the whole guide-ring artwork. Keep the interior transparent: this layer sits over slider bodies and beneath note heads. |
| `sticks-playfield-background` | Same 640 × 640 canvas | Optional interior/background art behind all notes, paths and the guide ring. |
| `sticks-cursor-left`, `sticks-cursor-right` | Native image size; image centre follows the stick. Around 24 × 24 is a useful starting point. | Full-colour artwork for each stick, replacing its built-in disc, border and shadow. |
| `sticks-cursortrail-left`, `sticks-cursortrail-right` | Native stamp size; try 12–24 px square | Full-colour trail stamp for each stick. Repeated along cursor movement with the existing short fade. |
| `sticks-note-centre` | 22 × 22 box, centred on the hit point | Replaces the central tick on flicks and stationary slider heads. Tinted with the note colour. |
| `sticks-slider-head` | 22 × 22; draw pointing right | Replaces the centre of a moving slider head. Rotates to show its travel direction and takes the note colour. |
| `sticks-slider-reversal` | 22 × 22; draw pointing right | Same layout for reversal markers; points in the required outgoing direction. |
| `sticks-double-note` | 36 × 40, centred | Replaces the exact-stack diamond collar. Tinted with the overlap colour; disappears when the first head is judged. |
| `sticks-click` | 465 × 465; centre (232.5, 232.5), ring stroke midpoint radius 230 | Full click halo, tinted with its stick colour or the overlap colour. Expands to the timing ring. |
| `sticks-judgement` | 10 × 10, centred | Replaces non-perfect judgement dots. Tinted by result; retains the immediate 600 ms fade and note-relative position. |

Apart from cursors and trail stamps, images fit the specified box regardless of their
pixel resolution. Note-centre images shrink near the playfield centre or with very
narrow hit windows so they remain inside the visible angular band. Timing, acceptable
angles, approach rate and stick positions are independent of the skin.

Stick-specific cursors/trails retain their authored colours. Note-marker images
are multiplied by their gameplay colour; use white
for the bright parts of a tintable image. Guide-ring and background art retain their
authored colours. Ordinary osu! `cursor` and `cursortrail` assets are not substituted
for these Sticks-specific slots.

## Colours

Optional entries in `skin.ini` use RGB values from 0 to 255:

```ini
[General]
Name: My Sticks Skin
Author: Your name
Version: 2.7

[Colours]
SticksLeft: 51,190,234
SticksRight: 255,103,139
SticksOverlap: 192,92,255
```

**Use skin colours** is enabled by default in Sticks settings. A colour absent from
the skin uses your saved Sticks colour. Turn this option off to use your saved colours
for all three roles, including slider bodies and built-in cursors and trails. Custom
cursor and trail assets still retain their artwork.

## Scope

Skin changes refresh existing objects and previews; assets from the previous skin are
removed when the new skin does not supply them. Cursor trail history clears on a skin
change. Trails keep their bounded stamp buffer and existing fade rather than allocating
a drawable for each sample.

Sticks skinning supports image assets and the three gameplay colours. Angular hit-band arcs,
slider bodies, particle effects and editor handles keep their procedural geometry;
slider paths take the skin palette. It does not add movable components to lazer's skin
layout editor or custom gameplay shaders. Existing hitsound skinning remains available.

The example's source images, `skin.ini` and a dependency-free
[generator](skin-example/generate.py) are included. Run `python3 Design/skin-example/generate.py`
from the repository root to rebuild them and the `.osk` archive.
