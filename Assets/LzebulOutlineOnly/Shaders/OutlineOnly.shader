Shader "Lzebul/VRChat/Outline Only"
{
    Properties
    {
        [HideInInspector] _Color ("Fallback Color", Color) = (1, 1, 1, 1)
        [MainTexture] _MainTex ("メインテクスチャ（アルファ元）", 2D) = "white" {}
        [Toggle] _UseMainTexAlpha ("メインテクスチャのアルファで切り抜く", Float) = 0
        _Cutoff ("アルファしきい値", Range(0, 1)) = 0.5
        [Enum(Off,0,Replace,1,Multiply,2,Add,3,Subtract,4)] _AlphaMaskMode ("lilToonアルファマスクモード", Float) = 0
        _AlphaMask ("lilToonアルファマスク", 2D) = "white" {}
        _AlphaMaskScale ("アルファマスク倍率", Float) = 1
        _AlphaMaskValue ("アルファマスク補正", Float) = 0
        [Toggle] _UseVisibleDepth ("同一Rendererで不可視深度を書く", Float) = 0

        [Toggle] _UseMeshOutline ("メッシュアウトラインを描画", Float) = 1
        [MainColor] _OutlineColor ("アウトライン色", Color) = (0, 0, 0, 1)
        _OutlineTex ("アウトライン色テクスチャ", 2D) = "white" {}
        [Toggle] _UseOutlineTex ("アウトライン色テクスチャを使用", Float) = 0
        _OutlineWidth ("アウトライン幅", Range(0, 1)) = 0.08
        [NoScaleOffset] _OutlineWidthMask ("アウトライン幅マスク", 2D) = "white" {}
        _OutlineFixWidth ("距離による太さ補正", Range(0, 1)) = 0.5
        [Enum(None,0,VertexColorR,1,VertexColorA,2)] _OutlineVertexR2Width ("頂点カラーによる幅制御", Float) = 0
        [NoScaleOffset][Normal] _OutlineVectorTex ("アウトライン方向テクスチャ", 2D) = "bump" {}
        _OutlineVectorScale ("アウトライン方向の強さ", Range(-10, 10)) = 1
        _OutlineBackfaceSuppress ("裏面塗りつぶし抑制", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _OutlineCull ("メッシュアウトラインのカリング", Float) = 1
        [HideInInspector] _OutlineOffsetFactor ("内部: アウトライン深度補正 Factor", Float) = 0
        [HideInInspector] _OutlineOffsetUnits ("内部: アウトライン深度補正 Units", Float) = 0

        [Toggle] _UseSurfaceLines ("マスク線を描画", Float) = 0
        _SurfaceLineColor ("マスク線の色", Color) = (0, 0, 0, 1)
        _SurfaceLineOpacity ("マスク線の不透明度", Range(0, 1)) = 1
        _SurfaceLineThreshold ("マスク線のしきい値", Range(0, 1)) = 0.5
        _SurfaceLineSoftness ("マスク線のぼかし", Range(0.001, 0.5)) = 0.02
        [Toggle] _SurfaceLineInvert ("マスク線を反転", Float) = 0
        _SurfaceLineMask ("マスク線テクスチャ", 2D) = "black" {}

        [HideInInspector][Toggle] _UseFutureEmission ("線の発光", Float) = 0
        [HideInInspector] _FutureEmissionStrength ("発光の強さ", Range(0, 5)) = 0
        [HideInInspector][Toggle] _UseFutureChromaticAberration ("色収差", Float) = 0
        [HideInInspector] _FutureChromaticAberrationStrength ("色収差の強さ", Range(0, 1)) = 0
        [HideInInspector][Toggle] _UseFutureHandDrawn ("手書き風 jitter/noise", Float) = 0
        [HideInInspector] _FutureHandDrawnStrength ("手書き風の強さ", Range(0, 1)) = 0
        [HideInInspector][NoScaleOffset] _FutureNoiseTex ("ノイズテクスチャ", 2D) = "gray" {}
        [HideInInspector][Toggle] _UseFuturePixelStyle ("ピクセル風", Float) = 0
        [HideInInspector] _FuturePixelSize ("ピクセルサイズ", Range(1, 64)) = 8
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "IgnoreProjector" = "True"
            "VRCFallback" = "UnlitTransparent"
        }

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_ST;
        sampler2D _AlphaMask;
        float4 _AlphaMask_ST;
        sampler2D _OutlineTex;
        float4 _OutlineTex_ST;
        sampler2D _OutlineWidthMask;
        sampler2D _OutlineVectorTex;
        sampler2D _SurfaceLineMask;
        float4 _SurfaceLineMask_ST;

        float _UseMainTexAlpha;
        float _Cutoff;
        float _AlphaMaskMode;
        float _AlphaMaskScale;
        float _AlphaMaskValue;
        float _UseVisibleDepth;
        float _UseMeshOutline;
        float4 _OutlineColor;
        float _UseOutlineTex;
        float _OutlineWidth;
        float _OutlineFixWidth;
        float _OutlineVertexR2Width;
        float _OutlineVectorScale;
        float _OutlineBackfaceSuppress;
        float _UseSurfaceLines;
        float4 _SurfaceLineColor;
        float _SurfaceLineOpacity;
        float _SurfaceLineThreshold;
        float _SurfaceLineSoftness;
        float _SurfaceLineInvert;

        struct basic_appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct basic_v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        struct outline_appdata
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            float4 tangent : TANGENT;
            float2 uv : TEXCOORD0;
            float4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct outline_v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float3 viewDirWS : TEXCOORD2;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        float SampleOutlineAlpha(float2 uv)
        {
            float alpha = 1.0;

            if (_UseMainTexAlpha > 0.5)
            {
                alpha *= tex2D(_MainTex, TRANSFORM_TEX(uv, _MainTex)).a;
            }

            if (_AlphaMaskMode > 0.5)
            {
                float alphaMask = tex2D(_AlphaMask, TRANSFORM_TEX(uv, _AlphaMask)).r;
                alphaMask = saturate(alphaMask * _AlphaMaskScale + _AlphaMaskValue);

                if (_AlphaMaskMode < 1.5)
                {
                    alpha = alphaMask;
                }
                else if (_AlphaMaskMode < 2.5)
                {
                    alpha *= alphaMask;
                }
                else if (_AlphaMaskMode < 3.5)
                {
                    alpha = saturate(alpha + alphaMask);
                }
                else
                {
                    alpha = saturate(alpha - alphaMask);
                }
            }

            return alpha;
        }

        void ClipOutlineAlpha(float2 uv)
        {
            clip(SampleOutlineAlpha(uv) - _Cutoff);
        }

        float3 SafeNormalize3(float3 value, float3 fallback)
        {
            float lengthValue = length(value);
            return lengthValue > 0.0001 ? value / lengthValue : fallback;
        }

        float3 GetPerpendicular(float3 normalOS)
        {
            float3 axis = abs(normalOS.y) < 0.999 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
            return SafeNormalize3(cross(axis, normalOS), float3(1.0, 0.0, 0.0));
        }

        basic_v2f BasicVert(basic_appdata input)
        {
            basic_v2f output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_OUTPUT(basic_v2f, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.pos = UnityObjectToClipPos(input.vertex);
            output.uv = input.uv;
            return output;
        }

        fixed4 DepthFrag(basic_v2f input) : SV_Target
        {
            clip(_UseVisibleDepth - 0.5);
            ClipOutlineAlpha(input.uv);
            return 0;
        }

        float GetSurfaceLineCoverage(float2 uv)
        {
            float mask = tex2D(_SurfaceLineMask, TRANSFORM_TEX(uv, _SurfaceLineMask)).r;
            mask = lerp(mask, 1.0 - mask, step(0.5, _SurfaceLineInvert));

            float softness = max(_SurfaceLineSoftness, 0.001);
            return smoothstep(_SurfaceLineThreshold - softness, _SurfaceLineThreshold + softness, mask);
        }

        float4 SurfaceLineFrag(basic_v2f input) : SV_Target
        {
            clip(_UseSurfaceLines - 0.5);
            ClipOutlineAlpha(input.uv);

            float lineAlpha = GetSurfaceLineCoverage(input.uv);
            clip(lineAlpha - 0.001);

            float4 outputColor = _SurfaceLineColor;
            outputColor.a *= saturate(lineAlpha * _SurfaceLineOpacity);
            return outputColor;
        }

        float3 GetOutlineNormalOS(outline_appdata input)
        {
            float3 vectorSample = UnpackNormal(tex2Dlod(_OutlineVectorTex, float4(input.uv, 0, 0)));
            float2 vectorXY = vectorSample.xy * _OutlineVectorScale;
            float3 normalOS = SafeNormalize3(input.normal, float3(0.0, 1.0, 0.0));
            if (dot(vectorXY, vectorXY) < 0.00000001)
            {
                return normalOS;
            }

            float3 tangentOS = SafeNormalize3(input.tangent.xyz, GetPerpendicular(normalOS));
            float tangentSign = input.tangent.w < 0.0 ? -1.0 : 1.0;
            float3 bitangentOS = SafeNormalize3(cross(normalOS, tangentOS) * tangentSign, GetPerpendicular(normalOS));
            return SafeNormalize3(normalOS + vectorXY.x * tangentOS + vectorXY.y * bitangentOS, normalOS);
        }

        float GetOutlineWidth(outline_appdata input)
        {
            float width = _OutlineWidth;
            width *= tex2Dlod(_OutlineWidthMask, float4(input.uv, 0, 0)).r;

            if (_OutlineVertexR2Width > 0.5 && _OutlineVertexR2Width < 1.5)
            {
                width *= input.color.r;
            }
            else if (_OutlineVertexR2Width > 1.5)
            {
                width *= input.color.a;
            }

            return max(width, 0.0);
        }

        outline_v2f OutlineVert(outline_appdata input)
        {
            outline_v2f output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_OUTPUT(outline_v2f, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            float width = GetOutlineWidth(input);
            float3 normalOS = GetOutlineNormalOS(input);
            float objectWidth = width * 0.01 * (1.0 - saturate(_OutlineFixWidth));
            float4 objectPos = input.vertex;
            objectPos.xyz += normalOS * objectWidth;

            float4 clipPos = UnityObjectToClipPos(objectPos);
            float3 normalVS = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, normalOS));
            float2 clipDir = normalVS.xy / max(length(normalVS.xy), 0.0001);
            clipPos.xy += clipDir * width * 0.01 * saturate(_OutlineFixWidth) * clipPos.w;

            float3 worldPos = mul(unity_ObjectToWorld, objectPos).xyz;
            output.pos = clipPos;
            output.uv = input.uv;
            output.normalWS = UnityObjectToWorldNormal(normalOS);
            output.viewDirWS = UnityWorldSpaceViewDir(worldPos);
            return output;
        }

        void ClipOutlineVisibility(outline_v2f input)
        {
            clip(_UseMeshOutline - 0.5);

            if (_OutlineBackfaceSuppress > 0.001)
            {
                float maxFacing = lerp(1.001, 0.08, saturate(_OutlineBackfaceSuppress));
                float facing = abs(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                clip(maxFacing - facing);
            }

            ClipOutlineAlpha(input.uv);
        }

        float4 GetOutlineBaseColor(float2 uv)
        {
            float4 outputColor = _OutlineColor;
            if (_UseOutlineTex > 0.5)
            {
                outputColor *= tex2D(_OutlineTex, TRANSFORM_TEX(uv, _OutlineTex));
            }

            return outputColor;
        }

        float4 OutlineFrag(outline_v2f input) : SV_Target
        {
            ClipOutlineVisibility(input);
            return GetOutlineBaseColor(input.uv);
        }
        ENDCG

        Pass
        {
            Name "INVISIBLE_DEPTH"
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            CGPROGRAM
            #pragma vertex BasicVert
            #pragma fragment DepthFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            ENDCG
        }

        Pass
        {
            Name "SURFACE_LINES"
            Cull Back
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex BasicVert
            #pragma fragment SurfaceLineFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            ENDCG
        }

        Pass
        {
            Name "OUTLINE_ONLY"
            Cull [_OutlineCull]
            ZWrite Off
            ZTest LEqual
            Offset [_OutlineOffsetFactor], [_OutlineOffsetUnits]
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            ENDCG
        }

    }

    Fallback "Unlit/Transparent"
    CustomEditor "LzebulOutlineOnly.OutlineOnlyShaderGUI"
}
