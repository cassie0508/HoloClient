Shader "DR/Buffer2Duplicate"{

	Properties
	{
		[NoScaleOffset] _MainTex("Texture", 2D) = "white" {}
	}
	
	SubShader
	{
		Tags{ "RenderType" = "Opaque" "Queue" = "Geometry" }

		Pass
		{
			CGPROGRAM

			#include "UnityCG.cginc"
			#pragma vertex vert
			#pragma fragment frag

			sampler2D _MainTex; 

			//Buffer interface for compute buffers
			StructuredBuffer<float3> vertices;
			StructuredBuffer<float2> uv;
			StructuredBuffer<int> triangles;

            // Duplicated Reality
            float _ROI_Scale = 1;
            half4x4 _ROI_Inversed;
            half4x4 _Dupl_Inversed;
            half4x4 _Roi2Dupl;

			struct appdata
			{
				uint vertex_id: SV_VertexID;

				// Single Instanced Pass Rendering
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
				float4 posWorld : TEXCOORD1;

				// Single Instanced Pass Rendering
				UNITY_VERTEX_OUTPUT_STEREO
			};

			//the vertex shader function
			v2f vert(appdata v){
				v2f o;

				// Single Instanced Pass Rendering
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				// Read triangle from compute buffer
				int positionID = triangles[v.vertex_id];

				if (positionID >= 0)
				{
					o.uv = uv[positionID];

					float4 vertex = float4(vertices[positionID], 1);

					vertex = mul(_Roi2Dupl, vertex);
                
					o.posWorld = vertex;
					o.vertex = mul(UNITY_MATRIX_VP, vertex);
				}

				return o;
			}


			fixed4 frag(v2f i) : SV_TARGET
			{
				float3 vertInDuplPos = mul(_Dupl_Inversed, i.posWorld);

				clip(vertInDuplPos + 0.5);
				clip(0.5 - vertInDuplPos);

				return tex2D(_MainTex, i.uv);
			}

			ENDCG
		}
	}
		Fallback "Diffuse"
}
