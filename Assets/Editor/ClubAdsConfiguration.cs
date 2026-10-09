#if UNITY_EDITOR
using System.IO;
using ClubClash;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
public sealed class ClubAdsConfiguration : AssetPostprocessor, IPreprocessBuildWithReport
{
    const string ConfigPath = "Assets/Resources/ClubAdSettings.json";
    const string SdkPath = "Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset";
    static ClubAdsConfiguration() { EditorApplication.delayCall += Sync; }
    public int callbackOrder => -10000;

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] from)
    {
        foreach (string path in imported) if (path == ConfigPath) { EditorApplication.delayCall += Sync; break; }
    }

    public static void Sync()
    {
        if (!File.Exists(ConfigPath)) return;
        var settings = JsonUtility.FromJson<ClubAdSettings>(File.ReadAllText(ConfigPath));
        var asset = AssetDatabase.LoadAssetAtPath<Object>(SdkPath);
        if (settings == null || asset == null) return;
        var serialized = new SerializedObject(asset);
        Set(serialized, "adMobIOSAppId", settings.ApplicationId(true));
        // Android monetization remains unconfigured until its own IDs are supplied.
        string android = settings.ApplicationId(false);
        Set(serialized, "adMobAndroidAppId", string.IsNullOrEmpty(android) ? ClubAdSettings.TestAndroidApplicationId : android);
        if (serialized.ApplyModifiedPropertiesWithoutUndo()) AssetDatabase.SaveAssets();
    }

    static void Set(SerializedObject target, string field, string value)
    {
        var property = target.FindProperty(field);
        if (property != null && property.stringValue != value) property.stringValue = value;
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        Sync();
        if (report.summary.platform != BuildTarget.iOS && report.summary.platform != BuildTarget.Android) return;
        var settings = JsonUtility.FromJson<ClubAdSettings>(File.ReadAllText(ConfigPath));
        if (settings == null || (settings.Enabled && !settings.Valid(report.summary.platform == BuildTarget.iOS)))
            throw new BuildFailedException("ClubAdSettingsのアプリIDとインタースティシャルIDを確認してください。");
    }
}
#endif
