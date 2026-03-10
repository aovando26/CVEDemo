Shader "Kinect/ColorTexFlipYShader"
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

            // model output texture
            sampler2D _MainTex;

            
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

                o.uv = float2(v.uv.x, 1.0 - v.uv.y);
				o.pos = UnityObjectToClipPos(v.vertex);
             
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
				fixed4 texColor = tex2D(_MainTex, i.uv);
				return texColor;
            }

            ENDCG
        }
    }
}
