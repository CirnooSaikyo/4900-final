Shader "Custom/OrbStarfield"
{
    Properties
    {
        _BaseColor       ("Base Color (RGBA)", Color)            = (1.0, 0.85, 0.1, 0.22)
        _StarColor       ("Star Color",        Color)            = (1.0, 1.0,  0.6, 1.0)
        _RimColor        ("Rim Color",         Color)            = (1.0, 0.9,  0.2, 1.0)
        _StarDensity     ("Star Density",      Float)            = 70.0
        _StarSize        ("Star Size",         Range(0.01, 0.5)) = 0.14
        _StarBrightness  ("Star Brightness",   Float)            = 3.0
        _RimPower        ("Rim Power",         Float)            = 2.8
        _RimStrength     ("Rim Strength",      Float)            = 1.2
        _PulseSpeed      ("Pulse Speed",       Float)            = 1.2
        _PulseAmp        ("Pulse Amplitude",   Range(0, 0.4))    = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "OrbStarfieldFront"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                half4  _StarColor;
                half4  _RimColor;
                float  _StarDensity;
                float  _StarSize;
                float  _StarBrightness;
                float  _RimPower;
                float  _RimStrength;
                float  _PulseSpeed;
                float  _PulseAmp;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 viewDirWS  : TEXCOORD2;
            };

            float Hash(float3 p)
            {
                p = frac(p * float3(443.897, 441.423, 437.195));
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }

            // 20% chance per cell to spawn a star, position randomized within cell, with twinkle
            float Starfield(float3 posWS, float density, float size, float time)
            {
                float3 scaled = posWS * density;
                float3 cell   = floor(scaled);
                float3 local  = frac(scaled);

                float rnd = Hash(cell);
                if (rnd < 0.80) return 0.0;

                float cx = Hash(cell + float3(7.3,  0, 0));
                float cy = Hash(cell + float3(0, 13.1, 0));
                float cz = Hash(cell + float3(0, 0, 5.7));
                float3 center = float3(cx, cy, cz);

                float d       = length(local - center);
                float twinkle = 0.55 + 0.45 * sin(time * _PulseSpeed * 2.3 + rnd * 6.28318);
                return smoothstep(size, 0.0, d) * twinkle;
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS   = vni.normalWS;
                OUT.viewDirWS  = GetWorldSpaceViewDir(vpi.positionWS);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewDirWS);

                float pulse = 1.0 + _PulseAmp * sin(_Time.y * _PulseSpeed);

                float  rim    = pow(1.0 - saturate(dot(N, V)), _RimPower);
                half3  rimCol = _RimColor.rgb * rim * _RimStrength;

                float  starVal = Starfield(IN.positionWS, _StarDensity, _StarSize, _Time.y);
                half3  starCol = _StarColor.rgb * starVal * _StarBrightness;

                half3 col = _BaseColor.rgb + rimCol + starCol;

                half alpha = _BaseColor.a * pulse
                           + rim  * 0.35
                           + starVal * 0.55;
                alpha = saturate(alpha);

                return half4(col, alpha);
            }
            ENDHLSL
        }

        // back face pass: halved alpha for depth
        Pass
        {
            Name "OrbStarfieldBack"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                half4  _StarColor;
                half4  _RimColor;
                float  _StarDensity;
                float  _StarSize;
                float  _StarBrightness;
                float  _RimPower;
                float  _RimStrength;
                float  _PulseSpeed;
                float  _PulseAmp;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            float Hash(float3 p)
            {
                p = frac(p * float3(443.897, 441.423, 437.195));
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }
            float Starfield(float3 posWS, float density, float size, float time)
            {
                float3 scaled = posWS * density;
                float3 cell   = floor(scaled);
                float3 local  = frac(scaled);
                float  rnd    = Hash(cell);
                if (rnd < 0.80) return 0.0;
                float3 center = float3(Hash(cell + 7.3), Hash(cell + 13.1), Hash(cell + 5.7));
                float  d      = length(local - center);
                float  twinkle = 0.55 + 0.45 * sin(time * _PulseSpeed * 2.3 + rnd * 6.28318);
                return smoothstep(size, 0.0, d) * twinkle;
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                return OUT;
            }
            half4 Frag(Varyings IN) : SV_Target
            {
                float pulse   = 1.0 + _PulseAmp * sin(_Time.y * _PulseSpeed);
                float starVal = Starfield(IN.positionWS, _StarDensity, _StarSize, _Time.y);
                half3 col     = _BaseColor.rgb + _StarColor.rgb * starVal * (_StarBrightness * 0.5);
                half  alpha   = _BaseColor.a * pulse * 0.4 + starVal * 0.3;
                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
