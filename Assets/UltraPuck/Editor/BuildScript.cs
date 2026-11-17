using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UltraPuck.Editor
{
    /// <summary>
    /// Ultra Puck > Build Windows, or from the command line:
    /// Unity.exe -batchmode -quit -projectPath "..." -executeMethod UltraPuck.Editor.BuildScript.BuildWindows
    /// Puts the game in Builds/Windows/UltraPuck.exe.
    /// </summary>
    public static class BuildScript
    {
        private const string ScenePath = "Assets/Scenes/Rink.unity";
        private const string FlatPath = "Assets/Resources/Flat.mat";
        private const string Output = "Builds/Windows/UltraPuck.exe";

        [MenuItem("Ultra Puck/Build Windows")]
        public static void BuildWindows()
        {
            Prepare();
            // the version comes from version.txt (the release script bumps it; the updater compares it)
            if (File.Exists("version.txt")) PlayerSettings.bundleVersion = File.ReadAllText("version.txt").Trim();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            var ok = report.summary.result == BuildResult.Succeeded;
            // Steam's test app id (480) next to the exe, until the game has its own; Steam won't start without it
            if (ok && File.Exists("steam_appid.txt")) File.Copy("steam_appid.txt", Path.Combine(Path.GetDirectoryName(Output), "steam_appid.txt"), true);
            Debug.Log("Ultra Puck build: " + report.summary.result + " (" + report.summary.totalErrors + " errors) -> " + Path.GetFullPath(Output));
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Makes sure the scene, the flat material and the player settings exist (safe to run any time).</summary>
        [MenuItem("Ultra Puck/Set Up Project")]
        public static void Prepare()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                // the game builds itself at runtime (GameRunner), so the scene only needs the default camera
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            if (!File.Exists(FlatPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FlatPath));
                AssetDatabase.CreateAsset(new Material(Shader.Find("Sprites/Default")), FlatPath);
            }

            PlayerSettings.productName = "Ultra Puck";
            // Mono, not IL2CPP: mods can then be loaded as ordinary C# DLLs
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
        }
    }
}
