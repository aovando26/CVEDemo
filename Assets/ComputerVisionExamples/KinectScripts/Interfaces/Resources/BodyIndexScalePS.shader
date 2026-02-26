Shader "Hidden/Kinect/BodyIndexScalePS"
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


            uint _BodyIndexImgWidth4;
            uint _TargetImgWidth4;

            int _BodyIndexImgWidth;
            int _BodyIndexImgHeight;
            int _TargetImgWidth;
            int _TargetImgHeight;


            inline uint GetSrcBodyIndex(uint outPosX, uint outPosY)
            {
                uint2 srcPos = uint2(outPosX * _BodyIndexImgWidth / _TargetImgWidth, outPosY * _BodyIndexImgHeight / _TargetImgHeight);  // src-tensor position
                uint texIndex = (srcPos.y * _BodyIndexImgWidth4 + (srcPos.x >> 4));  // src-block position

                return (SampleIntElementX(texIndex, (srcPos.x >> 2) & 3) >> (8 * (srcPos.x & 2))) & 0xFF;
            }


            int4 frag(v2f i, UNITY_VPOS_TYPE screenPos : VPOS) : SV_Target
            {
                uint outIndex = GetBlockIndexO(screenPos);
                uint2 outPos = uint2((outIndex % _TargetImgWidth4) << 4, outIndex / _TargetImgWidth4);  // out-tensor position (4 pixels per block, 2 values per element)

                uint bi0 = GetSrcBodyIndex(outPos.x, outPos.y);
                uint bi1 = GetSrcBodyIndex(outPos.x + 1, outPos.y);
                uint bi2 = GetSrcBodyIndex(outPos.x + 2, outPos.y);
                uint bi3 = GetSrcBodyIndex(outPos.x + 3, outPos.y);
                uint bi4 = GetSrcBodyIndex(outPos.x + 4, outPos.y);
                uint bi5 = GetSrcBodyIndex(outPos.x + 5, outPos.y);
                uint bi6 = GetSrcBodyIndex(outPos.x + 6, outPos.y);
                uint bi7 = GetSrcBodyIndex(outPos.x + 7, outPos.y);

                uint bi8 = GetSrcBodyIndex(outPos.x + 8, outPos.y);
                uint bi9 = GetSrcBodyIndex(outPos.x + 9, outPos.y);
                uint bi10 = GetSrcBodyIndex(outPos.x + 10, outPos.y);
                uint bi11 = GetSrcBodyIndex(outPos.x + 11, outPos.y);
                uint bi12 = GetSrcBodyIndex(outPos.x + 12, outPos.y);
                uint bi13 = GetSrcBodyIndex(outPos.x + 13, outPos.y);
                uint bi14 = GetSrcBodyIndex(outPos.x + 14, outPos.y);
                uint bi15 = GetSrcBodyIndex(outPos.x + 15, outPos.y);

                uint4 biOut = uint4((bi3 << 24) | (bi2 << 16) | (bi1 << 8) | bi0,
                    (bi7 << 24) | (bi6 << 16) | (bi5 << 8) | bi4,
                    (bi11 << 24) | (bi10 << 16) | (bi9 << 8) | bi8,
                    (bi15 << 24) | (bi14 << 16) | (bi13 << 8) | bi12);

                return biOut;
            }

            ENDCG
        }
    }
}
