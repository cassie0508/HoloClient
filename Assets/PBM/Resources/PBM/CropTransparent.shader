Shader "PBM/CropTransparent"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _MirrorFrameTex("Mirror Frame Texture", 2D) = "white"{}
        _MirrorSpecular("Mirror Specular Texture", 2D) = "white"{}

    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ USE_MIRROR_SPECULAR_

            #include "UnityCG.cginc"

            float _EnableCropping;
            float2 uv_topleft;
            float2 uv_topright;
            float2 uv_bottomleft;
            float2 uv_bottomright;

            float CompensationRatio = 1;
            float MainTextureTransparency;

            float BorderSize;

            float sign(float2 p1, float2 p2, float2 p3)
            {
                return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
            }

            bool PointInTriangle(float2 pt, float2 v1, float2 v2, float2 v3)
            {
                float d1, d2, d3;
                bool has_neg, has_pos;

                d1 = sign(pt, v1, v2);
                d2 = sign(pt, v2, v3);
                d3 = sign(pt, v3, v1);

                has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
                has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

                return !(has_neg && has_pos);
            }

            float DistancePoint2Line2D(float2 pt, float2 a, float2 b)
            {
                return abs((b.x - a.x) * (a.y - pt.y) - (a.x - pt.x) * (b.y - a.y)) /
                    sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
            }


            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID //Insert
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;

                UNITY_VERTEX_OUTPUT_STEREO //Insert
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v); //Insert
                UNITY_INITIALIZE_OUTPUT(v2f, o); //Insert
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); //Insert

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = (v.uv - 0.5) / CompensationRatio + 0.5;
                return o;
            }

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            uniform sampler2D _MirrorFrameTex;
            float4 _MirrorFrameTex_TexelSize;
            uniform sampler2D _MirrorSpecular;


            fixed4 frag(v2f i) : SV_Target
            {
                float2 croppedUV = i.uv;

                uv_topleft = (uv_topleft - 0.5) / CompensationRatio + 0.5;
                uv_bottomleft = (uv_bottomleft - 0.5) / CompensationRatio + 0.5;
                uv_topright = (uv_topright - 0.5) / CompensationRatio + 0.5;
                uv_bottomright = (uv_bottomright - 0.5) / CompensationRatio + 0.5;
                if (_EnableCropping > 0.5f)
                {
                    if (!PointInTriangle(i.uv, uv_topleft, uv_bottomleft, uv_topright) && !PointInTriangle(i.uv, uv_bottomleft, uv_bottomright, uv_topright))
                        return float4(0, 0, 0, 0);

                    croppedUV = float2((i.uv.x - uv_topleft.x) / abs(uv_topright.x - uv_topleft.x),
                        (i.uv.y - uv_bottomright.y) / abs(uv_topright.y - uv_bottomright.y));

                    if (BorderSize > 0)
                    {
                        float4 frameColor = tex2D(_MirrorFrameTex, croppedUV);

                        
                        if (DistancePoint2Line2D(i.uv, uv_topleft, uv_bottomleft) < BorderSize || DistancePoint2Line2D(i.uv, uv_topright, uv_bottomright) < BorderSize)
                        {
                            return frameColor;
                        }

                        if (DistancePoint2Line2D(i.uv, uv_topleft, uv_topright) < BorderSize || DistancePoint2Line2D(i.uv, uv_bottomleft, uv_bottomright) < BorderSize)
                        {
                            return frameColor;
                        }
                    }
                }
                else
                {
                    if (BorderSize > 0)
                    {
                        float4 frameColor = tex2D(_MirrorFrameTex, i.uv);
                        croppedUV = (i.uv - 0.5) * CompensationRatio + 0.5;
                        if (croppedUV.x < BorderSize || croppedUV.x > 1 - BorderSize)
                        {
                            return frameColor;
                        }

                        if (croppedUV.y < BorderSize || croppedUV.y > 1 - BorderSize)
                        {
                            return frameColor;
                        }
                    }
                }

                fixed4 col = fixed4(tex2D(_MainTex, i.uv).rgb, MainTextureTransparency);

#ifdef USE_MIRROR_SPECULAR_
                float4 specular = tex2D(_MirrorSpecular, croppedUV);

                col.rgb = col.rgb * MainTextureTransparency + specular.rgb * specular.a * (3 - MainTextureTransparency);
                col.a = max(MainTextureTransparency, specular.a);
#endif
                return col;
            }
            ENDCG
        }
    }
}
