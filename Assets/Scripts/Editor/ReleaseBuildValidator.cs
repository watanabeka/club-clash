#if UNITY_EDITOR
using System.IO;
using ClubClash;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

public sealed class ReleaseBuildValidator : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report)
    {
        if (PlayerSettings.productName != AppReleaseConfig.HomeScreenName
            || PlayerSettings.bundleVersion != AppReleaseConfig.MarketingVersion
            || PlayerSettings.iOS.buildNumber != AppReleaseConfig.BuildNumber.ToString())
            throw new BuildFailedException("App names and existing versions must match AppReleaseConfig.");
        if (report.summary.platform == BuildTarget.iOS
            && PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS) != AppReleaseConfig.BundleIdentifier)
            throw new BuildFailedException("iOS Bundle ID must match AppReleaseConfig.");
    }

    [PostProcessBuild(100)]
    public static void ApplyHomeScreenName(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;
        string plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument(); plist.ReadFromFile(plistPath);
        plist.root.SetString("CFBundleDisplayName", AppReleaseConfig.HomeScreenName);
        plist.WriteToFile(plistPath);
    }
}
#endif
