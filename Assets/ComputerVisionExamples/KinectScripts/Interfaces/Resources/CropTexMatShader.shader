Shader "Kinect/CropTexMatShader"
{
    Properties
	{
		_MainTex("_MainTex", 2D) = "white" {}
	}

    SubShader
    {
		ZTest Always Cull Off ZWrite Off
		Fog { Mode off }

		Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4x4 _cropMatrix;
            float4 _boxXYWH;

            uint _poseIndex;
            float2 _lboxScale;
            uint _cropTexWidth;
            uint _cropTexHeight;
            uint _isLinearColorSpace;


            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 pos : SV_POSITION;
            };

         
            v2f vert (appdata v)
            {
                v2f o;
                o.uv = v.uv;
				o.pos = UnityObjectToClipPos(v.vertex);
             
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 bcenter = _boxXYWH.xy;
                float2 bsize = _boxXYWH.zw * _lboxScale;

                bcenter = (bcenter - 0.5) * _lboxScale + 0.5;
                float4 bbox = float4(bcenter.x - bsize.x * 0.55, bcenter.y - bsize.y * 0.55, bcenter.x + bsize.x * 0.55, bcenter.y + bsize.y * 0.55);

                float2 uv = mul(_cropMatrix, float4(i.uv, 0, 1)).xy;
                uv = (uv - 0.5) * _lboxScale + 0.5;

                // UV gradients
                //float2 duv_dx = mul(_cropMatrix, float4(1.0 / _cropTexWidth, 0, 0, 0)).xy;
                //float2 duv_dy = mul(_cropMatrix, float4(0, -1.0 / _cropTexHeight, 0, 0)).xy;
                //float2 duv_dx = float2(1.0 / _cropTexWidth, 0);
                //float2 duv_dy = float2(0, 1.0 / _cropTexWidth);

                // Texture sample
                float3 rgb = tex2D(_MainTex, uv).rgb;  // tex2Dgrad(_MainTex, uv, duv_dx, duv_dy).rgb;
                rgb *= (uv.x > bbox.x && uv.y > bbox.y && uv.x < bbox.z && uv.y < bbox.w) && all(uv > 0) && all(uv < 1);

                // Comvert sRGB color (= Liner color space) because Compute Shader texture output is not converted.
                if(_isLinearColorSpace) 
		            rgb = LinearToGammaSpace(rgb);

				return float4(rgb, 1);
            }

            ENDCG
        }
    }
}
