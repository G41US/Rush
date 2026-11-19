using UnityEditor;
using UnityEngine;

/// Import rules for the realistic art: Poly Haven PBR textures and Mixamo humanoid FBX files.
public class RushImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        var ti = (TextureImporter)assetImporter;
        if (assetPath.Contains("/Art/Textures/"))
        {
            ti.maxTextureSize = 1024;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            ti.wrapMode = TextureWrapMode.Repeat;
            if (assetPath.Contains("_normal")) ti.textureType = TextureImporterType.NormalMap;
            if (assetPath.Contains("_rough") || assetPath.Contains("_ms")) ti.sRGBTexture = false;
            if (assetPath.Contains("_rough")) ti.isReadable = true;
        }
        else if (assetPath.Contains("/Art/Sky/"))
        {
            ti.maxTextureSize = 2048;
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.mipmapEnabled = false;
        }
    }

    void OnPreprocessModel()
    {
        if (!assetPath.Contains("/Art/Characters/")) return;
        var mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        bool isCharacter = assetPath.EndsWith("Remy.fbx");
        mi.importAnimation = !isCharacter;
        mi.materialImportMode = isCharacter ? ModelImporterMaterialImportMode.ImportViaMaterialDescription : ModelImporterMaterialImportMode.None;
    }

    void OnPreprocessAnimation()
    {
        if (!assetPath.Contains("/Art/Characters/")) return;
        var mi = (ModelImporter)assetImporter;
        var clips = mi.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;
        string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        bool loop = file == "Run" || file == "Idle";
        foreach (var c in clips)
        {
            c.name = file;
            c.loopTime = loop; c.loopPose = loop;
            c.lockRootRotation = true; c.keepOriginalOrientation = true;
            c.lockRootHeightY = true; c.keepOriginalPositionY = true;     // keep the clip's own vertical motion
            c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;  // runner stays in place; world scrolls
        }
        mi.clipAnimations = clips;
    }
}
