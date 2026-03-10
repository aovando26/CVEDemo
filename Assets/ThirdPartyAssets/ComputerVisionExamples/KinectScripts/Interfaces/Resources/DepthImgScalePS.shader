Shader "Hidden/Kinect/DepthImgScalePS"
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


            uint _DepthImgWidth4;
            uint _TargetImgWidth4;

            int _DepthImgWidth;
            int _DepthImgHeight;
            int _TargetImgWidth;
            int _TargetImgHeight;


            inline uint GetSrcDepth(uint outPosX, uint outPosY)
            {
                uint2 srcPos = uint2(outPosX * _DepthImgWidth / _TargetImgWidth, outPosY * _DepthImgHeight / _TargetImgHeight);  // src-tensor position
                uint texIndex = (srcPos.y * _DepthImgWidth4 + (srcPos.x >> 3));  // src-block position

                return (SampleIntElementX(texIndex, (srcPos.x >> 1) & 3) >> (16 * (srcPos.x & 1))) & 0xFFFF;
            }


            int4 frag(v2f i, UNITY_VPOS_TYPE screenPos : VPOS) : SV_Target
            {
                uint outIndex = GetBlockIndexO(screenPos);
                uint2 outPos = uint2((outIndex % _TargetImgWidth4) << 3, outIndex / _TargetImgWidth4);  // out-tensor position (4 pixels per block, 2 values per element)

                uint d0 = GetSrcDepth(outPos.x, outPos.y);
                uint d1 = GetSrcDepth(outPos.x + 1, outPos.y);
                uint d2 = GetSrcDepth(outPos.x + 2, outPos.y);
                uint d3 = GetSrcDepth(outPos.x + 3, outPos.y);
                uint d4 = GetSrcDepth(outPos.x + 4, outPos.y);
                uint d5 = GetSrcDepth(outPos.x + 5, outPos.y);
                uint d6 = GetSrcDepth(outPos.x + 6, outPos.y);
                uint d7 = GetSrcDepth(outPos.x + 7, outPos.y);

                return int4((d1 << 16) | d0, (d3 << 16) | d2, (d5 << 16) | d4, (d7 << 16) | d6);
            }

            ENDCG
        }
    }
}
