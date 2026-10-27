using System;
using System.Collections.Generic;
using System.IO;
using CameraControls;
using Lineart.EdgeGraph;
using Lineart.Stylize;
using NPR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
using GreasePencil;
using Lineart.Silhouette;

namespace SpikePod.Editor
{
    /// <summary>
    /// Builds the Unity side of the Spike Pod from the Blender export (Art/SpikePod.fbx + Art/spikepod_manifest.json):
    /// toon + outline materials, taper profiles, the SpikePod prefab and the SpikePod scene (camera, lights, volume).
    /// Re-run after re-exporting from Blender; generated assets are updated in place.
    /// </summary>
    public static class SpikePodBuilder
    {
        const string Root = "Assets/SpikePod";
        const string ArtFolder = Root + "/Art";
        const string ModelPath = ArtFolder + "/SpikePod.fbx";
        const string ManifestPath = ArtFolder + "/spikepod_manifest.json";
        const string LutPath = ArtFolder + "/AgX_LUT.png";
        const string MaterialsFolder = Root + "/Materials";
        const string SettingsFolder = Root + "/Settings";
        const string PrefabsFolder = Root + "/Prefabs";
        const string ScenesFolder = Root + "/Scenes";
        const string PrefabPath = PrefabsFolder + "/SpikePod.prefab";
        const string ScenePath = ScenesFolder + "/SpikePod.unity";
        const string VolumeProfilePath = SettingsFolder + "/SpikePod_Volume.asset";

        const string ToonShaderName = "NPR/Toon";
        const string OutlineShaderName = "Lineart/GreasePencilBlended";

        [MenuItem("Tools/SpikePod/Rebuild From Blender Export")]
        public static void Rebuild()
        {
            SpikePodManifest manifest = LoadManifest();
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new FileNotFoundException($"Model not found: {ModelPath}");

            EnsureFolder(MaterialsFolder);
            EnsureFolder(SettingsFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(ScenesFolder);

            foreach (var outline in manifest.outlines)
            {
                foreach (var warning in outline.warnings) Debug.LogWarning($"SpikePod: {outline.name}: {warning}");
            }

            var toonMaterials = BuildToonMaterials(manifest);
            Material outlineMaterial = BuildOutlineMaterial();
            GameObject prefab = BuildPrefab(manifest, model, toonMaterials, outlineMaterial);
            BuildScene(manifest, model, prefab);

            AssetDatabase.SaveAssets();
            Debug.Log($"SpikePod: rebuilt {PrefabPath} and {ScenePath} from {manifest.export_blend}");
        }

        // ------------------------------------------------------------------------------------------
        // Manifest / assets

        static SpikePodManifest LoadManifest()
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (text == null) throw new FileNotFoundException($"Manifest not found: {ManifestPath}");
            return JsonUtility.FromJson<SpikePodManifest>(text.text);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static T LoadOrCreate<T>(string path, Func<T> create) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = create();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static string AssetName(string blenderName)
        {
            var chars = blenderName.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '_';
            }
            return new string(chars).Trim('_');
        }

        /// <summary>Material / camera colours are authored in sRGB and linearized by Unity; Blender values are linear.</summary>
        static Color LinearToAuthored(float[] linear)
        {
            Color c = SpikePodManifest.ToColor(linear);
            return new Color(c.r, c.g, c.b, c.a).gamma;
        }

        static Dictionary<string, Material> BuildToonMaterials(SpikePodManifest manifest)
        {
            Shader shader = FindShader(ToonShaderName);
            var result = new Dictionary<string, Material>();
            foreach (var m in manifest.materials)
            {
                var material = LoadOrCreate($"{MaterialsFolder}/{AssetName(m.name)}.mat", () => new Material(shader));
                material.shader = shader;
                material.SetColor("_BaseColor", LinearToAuthored(m.baseColor));
                material.SetColor("_ShadowTint", LinearToAuthored(m.shadowTint));
                material.SetFloat("_BandThreshold", m.bandThreshold);
                material.SetFloat("_BandSoftness", m.bandSoftness);
                material.SetFloat("_ReceiveShadows", m.receiveShadows);
                material.SetFloat("_ShadowThreshold", m.shadowThreshold);
                material.SetFloat("_RimStrength", m.rimStrength);
                material.SetColor("_RimTint", LinearToAuthored(m.rimTint));
                material.SetFloat("_RimWidth", m.rimWidth);
                material.SetFloat("_RimSoftness", m.rimSoftness);
                material.SetFloat("_RimFacing", m.rimFacing);
                material.SetFloat("_AoMask", m.hasAoMask ? 1f : 0f);
                material.SetFloat("_AoThreshold", m.aoThreshold);
                material.SetColor("_AoDarkColor", LinearToAuthored(m.aoDarkColor));
                if (m.hasAoMask) material.EnableKeyword("_NPR_AO_MASK");
                else material.DisableKeyword("_NPR_AO_MASK");
                EditorUtility.SetDirty(material);
                result[m.name] = material;
            }
            return result;
        }

