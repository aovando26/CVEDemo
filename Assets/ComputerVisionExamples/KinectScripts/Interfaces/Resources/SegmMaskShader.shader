Shader "Kinect/SegmMaskShader"
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
            StructuredBuffer<float> _SegmBuf;

            uint _SegmTexWidth;
            uint _SegmTexHeight;

            uint _LmCount;
            StructuredBuffer<float> _LmBuffer;

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
                uint ix = (uint)(i.uv.x * _SegmTexWidth);
                uint iy = (uint)((1.0 - i.uv.y) * _SegmTexHeight);
                uint ind = ix + iy * _SegmTexWidth;

				float segm = _SegmBuf[ind];
				float col = segm > 0.0;
                float4 fragColor = float4(col, col, col, 1);

                float dx = 2.0 / _SegmTexWidth;
                float dy = 2.0 / _SegmTexHeight;

                // landmarks
                for(uint j = 0; j < _LmCount; j++)  // 39
                {
                    float lmX = _LmBuffer[j * 5] / _SegmTexWidth;
                    float lmY = 1.0 - _LmBuffer[j * 5 + 1] / _SegmTexHeight;

                    if(i.uv.x >= (lmX - dx) && i.uv.x <= (lmX + dx) &&
                        i.uv.y >= (lmY - dx) && i.uv.y <= (lmY + dx))
                    {
                        if(j < 33)
                        {
                            fragColor = col ? float4(1, 0, 0, 1) : float4(1, 0, 1, 1);
                        }
                        else
                        {
                            fragColor = col ? float4(0, 1, 0, 1) : float4(1, 0.8, 0, 1);
                        }
                    }
                }

				return fragColor;
            }

            ENDCG
        }
    }
}
