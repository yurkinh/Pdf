namespace Plugin.Maui.Pdf.TestApp.Diagnostics;

/// <summary>
/// Whether the app was launched to run the diagnostics unattended: environment variable MELVILLE_AUTORUN=1 (Mac
/// Catalyst; iOS simulator via SIMCTL_CHILD_MELVILLE_AUTORUN=1), or on Android the intent extra melville_autorun
/// (adb shell am start ... --ez melville_autorun true).
/// </summary>
public static class AutoRun
{
    public static bool Requested { get; set; } = Environment.GetEnvironmentVariable("MELVILLE_AUTORUN") == "1";
}
