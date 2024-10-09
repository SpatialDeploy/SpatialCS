Shader "Custom/PartialColorShader"
{
    Properties
    {
        _Color1 ("Color 1", Color) = (0.129, 0.753, 0.741, 1)
        _Color2 ("Color 2", Color) = (0.161, 0.098, 0.294, 1)
        _PlayheadColor ("Playhead Color", Color) = (0, 0, 0, 1)
        _Threshold ("Threshold", Range(0, 1)) = 0.5
        _PlayheadSize ("Playhead Size", Range(0, 1)) = 0.1
        _MinX ("Min X", Float) = -0.5
        _MaxX ("Max X", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 pos : TEXCOORD1;
            };

            fixed4 _Color1;
            fixed4 _Color2;
            float _Threshold;
            float _MinX;
            float _MaxX;
            float _PlayheadSize;
            fixed4 _PlayheadColor;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.pos = v.vertex;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float normalizedX = (i.pos.x - _MinX) / (_MaxX - _MinX);
                normalizedX = 1.0 - clamp(normalizedX, 0.0, 1.0);

                fixed4 color;
                if(abs(normalizedX - _Threshold) < _PlayheadSize / 2.0)
                    color = _PlayheadColor;
                else if(normalizedX < _Threshold)
                    color = _Color1;
                else
                    color = _Color2;

                return color;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
