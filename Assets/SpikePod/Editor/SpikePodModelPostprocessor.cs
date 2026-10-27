using System;
using Lineart.Editor;
using UnityEditor;
using UnityEngine;

namespace SpikePod.Editor
{
    /// <summary>Import settings for the Blender export in Assets/SpikePod/Art and conversion of its baked streams.</summary>
    public class SpikePodModelPostprocessor : AssetPostprocessor
    {
        public const string ArtFolder = "Assets/SpikePod/Art/";

        bool InScope => assetPath.StartsWith(ArtFolder, StringComparison.Ordinal);

        void OnPreprocessModel()
        {
            if (!InScope) return;
            var importer = (ModelImporter)assetImporter;
            importer.isReadable = true;                       // edge outlines build their topology on the CPU
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.bakeAxisConversion = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.importBlendShapes = false;
            importer.importCameras = true;
            importer.importLights = true;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        void OnPreprocessTexture()
        {
            if (!InScope || !assetPath.EndsWith("_LUT.png", StringComparison.OrdinalIgnoreCase)) return;
            // URP Color Lookup strip: raw values, no filtering artefacts from mips / compression
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaSource = TextureImporterAlphaSource.None;
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!InScope) return;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                var result = BlenderLineStreamConverter.Convert(mesh);
                if (!result.converted)
                {
                    Debug.LogWarning($"SpikePod import: '{mesh.name}' has no baked line streams.");
                }
                else if (result.agreement < 0.5f)
                {
                    Debug.LogWarning($"SpikePod import: '{mesh.name}' line normal basis is uncertain (agreement {result.agreement:F2}).");
                }
            }
        }
    }
}
