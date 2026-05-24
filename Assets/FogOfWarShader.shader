Shader "Custom/FogOfWar"
{
    Properties
    {
        _FogTex ("Fog Texture", 2D) = "white" {}
        _MapHalfSize ("Map Half Size", Float) = 23
    }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" }
        LOD 100

        // Blend the fog color over the ground
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        // Make sure it draws on top of the ground but under UI
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            sampler2D _FogTex;
            float _MapHalfSize;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Map world XZ to UV 0..1 based on mapHalfSize
                float2 uv = (i.worldPos.xz + _MapHalfSize) / (_MapHalfSize * 2.0);
                return tex2D(_FogTex, uv);
            }
            ENDCG
        }
    }
}