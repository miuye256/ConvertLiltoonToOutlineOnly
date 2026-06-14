using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

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
                    $"アウトライン用Prefabを作成しました。\n\n{prefabPath}\n\n変換Renderer: {result.RendererCount}\n生成Material: {result.MaterialCount}\nDepth Proxy: {result.DepthProxyCount}\nExpressionMenu: {(result.ExpressionInstalled ? "追加済み" : "未追加")}",
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
        internal const string ExpressionsFolder = OutputRoot + "/Expressions";
        internal const string DepthProxyPrefix = "__OutlineDepthProxy_";
        internal const string ExpressionPrefix = "LOO_";
        internal const string ParamColorR = ExpressionPrefix + "ColorR";
        internal const string ParamColorG = ExpressionPrefix + "ColorG";
        internal const string ParamColorB = ExpressionPrefix + "ColorB";
        internal const string ParamChromaticAberration = ExpressionPrefix + "ChromaticAberration";
        internal const string ParamChromaticAberrationStrength = ExpressionPrefix + "ChromaticAberrationStrength";
        internal const string ExpressionLayerPrefix = "LOO Color ";
        internal const string EffectLayerPrefix = "LOO Effect ";
        internal const float DefaultOutlineWidth = 0.08f;
        internal const float DefaultChromaticAberrationStrength = 0.35f;
        internal const float HairOutlineOffsetFactor = -1f;
        internal const float HairOutlineOffsetUnits = -1f;
    }

    internal sealed class OutlineOnlyConversionResult
    {
        internal OutlineOnlyConversionResult(GameObject prefab, int rendererCount, int materialCount, int depthProxyCount, bool expressionInstalled)
        {
            Prefab = prefab;
            RendererCount = rendererCount;
            MaterialCount = materialCount;
            DepthProxyCount = depthProxyCount;
            ExpressionInstalled = expressionInstalled;
        }

        internal GameObject Prefab { get; }
        internal int RendererCount { get; }
        internal int MaterialCount { get; }
        internal int DepthProxyCount { get; }
        internal bool ExpressionInstalled { get; }
    }

    internal sealed class OutlineOnlyAvatarBuilder
    {
        private readonly OutlineOnlyMaterialConverter materialConverter;
        private readonly DepthProxyBuilder depthProxyBuilder;
        private readonly OutlineOnlyExpressionColorInstaller expressionColorInstaller;

        internal OutlineOnlyAvatarBuilder(Shader outlineShader, Shader depthShader)
        {
            materialConverter = new OutlineOnlyMaterialConverter(outlineShader);
            depthProxyBuilder = new DepthProxyBuilder(depthShader);
            expressionColorInstaller = new OutlineOnlyExpressionColorInstaller();
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
                var expressionInstalled = expressionColorInstaller.Install(instance, prefabPath);
                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return new OutlineOnlyConversionResult(prefab, rendererCount, materialConverter.CreatedMaterialCount, depthProxyCount, expressionInstalled);
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

    internal sealed class OutlineOnlyExpressionColorInstaller
    {
        private const string RootMenuAssetName = "RootMenu.asset";
        private const string OriginalMenuAssetName = "OriginalMenu.asset";
        private const string OutlineMenuAssetName = "OutlineMenu.asset";
        private const string ColorMenuAssetName = "OutlineColorMenu.asset";
        private const string ChromaticMenuAssetName = "ChromaticAberrationMenu.asset";
        private const string ParametersAssetName = "ExpressionParameters.asset";
        private const string FxControllerAssetName = "OutlineFX.controller";
        private const int MaxMenuControls = 8;

        private static readonly ColorChannel[] ColorChannels =
        {
            new ColorChannel("R", OutlineOnlyConstants.ParamColorR, "r"),
            new ColorChannel("G", OutlineOnlyConstants.ParamColorG, "g"),
            new ColorChannel("B", OutlineOnlyConstants.ParamColorB, "b")
        };

        internal bool Install(GameObject root, string prefabPath)
        {
            var avatar = root.GetComponent<VRCAvatarDescriptor>();
            if (avatar == null)
            {
                return false;
            }

            var colorBindings = CollectColorBindings(root);
            if (colorBindings.Count == 0)
            {
                Debug.LogWarning("Lzebul Outline: ExpressionMenu用の色変更対象Rendererが見つからなかったため、Expression追加をスキップしました。");
                return false;
            }

            var assetFolder = GetExpressionAssetFolder(prefabPath);
            OutlineOnlyAssetPaths.EnsureFolder(assetFolder);

            var sourceExpressionParameters = avatar.expressionParameters;
            var sourceExpressionsMenu = avatar.expressionsMenu;
            var sourceFxController = GetFxController(avatar);

            var expressionParameters = CreateExpressionParameters(sourceExpressionParameters, $"{assetFolder}/{ParametersAssetName}");
            var colorMenu = CreateColorMenu($"{assetFolder}/{ColorMenuAssetName}");
            var chromaticMenu = CreateChromaticMenu($"{assetFolder}/{ChromaticMenuAssetName}");
            var outlineMenu = CreateOutlineMenu(colorMenu, chromaticMenu, $"{assetFolder}/{OutlineMenuAssetName}");
            var rootMenu = CreateRootMenu(sourceExpressionsMenu, outlineMenu, assetFolder);
            var fxController = CreateFxController(sourceFxController, colorBindings, assetFolder);

            avatar.customExpressions = true;
            avatar.expressionParameters = expressionParameters;
            avatar.expressionsMenu = rootMenu;
            AssignFxController(avatar, fxController);

            EditorUtility.SetDirty(avatar);
            return true;
        }

        private static List<RendererColorBinding> CollectColorBindings(GameObject root)
        {
            var bindings = new List<RendererColorBinding>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || DepthProxyBuilder.IsDepthProxy(renderer.transform))
                {
                    continue;
                }

                if (!UsesOutlineOnlyMaterial(renderer))
                {
                    continue;
                }

                bindings.Add(new RendererColorBinding(
                    AnimationUtility.CalculateTransformPath(renderer.transform, root.transform),
                    renderer.GetType()));
            }

            return bindings;
        }

        private static bool UsesOutlineOnlyMaterial(Renderer renderer)
        {
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material != null && material.shader != null && material.shader.name == OutlineOnlyConstants.OutlineShaderName)
                {
                    return true;
                }
            }

            return false;
        }

        private static VRCExpressionParameters CreateExpressionParameters(VRCExpressionParameters sourceParameters, string assetPath)
        {
            OutlineOnlyAssetPaths.DeleteAssetIfExists(assetPath);

            var parameters = sourceParameters == null
                ? ScriptableObject.CreateInstance<VRCExpressionParameters>()
                : UnityEngine.Object.Instantiate(sourceParameters);
            parameters.name = "Lzebul Outline Parameters";
            parameters.hideFlags = HideFlags.None;
            if (parameters.parameters == null)
            {
                parameters.parameters = Array.Empty<VRCExpressionParameters.Parameter>();
            }

            UpsertGeneratedParameters(parameters, true);

            if (parameters.CalcTotalCost() > VRCExpressionParameters.MAX_PARAMETER_COST)
            {
                UpsertGeneratedParameters(parameters, false);
                Debug.LogWarning("Lzebul Outline: ExpressionParametersの同期容量を超えるため、アウトライン操作パラメータは非同期で追加しました。自分の画面では変更できますが、他ユーザーには同期されません。");
            }

            AssetDatabase.CreateAsset(parameters, assetPath);
            EditorUtility.SetDirty(parameters);
            return parameters;
        }

        private static void UpsertGeneratedParameters(VRCExpressionParameters parameters, bool networkSynced)
        {
            var list = new List<VRCExpressionParameters.Parameter>();
            if (parameters.parameters != null)
            {
                for (var index = 0; index < parameters.parameters.Length; index++)
                {
                    var parameter = parameters.parameters[index];
                    if (parameter != null && !IsGeneratedExpressionParameter(parameter.name))
                    {
                        list.Add(parameter);
                    }
                }
            }

            for (var index = 0; index < ColorChannels.Length; index++)
            {
                list.Add(new VRCExpressionParameters.Parameter
                {
                    name = ColorChannels[index].ParameterName,
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = networkSynced
                });
            }

            list.Add(new VRCExpressionParameters.Parameter
            {
                name = OutlineOnlyConstants.ParamChromaticAberration,
                valueType = VRCExpressionParameters.ValueType.Bool,
                defaultValue = 0f,
                saved = true,
                networkSynced = networkSynced
            });

            list.Add(new VRCExpressionParameters.Parameter
            {
                name = OutlineOnlyConstants.ParamChromaticAberrationStrength,
                valueType = VRCExpressionParameters.ValueType.Float,
                defaultValue = OutlineOnlyConstants.DefaultChromaticAberrationStrength,
                saved = true,
                networkSynced = networkSynced
            });

            parameters.parameters = list.ToArray();
        }

        private static VRCExpressionsMenu CreateColorMenu(string assetPath)
        {
            var menu = CreateMenuAsset(assetPath, "Lzebul Outline Color Menu");
            ResetMenuControls(menu);

            for (var index = 0; index < ColorChannels.Length; index++)
            {
                var channel = ColorChannels[index];
                menu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = channel.DisplayName,
                    type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                    parameter = new VRCExpressionsMenu.Control.Parameter
                    {
                        name = string.Empty
                    },
                    value = 0f,
                    subParameters = new[]
                    {
                        new VRCExpressionsMenu.Control.Parameter
                        {
                            name = channel.ParameterName
                        }
                    }
                });
            }

            SaveMenuAsset(menu);
            return menu;
        }

        private static VRCExpressionsMenu CreateChromaticMenu(string assetPath)
        {
            var menu = CreateMenuAsset(assetPath, "Lzebul Outline Chromatic Aberration Menu");
            ResetMenuControls(menu);

            menu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "ON/OFF",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter
                {
                    name = OutlineOnlyConstants.ParamChromaticAberration
                },
                value = 1f
            });

            menu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "強度",
                type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                parameter = new VRCExpressionsMenu.Control.Parameter
                {
                    name = string.Empty
                },
                value = OutlineOnlyConstants.DefaultChromaticAberrationStrength,
                subParameters = new[]
                {
                    new VRCExpressionsMenu.Control.Parameter
                    {
                        name = OutlineOnlyConstants.ParamChromaticAberrationStrength
                    }
                }
            });

            SaveMenuAsset(menu);
            return menu;
        }

        private static VRCExpressionsMenu CreateOutlineMenu(VRCExpressionsMenu colorMenu, VRCExpressionsMenu chromaticMenu, string assetPath)
        {
            var menu = CreateMenuAsset(assetPath, "Lzebul Outline Menu");
            ResetMenuControls(menu);

            AddSubMenuControl(menu, "アウトライン色", colorMenu);
            AddSubMenuControl(menu, "色収差", chromaticMenu);

            SaveMenuAsset(menu);
            return menu;
        }

        private static VRCExpressionsMenu CreateRootMenu(VRCExpressionsMenu sourceMenu, VRCExpressionsMenu outlineMenu, string assetFolder)
        {
            if (sourceMenu == null)
            {
                return outlineMenu;
            }

            var sourceControlCount = sourceMenu.controls == null ? 0 : sourceMenu.controls.Count;
            var copiedSourceMenuPath = sourceControlCount < MaxMenuControls
                ? $"{assetFolder}/{RootMenuAssetName}"
                : $"{assetFolder}/{OriginalMenuAssetName}";
            var copiedSourceMenuName = sourceControlCount < MaxMenuControls
                ? "Lzebul Outline Root Menu"
                : "Lzebul Original Menu";
            var copiedSourceMenu = CreateMenuAsset(copiedSourceMenuPath, copiedSourceMenuName, sourceMenu);
            EnsureMenuControls(copiedSourceMenu);

            if (copiedSourceMenu.controls.Count < MaxMenuControls)
            {
                AddSubMenuControl(copiedSourceMenu, "アウトライン", outlineMenu);
                SaveMenuAsset(copiedSourceMenu);
                return copiedSourceMenu;
            }

            var rootMenu = CreateMenuAsset($"{assetFolder}/{RootMenuAssetName}", "Lzebul Outline Root Menu");
            ResetMenuControls(rootMenu);
            AddSubMenuControl(rootMenu, "元メニュー", copiedSourceMenu);
            AddSubMenuControl(rootMenu, "アウトライン", outlineMenu);
            SaveMenuAsset(rootMenu);
            return rootMenu;
        }

        private static void AddSubMenuControl(VRCExpressionsMenu menu, string name, VRCExpressionsMenu subMenu)
        {
            menu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                parameter = new VRCExpressionsMenu.Control.Parameter
                {
                    name = string.Empty
                },
                value = 1f,
                subMenu = subMenu
            });
        }

        private static VRCExpressionsMenu CreateMenuAsset(string assetPath, string name, VRCExpressionsMenu sourceMenu = null)
        {
            OutlineOnlyAssetPaths.DeleteAssetIfExists(assetPath);

            var menu = sourceMenu == null
                ? ScriptableObject.CreateInstance<VRCExpressionsMenu>()
                : UnityEngine.Object.Instantiate(sourceMenu);
            menu.name = name;
            menu.hideFlags = HideFlags.None;
            EnsureMenuControls(menu);
            AssetDatabase.CreateAsset(menu, assetPath);
            return menu;
        }

        private static void ResetMenuControls(VRCExpressionsMenu menu)
        {
            EnsureMenuControls(menu);
            menu.controls.Clear();
        }

        private static void EnsureMenuControls(VRCExpressionsMenu menu)
        {
            if (menu.controls == null)
            {
                menu.controls = new List<VRCExpressionsMenu.Control>();
            }
        }

        private static void SaveMenuAsset(VRCExpressionsMenu menu)
        {
            EditorUtility.SetDirty(menu);
            AssetDatabase.SaveAssetIfDirty(menu);
        }

        private static AnimatorController CreateFxController(RuntimeAnimatorController sourceFxController, List<RendererColorBinding> colorBindings, string assetFolder)
        {
            var controllerPath = $"{assetFolder}/{FxControllerAssetName}";
            var controller = CreateAnimatorController(sourceFxController, controllerPath);

            RemoveGeneratedAnimatorLayers(controller);
            WarnIfControllerAnimatesMaterialProperties(controller);
            UpsertAnimatorParameters(controller);

            for (var index = 0; index < ColorChannels.Length; index++)
            {
                var channel = ColorChannels[index];
                var clip = CreateColorClip(colorBindings, channel, $"{assetFolder}/Color_{channel.DisplayName}.anim");
                AddColorLayer(controller, channel, clip);
            }

            var chromaticOffClip = CreateFloatClip(
                colorBindings,
                "Lzebul Outline Chromatic Aberration Off",
                "_UseFutureChromaticAberration",
                0f,
                $"{assetFolder}/ChromaticAberration_Off.anim");
            var chromaticOnClip = CreateFloatClip(
                colorBindings,
                "Lzebul Outline Chromatic Aberration On",
                "_UseFutureChromaticAberration",
                1f,
                $"{assetFolder}/ChromaticAberration_On.anim");
            AddBoolToggleLayer(
                controller,
                OutlineOnlyConstants.EffectLayerPrefix + "Chromatic Aberration",
                OutlineOnlyConstants.ParamChromaticAberration,
                chromaticOffClip,
                chromaticOnClip);

            var chromaticStrengthClip = CreateFloatRangeClip(
                colorBindings,
                "Lzebul Outline Chromatic Aberration Strength",
                "_FutureChromaticAberrationStrength",
                0f,
                1f,
                $"{assetFolder}/ChromaticAberration_Strength.anim");
            AddTimeParameterLayer(
                controller,
                OutlineOnlyConstants.EffectLayerPrefix + "Chromatic Aberration Strength",
                OutlineOnlyConstants.ParamChromaticAberrationStrength,
                chromaticStrengthClip);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorController CreateAnimatorController(RuntimeAnimatorController sourceFxController, string controllerPath)
        {
            OutlineOnlyAssetPaths.DeleteAssetIfExists(controllerPath);

            if (sourceFxController is AnimatorController sourceAnimatorController)
            {
                var sourcePath = AssetDatabase.GetAssetPath(sourceAnimatorController);
                if (!string.IsNullOrEmpty(sourcePath) && AssetDatabase.CopyAsset(sourcePath, controllerPath))
                {
                    AssetDatabase.ImportAsset(controllerPath);
                    var copiedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                    if (copiedController != null)
                    {
                        copiedController.name = "Lzebul Outline FX";
                        return copiedController;
                    }
                }

                Debug.LogWarning("Lzebul Outline: Source FX controller could not be copied. A new outline-only FX controller will be generated.");
            }
            else if (sourceFxController != null)
            {
                Debug.LogWarning("Lzebul Outline: Source FX controller is not an AnimatorController. A new outline-only FX controller will be generated.");
            }

            return AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        }

        private static void RemoveGeneratedAnimatorLayers(AnimatorController controller)
        {
            var sourceLayers = controller.layers ?? Array.Empty<AnimatorControllerLayer>();
            var keptLayers = new List<AnimatorControllerLayer>();
            for (var index = 0; index < sourceLayers.Length; index++)
            {
                var layer = sourceLayers[index];
                if (!IsGeneratedAnimatorLayer(layer.name))
                {
                    keptLayers.Add(layer);
                }
            }

            controller.layers = keptLayers.ToArray();
        }

        private static bool IsGeneratedAnimatorLayer(string layerName)
        {
            return layerName != null
                   && (layerName.StartsWith(OutlineOnlyConstants.ExpressionLayerPrefix, StringComparison.Ordinal)
                       || layerName.StartsWith(OutlineOnlyConstants.EffectLayerPrefix, StringComparison.Ordinal));
        }

        private static void WarnIfControllerAnimatesMaterialProperties(AnimatorController controller)
        {
            var clips = controller.animationClips;
            if (clips == null || clips.Length == 0)
            {
                return;
            }

            var visitedClips = new HashSet<AnimationClip>();
            var materialBindings = new List<string>();
            for (var clipIndex = 0; clipIndex < clips.Length; clipIndex++)
            {
                var clip = clips[clipIndex];
                if (clip == null || !visitedClips.Add(clip))
                {
                    continue;
                }

                AppendMaterialBindingWarnings(clip, materialBindings);
            }

            if (materialBindings.Count == 0)
            {
                return;
            }

            var sampleCount = Mathf.Min(materialBindings.Count, 8);
            var samples = new string[sampleCount];
            for (var index = 0; index < sampleCount; index++)
            {
                samples[index] = materialBindings[index];
            }

            Debug.LogWarning("Lzebul Outline: Source FX animates material bindings that may conflict with outline materials: " + string.Join(", ", samples));
        }

        private static void AppendMaterialBindingWarnings(AnimationClip clip, List<string> materialBindings)
        {
            var curveBindings = AnimationUtility.GetCurveBindings(clip);
            for (var index = 0; index < curveBindings.Length; index++)
            {
                var binding = curveBindings[index];
                if (IsMaterialBinding(binding.propertyName))
                {
                    materialBindings.Add($"{clip.name}:{binding.path}:{binding.propertyName}");
                }
            }

            var objectReferenceBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            for (var index = 0; index < objectReferenceBindings.Length; index++)
            {
                var binding = objectReferenceBindings[index];
                if (IsMaterialBinding(binding.propertyName))
                {
                    materialBindings.Add($"{clip.name}:{binding.path}:{binding.propertyName}");
                }
            }
        }

        private static bool IsMaterialBinding(string propertyName)
        {
            return propertyName != null
                   && (propertyName.StartsWith("material.", StringComparison.Ordinal)
                       || propertyName.StartsWith("m_Materials", StringComparison.Ordinal));
        }

        private static void UpsertAnimatorParameters(AnimatorController controller)
        {
            RemoveAnimatorParameterIfExists(controller, OutlineOnlyConstants.ParamColorR);
            RemoveAnimatorParameterIfExists(controller, OutlineOnlyConstants.ParamColorG);
            RemoveAnimatorParameterIfExists(controller, OutlineOnlyConstants.ParamColorB);
            RemoveAnimatorParameterIfExists(controller, OutlineOnlyConstants.ParamChromaticAberration);
            RemoveAnimatorParameterIfExists(controller, OutlineOnlyConstants.ParamChromaticAberrationStrength);

            for (var index = 0; index < ColorChannels.Length; index++)
            {
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = ColorChannels[index].ParameterName,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 0f
                });
            }

            controller.AddParameter(new AnimatorControllerParameter
            {
                name = OutlineOnlyConstants.ParamChromaticAberration,
                type = AnimatorControllerParameterType.Bool,
                defaultBool = false
            });

            controller.AddParameter(new AnimatorControllerParameter
            {
                name = OutlineOnlyConstants.ParamChromaticAberrationStrength,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = OutlineOnlyConstants.DefaultChromaticAberrationStrength
            });
        }

        private static void RemoveAnimatorParameterIfExists(AnimatorController controller, string parameterName)
        {
            while (true)
            {
                var removed = false;
                var parameters = controller.parameters;
                for (var index = 0; index < parameters.Length; index++)
                {
                    var parameter = parameters[index];
                    if (parameter.name == parameterName)
                    {
                        controller.RemoveParameter(parameter);
                        removed = true;
                        break;
                    }
                }

                if (!removed)
                {
                    return;
                }
            }
        }

        private static AnimationClip CreateColorClip(List<RendererColorBinding> colorBindings, ColorChannel channel, string assetPath)
        {
            OutlineOnlyAssetPaths.DeleteAssetIfExists(assetPath);

            var clip = new AnimationClip
            {
                name = $"Lzebul Outline Color {channel.DisplayName}",
                wrapMode = WrapMode.ClampForever
            };

            var curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            var opaqueCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
            for (var index = 0; index < colorBindings.Count; index++)
            {
                var binding = colorBindings[index];
                SetMaterialColorCurve(clip, binding, "_OutlineColor", channel.CurveSuffix, curve);
                SetMaterialColorCurve(clip, binding, "_OutlineColor", "a", opaqueCurve);
                SetMaterialColorCurve(clip, binding, "_SurfaceLineColor", channel.CurveSuffix, curve);
                SetMaterialColorCurve(clip, binding, "_SurfaceLineColor", "a", opaqueCurve);
            }

            AssetDatabase.CreateAsset(clip, assetPath);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static void SetMaterialColorCurve(AnimationClip clip, RendererColorBinding target, string propertyName, string curveSuffix, AnimationCurve curve)
        {
            AnimationUtility.SetEditorCurve(
                clip,
                new EditorCurveBinding
                {
                    path = target.Path,
                    type = target.RendererType,
                    propertyName = $"material.{propertyName}.{curveSuffix}"
                },
                curve);
        }

        private static AnimationClip CreateFloatClip(List<RendererColorBinding> colorBindings, string clipName, string propertyName, float value, string assetPath)
        {
            return CreateFloatRangeClip(colorBindings, clipName, propertyName, value, value, assetPath);
        }

        private static AnimationClip CreateFloatRangeClip(List<RendererColorBinding> colorBindings, string clipName, string propertyName, float startValue, float endValue, string assetPath)
        {
            OutlineOnlyAssetPaths.DeleteAssetIfExists(assetPath);

            var clip = new AnimationClip
            {
                name = clipName,
                wrapMode = WrapMode.ClampForever
            };

            var curve = AnimationCurve.Linear(0f, startValue, 1f, endValue);
            for (var index = 0; index < colorBindings.Count; index++)
            {
                SetMaterialFloatCurve(clip, colorBindings[index], propertyName, curve);
            }

            AssetDatabase.CreateAsset(clip, assetPath);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static void SetMaterialFloatCurve(AnimationClip clip, RendererColorBinding target, string propertyName, AnimationCurve curve)
        {
            AnimationUtility.SetEditorCurve(
                clip,
                new EditorCurveBinding
                {
                    path = target.Path,
                    type = target.RendererType,
                    propertyName = $"material.{propertyName}"
                },
                curve);
        }

        private static void AddColorLayer(AnimatorController controller, ColorChannel channel, AnimationClip clip)
        {
            AddTimeParameterLayer(controller, OutlineOnlyConstants.ExpressionLayerPrefix + channel.DisplayName, channel.ParameterName, clip);
        }

        private static void AddTimeParameterLayer(AnimatorController controller, string layerName, string timeParameterName, AnimationClip clip)
        {
            var stateMachine = new AnimatorStateMachine
            {
                name = layerName
            };
            AssetDatabase.AddObjectToAsset(stateMachine, controller);

            var state = stateMachine.AddState("Value");
            state.motion = clip;
            state.writeDefaultValues = false;
            state.speed = 0f;
            state.timeParameterActive = true;
            state.timeParameter = timeParameterName;
            stateMachine.defaultState = state;

            controller.AddLayer(new AnimatorControllerLayer
            {
                name = layerName,
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = stateMachine
            });
        }

        private static void AddBoolToggleLayer(AnimatorController controller, string layerName, string parameterName, AnimationClip offClip, AnimationClip onClip)
        {
            var stateMachine = new AnimatorStateMachine
            {
                name = layerName
            };
            AssetDatabase.AddObjectToAsset(stateMachine, controller);

            var offState = stateMachine.AddState("OFF");
            offState.motion = offClip;
            offState.writeDefaultValues = false;

            var onState = stateMachine.AddState("ON");
            onState.motion = onClip;
            onState.writeDefaultValues = false;

            var toOn = offState.AddTransition(onState);
            ConfigureInstantTransition(toOn);
            toOn.AddCondition(AnimatorConditionMode.If, 0f, parameterName);

            var toOff = onState.AddTransition(offState);
            ConfigureInstantTransition(toOff);
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, parameterName);

            stateMachine.defaultState = offState;

            controller.AddLayer(new AnimatorControllerLayer
            {
                name = layerName,
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = stateMachine
            });
        }

        private static void ConfigureInstantTransition(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
            transition.exitTime = 0f;
        }

        private static void AssignFxController(VRCAvatarDescriptor avatar, RuntimeAnimatorController controller)
        {
            var index = GetFxLayerIndex(avatar);
            if (index < 0)
            {
                Debug.LogWarning("Lzebul Outline: FX Layerが見つからないため、アウトライン色のAnimatorを設定できませんでした。");
                return;
            }

            var layer = avatar.baseAnimationLayers[index];
            layer.isDefault = false;
            layer.animatorController = controller;
            avatar.baseAnimationLayers[index] = layer;
        }

        private static RuntimeAnimatorController GetFxController(VRCAvatarDescriptor avatar)
        {
            var index = GetFxLayerIndex(avatar);
            if (index < 0)
            {
                return null;
            }

            return avatar.baseAnimationLayers[index].animatorController;
        }

        private static int GetFxLayerIndex(VRCAvatarDescriptor avatar)
        {
            if (avatar.baseAnimationLayers == null)
            {
                return -1;
            }

            for (var index = 0; index < avatar.baseAnimationLayers.Length; index++)
            {
                if (avatar.baseAnimationLayers[index].type == VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    return index;
                }
            }

            return -1;
        }

        private static string GetExpressionAssetFolder(string prefabPath)
        {
            var prefabName = Path.GetFileNameWithoutExtension(prefabPath);
            return $"{OutlineOnlyConstants.ExpressionsFolder}/{OutlineOnlyAssetPaths.SanitizeFileName(prefabName)}";
        }

        private static T CreateAsset<T>(string assetPath, string name) where T : ScriptableObject
        {
            OutlineOnlyAssetPaths.DeleteAssetIfExists(assetPath);
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = name;
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        private static bool IsGeneratedExpressionParameter(string parameterName)
        {
            return parameterName == OutlineOnlyConstants.ParamColorR
                   || parameterName == OutlineOnlyConstants.ParamColorG
                   || parameterName == OutlineOnlyConstants.ParamColorB
                   || parameterName == OutlineOnlyConstants.ParamChromaticAberration
                   || parameterName == OutlineOnlyConstants.ParamChromaticAberrationStrength;
        }

        private readonly struct RendererColorBinding
        {
            internal RendererColorBinding(string path, Type rendererType)
            {
                Path = path;
                RendererType = rendererType;
            }

            internal string Path { get; }
            internal Type RendererType { get; }
        }

        private readonly struct ColorChannel
        {
            internal ColorChannel(string displayName, string parameterName, string curveSuffix)
            {
                DisplayName = displayName;
                ParameterName = parameterName;
                CurveSuffix = curveSuffix;
            }

            internal string DisplayName { get; }
            internal string ParameterName { get; }
            internal string CurveSuffix { get; }
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
