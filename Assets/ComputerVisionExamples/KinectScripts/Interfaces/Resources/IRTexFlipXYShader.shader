Shader "Kinect/IRTexFlipXYShader" 
{
	SubShader 
	{
		Pass 
		{
			ZTest Always Cull Off ZWrite Off
			Fog { Mode off }
		
			CGPROGRAM
			//#pragma target 5.0

			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			//uniform sampler2D _MainTex;
			uniform uint _TexResX;
			uniform uint _TexResY;
			uniform float _MinValue;
			uniform float _MaxValue;

			StructuredBuffer<uint> _IrBufRaw;
			StructuredBuffer<float> _IrBufMM;


			struct v2f {
				float4 pos : SV_POSITION;
			    float2 uv : TEXCOORD0;
			};

			v2f vert (appdata_base v)
			{
				v2f o;
				
                o.uv = float2(1.0 - v.texcoord.x, 1.0 - v.texcoord.y);
				o.pos = UnityObjectToClipPos (v.vertex);
				
				return o;
			}


			half4 frag (v2f i) : COLOR
			{
				uint dx = (uint)(i.uv.x * _TexResX);
				uint dy = (uint)(i.uv.y * _TexResY);
				uint di = (dx + dy * _TexResX);

				//_MinValue = _IrBufMM[0];
				//_MaxValue = _IrBufMM[1];
				
				uint ir2 = _IrBufRaw[di >> 1];
				uint ir = di & 1 != 0 ? ir2 >> 16 : ir2 & 0xffff;
				half clr = saturate(((float)ir - _MinValue) / (_MaxValue - _MinValue));

				return half4(clr, clr, clr, 1);
			}

			ENDCG
		}
	}

	Fallback Off
}