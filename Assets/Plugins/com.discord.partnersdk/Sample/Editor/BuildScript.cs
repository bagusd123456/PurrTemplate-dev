using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;
using System.IO;
using System;
using System.Linq;

namespace DiscordxUnity {
public class BuildScript {
    [MenuItem("Discord/Build Game")]
    public static void BuildGame() {
        // Parse command line arguments
        string[] args = System.Environment.GetCommandLineArgs();

        // Default values
        string buildTargetString = "Win64";
        BuildTarget buildTarget = BuildTarget.StandaloneWindows64;
        string customOutputPath = null;

        // Parse build target from command line
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "-buildTarget" && i + 1 < args.Length) {
                buildTargetString = args[i + 1];
                buildTarget = ParseBuildTarget(buildTargetString);
                Debug.Log(
                  $"Using build target from command line: {buildTargetString} -> {buildTarget}");
            } else if (args[i] == "-customOutputPath" && i + 1 < args.Length) {
                customOutputPath = args[i + 1];
                Debug.Log($"Using custom output path from command line: {customOutputPath}");
            }
        }

        // Get the project path
        string projectPath = Application.dataPath;
        string projectRoot = Directory.GetParent(projectPath).FullName;

        // Define build paths - use custom output path if provided, otherwise default
        string buildPath;
        string buildName;

        if (!string.IsNullOrEmpty(customOutputPath)) {
            buildPath = customOutputPath;
            buildName = "DiscordxUnity.exe";
        } else {
            buildPath = Path.Combine(projectRoot, "build\\unity_sample\\", buildTargetString);
            buildName = "DiscordxUnity.exe";
        }

        string fullBuildPath = Path.Combine(buildPath, buildName);

        // Ensure build directory exists
        Directory.CreateDirectory(buildPath);

        // Get all scenes in the project
        string[] scenes = new string[EditorBuildSettings.scenes.Length];
        for (int i = 0; i < scenes.Length; i++) {
            scenes[i] = EditorBuildSettings.scenes[i].path;
        }

        // Build the project
        Debug.Log($"Building DiscordxUnity to: {fullBuildPath}");
        Debug.Log($"Build target: {buildTarget}");

        BuildPlayerOptions buildPlayerOptions =
          new BuildPlayerOptions { scenes = scenes,
                                   locationPathName = fullBuildPath,
                                   target = buildTarget,
                                   options = BuildOptions.CleanBuildCache |
                                     BuildOptions.StrictMode | BuildOptions.AutoRunPlayer };

        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildResult result = report.summary.result;

        if (result == BuildResult.Succeeded) {
            Debug.Log($"Build succeeded: {fullBuildPath}");
        } else {
            Debug.LogError($"Build failed: {result}");
            EditorApplication.Exit(1);
        }
    }

    private static BuildTarget ParseBuildTarget(string buildTargetString) {
        switch (buildTargetString.ToLower()) {
        case "windows64":
        case "win64":
        case "standalonewindows64":
            return BuildTarget.StandaloneWindows64;
        case "windows32":
        case "standalonewindows":
            return BuildTarget.StandaloneWindows;
        case "osx":
        case "standaloneosx":
            return BuildTarget.StandaloneOSX;
        case "linux64":
        case "standalonelinux64":
            return BuildTarget.StandaloneLinux64;
        case "android":
            return BuildTarget.Android;
        case "ios":
            return BuildTarget.iOS;
        case "webgl":
            return BuildTarget.WebGL;
        default:
            Debug.LogWarning(
              $"Unknown build target: {buildTargetString}, defaulting to StandaloneWindows64");
            return BuildTarget.StandaloneWindows64;
        }
    }

    [MenuItem("Discord/Build Game (Development)")]
    public static void BuildGameDevelopment() {
        // Parse command line arguments
        string[] args = System.Environment.GetCommandLineArgs();

        // Default values
        string buildTargetString = "Windows64";
        BuildTarget buildTarget = BuildTarget.StandaloneWindows64;
        string customOutputPath = null;

        // Parse build target from command line
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "-buildTarget" && i + 1 < args.Length) {
                buildTargetString = args[i + 1];
                buildTarget = ParseBuildTarget(buildTargetString);
                Debug.Log(
                  $"Using build target from command line: {buildTargetString} -> {buildTarget}");
            } else if (args[i] == "-customOutputPath" && i + 1 < args.Length) {
                customOutputPath = args[i + 1];
                Debug.Log($"Using custom output path from command line: {customOutputPath}");
            }
        }

        // Get the project path
        string projectPath = Application.dataPath;
        string projectRoot = Directory.GetParent(projectPath).FullName;

        // Define build paths - use custom output path if provided, otherwise default
        string buildPath;
        string buildName;

        if (!string.IsNullOrEmpty(customOutputPath)) {
            buildPath = customOutputPath;
            buildName = "DiscordxUnity_Development.exe";
        } else {
            buildPath = Path.Combine(projectRoot, "Builds", buildTargetString + "_Development");
            buildName = "DiscordxUnity_Development.exe";
        }

        string fullBuildPath = Path.Combine(buildPath, buildName);

        // Ensure build directory exists
        Directory.CreateDirectory(buildPath);

        // Get all scenes in the project
        string[] scenes = new string[EditorBuildSettings.scenes.Length];
        for (int i = 0; i < scenes.Length; i++) {
            scenes[i] = EditorBuildSettings.scenes[i].path;
        }

        // Build the project with development options
        Debug.Log($"Building DiscordxUnity (Development) to: {fullBuildPath}");
        Debug.Log($"Build target: {buildTarget}");

        BuildPlayerOptions buildPlayerOptions =
          new BuildPlayerOptions { scenes = scenes,
                                   locationPathName = fullBuildPath,
                                   target = buildTarget,
                                   options =
                                     BuildOptions.Development | BuildOptions.AllowDebugging };

        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildResult result = report.summary.result;

        if (result == BuildResult.Succeeded) {
            Debug.Log($"Build succeeded: {fullBuildPath}");
        } else {
            Debug.LogError($"Build failed: {result}");
            EditorApplication.Exit(1);
        }
    }
}
}
