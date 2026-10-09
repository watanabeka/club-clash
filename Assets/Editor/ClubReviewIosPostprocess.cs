#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

public static class ClubReviewIosPostprocess
{
    [PostProcessBuild(10001)]
    public static void Apply(BuildTarget target,string directory)
    {
        if(target!=BuildTarget.iOS)return;
        string path=PBXProject.GetPBXProjectPath(directory);
        var project=new PBXProject();project.ReadFromFile(path);
        project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(),"StoreKit.framework",false);
        project.WriteToFile(path);
    }
}
#endif
