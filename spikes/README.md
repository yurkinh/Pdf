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

## Phase 6 results (sample app)

The sample now has four tabs, plus a viewer page opened from them:
- **Library**: 10 bundled PDFs and Open file….
- **Gallery**: all 239 reference documents, with a filter.
- **Diagnostics**: timings for one page, Maui.Graphics and SkiaSharp renders side by side, and a smoke test that records every page of every document.
- **Checks**: the phase 1 canvas checks.

The bundled PDFs (`Resources/Raw/samples`) are written by `Src/tools/BuildSampleCorpus.cs`, a file-based app, from original generated content and reference generators, so there are no licensing questions. `SampleCorpusTest` checks that every bundled file is indexed and records on every page, that the encrypted one needs its password, and that the layers document has three layers that start visible and can be hidden.

Smoke test (every page of all 249 documents, Debug builds): **0 failures** on Mac Catalyst (230 s), the Android 17 emulator (139 s) and the iOS 26.5 simulator (119 s). The encrypted sample opens with its password on all three.

Checked on Mac Catalyst through DevFlow: Library, Gallery filter and open, encrypted document (wrong password → retry prompt → correct password opens; Cancel → error message), layers panel toggling, and Diagnostics.

- **Password prompt**: a modal page with a masked `Entry`, because `DisplayPromptAsync` cannot hide input. Its `TaskCompletionSource` must use `RunContinuationsAsynchronously`. Otherwise the reader resumes inside the button handler on the UI thread, and a wrong password hung the app (it was then killed by an unhandled exception in a finalizer).
- **Optional content (upstream)**: a group without `/Intent` starts hidden. The spec's default is `/View`, but upstream's `OptionalContentParserTest.DefaultVisibilityAsync` expects hidden, so it is left as is. Such groups are still drawn until a layer is toggled, so the layers panel and the page disagree. The sample states `/Intent /View` and `/BaseState /ON` explicitly.
- **Layers panel**: `MeasureFirstItem` truncated longer layer names to the first row's width. Removed.
- **DevFlow**: `ui tap` does not trigger CollectionView selection, so rows use a `TapGestureRecognizer` command. Tapping by `--text` hits the label, which has no handler, so the script taps the row (the label's parent). Each separate CLI call can hit the session lock, so one long-lived `maui devflow batch` process is driven from a script.
- **Not done**: CJK samples (the CJK font library is 468 MB), "Open in…" document types on iOS/Android, packaged MSIX on Windows (still unpackaged; no Windows machine). CI builds the sample but runs no device tests.

## Phase 11 results (continuous scroll)

**What it does.** `PdfView.DisplayMode="Continuous"` shows every page in one scrolling strip, vertical or horizontal (`ScrollOrientation`). The toolbar has a Scroll/Pages toggle. `SinglePage` stays the default.

**View model.**
- `ContinuousLayout` is pure and unit tested: pages stacked with an 8-unit gap and centred across the strip; one scale from the largest page; centring when the content is smaller than the view; clamping; hit-testing (`ToPage`); `PagesInView(margin)`.
- In continuous mode the view model renders only the pages in view and their neighbours (up to one viewport away), one at a time, nearest first. It forgets pages more than three viewports away.
- Scrolling updates `PageNumber` without jumping; navigation scrolls to the page.
- Zoom keeps the point under the finger, and fit modes keep the top-left page point.
- `ViewState` is the page and page point at the top-left, so save and restore work in both modes.
- Both modes publish `VisiblePages`, which the control draws.

**Melville.Pdf change:** `DocumentRenderer.GetPageBoxAsync(page)` returns the visible box (crop box after rotation) without rendering, so every page is measured up front. It's additive.

**Control.** The drawable draws a list of placements. Each page has its own backdrop and detail bitmaps, and the 6 MP budget is shared by the pages in view (at least 1.5 MP each). Bitmaps of pages that leave the view are freed.

**Bug found and fixed:** the new toolbar toggle set its `AutomationId` twice, and MAUI allows only one, so *every* `PdfView` threw during construction. Shell swallowed the exception, so opening a document simply did nothing.

**Checked.**
- Tests: 63 view-model tests, including 16 new ones. 10 repeated runs were stable.
- Mac Catalyst through DevFlow: strip, scrolling, page tracking, Last page jump, zoomed-out multi-page view.
- Android emulator: strip at screen density; the page number follows scrolling.
- The Native AOT publish still has zero IL warnings.

**Not done.** Kinetic (fling) scrolling, page-turn animation, and a two-page spread mode.

## Phase 9 results (packaging as Plugin.Maui.Pdf)

**Rename.** The control now follows the Plugin.Maui.* conventions:
- `Melville.Pdf.Maui` → **`Plugin.Maui.Pdf`**: namespace `Plugin.Maui.Pdf`, control `PdfView`, registration `builder.UseMauiPdf()`, options class `PdfViewOptions`.
- The XAML namespace `https://github.com/yurkinh/Pdf/maui` (prefix `pdf`), also registered in .NET MAUI 10's global XAML namespace.
- The shared `PdfViewerViewModel` and the WPF `PdfViewer` keep their names.

**Apps.**
- `Plugin.Maui.Pdf.Sample` (new, from the `dotnet new maui` template, all four platform heads) is what a user copies: one page with a `PdfView`, bundled PDFs, and Open file….
- The former sample is now `Plugin.Maui.Pdf.TestApp` (Library, Gallery, Diagnostics, smoke test, autorun).
- The new sample was built for Android, iOS and Mac Catalyst, and run on Mac Catalyst. Windows is built in CI only.

**Package.**
- `./pack-nuget.sh` writes `artifacts/nuget`: `Plugin.Maui.Pdf` 0.1.0-preview.1 (with README, icon, MIT, symbols and SourceLink), plus the 12 Melville.Pdf libraries it depends on at `0.6.9-maui.1`, and the local Melville.StreamInterfaces packages.
- Packing on macOS gives Android, iOS and Mac Catalyst assemblies. CI packs on Windows, which can build all four.
- `tools/test-package-consumer.sh` builds a new template app with the installed .NET 10 SDK (10.0.403) against the package. It passed for Android and iOS, and the app rendered a PDF on the iOS simulator.

**Not published.**
- `release-nuget.yml` only runs for a published GitHub release with the repository variable `PUBLISH_NUGET=true`.
- It cannot work yet: the `Melville.*` IDs on nuget.org belong to upstream, so their packages must come from upstream, take other IDs, or be bundled into Plugin.Maui.Pdf.

## Phase 8 results (platform hardening)

**Trimming and AOT.** The libraries now build with no IL warnings:
- CSJ2K's `J2kSetup` searched its own assembly by reflection for four interfaces. It only ever found two classes, now constructed directly, and the reflection is gone. (Correction, phase 9: nothing references the in-repo `Melville.CSJ2k` project any more. `Melville.Pdf.LowLevel` uses the CoreJ2K NuGet package, which was already part of the clean AOT publish.)
- `StackTraceClipper` uses the trim-safe `DiagnosticMethodInfo`.
- `GeneratorFactory.AllGenerators` is `[RequiresUnreferencedCode]`. Trimmed apps use `KnownGenerators.All`, generated by `Src/tools/ListReferenceGenerators.cs`; `KnownGeneratorsTest` fails if the list is stale.

The sample publishes with **Native AOT and full trimming on Mac Catalyst with zero IL warnings** (`-p:MelvilleNativeAot=true`), and CI now enforces this. Two changes were needed in the control and sample:
- The page entry no longer uses `Binding.DoNothing`, which is not trim-safe; it now commits on Enter or focus loss.
- The menu bindings are compiled.

net10.0 iOS simulator Release with `TrimMode=full`: the only warning is IL2104 from `Microsoft.Maui.Controls` itself.

**Release runs.** These use the sample's unattended autorun (`MELVILLE_AUTORUN=1` or the Android intent extra `melville_autorun`), which writes `[BENCH]` and `[SMOKE]` lines. All runs: 249 documents, 382 pages, **0 failures**.

| Build | Runtime | Smoke test | Text page: record, replay | Skia (same page) |
|---|---|---|---|---|
| Mac Catalyst net10 Native AOT | NativeAOT | 2 s | 10 ms, 135 ms | 12 ms |
| iOS 26.5 simulator net10 Release | Mono | 9 s | 55 ms, 140 ms | 58 ms |
| iOS simulator net11 Release | CoreCLR (R2R) | 7 s | 43 ms, 140 ms | 39 ms |
| Android 17 emulator net10 Release | Mono | 23 s | 132 ms, 29 ms | 137 ms |
| Android emulator net11 Release | CoreCLR | 7 s | 60 ms, 25 ms | 54 ms |

Emulator and simulator numbers are indicative, not real-device numbers.

**Finding: CoreGraphics replay.** Filling a page of text glyph paths takes about 135–140 ms on CoreGraphics. `sample` shows 99% of that in `CGContextDrawPath` → `aa_render`; the paths themselves are normal (about 1,500 cubics per line, page coordinates). Drawing the cached bitmap instead takes 0.19 ms. The viewer therefore never replays on the UI thread any more:
- A whole-page backdrop bitmap is made, reduced in scale when the exact scale would exceed 6 MP.
- When zoomed in, a detail bitmap covers the visible region plus a 25% margin at the exact scale (`BitmapPlan`, unit tested).
- A new page shows white until its bitmap is ready, which starts at once with no 150 ms debounce.

A rounding bug was found on the way: a backdrop sized to exactly the budget exceeded it once pixel counts were rounded up, so it was never made.

**Runtime-async gate: passed.** The net11.0 libraries, built with runtime async on, ran the full smoke test with 0 failures on Android (CoreCLR, Debug and Release) and on the iOS simulator (CoreCLR ReadyToRun, Release). net11.0 heads are built with `-p:MelvilleMauiTfm=net11.0`; Android on .NET 11 needs API 24 or later. Not covered: a real iOS device.

**Not done.**
- Windows: no machine. CI builds it but nothing has run it.
- Real-device profiling on a mid-range Android phone.
- The Mac AOT run's working set grew from 159 to 232 MB over 16 measurements. Not investigated (it could just be uncollected garbage).

## Phase 7 results (lifecycle and accessibility)

**Lifecycle.**
- `PdfViewer.ViewState` is a two-way `PdfViewState` (page, zoom mode, zoom, scroll). The scroll is stored as the page point at the viewport's top-left corner, so it survives another viewport size or zoom. Setting it before `Source` opens the next document at that position.
- The viewer frees its bitmap when the window stops and closes the document when the window is destroyed.
- The sample saves the position per document, in `Preferences`. It saves when the window stops or is destroyed (Android's back button can skip Stopped), and when the user leaves the viewer, which also closes the document. The Library offers "Continue reading …".
- Checked on Mac Catalyst: leave and continue restores page and zoom; restarting the app restores a fit-width scroll position.
- Checked on Android: Home, then force-stop, then relaunch restores the page.

**Accessibility.**
- Opening, opened (with the page count) and errors are announced. Errors also take semantic focus.
- Layer check boxes are named after their layer. The label beside each box is kept out of the accessibility tree, and tapping it toggles the layer.
- The page-number entry has a hint rather than a description, which would break TalkBack's edit action.
- Toolbar targets are at least 44 × 44. The page entry has a minimum width, not a fixed one.
- In the sample, rows read title and description. The SearchBar and Entry descriptions were removed, for the same TalkBack reason. Diagnostics announces only its final results.
- Desktop menu bar (Mac Catalyst, Windows) with keyboard shortcuts: ⌘←/→ for previous/next page, ⌘↑/↓ for first/last, ⌘= and ⌘− to zoom, ⌘0 for fit page, ⌘9 for fit width. These use the new public `PdfViewer` commands.
- Checked with the Android accessibility tree (`uiautomator dump`) and DevFlow. Not yet tried with a real screen reader (TalkBack, VoiceOver, Narrator).

**Bug found (from phase 5).** The view model raises `CanExecuteChanged` from the render thread. Toolbar buttons bound directly to its commands then updated `IsEnabled` off the UI thread. On Android that throws ("Only the original thread that created a view hierarchy can touch its views"), and the message showed as a render error on the layers document. The toolbar and the host now use forwarding commands that raise `CanExecuteChanged` on the UI thread.

**Not done.**
- There is no text alternative for page content, so a screen reader only hears "Page X of Y". Melville.Pdf has a text extractor; using it would be a later feature.
- Memory-pressure events (iOS memory warning, Android trim) are not handled; the viewer only frees memory on Stopped.
- Windows has not been tested (no machine).

## Merge of upstream main (2026-11-05)

- **JPEG pooled buffer**: fixed upstream (the decoded stream now owns the rented array). We took upstream's `JpegStreamFactory` and kept our `RentedArrayReadingStream` hardening: reads stop at the decoded length, and the array is returned at most once.
- **Culture parsing**: PR #44 was merged upstream. Upstream also made CA1305 an error. Our net11.0 test code now passes an explicit culture. CSJ2K is exempt from CA1305 in its project file, because upstream main gives the same ~90 errors there with SDK 11 rc3.
- **Flate on net10.0**: upstream's span-based `FlateEncoder`/`FlateDecoder` use `DeflateEncoder`/`DeflateDecoder`, which are .NET 11 APIs. On net10.0 the previous stream-based codec is compiled instead (`*.Net10.cs`). Its output is valid zlib but not byte-identical, so two exact-bytes tests fail on net10.0 only: `FlateDecode.EncodingTestAsync` and `FileWriterTest.RefStreamContentsAsync`. That gives 45 known DataModelTests failures on macOS for net10.0 and 43 for net11.0.
- **JPEG 2000 images rendered blank** before this merge. The JPX image creator was registered by reflection on a private CoreJ2K field, and the pixel compaction loop was wrong. Upstream fixed both. The two JPX render baselines were re-recorded, and the image now shows.

## Other findings

- **Concurrent JPEG decodes corrupted each other** (fixed in phase 4). `JpegStreamFactory` returned its pooled output buffer to `ArrayPool<byte>.Shared` while the decoded stream still read it, and the stream returned it again on dispose. The stream also read past the decoded length into stale pool data. Twelve concurrent renders of one JPEG gave about 90 different outputs, with wrong colours, in **both** renderers, and the shared pool spread the damage to unrelated code (a CMap document failed once). Introduced upstream on 2026-10-12; now fixed upstream (see the merge notes above).

- **Encrypted reference documents** fail to read back 0.3–1.3% of the time (random keys; `PdfParseException: Unknown token` or a PostScript stack overflow in the encryption dictionary): `-EncryptedV6` 4/300, `-EncryptedV1Rc4` 2/300, three others 1/300. This is an upstream writer or parser bug and is likely to affect real encrypted PDFs written by Melville. The renderer tests regenerate such documents up to 3 times.

- Opening a second `PdfReader` document over the **same** `IMultiplexSource` while the first is still open fails for `-StandardFonts` (`PdfParseException: Unknown token: f`). This happens on every runtime. The benchmarks use a separate copy per open; still to investigate.
