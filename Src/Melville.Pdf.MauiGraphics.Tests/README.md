# Melville.Pdf.MauiGraphics.Tests

Regression tests for the Maui.Graphics renderer. They run on the Skia backend (`Microsoft.Maui.Graphics.Skia`), on `net10.0` and `net11.0`.

## What runs by default

| Test | Checks | Fails when |
|---|---|---|
| `ReferenceComparisonTest.MatchesTheSkiaSharpRenderer` | Every reference document against the existing SkiaSharp renderer. Both are rendered at 4× and box-filtered down, so anti-aliasing differences cancel. | More than 2% of pixels differ. Three text-clip documents are listed as known errors **in the SkiaSharp renderer**. |
| `RenderBaselineTest.MatchesTheBaseline` | A hash of every reference document as this renderer draws it, against `Baselines/maui-graphics-skia-<os>-<arch>.tsv`. Both target frameworks use the same file. | The output changes at all. Skipped on a platform with no baseline file. |
| `DrawingTest.*` | Dash conversion and phase, display-list recording and replay, image resampling. | |

The whole project runs in about 10 seconds.

## When a baseline test fails

1. Look at the change: `MELVILLE_DUMP="-DocName" MELVILLE_DUMP_DIR=/some/folder dotnet test ... -- --filter-method "*DumpSideBySide" --explicit on` writes a PNG with this renderer, the SkiaSharp renderer, and the differing pixels in red.
2. If the change is intended, re-record: `MELVILLE_RECORD_BASELINE=1 dotnet test ... -f net10.0 -- --filter-class "*RenderBaselineTest"`.
3. Then run without the variable on **both** `net10.0` and `net11.0`: they must match the same file.
4. Review the baseline diff in the commit; it lists exactly which documents changed.

To add a platform (for example Windows or Linux CI), record on that platform and commit the new file.

## Diagnostics (explicit, not run by default)

Run these with `--explicit on`. They write their results to `MELVILLE_DUMP_DIR` (or the temp folder).

| Test | Use |
|---|---|
| `ReportDifferencesForAllDocuments` | A TSV of the difference for every document, largest first |
| `DumpSideBySide` | Side-by-side images for the documents in `MELVILLE_DUMP` (width from `MELVILLE_DUMP_WIDTH`) |
| `PrintInkBounds` | Bounding box of drawn pixels in both renderers, to tell misplacement from rasterization differences |
| `TimeRecording` | Recording time against a SkiaSharp render for one document |
| `CountDistinctRenders` | Renders one document 300 times (`MELVILLE_PARALLEL` at once) and counts distinct outputs |
| `OpenEncryptedDocumentsRepeatedly` | How often encrypted reference documents fail to read back (an upstream bug) |

## On devices

The native backends (CoreGraphics, Android) can't run here. `Melville.Pdf.Maui.Sample` runs the same comparison on a device or simulator at startup and logs `[FIDELITY]` lines, plus a `[FIDELITY-SUMMARY]` line, with side-by-side images for any document above 1%. See `spikes/README.md` for the results so far.
