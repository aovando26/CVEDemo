Shader "Kinect/ObjectSegmShader"
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
            StructuredBuffer<float> _MaskBuf;
            StructuredBuffer<uint> _ObjCountBuf;


            uint _MaskTexWidth;
            uint _MaskTexHeight;
            //uint _MaskTexCount;


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
                float4 fragColor = float4(0, 0, 0, 1);

                uint maskTexCount = _ObjCountBuf[0];
                for(uint oi = 0; oi < maskTexCount; oi++)
                {
                    uint ofs = oi * _MaskTexWidth * _MaskTexHeight;
                    uint iy = (uint)((1.0 - i.uv.y) * _MaskTexHeight);
                    uint ix = (uint)(i.uv.x * _MaskTexWidth);
                    uint ind = ofs + iy * _MaskTexWidth + ix;

				    float segm = _MaskBuf[ind];
                    if(segm > 0.0)
                    {
                        switch(oi & 3)
                        {
                            case 0:
                                fragColor = float4(1, 0, 0, 1);
                                break;
                            case 1:
                                fragColor = float4(0, 1, 0, 1);
                                break;
                            case 2:
                                fragColor = float4(0, 0, 1, 1);
                                break;
                            case 3:
                                fragColor = float4(1, 0, 1, 1);
                                break;
                        }

                        break;
                    }
                }

				return fragColor;
            }

            ENDCG
        }
    }
}
