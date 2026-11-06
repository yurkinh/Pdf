using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Plugin.Maui.Pdf.TestApp;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		// adb shell am start -n <package>/<activity> --ez melville_autorun true (see Diagnostics.AutoRun)
		if (Intent?.GetBooleanExtra("melville_autorun", false) == true) Diagnostics.AutoRun.Requested = true;
		base.OnCreate(savedInstanceState);
	}
}
