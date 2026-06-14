using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LzebulOutlineOnly
{
    public static class OutlineOnlyConverter
    {
        private const string ContextMenuPath = "GameObject/Lzebul Outline/アウトライン用Prefabを作成";

        [MenuItem(ContextMenuPath, false, 49)]
        private static void CreateOutlineOnlyPrefab(MenuCommand command)
        {
            var source = ResolveContextGameObject(command);
            if (source == null)
            {
                EditorUtility.DisplayDialog("Lzebul Outline", "Hierarchy上の変換対象GameObjectを右クリックして実行してください。", "OK");
                return;
            }

            if (source.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                EditorUtility.DisplayDialog("Lzebul Outline", "選択したGameObjectにはRendererがありません。", "OK");
                return;
            }

            var outlineShader = Shader.Find(OutlineOnlyConstants.OutlineShaderName);
            if (outlineShader == null)
            {
                EditorUtility.DisplayDialog("Lzebul Outline", $"Shader.Find(\"{OutlineOnlyConstants.OutlineShaderName}\") に失敗しました。Shadersフォルダを再インポートしてください。", "OK");
                return;
            }

            var depthShader = Shader.Find(OutlineOnlyConstants.DepthShaderName);
            if (depthShader == null)
            {
                EditorUtility.DisplayDialog("Lzebul Outline", $"Shader.Find(\"{OutlineOnlyConstants.DepthShaderName}\") に失敗しました。Shadersフォルダを再インポートしてください。", "OK");
                return;
            }

            try
            {
                var builder = new OutlineOnlyAvatarBuilder(outlineShader, depthShader);
                var result = builder.Create(source);
                var prefabPath = AssetDatabase.GetAssetPath(result.Prefab);

                Selection.activeObject = result.Prefab;
                EditorGUIUtility.PingObject(result.Prefab);
                EditorUtility.DisplayDialog(
                    "Lzebul Outline",
                    $"アウトライン用Prefabを作成しました。\n\n{prefabPath}\n\n変換Renderer: {result.RendererCount}\n生成Material: {result.MaterialCount}\nDepth Proxy: {result.DepthProxyCount}",
                    "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Lzebul Outline", "アウトライン用Prefabの作成中にエラーが発生しました。Consoleを確認してください。", "OK");
            }
        }

        [MenuItem(ContextMenuPath, true)]
        private static bool CanCreateOutlineOnlyPrefab()
        {
            return Selection.activeGameObject != null;
        }

        private static GameObject ResolveContextGameObject(MenuCommand command)
        {
            return command.context as GameObject ?? Selection.activeGameObject;
        }
    }

    internal static class OutlineOnlyConstants
    {
        internal const string OutlineShaderName = "Lzebul/VRChat/Outline Only";
        internal const string DepthShaderName = "Lzebul/VRChat/Outline Depth Only";
        internal const string OutputRoot = "Assets/LzebulOutlineOnly/Generated";
        internal const string MaterialsFolder = OutputRoot + "/Materials";
        internal const string DepthMaterialsFolder = OutputRoot + "/DepthMaterials";
        internal const string DepthProxyPrefix = "__OutlineDepthProxy_";
        internal const float DefaultOutlineWidth = 0.08f;
        internal const float HairOutlineOffsetFactor = -1f;
        internal const float HairOutlineOffsetUnits = -1f;
    }

    internal sealed class OutlineOnlyConversionResult
    {
        internal OutlineOnlyConversionResult(GameObject prefab, int rendererCount, int materialCount, int depthProxyCount)
        {
            Prefab = prefab;
            RendererCount = rendererCount;
            MaterialCount = materialCount;
            DepthProxyCount = depthProxyCount;
        }

        internal GameObject Prefab { get; }
        internal int RendererCount { get; }
        internal int MaterialCount { get; }
        internal int DepthProxyCount { get; }
    }

    internal sealed class OutlineOnlyAvatarBuilder
    {
        private readonly OutlineOnlyMaterialConverter materialConverter;
        private readonly DepthProxyBuilder depthProxyBuilder;

        internal OutlineOnlyAvatarBuilder(Shader outlineShader, Shader depthShader)
        {
            materialConverter = new OutlineOnlyMaterialConverter(outlineShader);
            depthProxyBuilder = new DepthProxyBuilder(depthShader);
        }

        internal OutlineOnlyConversionResult Create(GameObject source)
        {
            OutlineOnlyAssetPaths.EnsureFolder(OutlineOnlyConstants.OutputRoot);
            OutlineOnlyAssetPaths.EnsureFolder(OutlineOnlyConstants.MaterialsFolder);
            OutlineOnlyAssetPaths.EnsureFolder(OutlineOnlyConstants.DepthMaterialsFolder);

            var instance = UnityEngine.Object.Instantiate(source);
            instance.name = OutlineOnlyAssetPaths.ToOutlineOnlyObjectName(source.name);
            instance.SetActive(true);

            try
            {
                DepthProxyBuilder.RemoveExistingDepthProxies(instance);
                var rendererCount = ConvertRendererMaterials(instance);
                var depthProxyCount = depthProxyBuilder.AddOrUpdateDepthProxies(instance);

                var prefabPath = $"{OutlineOnlyConstants.OutputRoot}/{OutlineOnlyAssetPaths.SanitizeFileName(instance.name)}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return new OutlineOnlyConversionResult(prefab, rendererCount, materialConverter.CreatedMaterialCount, depthProxyCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private int ConvertRendererMaterials(GameObject root)
        {
            var convertedMaterials = new Dictionary<MaterialConversionKey, Material>();
            var rendererCount = 0;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || DepthProxyBuilder.IsDepthProxy(renderer.transform))
                {
                    continue;
                }

                var materials = renderer.sharedMaterials;
                var useVisibleDepth = renderer is SkinnedMeshRenderer;
                for (var index = 0; index < materials.Length; index++)
                {
                    var sourceMaterial = materials[index];
                    if (sourceMaterial == null)
                    {
                        continue;
                    }

                    var conversionKey = new MaterialConversionKey(sourceMaterial, useVisibleDepth);
                    if (!convertedMaterials.TryGetValue(conversionKey, out var convertedMaterial))
                    {
                        convertedMaterial = materialConverter.Create(sourceMaterial, useVisibleDepth);
                        convertedMaterials.Add(conversionKey, convertedMaterial);
                    }

                    materials[index] = convertedMaterial;
                }

                renderer.sharedMaterials = materials;
                ConfigureVisibleRenderer(renderer);
                rendererCount++;
            }

            return rendererCount;
        }

        private static void ConfigureVisibleRenderer(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
        }
    }

    internal readonly struct MaterialConversionKey : IEquatable<MaterialConversionKey>
    {
        private readonly Material sourceMaterial;
        private readonly bool useVisibleDepth;

        internal MaterialConversionKey(Material sourceMaterial, bool useVisibleDepth)
        {
            this.sourceMaterial = sourceMaterial;
            this.useVisibleDepth = useVisibleDepth;
        }

        public bool Equals(MaterialConversionKey other)
        {
            return sourceMaterial == other.sourceMaterial && useVisibleDepth == other.useVisibleDepth;
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialConversionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((sourceMaterial != null ? sourceMaterial.GetInstanceID() : 0) * 397) ^ useVisibleDepth.GetHashCode();
            }
        }
    }

    internal sealed class OutlineOnlyMaterialConverter
    {
        private readonly Shader outlineShader;

        internal OutlineOnlyMaterialConverter(Shader outlineShader)
        {
            this.outlineShader = outlineShader;
        }

        internal int CreatedMaterialCount { get; private set; }

        internal Material Create(Material sourceMaterial, bool useVisibleDepth)
        {
            var material = new Material(outlineShader)
            {
                name = OutlineOnlyAssetPaths.ToOutlineOnlyMaterialName(sourceMaterial.name) + (useVisibleDepth ? "_SkinnedDepth" : string.Empty)
            };

            CopyAlphaProperties(sourceMaterial, material);
            CopyOutlineProperties(sourceMaterial, material);
            ApplySurfaceLineDefaults(sourceMaterial, material);
            ApplyDepthDefaults(material, useVisibleDepth);
            ApplyReservedEffectDefaults(material);

            material.shaderKeywords = Array.Empty<string>();
            material.renderQueue = -1;

            var materialPath = $"{OutlineOnlyConstants.MaterialsFolder}/{OutlineOnlyAssetPaths.SanitizeFileName(material.name)}.mat";
            OutlineOnlyAssetPaths.DeleteAssetIfExists(materialPath);
            AssetDatabase.CreateAsset(material, materialPath);
            CreatedMaterialCount++;
            return material;
        }

        private static void CopyAlphaProperties(Material sourceMaterial, Material targetMaterial)
        {
            MaterialPropertyUtility.CopyTexture(sourceMaterial, targetMaterial, "_MainTex");
            MaterialPropertyUtility.CopyTexture(sourceMaterial, targetMaterial, "_AlphaMask");
            MaterialPropertyUtility.CopyFloat(sourceMaterial, targetMaterial, "_Cutoff", 0.5f);
            MaterialPropertyUtility.CopyFloat(sourceMaterial, targetMaterial, "_AlphaMaskMode", 0f);
            MaterialPropertyUtility.CopyFloat(sourceMaterial, targetMaterial, "_AlphaMaskScale", 1f);
            MaterialPropertyUtility.CopyFloat(sourceMaterial, targetMaterial, "_AlphaMaskValue", 0f);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_UseMainTexAlpha", MaterialHeuristics.ShouldUseMainTexAlpha(sourceMaterial) ? 1f : 0f);
        }

        private static void CopyOutlineProperties(Material sourceMaterial, Material targetMaterial)
        {
            MaterialPropertyUtility.SetColor(targetMaterial, "_OutlineColor", Color.black);
            MaterialPropertyUtility.SetColor(targetMaterial, "_Color", Color.white);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_UseOutlineTex", 0f);
            MaterialPropertyUtility.CopyTexture(sourceMaterial, targetMaterial, "_OutlineWidthMask");
            MaterialPropertyUtility.CopyTexture(sourceMaterial, targetMaterial, "_OutlineVectorTex");
            MaterialPropertyUtility.CopyFloat(sourceMaterial, targetMaterial, "_OutlineWidth", OutlineOnlyConstants.DefaultOutlineWidth);
            MaterialPropertyUtility.CopyFloat(sourceMaterial, targetMaterial, "_OutlineFixWidth", 0.5f);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_OutlineVertexR2Width", 0f);
            MaterialPropertyUtility.CopyFloat(sourceMaterial, targetMaterial, "_OutlineVectorScale", 1f);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_UseMeshOutline", MaterialHeuristics.ShouldUseMeshOutline(sourceMaterial) ? 1f : 0f);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_OutlineBackfaceSuppress", MaterialHeuristics.GetBackfaceSuppression(sourceMaterial));
            MaterialPropertyUtility.SetFloat(targetMaterial, "_OutlineCull", (float)CullMode.Front);
            ApplyOutlineDepthOffset(sourceMaterial, targetMaterial);
        }

        private static void ApplyOutlineDepthOffset(Material sourceMaterial, Material targetMaterial)
        {
            var shouldPullForward = MaterialHeuristics.ShouldPullOutlineForward(sourceMaterial);
            var offsetFactor = shouldPullForward ? OutlineOnlyConstants.HairOutlineOffsetFactor : 0f;
            var offsetUnits = shouldPullForward ? OutlineOnlyConstants.HairOutlineOffsetUnits : 0f;
            MaterialPropertyUtility.SetFloat(targetMaterial, "_OutlineOffsetFactor", offsetFactor);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_OutlineOffsetUnits", offsetUnits);
        }

        private static void ApplySurfaceLineDefaults(Material sourceMaterial, Material targetMaterial)
        {
            var hasSurfaceLineMask = MaterialPropertyUtility.HasTexture(sourceMaterial, "_SurfaceLineMask");
            MaterialPropertyUtility.SetFloat(targetMaterial, "_UseSurfaceLines", hasSurfaceLineMask ? 1f : 0f);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_SurfaceLineOpacity", 1f);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_SurfaceLineThreshold", 0.5f);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_SurfaceLineSoftness", 0.02f);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_SurfaceLineInvert", 0f);
            MaterialPropertyUtility.CopyTexture(sourceMaterial, targetMaterial, "_SurfaceLineMask");
            MaterialPropertyUtility.SetColor(targetMaterial, "_SurfaceLineColor", Color.black);
        }

        private static void ApplyDepthDefaults(Material targetMaterial, bool useVisibleDepth)
        {
            MaterialPropertyUtility.SetFloat(targetMaterial, "_UseVisibleDepth", useVisibleDepth ? 1f : 0f);
        }

        private static void ApplyReservedEffectDefaults(Material targetMaterial)
        {
            MaterialPropertyUtility.SetFloat(targetMaterial, "_UseFutureEmission", 0f);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_FutureEmissionStrength", 0f);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_UseFutureChromaticAberration", 0f);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_FutureChromaticAberrationStrength", 0f);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_UseFutureHandDrawn", 0f);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_FutureHandDrawnStrength", 0f);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_UseFuturePixelStyle", 0f);
            MaterialPropertyUtility.SetFloat(targetMaterial, "_FuturePixelSize", 8f);
        }
    }

    internal sealed class DepthProxyBuilder
    {
        private readonly Shader depthShader;

        internal DepthProxyBuilder(Shader depthShader)
        {
            this.depthShader = depthShader;
        }

        internal int AddOrUpdateDepthProxies(GameObject root)
        {
            RemoveExistingDepthProxies(root);

            var depthMaterials = new Dictionary<Material, Material>();
            var proxyCount = 0;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || IsDepthProxy(renderer.transform))
                {
                    continue;
                }

                if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
                {
                    if (skinnedMeshRenderer.sharedMesh == null)
                    {
                        continue;
                    }

                    CreateSkinnedDepthProxy(skinnedMeshRenderer, depthMaterials);
                    proxyCount++;
                    continue;
                }

                if (renderer is MeshRenderer meshRenderer)
                {
                    var meshFilter = meshRenderer.GetComponent<MeshFilter>();
                    if (meshFilter == null || meshFilter.sharedMesh == null)
                    {
                        continue;
                    }

                    CreateMeshDepthProxy(meshRenderer, meshFilter, depthMaterials);
                    proxyCount++;
                }
            }

            return proxyCount;
        }

        internal static void RemoveExistingDepthProxies(GameObject root)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = transforms.Length - 1; index >= 0; index--)
            {
                var transform = transforms[index];
                if (transform != root.transform && transform.name.StartsWith(OutlineOnlyConstants.DepthProxyPrefix, StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(transform.gameObject);
                }
            }
        }

        internal static bool IsDepthProxy(Transform transform)
        {
            while (transform != null)
            {
                if (transform.name.StartsWith(OutlineOnlyConstants.DepthProxyPrefix, StringComparison.Ordinal))
                {
                    return true;
                }

                transform = transform.parent;
            }

            return false;
        }

        private void CreateMeshDepthProxy(MeshRenderer source, MeshFilter sourceFilter, Dictionary<Material, Material> depthMaterials)
        {
            var proxy = CreateDepthProxyObject(source);
            var meshFilter = proxy.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = sourceFilter.sharedMesh;
            var renderer = proxy.AddComponent<MeshRenderer>();
            ApplyDepthRendererSettings(source, renderer, depthMaterials);
        }

        private void CreateSkinnedDepthProxy(SkinnedMeshRenderer source, Dictionary<Material, Material> depthMaterials)
        {
            var proxy = CreateDepthProxyObject(source);
            var renderer = proxy.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = source.sharedMesh;
            renderer.rootBone = source.rootBone;
            renderer.bones = source.bones;
            renderer.localBounds = source.localBounds;
            renderer.quality = source.quality;
            renderer.updateWhenOffscreen = source.updateWhenOffscreen;
            renderer.skinnedMotionVectors = false;
            CopyBlendShapeWeights(source, renderer);
            ApplyDepthRendererSettings(source, renderer, depthMaterials);
        }

        private static void CopyBlendShapeWeights(SkinnedMeshRenderer source, SkinnedMeshRenderer target)
        {
            var mesh = source.sharedMesh;
            if (mesh == null)
            {
                return;
            }

            for (var index = 0; index < mesh.blendShapeCount; index++)
            {
                target.SetBlendShapeWeight(index, source.GetBlendShapeWeight(index));
            }
        }

        private static GameObject CreateDepthProxyObject(Renderer source)
        {
            var proxy = new GameObject(OutlineOnlyConstants.DepthProxyPrefix + source.gameObject.name)
            {
                layer = source.gameObject.layer
            };

            var proxyTransform = proxy.transform;
            var sourceTransform = source.transform;
            // Keep the proxy under the source so GameObject active state and transform animation are inherited.
            proxyTransform.SetParent(sourceTransform, false);
            proxyTransform.SetAsLastSibling();
            proxyTransform.localPosition = Vector3.zero;
            proxyTransform.localRotation = Quaternion.identity;
            proxyTransform.localScale = Vector3.one;
            return proxy;
        }

        private void ApplyDepthRendererSettings(Renderer source, Renderer target, Dictionary<Material, Material> depthMaterials)
        {
            target.enabled = source.enabled;
            target.sharedMaterials = CreateDepthMaterials(source.sharedMaterials, depthMaterials);
            target.shadowCastingMode = ShadowCastingMode.Off;
            target.receiveShadows = false;
            target.lightProbeUsage = LightProbeUsage.Off;
            target.reflectionProbeUsage = ReflectionProbeUsage.Off;
            target.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            target.allowOcclusionWhenDynamic = false;
            target.sortingLayerID = source.sortingLayerID;
            target.sortingOrder = source.sortingOrder;
        }

        private Material[] CreateDepthMaterials(Material[] sourceMaterials, Dictionary<Material, Material> depthMaterials)
        {
            var materials = new Material[sourceMaterials.Length];
            for (var index = 0; index < sourceMaterials.Length; index++)
            {
                var sourceMaterial = sourceMaterials[index];
                materials[index] = sourceMaterial == null ? null : GetOrCreateDepthMaterial(sourceMaterial, depthMaterials);
            }

            return materials;
        }

        private Material GetOrCreateDepthMaterial(Material sourceMaterial, Dictionary<Material, Material> depthMaterials)
        {
            if (depthMaterials.TryGetValue(sourceMaterial, out var depthMaterial))
            {
                return depthMaterial;
            }

            depthMaterial = new Material(depthShader)
            {
                name = sourceMaterial.name + "_DepthOnly"
            };

            CopyDepthMaterialProperties(sourceMaterial, depthMaterial);

            var materialPath = $"{OutlineOnlyConstants.DepthMaterialsFolder}/{OutlineOnlyAssetPaths.SanitizeFileName(depthMaterial.name)}.mat";
            OutlineOnlyAssetPaths.DeleteAssetIfExists(materialPath);
            AssetDatabase.CreateAsset(depthMaterial, materialPath);
            depthMaterials.Add(sourceMaterial, depthMaterial);
            return depthMaterial;
        }

        private static void CopyDepthMaterialProperties(Material sourceMaterial, Material depthMaterial)
        {
            MaterialPropertyUtility.CopyTexture(sourceMaterial, depthMaterial, "_MainTex");
            MaterialPropertyUtility.CopyTexture(sourceMaterial, depthMaterial, "_AlphaMask");
            MaterialPropertyUtility.CopyFloat(sourceMaterial, depthMaterial, "_Cutoff", 0.5f);
            MaterialPropertyUtility.CopyFloat(sourceMaterial, depthMaterial, "_UseMainTexAlpha", 0f);
            MaterialPropertyUtility.CopyFloat(sourceMaterial, depthMaterial, "_AlphaMaskMode", 0f);
            MaterialPropertyUtility.CopyFloat(sourceMaterial, depthMaterial, "_AlphaMaskScale", 1f);
            MaterialPropertyUtility.CopyFloat(sourceMaterial, depthMaterial, "_AlphaMaskValue", 0f);
            depthMaterial.renderQueue = -1;
        }
    }

    internal static class MaterialHeuristics
    {
        internal static bool ShouldUseMeshOutline(Material sourceMaterial)
        {
            if (sourceMaterial.HasProperty("_OutlineWidth"))
            {
                return sourceMaterial.GetFloat("_OutlineWidth") > 0.0001f;
            }

            return true;
        }

        internal static bool ShouldUseMainTexAlpha(Material sourceMaterial)
        {
            if (MaterialPropertyUtility.GetFloat(sourceMaterial, "_UseMainTexAlpha", 0f) > 0.5f)
            {
                return true;
            }

            if (MaterialPropertyUtility.GetFloat(sourceMaterial, "_AlphaMaskMode", 0f) > 0.5f)
            {
                return true;
            }

            if (MaterialPropertyUtility.GetFloat(sourceMaterial, "_TransparentMode", 0f) > 0.5f)
            {
                return true;
            }

            var cutoff = MaterialPropertyUtility.GetFloat(sourceMaterial, "_Cutoff", 0.5f);
            if (cutoff <= 0.01f)
            {
                return true;
            }

            if (sourceMaterial.renderQueue >= 2450 && sourceMaterial.renderQueue < 5000)
            {
                return true;
            }

            var shaderName = sourceMaterial.shader != null ? sourceMaterial.shader.name : string.Empty;
            return Contains(shaderName, "Cutout") || Contains(shaderName, "Transparent") || Contains(shaderName, "Trans");
        }

        internal static float GetBackfaceSuppression(Material sourceMaterial)
        {
            if (IsTwoSided(sourceMaterial))
            {
                return 0.9f;
            }

            if (ShouldUseMainTexAlpha(sourceMaterial))
            {
                return 0.8f;
            }

            var name = sourceMaterial.name;
            if (Contains(name, "hair") || Contains(name, "head_tp") || Contains(name, "face") || Contains(name, "eye"))
            {
                return 0.75f;
            }

            return 0f;
        }

        internal static bool ShouldPullOutlineForward(Material sourceMaterial)
        {
            var name = sourceMaterial != null ? sourceMaterial.name : string.Empty;
            return Contains(name, "hair")
                   || Contains(name, "髪")
                   || Contains(name, "bang")
                   || Contains(name, "front")
                   || Contains(name, "maegami")
                   || Contains(name, "前髪");
        }

        private static bool IsTwoSided(Material sourceMaterial)
        {
            if (Mathf.RoundToInt(MaterialPropertyUtility.GetFloat(sourceMaterial, "_Cull", 2f)) == (int)CullMode.Off)
            {
                return true;
            }

            var shaderName = sourceMaterial.shader != null ? sourceMaterial.shader.name : string.Empty;
            return Contains(shaderName, "TwoSide") || Contains(shaderName, "TwoPass");
        }

        private static bool Contains(string value, string fragment)
        {
            return value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal static class MaterialPropertyUtility
    {
        internal static void CopyTexture(Material sourceMaterial, Material targetMaterial, string propertyName)
        {
            if (!sourceMaterial.HasProperty(propertyName) || !targetMaterial.HasProperty(propertyName))
            {
                return;
            }

            var texture = sourceMaterial.GetTexture(propertyName);
            if (texture != null)
            {
                targetMaterial.SetTexture(propertyName, texture);
            }

            targetMaterial.SetTextureScale(propertyName, sourceMaterial.GetTextureScale(propertyName));
            targetMaterial.SetTextureOffset(propertyName, sourceMaterial.GetTextureOffset(propertyName));
        }

        internal static void CopyColor(Material sourceMaterial, Material targetMaterial, string propertyName, Color fallback)
        {
            if (!targetMaterial.HasProperty(propertyName))
            {
                return;
            }

            targetMaterial.SetColor(propertyName, sourceMaterial.HasProperty(propertyName) ? sourceMaterial.GetColor(propertyName) : fallback);
        }

        internal static void CopyFloat(Material sourceMaterial, Material targetMaterial, string propertyName, float fallback)
        {
            if (!targetMaterial.HasProperty(propertyName))
            {
                return;
            }

            targetMaterial.SetFloat(propertyName, GetFloat(sourceMaterial, propertyName, fallback));
        }

        internal static bool HasTexture(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) && material.GetTexture(propertyName) != null;
        }

        internal static float GetFloat(Material material, string propertyName, float fallback)
        {
            return material.HasProperty(propertyName) ? material.GetFloat(propertyName) : fallback;
        }

        internal static Color GetColor(Material material, string propertyName, Color fallback)
        {
            return material.HasProperty(propertyName) ? material.GetColor(propertyName) : fallback;
        }

        internal static void SetFloat(Material targetMaterial, string propertyName, float value)
        {
            if (targetMaterial.HasProperty(propertyName))
            {
                targetMaterial.SetFloat(propertyName, value);
            }
        }

        internal static void SetColor(Material targetMaterial, string propertyName, Color value)
        {
            if (targetMaterial.HasProperty(propertyName))
            {
                targetMaterial.SetColor(propertyName, value);
            }
        }
    }

    internal static class OutlineOnlyAssetPaths
    {
        internal static void EnsureFolder(string folder)
        {
            folder = folder.Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parent = Path.GetDirectoryName(folder)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(parent))
            {
                return;
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        internal static string ToOutlineOnlyObjectName(string sourceName)
        {
            return sourceName.EndsWith("_OutlineOnly", StringComparison.Ordinal) ? sourceName : sourceName + "_OutlineOnly";
        }

        internal static string ToOutlineOnlyMaterialName(string sourceName)
        {
            return sourceName.EndsWith("_OutlineOnly", StringComparison.Ordinal) ? sourceName : sourceName + "_OutlineOnly";
        }

        internal static string SanitizeFileName(string fileName)
        {
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            return fileName.Replace('/', '_').Replace('\\', '_');
        }

        internal static void DeleteAssetIfExists(string path)
        {
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }

}
