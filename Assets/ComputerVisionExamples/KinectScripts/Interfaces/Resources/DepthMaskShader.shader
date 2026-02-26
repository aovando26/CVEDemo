Shader "Custom/DepthMaskShader"
{
    Properties
	{
		_MainTex("_MainTex", 2D) = "white" {}
	}

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

		ZTest Always Cull Off ZWrite Off
		Fog { Mode off }

		Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            // properties
            StructuredBuffer<float> _DepthBufRaw;
            //StructuredBuffer<float> _DepthBufMM;

            uint _DepthTexWidth;
            uint _DepthTexHeight;

            // model output texture
            //sampler2D _MainTex;

            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

         
            v2f vert (appdata v)
            {
                v2f o;

                o.uv = v.uv;
				o.vertex = UnityObjectToClipPos(v.vertex);
             
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                uint ix = (uint)(i.uv.x * _DepthTexWidth);
                uint iy = (uint)((1.0 - i.uv.y) * _DepthTexHeight);
                uint ind = ix + iy * _DepthTexWidth;

				float depth = _DepthBufRaw[ind];  // tex2D(_MainTex, i.uv).x;
				float col = depth * 0.1;  // saturate((depth - _DepthBufMM[0]) / (_DepthBufMM[1] - _DepthBufMM[0]));

				return float4(col, col, col, 1);
            }

            ENDCG
        }
    }
}
