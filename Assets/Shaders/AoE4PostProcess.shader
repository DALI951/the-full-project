Shader "Hidden/AoE4PostProcess"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _VignetteIntensity ("Vignette Intensity", Float) = 0.12
        _MicroContrastIntensity ("Micro Contrast", Float) = 0.22
        _BloomIntensity ("Bloom Intensity", Float) = 0.25
        _BloomThreshold ("Bloom Threshold", Float) = 0.70
        _Warmth ("Sun Warmth", Float) = 1.0
        _LushGreensBoost ("Lush Greens Boost", Float) = 1.15
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _VignetteIntensity;
            float _MicroContrastIntensity;
            float _BloomIntensity;
            float _BloomThreshold;
            float _Warmth;
            float _LushGreensBoost;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float3 ACESFilm(float3 x)
            {
                float a = 2.51f;
                float b = 0.03f;
                float c = 2.43f;
                float d = 0.59f;
                float e = 0.14f;
                return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float3 col = tex2D(_MainTex, uv).rgb;
                float luma = dot(col, float3(0.299, 0.587, 0.114));

                // 1. Local Micro-Contrast / Fake SSAO
                // Samples 4 diagonal neighbor texels to detect local crevices & shadow edges
                float2 dUV = _MainTex_TexelSize.xy * 2.0;
                float3 t0 = tex2D(_MainTex, uv + float2( dUV.x,  dUV.y)).rgb;
                float3 t1 = tex2D(_MainTex, uv + float2(-dUV.x,  dUV.y)).rgb;
                float3 t2 = tex2D(_MainTex, uv + float2( dUV.x, -dUV.y)).rgb;
                float3 t3 = tex2D(_MainTex, uv + float2(-dUV.x, -dUV.y)).rgb;

                float neighborLuma = (dot(t0, float3(0.299, 0.587, 0.114)) +
                                      dot(t1, float3(0.299, 0.587, 0.114)) +
                                      dot(t2, float3(0.299, 0.587, 0.114)) +
                                      dot(t3, float3(0.299, 0.587, 0.114))) * 0.25;

                float crevice = saturate((neighborLuma - luma) * 3.5);
                float fakeSSAO = 1.0 - (crevice * _MicroContrastIntensity);
                col *= fakeSSAO;

                // 2. Soft Highlight Bloom
                // Samples wider cardinal taps for soft warm glow around bright sunlit roofs & veins
                float2 bUV = _MainTex_TexelSize.xy * 5.0;
                float3 b0 = tex2D(_MainTex, uv + float2( bUV.x, 0.0)).rgb;
                float3 b1 = tex2D(_MainTex, uv + float2(-bUV.x, 0.0)).rgb;
                float3 b2 = tex2D(_MainTex, uv + float2(0.0,  bUV.y)).rgb;
                float3 b3 = tex2D(_MainTex, uv + float2(0.0, -bUV.y)).rgb;

                float l0 = max(0.0, dot(b0, float3(0.299, 0.587, 0.114)) - _BloomThreshold);
                float l1 = max(0.0, dot(b1, float3(0.299, 0.587, 0.114)) - _BloomThreshold);
                float l2 = max(0.0, dot(b2, float3(0.299, 0.587, 0.114)) - _BloomThreshold);
                float l3 = max(0.0, dot(b3, float3(0.299, 0.587, 0.114)) - _BloomThreshold);

                float3 bloom = (b0 * l0 + b1 * l1 + b2 * l2 + b3 * l3) * 0.25;
                float3 warmBloom = bloom * float3(1.15, 1.05, 0.82) * _BloomIntensity;
                col += warmBloom;

                // 3. Warm Golden-Hour Color Grading & ACES-style Filmic Contrast
                // ACES Filmic tonemapping
                float3 aces = ACESFilm(col * 1.06);
                col = lerp(col, aces, 0.70);

                // Boost lush greens (dominant green channel)
                float greenDiff = col.g - max(col.r, col.b);
                if (greenDiff > 0.02)
                {
                    float greenWeight = saturate(greenDiff * 4.5);
                    col = lerp(col, col * float3(0.96, _LushGreensBoost, 0.90), greenWeight * 0.45);
                }

                // Warm golden sunlight highlights (upper luminance)
                float sunHighlights = smoothstep(0.48, 0.95, luma);
                col = lerp(col, col * float3(1.08, 1.03, 0.90), sunHighlights * 0.30 * _Warmth);

                // Rich cool slate shadows (deep shadows)
                float shadowRange = 1.0 - smoothstep(0.05, 0.42, luma);
                col = lerp(col, col * float3(0.92, 0.94, 1.05), shadowRange * 0.22);

                // 4. Subtle Edge Vignette (0.12 intensity)
                float2 centeredUV = uv - 0.5;
                float distSq = dot(centeredUV, centeredUV);
                float vignette = 1.0 - (distSq * 1.8 * _VignetteIntensity);
                col *= saturate(vignette);

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
