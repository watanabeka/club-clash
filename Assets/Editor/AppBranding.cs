#if UNITY_EDITOR
using System;
using System.IO;
using ClubClash;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class AppBranding
{
    const string IconPath="Assets/Branding/AppIcon.png";
    [MenuItem("Club Clash/Apply app icon and splash settings")]
    public static void Apply()
    {
        foreach(string path in new[]{"Assets/Resources/Branding/title-logo.png","Assets/Resources/Branding/title-lockup.png"})
        {
            if(!File.Exists(path))continue;
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var logoImporter=(TextureImporter)AssetImporter.GetAtPath(path);
            logoImporter.textureType=TextureImporterType.Default;logoImporter.filterMode=FilterMode.Point;
            logoImporter.textureCompression=TextureImporterCompression.Uncompressed;logoImporter.mipmapEnabled=false;
            logoImporter.isReadable=true;logoImporter.alphaIsTransparency=true;logoImporter.maxTextureSize=4096;
            logoImporter.npotScale=TextureImporterNPOTScale.None;logoImporter.wrapMode=TextureWrapMode.Clamp;
            logoImporter.SaveAndReimport();
        }
        AssetDatabase.ImportAsset(IconPath,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(IconPath);
        if(importer==null)throw new InvalidOperationException("The selected app icon is missing.");
        importer.textureType=TextureImporterType.Default;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled=false;importer.isReadable=true;importer.maxTextureSize=1024;
        importer.npotScale=TextureImporterNPOTScale.None;importer.alphaIsTransparency=false;
        importer.SaveAndReimport();
        var icon=AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        if(icon.width!=1024 || icon.height!=1024)throw new InvalidOperationException("App icon must be 1024 by 1024.");
        foreach(var pixel in icon.GetPixels32())if(pixel.a!=255)throw new InvalidOperationException("App icon must be opaque.");
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown,new[]{icon},IconKind.Any);
        int count=0;
        foreach(var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.iOS))
        {
            var slots=PlayerSettings.GetPlatformIcons(NamedBuildTarget.iOS,kind);
            foreach(var slot in slots){slot.SetTexture(icon,0);count++;}
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.iOS,kind,slots);
        }
        PlayerSettings.SplashScreen.show=false;PlayerSettings.SplashScreen.showUnityLogo=false;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,AppReleaseConfig.BundleIdentifier);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS,AppReleaseConfig.BundleIdentifier);
        PlayerSettings.bundleVersion=AppReleaseConfig.MarketingVersion;
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Verification/store-assets");
        File.WriteAllText("Verification/store-assets/icon-settings.json","{\"width\":1024,\"height\":1024,\"opaque\":true,\"iosSlots\":"+count+",\"unitySplash\":false,\"unityLogo\":false}");
        Debug.Log("APP_BRANDING_APPLIED iOS icon slots="+count);
    }

    [MenuItem("Club Clash/Build store screenshot player")]
    public static void BuildCapturePlayer()
    {
        Apply();
        if(!CombatTests.RunAll())throw new InvalidOperationException("Combat verification failed.");
        File.WriteAllText("Verification/store-assets/editor-checks.json",CombatTests.LastReport);
        string target=Path.GetFullPath("Builds/StoreCapture/部活ファイト.app");
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/Playground.unity"},locationPathName=target,target=BuildTarget.StandaloneOSX,options=BuildOptions.None});
        File.WriteAllText("Verification/store-assets/build.json","{\"result\":\""+report.summary.result+"\",\"errors\":"+report.summary.totalErrors+",\"warnings\":"+report.summary.totalWarnings+"}");
        if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Capture player build failed.");
        Debug.Log("STORE_CAPTURE_PLAYER_BUILT "+target);
    }
}
#endif
