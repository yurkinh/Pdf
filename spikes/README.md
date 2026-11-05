# MAUI phase 1 spike: findings

Measured on 2026-11-04 on macOS (Apple silicon), SDK 11.0.100-rc.3, MAUI 10.0.110 (net10.0 targets),
Microsoft.Maui.Graphics 10.0.120. These results feed the phase 3 renderer port.

## How to rerun

| What | Command |
|---|---|
| Canvas checks on Skia | `cd spikes/MauiGraphicsSpike && dotnet run -c Release -- --fix-transform` (omit the flag to see the stock `SkiaCanvas` bug) |
| Canvas checks on a device | Run `Src/Melville.Pdf.Maui.Sample`; results show on screen and in the log as `[SPIKE] ...` lines |
| Runtime benchmarks | From `Src/Melville.Pdf.RuntimeBenchmarks`, with the local SDK first on `PATH`: `PATH=$PWD/../.dotnet:$PATH DOTNET_ROOT=$PWD/../.dotnet dotnet run -c Release -f net10.0 -- --filter "*" --cli $PWD/../.dotnet/dotnet` |

## Canvas behavior by backend

| Check | Skia | iOS / Mac Catalyst (CoreGraphics) | Android | Windows (Win2D) |
|---|---|---|---|---|
| Dash length, pattern d at width w | d × w | d × w (see the correction below) | d × w (see the correction below) | not measured |
| Even-odd and non-zero fill and clip | correct | correct | correct | not measured |
| `IImage` from PBGRA pixels | correct (`SkiaImage`) | correct (PNG fallback) | correct (PNG fallback) | not measured |
| `ConcatenateTransform` | **throws** (bug); correct with workaround | correct | **wrong**; correct with workaround | not measured |
| `PictureCanvas` replay | identical (0 px differ) | identical (0 px differ) | **wrong** (13,724 of 25,600 px differ) | not measured |

APIs that don't exist anywhere: a stroke paint (strokes are solid colours only), an absolute transform setter, and image sampling control (only `Antialias`).

## Bugs found in Microsoft.Maui.Graphics 10.0.x

1. **Skia:** `SkiaCanvas.PlatformConcatenateTransform` calls `SKMatrix.GetValues` with a too-short array and throws `ArgumentException` with SkiaSharp 4. Workaround: subclass and call `Canvas.Concat` (`FixedSkiaCanvas` in `Program.cs`).
2. **Android, three stacked problems:**
   - `ScalingCanvas.ConcatenateTransform` and `Scale` both scale later coordinates **and** forward the transform, so scale is applied twice.
   - `PlatformCanvas.PlatformConcatenateTransform` uses `PostConcat` (device space) instead of pre-concatenating (user space).
   - `Matrix3x2.AsMatrix()` swaps the x and y translation (`MtransY = M31`, `MtransX = M32`).

   Workaround: unwrap `ScalingCanvas` and call `Canvas.Concat` on the native canvas with a correct matrix (`AndroidTransformFix.cs`). `IPicture.Draw` calls `ConcatenateTransform` internally, so `PictureCanvas` replay stays wrong on Android.
3. **Correction (phase 3):** the original spike reported native dashes as one stroke width short. Measuring every run, not just the first, shows only the **first dash at the canvas edge** is short; all later dashes are exactly d × w on every backend. The renderer applies no dash compensation.
4. **Skia:** `StrokeDashOffset` is ignored (`SKPathEffect.CreateDash(..., 0)`), so the renderer applies the PDF dash phase by rotating the pattern.
5. **Skia:** `Antialias = false` only affects strokes; fills are always anti-aliased. The desktop comparison therefore supersamples both renderers.

## Decisions for the port

- **Own display list.** Record the renderer's operations (path fill, stroke and clip, image, transform, save/restore) and replay them through a platform-aware canvas adapter. `PictureCanvas` can't be used, because of Android.
- **Transforms go through one adapter method** with three implementations: Skia uses `Canvas.Concat`; Android unwraps `ScalingCanvas` and calls native `Concat` (on a `GraphicsView`, the display density has to be applied as a native matrix too); Apple calls `ConcatenateTransform`.
- **Dashes:** pass d/w on every backend; apply the phase by rotating the pattern.
- **Images:** native per-platform `IImage` factories in phase 3; PNG encoding works everywhere as the fallback.
- **Tests:** the headless Skia path needs the `FixedSkiaCanvas` workaround.

## Toolchain

| Finding | Effect |
|---|---|
| SDK 11 with the MAUI 11 RC workload builds `net10.0-android` (MAUI 10.0.110) | Works as planned |
| Its `net10.0` iOS and Mac Catalyst packs (26.0.11017) demand Xcode 26.0; this Mac has 27.1 | `ValidateXcodeVersion=false` in the sample. It builds and runs on the iOS 26.5 simulator and Mac Catalyst. Recheck at SDK 11 GA. |
| SDK 10 can't build the core | The core uses C# 15 preview syntax (`closed`) |
| The `net10.0` core libraries run on iOS (simulator, Debug), Mac Catalyst and Android 17 (emulator, Debug) | The 7 benchmark documents render with byte-identical output sizes on all three |

