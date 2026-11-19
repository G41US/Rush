using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
#endif

/// CI entry point (game-ci buildMethod). Creates the scene and materials from code, applies player
/// settings, then builds the APK or the Xcode project to -customBuildPath.
public static class BuildScript
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    public static void Build()
    {
        Prepare();
        var target = EditorUserBuildSettings.activeBuildTarget;
        string path = Arg("-customBuildPath") ?? (target == BuildTarget.Android ? "Builds/Android/RailRush.apk" : "Builds/iOS");
        Directory.CreateDirectory(target == BuildTarget.Android ? Path.GetDirectoryName(path) : path);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            target = target,
            locationPathName = path,
            options = BuildOptions.None,
        });
        Debug.Log($"Build result: {report.summary.result}, size {report.summary.totalSize}, errors {report.summary.totalErrors}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    [MenuItem("Rush/Prepare project")]
    public static void Prepare()
    {
        // ---- materials kept in Resources so their shaders/variants ship in the build
        Directory.CreateDirectory("Assets/Resources");
        var std = Shader.Find("Standard");
        SaveMat("Assets/Resources/BaseMat.mat", new Material(std));
        var em = new Material(std); em.EnableKeyword("_EMISSION"); em.SetColor("_EmissionColor", Color.yellow);
        em.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        SaveMat("Assets/Resources/EmissiveMat.mat", em);
        var sky = new Material(Shader.Find("Skybox/Procedural"));
        sky.SetFloat("_SunSize", 0.05f); sky.SetFloat("_AtmosphereThickness", 0.8f);
        sky.SetColor("_SkyTint", new Color(0.45f, 0.7f, 1f)); sky.SetColor("_GroundColor", new Color(0.55f, 0.6f, 0.68f)); sky.SetFloat("_Exposure", 1.35f);
        SaveMat("Assets/Resources/SkyMat.mat", sky);

        // ---- scene (empty: RushGame bootstraps itself)
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        // ---- player settings
        PlayerSettings.companyName = "Marvelous";
        PlayerSettings.productName = "Rail Rush";
        PlayerSettings.bundleVersion = "1.0";
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SplashScreen.show = false;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.Medium;
        QualitySettings.antiAliasing = 4;

        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.marvelous.railrush");
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS, "com.marvelous.railrush");
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        PlayerSettings.iOS.cameraUsageDescription = "The camera tracks your body so you can control the runner by moving.";
        PlayerSettings.iOS.requiresFullScreen = true;
        PlayerSettings.iOS.appleEnableAutomaticSigning = false;
        AssetDatabase.SaveAssets();
    }

    static void SaveMat(string path, Material m)
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
    }

    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return null;
    }

#if UNITY_IOS
    [PostProcessBuild(100)]
    public static void OnPostprocessBuild(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;
        string projPath = PBXProject.GetPBXProjectPath(path);
        var proj = new PBXProject(); proj.ReadFromFile(projPath);
        string fw = proj.GetUnityFrameworkTargetGuid();
        proj.AddFrameworkToProject(fw, "Vision.framework", false);
        proj.AddFrameworkToProject(fw, "AVFoundation.framework", false);
        proj.AddFrameworkToProject(fw, "CoreMedia.framework", false);
        proj.AddFrameworkToProject(fw, "CoreVideo.framework", false);
        foreach (var g in new[] { proj.GetUnityMainTargetGuid(), fw })
        {
            proj.SetBuildProperty(g, "ENABLE_BITCODE", "NO");
            proj.SetBuildProperty(g, "CODE_SIGNING_ALLOWED", "NO");
        }
        proj.WriteToFile(projPath);

        var plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument(); plist.ReadFromFile(plistPath);
        plist.root.SetString("NSCameraUsageDescription", PlayerSettings.iOS.cameraUsageDescription);
        plist.root.SetBoolean("UIRequiresFullScreen", true);
        plist.WriteToFile(plistPath);
    }
#endif
}
