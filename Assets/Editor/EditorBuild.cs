#if UNITY_EDITOR
using System;
using System.IO;
using ClubClash;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class EditorBuild
{
    [Serializable] public class CheckReport { public int passed,failed; public CheckResult[] checks; }
    [Serializable] public class CheckResult { public string name,error; public bool passed; }
    static string Work { get { return Path.GetFullPath(Path.Combine(Application.dataPath,"../Verification/v7")); } }

    [MenuItem("Club Clash/Prepare project")]
    public static void Prepare()
    {
        Directory.CreateDirectory(Work);
        PlayerSettings.companyName="Club Clash";
        PlayerSettings.productName=AppReleaseConfig.HomeScreenName;
        PlayerSettings.bundleVersion=AppReleaseConfig.MarketingVersion;
        PlayerSettings.SplashScreen.show=false;
        PlayerSettings.SplashScreen.showUnityLogo=false;
        AppBranding.Apply();
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,AppReleaseConfig.BundleIdentifier);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS,AppReleaseConfig.BundleIdentifier);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
        PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=720;
        PlayerSettings.defaultIsNativeResolution=false;
        PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
        PlayerSettings.resizableWindow=true;
        PlayerSettings.defaultInterfaceOrientation=UIOrientation.LandscapeLeft;
        PlayerSettings.allowedAutorotateToPortrait=false;PlayerSettings.allowedAutorotateToPortraitUpsideDown=false;
        PlayerSettings.allowedAutorotateToLandscapeLeft=true;PlayerSettings.allowedAutorotateToLandscapeRight=true;
        QualitySettings.vSyncCount=0;QualitySettings.antiAliasing=0;
        if(AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/SpriteMaterial.mat")==null)
        {
            Shader shader=Shader.Find("Sprites/Default");
            if(shader==null)throw new InvalidOperationException("Built-in sprite shader is unavailable.");
            AssetDatabase.CreateAsset(new Material(shader){name="SpriteMaterial"},"Assets/Resources/SpriteMaterial.mat");
        }
        foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Resources/Art"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null)continue;
            importer.textureType=TextureImporterType.Default;importer.filterMode=FilterMode.Point;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;
            importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;
            importer.isReadable=true;importer.maxTextureSize=4096;importer.npotScale=TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }
        if(!AssetDatabase.IsValidFolder("Assets/Scenes"))AssetDatabase.CreateFolder("Assets","Scenes");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var controller=new GameObject("Club Clash");controller.AddComponent<GameController>();
        var camera=new GameObject("Battle Camera",typeof(Camera),typeof(AudioListener));camera.tag="MainCamera";
        // Keep the native audio components explicit in the scene as well as the
        // runtime initializer, so build dependency stripping can retain them.
        foreach(string name in new[]{"Battle Music","Battle Effects"})
        {
            var audio=new GameObject(name,typeof(AudioSource));audio.transform.SetParent(controller.transform,false);
            var source=audio.GetComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;
            if(name=="Battle Music")source.clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Resources/Audio/battle-loop.wav");
        }
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/Playground.unity");
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Playground.unity",true)};
        AssetDatabase.SaveAssets();
    }

    public static void Verify()
    {
        Prepare();
        var started=System.Diagnostics.Stopwatch.StartNew();
        bool ok=CombatTests.RunAll();started.Stop();
        var report=JsonUtility.FromJson<CheckReport>(CombatTests.LastReport);
        var xml=new System.Xml.XmlDocument();var suite=xml.CreateElement("testsuite");xml.AppendChild(suite);suite.SetAttribute("tests",(report.passed+report.failed).ToString());suite.SetAttribute("failures",report.failed.ToString());suite.SetAttribute("skipped","0");suite.SetAttribute("time",started.Elapsed.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach(var check in report.checks){var node=xml.CreateElement("testcase");node.SetAttribute("name",check.name);suite.AppendChild(node);if(!check.passed){var failure=xml.CreateElement("failure");failure.InnerText=check.error;node.AppendChild(failure);}}
        xml.Save(Path.Combine(Work,"editor-checks.xml"));
        File.WriteAllText(Path.Combine(Work,"unity-tests.json"),CombatTests.LastReport);
        if(!ok)throw new InvalidOperationException("Combat checks failed. See work/unity-tests.json.");
        Debug.Log("CLUB_CLASH_UNITY_VERIFIED");
    }

    [MenuItem("Club Clash/Build Mac")]
    public static void BuildMac()
    {
        Verify();
        string target=Environment.GetEnvironmentVariable("CLASH_BUILD_PATH");
        if(string.IsNullOrEmpty(target))target=Path.GetFullPath(Path.Combine(Application.dataPath,"../../ClubClashMac/Club Clash.app"));
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes=new[]{"Assets/Scenes/Playground.unity"},locationPathName=target,
            target=BuildTarget.StandaloneOSX, options=BuildOptions.None
        });
        File.WriteAllText(Path.Combine(Work,"unity-build.json"),"{\"result\":\""+report.summary.result+"\",\"bytes\":"+report.summary.totalSize+",\"errors\":"+report.summary.totalErrors+",\"warnings\":"+report.summary.totalWarnings+",\"seconds\":"+report.summary.totalTime.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
        if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Unity Mac build failed.");
        Debug.Log("CLUB_CLASH_MAC_BUILT "+target);
    }

    // Unsigned local simulator build for UI checks; does not increment versions.
    public static void BuildSimulator()
    {
        Verify();
        var sdk=PlayerSettings.iOS.sdkVersion;var architecture=PlayerSettings.iOS.simulatorSdkArchitecture;
        try
        {
            PlayerSettings.iOS.sdkVersion=iOSSdkVersion.SimulatorSDK;
            PlayerSettings.iOS.simulatorSdkArchitecture=AppleMobileArchitectureSimulator.ARM64;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS,AppReleaseConfig.BundleIdentifier);
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{"Assets/Scenes/Playground.unity"},
                locationPathName=Path.Combine(Work,"iOSSimulator"),target=BuildTarget.iOS,
                options=BuildOptions.None
            });
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Simulator build failed.");
            Debug.Log("CLUB_CLASH_SIMULATOR_BUILT");
        }
        finally { PlayerSettings.iOS.sdkVersion=sdk;PlayerSettings.iOS.simulatorSdkArchitecture=architecture;AssetDatabase.SaveAssets(); }
    }
}
#endif