## Runtime benchmarks (net10.0 vs net11.0)

Full BenchmarkDotNet run (default accuracy), 7 documents, re-render and open-plus-render. The machine was heavily loaded (load average 20–38 on 12 cores), and standard deviations were 2–10%. Rerun on an idle machine before deciding.

| Comparison | Result |
|---|---|
| net11.0 (runtime async) vs net10.0 | typically 2–6% faster (ratios 0.94–1.03), a few noisy outliers |
| net11.0 with vs without runtime async | within noise; they trade places across documents |
| Allocations | identical (alloc ratio 0.99–1.00) |

Against the plan's gate (keep net11.0 only if it is at least ~10% better), this run does **not** justify the dual target. The decision is the owner's.

## Phase 3 results (renderer port)

Fidelity against the SkiaSharp renderer, per reference document (239). Both renders are supersampled 4× and compared at 1×; "differ" means a channel differs by more than 48.

| Platform | Backend | Identical | < 0.5% | 0.5–2% | > 2% | Exceptions |
|---|---|---|---|---|---|---|
| Desktop (tests) | Skia | 156 | 63 | 20 | 0 | 0 |
| Mac Catalyst | CoreGraphics | 118 | 117 | 2 | 2* | 0 |
| iOS 26.5 simulator | CoreGraphics | 118 | 117 | 2 | 2* | 0 |
| Android 17 emulator | Android Canvas | 116 | 95 | 27 | 1* | 0 |

\* only the text-clip documents (`-StrokeAndClip`, `-StrokeFillAndClip`, `-FillAndClip`), where the **SkiaSharp renderer is wrong**: its path builder is detached by the paint, so the clip that follows is empty and the clipped fill disappears. The Maui.Graphics renderer clips to the glyphs correctly.

Speed (net10.0, 1024 px, this Mac), recording a page vs a full SkiaSharp render: 0.19–0.67× for text, patterns and shadings; 1.85× for an upscaled JPEG (managed bilinear resampling). Replay, which happens on every redraw, costs 0.2–4.2× a full render (about 2 ms for a text page, 1.5 ms for a tiling pattern). So the planned glyph cache is not needed, but the control should cache the replayed page as a bitmap while the user pans and zooms.

## Phase 5 results (viewer control)

`PdfViewer` (Melville.Pdf.Maui) was checked through DevFlow on Mac Catalyst and on an Android 17 emulator. Both open at fit-page, pinch-zoom around the touch point, pan, and toggle fit/2× on double-tap, rendering at screen density. The replayed page is cached as a platform bitmap (up to 6 MP), refreshed 150 ms after the last gesture. Not checked yet: iOS, and Windows (no machine, and mouse-wheel zoom is not done).

- **ParsingBuffer bug in Melville.StreamInterfaces** (patch 0004). A content-stream token as long as the whole buffer made `ParsingBuffer` ask for 0 more bytes, which failed with ``"minimumBytes" should be between 0 and 0 but is 1``. Pages 3–5 of `-FiltersGenerator` failed in **both** renderers. The new `EveryPageRecords` test renders every page of every document (253 pages); before, only page 1 was tested. This is a candidate upstream fix.
- **DevFlow friction**:
  - Run one `maui devflow batch` per sequence. Separate commands hit "Another DevFlow session is driving this app".
  - The CLI parses decimals with the current culture, so set `LC_ALL=en_US.UTF-8` on decimal-comma locales.
  - On Android the agent did not register with the broker. Use `adb forward tcp:19300 tcp:9223` and `--agent-port 19300`.

## Other findings

- **Concurrent JPEG decodes corrupted each other** (fixed in phase 4). `JpegStreamFactory` returned its pooled output buffer to `ArrayPool<byte>.Shared` while the decoded stream still read it, and the stream returned it again on dispose. The stream also read past the decoded length into stale pool data. Twelve concurrent renders of one JPEG gave about 90 different outputs, with wrong colours, in **both** renderers, and the shared pool spread the damage to unrelated code (a CMap document failed once). Introduced upstream on 2026-10-12. Fixed, with a regression test, on local branch `fix/jpeg-pooled-buffer-ownership` (not yet proposed upstream).

- **Encrypted reference documents** fail to read back 0.3–1.3% of the time (random keys; `PdfParseException: Unknown token` or a PostScript stack overflow in the encryption dictionary): `-EncryptedV6` 4/300, `-EncryptedV1Rc4` 2/300, three others 1/300. This is an upstream writer or parser bug and is likely to affect real encrypted PDFs written by Melville. The renderer tests regenerate such documents up to 3 times.

- Opening a second `PdfReader` document over the **same** `IMultiplexSource` while the first is still open fails for `-StandardFonts` (`PdfParseException: Unknown token: f`). This happens on every runtime. The benchmarks use a separate copy per open; still to investigate.