        static Material BuildOutlineMaterial()
        {
            Shader shader = FindShader(OutlineShaderName);
            var material = LoadOrCreate($"{MaterialsFolder}/Outline_GreasePencil.mat", () => new Material(shader));
            material.shader = shader;
            // strokes behind the plant's own meshes are hidden
            material.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            EditorUtility.SetDirty(material);
            return material;
        }

        static StrokeTaperProfile BuildTaperProfile(SpikePodManifest manifest, SpikePodManifest.Outline outline)
        {
            var start = manifest.FindCurve(outline.taperStartCurve);
            var end = manifest.FindCurve(outline.taperEndCurve);
            string name = $"Taper_{AssetName(outline.taperStartCurve)}__{AssetName(outline.taperEndCurve)}";
            var profile = LoadOrCreate($"{SettingsFolder}/{name}.asset", ScriptableObject.CreateInstance<StrokeTaperProfile>);
            // a missing curve samples as Y = 1 in Blender (no taper)
            profile.startCurve = start?.samples ?? new[] { 1f };
            profile.endCurve = end?.samples ?? new[] { 1f };
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static Shader FindShader(string name)
        {
            Shader shader = Shader.Find(name);
            if (shader == null) throw new InvalidOperationException($"Shader '{name}' not found");
            return shader;
        }

        // ------------------------------------------------------------------------------------------
        // Prefab

        static Transform FindNode(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindNode(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Copies the node's transform relative to the model root.</summary>
        static void CopyPlacement(Transform modelRoot, Transform node, Transform target)
        {
            Matrix4x4 m = modelRoot.worldToLocalMatrix * node.localToWorldMatrix;
            target.localPosition = m.GetPosition();
            target.localRotation = m.rotation;
            target.localScale = m.lossyScale;
        }

        static GameObject BuildPrefab(SpikePodManifest manifest, GameObject model,
                                      Dictionary<string, Material> toonMaterials, Material outlineMaterial)
        {
            var root = new GameObject("SpikePod");
            try
            {
                foreach (var part in manifest.parts)
                {
                    Transform node = FindNode(model.transform, part.name);
                    var sourceFilter = node != null ? node.GetComponent<MeshFilter>() : null;
                    if (sourceFilter == null)
                    {
                        Debug.LogError($"SpikePod: mesh '{part.name}' not found in {ModelPath}");
                        continue;
                    }

                    var go = new GameObject(part.name);
                    go.transform.SetParent(root.transform, false);
                    CopyPlacement(model.transform, node, go.transform);

                    go.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
                    var meshRenderer = go.AddComponent<MeshRenderer>();
                    meshRenderer.sharedMaterial = toonMaterials.TryGetValue(part.material, out var mat) ? mat : null;
                    meshRenderer.shadowCastingMode = ShadowCastingMode.On;
                    meshRenderer.receiveShadows = true;

                    var outline = manifest.FindOutline(part.name);
                    if (outline != null) AddOutline(go, manifest, outline, outlineMaterial);
                }

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void AddOutline(GameObject go, SpikePodManifest manifest, SpikePodManifest.Outline o, Material outlineMaterial)
        {
            bool edgeBased = o.method == "edge";
            if (edgeBased)
            {
                var calculator = go.AddComponent<EdgeSilhouetteCalculator>();
                calculator.includeBoundary = o.includeBoundary;
                calculator.skipConcaveEdges = o.skipConcave;
                calculator.concaveTolerance = o.concaveTolerance;
                calculator.outlineFalloff = o.outlineFalloff;
                calculator.undersideFalloff = o.undersideFalloff;
                calculator.scaleBySpan = o.scaleBySpan;
                calculator.fullSpan = o.fullSpan;
                calculator.scaleByPolarAngle = o.scaleByPolar;
                calculator.joinBranches = o.joinBranches;
                calculator.straightnessWeight = o.straightnessWeight;
            }
            else
            {
                var calculator = go.AddComponent<SilhouetteEdgeCalculator>();
                calculator.radiusMultiplier = o.strokeRadius;
                calculator.lateralShiftFactor = 0f;
                calculator.lateralShiftConstant = 0f;
                calculator.skipConcaveEdges = o.skipConcave;
                calculator.concaveTolerance = o.concaveTolerance;
            }

            var stylizer = go.AddComponent<StrokeStylizer>();
            stylizer.radius.strokeRadius = o.strokeRadius;
            stylizer.radius.useWeights = edgeBased;
            stylizer.radius.useRadiusVariation = o.useRadiusVariation;
            stylizer.radius.cornerType = Enum.TryParse(o.cornerType, out StrokeCornerType corner) ? corner : StrokeCornerType.Round;
            stylizer.radius.miterAngle = o.miterAngle;

            stylizer.profile.enabled = o.profileEnabled;
            if (o.profileEnabled)
            {
                stylizer.profile.profile = BuildTaperProfile(manifest, o);
                stylizer.profile.taperStartDistance = o.taperStartDistance;
                stylizer.profile.taperEndDistance = o.taperEndDistance;
                stylizer.profile.strength = o.profileStrength;
                stylizer.profile.pixelsPerUnit = o.pixelsPerUnit;
                stylizer.profile.applyToRadius = o.profileApplyToRadius;
                stylizer.profile.applyToPushRadius = o.profileApplyToPush;
                stylizer.profile.resolution = o.profileResolution;
            }

            stylizer.push.enabled = o.pushEnabled;
            stylizer.push.push = o.push;
            stylizer.push.offset = o.pushOffset;
            stylizer.push.maxCornerPush = o.maxCornerPush;

            stylizer.spikeThinning.enabled = o.spikeThinning;
            stylizer.spikeThinning.maxSpikeLength = o.maxSpikeLength;

            // Stroke colour: Grease Pencil material colour, applied as a full layer tint (vertex colours are white).
            // The tint goes through SetColor (linearized by Unity), the material buffer is uploaded raw (linear).
            float[] gpColor = manifest.FindGpMaterial(o.material)?.color;
            Color strokeColor = SpikePodManifest.ToColor(gpColor);
            var renderer = go.AddComponent<SilhouetteRenderer>();
            renderer.material = outlineMaterial;
            renderer.opacity = 1f;
            Color tint = LinearToAuthored(gpColor);
            renderer.colorTint = new Color(tint.r, tint.g, tint.b, 1f);
            renderer.greasePencilMaterials = new List<GreasePencilMaterial>
            {
                new() { name = o.material, stroke_color = strokeColor, fill_color = Color.clear, fill_mix_color = Color.clear },
            };
        }

        // ------------------------------------------------------------------------------------------
        // Scene

        static void BuildScene(SpikePodManifest manifest, GameObject model, GameObject prefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Camera camera = BuildCamera(manifest, model);
            Light keyLight = BuildKeyLight(manifest, model);
            Transform rimLight = BuildPlacedNode(model, manifest.FindLight("rim")?.name, "Rim Light");
            BuildLightRig(manifest, keyLight.transform, rimLight);
            BuildVolume();

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.sun = keyLight;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

            // Blender style viewport navigation in play mode, orbiting / framing the plant
            var navigation = camera.gameObject.AddComponent<BlenderViewportCamera>();
            navigation.frameTarget = instance.transform;

            foreach (var calculator in instance.GetComponentsInChildren<EdgeSilhouetteCalculator>()) calculator.viewCamera = camera;
            foreach (var calculator in instance.GetComponentsInChildren<SilhouetteEdgeCalculator>()) calculator.viewCamera = camera;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
            foreach (var component in instance.GetComponentsInChildren<MonoBehaviour>())
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static Transform BuildPlacedNode(GameObject model, string nodeName, string objectName)
        {
            var go = new GameObject(objectName);
            Transform node = nodeName != null ? FindNode(model.transform, nodeName) : null;
            if (node != null) CopyPlacement(model.transform, node, go.transform);
            else Debug.LogWarning($"SpikePod: node '{nodeName}' not found in {ModelPath}");
            return go.transform;
        }

        static Camera BuildCamera(SpikePodManifest manifest, GameObject model)
        {
            var info = manifest.camera;
            Transform t = BuildPlacedNode(model, info.name, "Main Camera");
            t.gameObject.tag = "MainCamera";
            t.localScale = Vector3.one;

            float width = manifest.render.resolution?.Length == 2 ? manifest.render.resolution[0] : 1920;
            float height = manifest.render.resolution?.Length == 2 ? manifest.render.resolution[1] : 1080;
            // Blender sensor fit AUTO: ortho scale / sensor width spans the larger image dimension
            float verticalFraction = width >= height ? height / width : 1f;

            var camera = t.gameObject.AddComponent<Camera>();
            camera.orthographic = info.orthographic;
            camera.orthographicSize = 0.5f * info.orthoScale * verticalFraction;
            camera.fieldOfView = 2f * Mathf.Atan(0.5f * info.sensorWidth * verticalFraction / Mathf.Max(info.lens, 1e-3f)) * Mathf.Rad2Deg;
            camera.nearClipPlane = info.clipStart;
            camera.farClipPlane = info.clipEnd;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = LinearToAuthored(manifest.render.background);

            var data = t.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.renderShadows = true;
            return camera;
        }

        static Light BuildKeyLight(SpikePodManifest manifest, GameObject model)
        {
            Transform t = BuildPlacedNode(model, manifest.FindLight("key")?.name, "Key Light");
            t.localScale = Vector3.one;
            var light = t.gameObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 1f;
            return light;
        }

        static void BuildLightRig(SpikePodManifest manifest, Transform key, Transform rim)
        {
            var rig = new GameObject("NPR Light Rig").AddComponent<NprLightRig>();
            rig.keyLight = key;
            rig.rimLight = rim;
            var keyInfo = manifest.FindLight("key");
            var rimInfo = manifest.FindLight("rim");
            if (keyInfo != null)
            {
                rig.keyColor = SpikePodManifest.ToColor(keyInfo.color);
                rig.keyStrength = keyInfo.energy;
            }
            if (rimInfo != null)
            {
                rig.rimColor = SpikePodManifest.ToColor(rimInfo.color);
                rig.rimStrength = rimInfo.energy;
            }
        }

        static void BuildVolume()
        {
            var profile = LoadOrCreate(VolumeProfilePath, ScriptableObject.CreateInstance<VolumeProfile>);
            foreach (var component in profile.components.ToArray())
            {
                profile.Remove(component.GetType());
                Object.DestroyImmediate(component, true);
            }

            // Blender renders through AgX; the baked LUT reproduces it on top of an untonemapped image.
            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.None);

            // Blender's render has no lens / grading effects: neutralize whatever the project default profile sets.
            profile.Add<Bloom>(true).intensity.Override(0f);
            profile.Add<Vignette>(true).intensity.Override(0f);
            profile.Add<FilmGrain>(true).intensity.Override(0f);
            profile.Add<ChromaticAberration>(true).intensity.Override(0f);
            profile.Add<LensDistortion>(true).intensity.Override(0f);
            profile.Add<MotionBlur>(true).intensity.Override(0f);
            profile.Add<PaniniProjection>(true).distance.Override(0f);
            profile.Add<DepthOfField>(true).mode.Override(DepthOfFieldMode.Off);
            profile.Add<ScreenSpaceLensFlare>(true).intensity.Override(0f);
            var colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.postExposure.Override(0f);
            colorAdjustments.contrast.Override(0f);
            colorAdjustments.saturation.Override(0f);
            colorAdjustments.hueShift.Override(0f);
            colorAdjustments.colorFilter.Override(Color.white);
            var whiteBalance = profile.Add<WhiteBalance>(true);
            whiteBalance.temperature.Override(0f);
            whiteBalance.tint.Override(0f);

            var lut = AssetDatabase.LoadAssetAtPath<Texture2D>(LutPath);
            if (lut != null)
            {
                var colorLookup = profile.Add<ColorLookup>(true);
                colorLookup.texture.Override(lut);
                colorLookup.contribution.Override(1f);
            }
            else
            {
                Debug.LogWarning($"SpikePod: {LutPath} missing, colours will not match Blender's view transform.");
            }

            foreach (var component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);

            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }
    }
}
