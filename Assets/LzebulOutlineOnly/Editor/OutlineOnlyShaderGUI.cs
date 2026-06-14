using UnityEditor;
using UnityEngine;

namespace LzebulOutlineOnly
{
    public sealed class OutlineOnlyShaderGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            DrawMeshOutline(materialEditor, properties);
            DrawSurfaceLines(materialEditor, properties);
        }

        private static void DrawMeshOutline(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            var outlineColor = Find("_OutlineColor", properties);
            var outlineWidth = Find("_OutlineWidth", properties);

            EditorGUILayout.LabelField("メッシュアウトライン", EditorStyles.boldLabel);
            DrawProperty(materialEditor, outlineColor, "アウトライン色", "メッシュアウトラインの色です。変換直後は全て黒で揃えます。");
            DrawProperty(materialEditor, outlineWidth, "アウトライン幅", "メッシュの外形線の太さです。");
        }

        private static void DrawSurfaceLines(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            var useSurfaceLines = Find("_UseSurfaceLines", properties);
            var surfaceLineColor = Find("_SurfaceLineColor", properties);
            var surfaceLineThreshold = Find("_SurfaceLineThreshold", properties);
            var surfaceLineSoftness = Find("_SurfaceLineSoftness", properties);
            var surfaceLineMask = Find("_SurfaceLineMask", properties);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("マスク線", EditorStyles.boldLabel);
            DrawProperty(materialEditor, useSurfaceLines, "マスク線を描画", "目・眉・口など、メッシュ外形では出せない線をマスクで描画します。");

            var enabled = useSurfaceLines != null && useSurfaceLines.floatValue > 0.5f;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                DrawTexture(materialEditor, surfaceLineMask, "マスク線テクスチャ", "白い部分を線として描画します。");
                DrawProperty(materialEditor, surfaceLineColor, "マスク線の色", "マスク線の色です。");
                DrawProperty(materialEditor, surfaceLineThreshold, "マスク線のしきい値", "線として扱うマスク値の境界です。");
                DrawProperty(materialEditor, surfaceLineSoftness, "マスク線のぼかし", "マスク境界のなめらかさです。");
            }
        }

        private static MaterialProperty Find(string propertyName, MaterialProperty[] properties)
        {
            return FindProperty(propertyName, properties, false);
        }

        private static void DrawProperty(MaterialEditor materialEditor, MaterialProperty property, string label, string tooltip)
        {
            if (property == null)
            {
                return;
            }

            materialEditor.ShaderProperty(property, new GUIContent(label, tooltip));
        }

        private static void DrawTexture(MaterialEditor materialEditor, MaterialProperty property, string label, string tooltip)
        {
            if (property == null)
            {
                return;
            }

            materialEditor.TexturePropertySingleLine(new GUIContent(label, tooltip), property);
        }
    }
}
