Shader "Custom/FlashShader"
{
    Properties
    {
        _MainTex ("Albedo Texture", 2D) = "white" {}
        _TintColor ("Tint Color", Color) = (0,0,1,1) 
        _BaseColor ("Base Color", Color) = (1,1,1,0)
        _FlashColor ("Flash Color", Color) = (0,0,1,0.5) 
        _FlashSpeed ("Flash Speed", Float) = 2 
    }
    SubShader
    {
        // Transparent queue for blending
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100

        // Enable transparency blending and disable depth writing
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex; 
            fixed4 _TintColor; 
            fixed4 _BaseColor; 
            fixed4 _FlashColor;
            float _FlashSpeed; 

            v2f vert (appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex); // Transform to clip space
                o.uv = v.uv; // Pass UV coordinates
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample the texture
                fixed4 texColor = tex2D(_MainTex, i.uv);
                fixed4 blueTintedColor = texColor * _TintColor;

                // Calculate flashing factor
                float flashFactor = abs(sin(_Time.y * _FlashSpeed));
                fixed4 overlayColor = lerp(_BaseColor, _FlashColor, flashFactor);

                // Combine the blue-tinted texture with the flashing overlay
                return blueTintedColor + overlayColor * overlayColor.a; // Apply overlay with transparency
            }
            ENDCG
        }
    }
}
