#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

public static class VoicePostBuildProcessor {
    [PostProcessBuild]
    public static void OnPostprocessBuild(BuildTarget target, string path) {
        if (target == BuildTarget.iOS) {
            var projectPath = path + "/Unity-iPhone.xcodeproj/project.pbxproj";
            var capabilities = new ProjectCapabilityManager(
              projectPath, "Unity-iPhone/entitlements.plist", targetName: "Unity-iPhone");
            capabilities.AddBackgroundModes(BackgroundModesOptions.AudioAirplayPiP);
            capabilities.WriteToFile();
        }
    }
}
#endif