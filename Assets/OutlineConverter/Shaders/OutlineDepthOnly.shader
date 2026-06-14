Shader "OutlineConverter/Outline Depth Only"
{
    Properties
    {
        [MainTexture] _MainTex ("メインテクスチャ（アルファ元）", 2D) = "white" {}
        [Toggle] _UseMainTexAlpha ("メインテクスチャのアルファで切り抜く", Float) = 0
        _Cutoff ("アルファしきい値", Range(0, 1)) = 0.5
        [Enum(Off,0,Replace,1,Multiply,2,Add,3,Subtract,4)] _AlphaMaskMode ("lilToonアルファマスクモード", Float) = 0
        _AlphaMask ("lilToonアルファマスク", 2D) = "white" {}
        _AlphaMaskScale ("アルファマスク倍率", Float) = 1
        _AlphaMaskValue ("アルファマスク補正", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+40"
            "IgnoreProjector" = "True"
            "VRCFallback" = "Hidden"
        }

        Pass
        {
            Name "OUTLINE_OCCLUDER_DEPTH"
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _AlphaMask;
            float4 _AlphaMask_ST;
            float _UseMainTexAlpha;
            float _Cutoff;
            float _AlphaMaskMode;
            float _AlphaMaskScale;
            float _AlphaMaskValue;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float SampleAlpha(float2 uv)
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

            v2f Vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.pos = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            fixed4 Frag(v2f input) : SV_Target
            {
                clip(SampleAlpha(input.uv) - _Cutoff);
                return 0;
            }
            ENDCG
        }
    }
}
