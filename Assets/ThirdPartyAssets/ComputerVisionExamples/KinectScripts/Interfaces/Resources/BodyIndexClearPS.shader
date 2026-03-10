Shader "Hidden/Kinect/BodyIndexClearPS"
{
    Properties
    {
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

            #include "CommonPS.cginc"


            int4 frag(v2f i, UNITY_VPOS_TYPE screenPos : VPOS) : SV_Target
            {
                uint oiClear = 0xffffffff;
                uint4 oi4 = uint4(oiClear, oiClear, oiClear, oiClear);

                return oi4;
            }

            ENDCG
        }
    }
}
