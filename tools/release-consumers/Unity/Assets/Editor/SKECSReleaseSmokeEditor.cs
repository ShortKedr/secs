using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SKECSReleaseSmokeEditor
{
    public static void RunEditor()
    {
        try
        {
            SKECSRuntimeSmoke.Check();
            Debug.Log("SKECS_UNITY_EDITOR_OK");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    public static void BuildMono() => Build(ScriptingImplementation.Mono2x, "Mono");

    public static void BuildIl2Cpp() => Build(ScriptingImplementation.IL2CPP, "IL2CPP");

    private static void Build(ScriptingImplementation backend, string folder)
    {
        try
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, backend);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("SKECS Release Smoke").AddComponent<SKECSRuntimeSmoke>();
            const string scenePath = "Assets/SKECSReleaseSmoke.unity";
            if (!EditorSceneManager.SaveScene(scene, scenePath))
                throw new Exception("Could not save smoke scene.");

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var buildDirectory = Path.Combine(projectRoot, "Builds", folder);
            Directory.CreateDirectory(buildDirectory);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = Path.Combine(buildDirectory, "SKECSReleaseSmoke.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception("Unity player build failed: " + report.summary.result);
            Debug.Log("SKECS_UNITY_BUILD_OK " + folder);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }
}
