#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

public static class ClubAdsIosPostprocess
{
    public const string TrackingUsageDescription = "他社アプリやサイトでの利用情報を、広告の表示と効果測定に使用します。";
    [PostProcessBuild(10000)]
    public static void Apply(BuildTarget target, string directory)
    {
        if (target != BuildTarget.iOS) return;
        string plistPath = Path.Combine(directory, "Info.plist");
        var plist = new PlistDocument(); plist.ReadFromFile(plistPath);
        plist.root.SetString("NSUserTrackingUsageDescription", TrackingUsageDescription);
        plist.WriteToFile(plistPath);
        string projectPath = PBXProject.GetPBXProjectPath(directory);
        var project = new PBXProject(); project.ReadFromFile(projectPath);
        project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "AppTrackingTransparency.framework", true);
        project.WriteToFile(projectPath);

        // Modern Xcode rejects the SDK pod's older deployment targets. Match the
        // game's minimum iOS version, preserving any newer target set by a pod.
        string podsPath = Path.Combine(directory, "Pods/Pods.xcodeproj/project.pbxproj");
        if (File.Exists(podsPath)) File.WriteAllText(podsPath, RaisePodDeploymentTargets(File.ReadAllText(podsPath)));
        string podfilePath = Path.Combine(directory, "Podfile");
        if (File.Exists(podfilePath))
        {
            string text = File.ReadAllText(podfilePath);
            if (!text.Contains("post_install")) File.AppendAllText(podfilePath,
                "\n# Club Fight: preserve the iOS 15 minimum on subsequent pod installs.\n" +
                "post_install do |installer|\n  installer.pods_project.targets.each do |target|\n" +
                "    target.build_configurations.each do |config|\n" +
                "      current = config.build_settings['IPHONEOS_DEPLOYMENT_TARGET']\n" +
                "      if current.nil? || current.to_f < 15.0\n" +
                "        config.build_settings['IPHONEOS_DEPLOYMENT_TARGET'] = '15.0'\n" +
                "      end\n    end\n  end\nend\n");
        }
    }
    public static string RaisePodDeploymentTargets(string project)
    {
        return Regex.Replace(project, @"IPHONEOS_DEPLOYMENT_TARGET\s*=\s*([0-9.]+)\s*;", match =>
            Version.TryParse(match.Groups[1].Value, out var version) && version < new Version(15,0)
                ? "IPHONEOS_DEPLOYMENT_TARGET = 15.0;" : match.Value);
    }
}
#endif
