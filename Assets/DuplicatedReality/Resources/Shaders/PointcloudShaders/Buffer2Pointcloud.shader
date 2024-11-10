Shader "DR/Buffer2Pointcloud"{

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
					o.vertex = UnityObjectToClipPos(vertex);
				}

				return o;
			}


			fixed4 frag(v2f i) : SV_TARGET
			{
			  return tex2D(_MainTex, i.uv);
			}

			ENDCG
		}
	}
		Fallback "Diffuse"
}
