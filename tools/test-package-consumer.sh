#!/usr/bin/env bash
# Check the packed Plugin.Maui.Pdf works for an app developer: create a fresh .NET MAUI app with the installed
# (non-repository) SDK, restore Plugin.Maui.Pdf from artifacts/nuget, show a bundled PDF in a PdfView, and build it.
#
#   ./pack-nuget.sh && ./tools/test-package-consumer.sh [net10.0-android net10.0-ios ...]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FEED="$ROOT/artifacts/nuget"
VERSION="$(ls "$FEED" | sed -n 's/^Plugin\.Maui\.Pdf\.\([0-9].*\)\.nupkg$/\1/p' | head -1)"
[ -n "$VERSION" ] || { echo "No Plugin.Maui.Pdf package in $FEED; run ./pack-nuget.sh first." >&2; exit 1; }
FRAMEWORKS=("${@:-net10.0-android}")
DOTNET="${CONSUMER_DOTNET:-dotnet}"      # deliberately not Src/.dotnet: a consumer has a normal SDK
WORK="$(mktemp -d)"; trap 'status=$?; rm -rf "$WORK"; exit $status' EXIT
cd "$WORK"

"$DOTNET" new maui -n PdfConsumer -o . >/dev/null
cat > nuget.config <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$FEED" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML
python3 - "$VERSION" <<'PY'
import sys
version = sys.argv[1]
p = 'PdfConsumer.csproj'; s = open(p, encoding='utf-8-sig').read()
s = s.replace('<PackageReference Include="Microsoft.Maui.Controls" Version="$(MauiVersion)" />',
              '<PackageReference Include="Microsoft.Maui.Controls" Version="$(MauiVersion)" />\n'
              f'    <PackageReference Include="Plugin.Maui.Pdf" Version="{version}" />')
open(p, 'w').write(s)
p = 'MauiProgram.cs'; s = open(p, encoding='utf-8-sig').read()
open(p, 'w').write('using Plugin.Maui.Pdf;\n' + s.replace('.UseMauiApp<App>()', '.UseMauiApp<App>()\n\t\t\t.UseMauiPdf()'))
open('MainPage.xaml', 'w').write('''<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:pdf="https://github.com/yurkinh/Pdf/maui"
             x:Class="PdfConsumer.MainPage">
    <pdf:PdfView x:Name="Viewer" />
</ContentPage>
''')
open('MainPage.xaml.cs', 'w').write('''using Plugin.Maui.Pdf;

namespace PdfConsumer;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        Viewer.Source = PdfSource.FromAppPackage("hello.pdf");
    }
}
''')
PY
cp "$ROOT/Src/Plugin.Maui.Pdf.Sample/Resources/Raw/layers.pdf" Resources/Raw/hello.pdf

for tfm in "${FRAMEWORKS[@]}"; do
  rid=""
  if [[ $tfm == *-ios ]]; then rid="-p:RuntimeIdentifier=iossimulator-arm64"; fi  # simulator: no signing
  echo "== $tfm (Plugin.Maui.Pdf $VERSION)"
  "$DOTNET" build -f "$tfm" -p:TargetFrameworks="$tfm" $rid -nologo -v:quiet
done
echo "Consumer app built against Plugin.Maui.Pdf $VERSION."
