using System;
using System.IO;
using System.Linq;
using UnityEditor.Animations;
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

        BuildArtMaterials();
        BuildRunnerPrefab();

        // ---- scene (empty: RushGame bootstraps itself)
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        // ---- player settings
        PlayerSettings.companyName = "Marvelous";
        PlayerSettings.productName = "Rail Rush";
        PlayerSettings.bundleVersion = "1.0";
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;
        PlayerSettings.allowedAutorotateToPortrait = true;
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

    // ---- Realistic PBR materials from Poly Haven textures (Assets/Art/Textures/<role>/<role>_albedo|normal|rough.jpg)
    static void BuildArtMaterials()
    {
        if (!Directory.Exists("Assets/Art/Textures")) return;
        var std = Shader.Find("Standard");
        foreach (var dir in Directory.GetDirectories("Assets/Art/Textures"))
        {
            string role = Path.GetFileName(dir);
            string albedo = $"{dir}/{role}_albedo.jpg", normal = $"{dir}/{role}_normal.jpg", rough = $"{dir}/{role}_rough.jpg", ms = $"{dir}/{role}_ms.png";
            bool metal = role == "rail" || role == "metal";
            // Standard shader wants metallic in R and smoothness (1 - roughness) in A
            if (File.Exists(rough) && !File.Exists(ms))
            {
                var src = new Texture2D(2, 2); src.LoadImage(File.ReadAllBytes(rough));
                var px = src.GetPixels32();
                for (int i = 0; i < px.Length; i++) { byte r = px[i].r; px[i] = new Color32(metal ? (byte)180 : (byte)0, 0, 0, (byte)(255 - r)); }
                var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true); dst.SetPixels32(px); dst.Apply();
                File.WriteAllBytes(ms, dst.EncodeToPNG());
                AssetDatabase.ImportAsset(ms);
            }
            var m = new Material(std);
            var a = AssetDatabase.LoadAssetAtPath<Texture2D>(albedo); if (a) m.SetTexture("_MainTex", a);
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>(normal);
            if (n) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f); }
            var g = AssetDatabase.LoadAssetAtPath<Texture2D>(ms);
            if (g) { m.SetTexture("_MetallicGlossMap", g); m.EnableKeyword("_METALLICGLOSSMAP"); m.SetFloat("_GlossMapScale", metal ? 0.9f : 0.7f); }
            m.enableInstancing = true;
            SaveMat($"Assets/Resources/Mat_{role}.mat", m);
        }
        if (File.Exists("Assets/Art/Sky/sky.hdr"))
        {
            var sky = new Material(Shader.Find("Skybox/Panoramic"));
            sky.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Sky/sky.hdr"));
            sky.SetFloat("_Exposure", 1.1f);
            SaveMat("Assets/Resources/SkyHdri.mat", sky);
        }
    }

    // ---- Mixamo runner: humanoid Remy + Run/Jump/Slide/Stumble/Idle states, saved as Resources/Runner.prefab
    static void BuildRunnerPrefab()
    {
        const string dir = "Assets/Art/Characters/Remy";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/Remy.fbx");
        if (model == null) { Debug.LogWarning("Remy.fbx missing; using primitive runner"); return; }
        const string ctrlPath = "Assets/Resources/RunnerAnim.controller";
        if (File.Exists(ctrlPath)) AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        var sm = ctrl.layers[0].stateMachine;
        foreach (var name in new[] { "Run", "Jump", "Slide", "Stumble", "Idle" })
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath($"{dir}/{name}.fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (clip == null) { Debug.LogWarning("Missing clip " + name); continue; }
            var st = sm.AddState(name); st.motion = clip;
            if (name == "Run") sm.defaultState = st;
        }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        var anim = inst.GetComponent<Animator>(); if (anim == null) anim = inst.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (var r in inst.GetComponentsInChildren<SkinnedMeshRenderer>()) { r.updateWhenOffscreen = true; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; }
        PrefabUtility.SaveAsPrefabAsset(inst, "Assets/Resources/Runner.prefab");
        UnityEngine.Object.DestroyImmediate(inst);
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
        proj.AddFrameworkToProject(fw, "AVFoundation.framework", false);
        proj.AddFrameworkToProject(fw, "CoreMedia.framework", false);
        proj.AddFrameworkToProject(fw, "CoreVideo.framework", false);
        foreach (var g in new[] { proj.GetUnityMainTargetGuid(), fw })
        {
            proj.SetBuildProperty(g, "ENABLE_BITCODE", "NO");
            proj.SetBuildProperty(g, "CODE_SIGNING_ALLOWED", "NO");
        }
        proj.WriteToFile(projPath);

        // Google MediaPipe Pose Landmarker (CocoaPods; `pod install` runs in CI before xcodebuild)
        File.WriteAllText(Path.Combine(path, "Podfile"),
            "platform :ios, '15.0'\n" +
            "use_frameworks! :linkage => :static\n" +
            "target 'UnityFramework' do\n  pod 'MediaPipeTasksVision', '0.10.14'\nend\n" +
            "target 'Unity-iPhone' do\nend\n");

        var plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument(); plist.ReadFromFile(plistPath);
        plist.root.SetString("NSCameraUsageDescription", PlayerSettings.iOS.cameraUsageDescription);
        plist.root.SetBoolean("UIRequiresFullScreen", true);
        plist.WriteToFile(plistPath);
    }
#endif
}
