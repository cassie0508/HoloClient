Shader "Custom/FresnelShader"
{
    Properties
    {
        _Color ("Base Color", Color) = (1,1,1,1) 
        _FresnelPower ("Fresnel Power", Range(0.1, 5)) = 2.0
        _Transparency ("Transparency", Range(0, 1)) = 0.3
        _ReflectStrength ("Reflection Strength", Range(0, 1)) = 0.5
        _CubeMap ("Environment Reflection", Cube) = "" {}
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            Cull Back
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;

                // Single Instanced Pass Rendering
				UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;

                // Single Instanced Pass Rendering
				UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _Color;
            float _FresnelPower;
            float _Transparency;
            float _ReflectStrength;
            samplerCUBE _CubeMap;

            v2f vert (appdata_t v)
            {
                v2f o;

                // Single Instanced Pass Rendering
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(WorldSpaceViewDir(v.vertex));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Compute Fresnel Effect
                float fresnel = 1.0 - dot(i.viewDir, i.normal);
                fresnel = pow(fresnel, _FresnelPower);

                // Environment Reflection
                float3 reflection = texCUBE(_CubeMap, reflect(-i.viewDir, i.normal)).rgb;

                // Combine Color and Reflection
                float3 finalColor = lerp(_Color.rgb, reflection, _ReflectStrength);

                // Adjust Transparency to Ensure Visibility in MRC
                float alpha = saturate(lerp(_Transparency, 1.0, fresnel)); 
                alpha = max(alpha, 0.15);  // Ensure minimum visibility in MRC

                return float4(finalColor * alpha, alpha); // Premultiplied Alpha
            }
            ENDCG
        }
    }
}
