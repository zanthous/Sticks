# Sticks star-rating testbed

- `cases.json` records each calibration map, why it is useful, its preferred range, and the history of difficulty changes made because of it.
- Source beatmaps are not committed. Each case uses the SHA-256 filename already present in lazer's local `files` store.
- The runner converts locally available maps with the current Sticks converter and reports current stars, skill values, and drift from the last recorded milestone.
- Preferred ranges are review guidance rather than hard assertions, so experimental changes remain easy to compare.

Run from the repository root:

```text
dotnet run --project osu.Game.Rulesets.Sticks.DifficultyTestbed
```

The usual stable and development lazer stores are detected automatically. Use `--files-root "path/to/osu/files"` to supply another store; the option may be repeated.

When adding a case:

1. Add the source map's SHA-256 and identifying metadata.
2. Record the specific mechanic and why the current rating feels wrong.
3. Use a broad target range until playtesting supports something narrower.
4. After a meaningful model change, append a milestone with the before/after rating and its design reason.
