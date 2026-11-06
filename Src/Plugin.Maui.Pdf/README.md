# Plugin.Maui.Pdf

`PdfView` is a PDF viewer control for .NET MAUI. It runs on Android, iOS, Mac Catalyst and Windows.

Pages are rendered by [Melville.Pdf](https://github.com/DrJohnMelville/Pdf), a PDF renderer written entirely in .NET, and drawn with Microsoft.Maui.Graphics. There are no native PDF libraries, so a page looks the same on every platform. The package is trim and Native AOT compatible.

> **Preview.** Version 0.1.0-preview.1. It is not on nuget.org yet; see [Building the package](#building-the-package).

## Features

- **Display modes:** one page at a time, or **continuous scroll** through all pages, vertically or horizontally. Only the pages near the view are rendered.
- **Gestures:** pinch to zoom around your fingers, drag to pan, double-tap to switch between the whole page and 2×.
- **Toolbar:** first, previous, next and last page; a page number box; zoom in and out; fit page; fit width; single page or continuous scroll; layers. Hide it with `ShowToolbar="False"` to use your own controls with the public commands.
- **Layers:** optional content groups can be shown and hidden.
- **Encrypted PDFs:** plug in your own `IPasswordSource` to supply passwords.
- **Sources:** app package files, the file system, `FilePicker` results (including Android content URIs), byte arrays and streams.
- **Saving the reading position:** the two-way `ViewState` holds the page, zoom and scroll, so you can save it when the app is backgrounded and restore it later.
- **Accessibility:** screen reader announcements, named controls, 44 × 44 touch targets and desktop keyboard commands.
- **Dark theme:** the area around the page follows the app theme; the page itself stays white.
- **Fonts:** fonts are included for documents that don't embed theirs, since iOS and Android have no system font folder to fall back on. A separate CJK font package is available.

## Getting started

### 1. Install

```
dotnet add package Plugin.Maui.Pdf --prerelease
```

### 2. Register

```csharp
// MauiProgram.cs
var builder = MauiApp.CreateBuilder();
builder
    .UseMauiApp<App>()
    .UseMauiPdf();
```

`UseMauiPdf(o => o.FontMapper = ...)` changes the fonts used for documents that do not embed theirs. For Chinese, Japanese or Korean documents, use `SelfContainedCjkFonts` from Melville.Pdf.FontLibrary.Cjk (about 90 MB).

### 3. Show a PDF

```xml
<ContentPage xmlns:pdf="https://github.com/yurkinh/Pdf/maui" ...>
    <pdf:PdfView Source="{Binding Source}"
                 PageNumber="{Binding PageNumber}" />
</ContentPage>
```

```csharp
Source = PdfSource.FromAppPackage("manual.pdf");          // Resources/Raw/manual.pdf
Source = PdfSource.FromFileResult(await FilePicker.Default.PickAsync(...));
Source = PdfSource.FromBytes(bytes);
Source = PdfSource.FromStream(async ct => await http.GetStreamAsync(url, ct));
```

Apps that use .NET MAUI 10's implicit (global) XAML namespaces can write `<PdfView />` with no prefix.

## API

### Properties

| Property | Type | | Description |
|---|---|---|---|
| `Source` | `PdfSource?` | | The document. Setting it closes the previous one. |
| `PasswordSource` | `IPasswordSource?` | | Supplies passwords for encrypted documents. Set it before `Source`. |
| `PageNumber` | `int` | two-way | The page shown, starting at 1. |
| `PageCount` | `int` | read-only | The number of pages, or 0 when no document is open. |
| `ZoomMode` | `ZoomMode` | two-way | `FitPage` (the default), `FitWidth`, or `Custom` after zooming. |
| `DisplayMode` | `DisplayMode` | two-way | `SinglePage` (the default) or `Continuous`. The toolbar button switches between them. |
| `ScrollOrientation` | `ScrollOrientation` | | In continuous mode: `Vertical` (the default) or `Horizontal`. This is MAUI's `ScrollView` enum; `Both` and `Neither` mean vertical. |
| `ViewState` | `PdfViewState?` | two-way | The page, zoom mode, zoom and scroll. Set it before `Source` to open at a saved position. It works in both display modes. |
| `IsLoading` | `bool` | read-only | True while a document is opening. |
| `ErrorMessage` | `string?` | read-only | Why the document or the page could not be shown. |
| `ShowToolbar` | `bool` | | Shows the built-in toolbar. The default is true. |

### Commands

`FirstPageCommand`, `PreviousPageCommand`, `NextPageCommand`, `LastPageCommand`, `ZoomInCommand`, `ZoomOutCommand`, `FitPageCommand` and `FitWidthCommand` are for your own toolbars, menus and keyboard shortcuts. Their `CanExecute` follows the open document, and `CanExecuteChanged` is raised on the UI thread.

### Events

`DocumentLoaded` is raised once the first page is shown. `LoadFailed` is raised with the exception when a document cannot be opened. Both are raised on the UI thread.

### Saving the reading position

```csharp
// Save when the window stops (and when it is destroyed: Android's back button can skip Stopped).
Preferences.Set("position", JsonSerializer.Serialize(viewModel.ViewState, MyJsonContext.Default.PdfViewState));

// Restore: set ViewState, then Source.
viewModel.ViewState = saved;
viewModel.Source = PdfSource.FromAppPackage("manual.pdf");
```

### Passwords

```csharp
public sealed class AskForPassword : IPasswordSource
{
    // Called on a worker thread, again after each wrong password. Return null to cancel opening.
    public async ValueTask<(string?, PasswordType)> GetPasswordAsync() =>
        (await MainThread.InvokeOnMainThreadAsync(ShowMaskedPromptAsync), PasswordType.User);
}
```

`DisplayPromptAsync` cannot mask what is typed. The sample app has a small modal page that does.

## Platform notes

| Platform | Minimum | Notes |
|---|---|---|
| Android | API 21 (API 24 on .NET 11) | No permissions needed. `FilePicker` returns content URIs; `PdfSource.FromFileResult` reads them. |
| iOS | 15.0 | |
| Mac Catalyst | 17.0 | To open picked files in a sandboxed app, add `com.apple.security.files.user-selected.read-only`. Trackpad scrolling pans the page. |
| Windows | 10.0.17763 | Built in CI, but not yet tested on a device. |

## Performance

A page is drawn once into a display list on a worker thread. The viewer then draws cached bitmaps: one for the whole page, plus one for the visible area when you zoom in a long way. It never replays the page on the UI thread, which matters most on iOS and Mac: CoreGraphics takes about 140 ms to fill a page of text, while drawing the cached bitmap takes 0.2 ms. A new page shows white until its bitmap is ready.

These Release-build timings come from a simulator and an emulator, not real devices. To record a text page:

| Platform | .NET 10 | .NET 11 |
|---|---|---|
| iOS simulator | 55 ms | 43 ms |
| Android emulator | 132 ms | 60 ms |

## Limitations

- No text search or selection, annotations, form filling, link navigation or printing.
- A screen reader hears "Page X of Y", not the page text.
- Windows has not been tested on a device.

## Building the package

The package is not published. Build it locally:

```
./pack-nuget.sh            # writes artifacts/nuget/*.nupkg
```

The package depends on this repository's builds of the Melville.Pdf libraries, which target net10.0 and net11.0; upstream's packages target net11.0 only. These are packed together as version `0.6.9-maui.1`. Melville.StreamInterfaces is built from the `external/Melville.Streams` submodule. To try the package in an app, add `artifacts/nuget` as a package source.

`./tools/test-package-consumer.sh net10.0-android net10.0-ios` checks the package the way an app developer would use it. It creates a new .NET MAUI app with the installed SDK, restores Plugin.Maui.Pdf from `artifacts/nuget`, shows a PDF in a `PdfView`, and builds the app.

Before the package can be published, one of these is needed:
- upstream publishes Melville.Pdf packages with net10.0 assets;
- this fork's libraries get their own package IDs;
- the libraries are bundled into the Plugin.Maui.Pdf package.

The `Melville.*` IDs on nuget.org belong to the upstream author, so this fork can't publish them.

## Credits and license

MIT. Rendering is done by [Melville.Pdf](https://github.com/DrJohnMelville/Pdf) by John Melville (MIT).
