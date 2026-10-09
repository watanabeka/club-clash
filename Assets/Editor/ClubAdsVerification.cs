#if UNITY_EDITOR
using System.IO;
using System;
using ClubClash;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

public static class ClubAdsVerification
{
    static TestRunnerApi runner;
    // Called only in the isolated verification workspace. No Archive or version change.
    public static void BuildSimulator()
    {
        ClubAdsConfiguration.Sync();
        PlayerSettings.iOS.sdkVersion=iOSSdkVersion.SimulatorSDK;
        PlayerSettings.iOS.simulatorSdkArchitecture=AppleMobileArchitectureSimulator.ARM64;
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes=new[]{"Assets/Scenes/Playground.unity"},locationPathName="Builds/AdMobSimulator",
            target=BuildTarget.iOS,options=BuildOptions.Development
        });
        Directory.CreateDirectory("Verification/admob");
        File.WriteAllText("Verification/admob/simulator-export.json","{\"result\":\""+report.summary.result+"\",\"errors\":"+report.summary.totalErrors+",\"warnings\":"+report.summary.totalWarnings+"}");
        if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("AdMob simulator export failed.");
    }
    [MenuItem("Club Clash/Verify AdMob result transitions")]
    public static void Run()
    {
        Directory.CreateDirectory("Verification/admob");
        ClubAdsConfiguration.Sync();
        runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        runner.RegisterCallbacks(new Callbacks());
        runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
    }
    sealed class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor test) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            File.WriteAllText("Verification/admob/editmode-results.xml", result.ToXml().OuterXml);
            File.WriteAllText("Verification/admob/combat-checks.json", CombatTests.LastReport ?? "{}");
            Debug.Log("CLUB_ADS_TESTS_FINISHED " + result.TestStatus + " duration=" + result.Duration);
            UnityEngine.Object.DestroyImmediate(runner); runner = null;
        }
    }
}
#endif
