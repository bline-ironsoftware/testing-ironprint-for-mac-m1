using Android.App;
using Android.Content.PM;
using Android.OS;
using IronPrint;

namespace MauiTest;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Required on Android before any other Printer method -- see IronPrint.xml's
        // doc comment on Printer.Initialize(Android.Content.Context).
        Printer.Initialize(this);
    }
}
