Shader "Hidden/Kinect/DepthTexToBufferPS"
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


            uint _DepthTexWidth4;
            uint _DepthImgWidth4;

            uint _DepthTexWidth;
            uint _DepthTexHeight;
            uint _DepthImgWidth;
            uint _DepthImgHeight;


            inline int GetTexDepth(uint imgPosX, uint imgPosY)
            {
                uint2 srcPos = uint2(imgPosX * _DepthTexWidth / _DepthImgWidth, imgPosY * _DepthTexHeight / _DepthImgHeight);  // src-tensor position
                uint texIndex = (srcPos.y * _DepthTexWidth4 + (srcPos.x >> 2));
                float fDepth = SampleFloatElementX(texIndex, srcPos.x & 3);

                return (int)(fDepth * 1000);
            }


            int4 frag(v2f i, UNITY_VPOS_TYPE screenPos : VPOS) : SV_Target
            {
                uint outIndex = GetBlockIndexO(screenPos);
                uint2 imgPos = uint2((outIndex % _DepthImgWidth4) << 3, outIndex / _DepthImgWidth4);  // out-tensor position (4 pixels per block, 2 values per element)

                int d0 = GetTexDepth(imgPos.x, imgPos.y);
                int d1 = GetTexDepth(imgPos.x + 1, imgPos.y);
                int d2 = GetTexDepth(imgPos.x + 2, imgPos.y);
                int d3 = GetTexDepth(imgPos.x + 3, imgPos.y);
                int d4 = GetTexDepth(imgPos.x + 4, imgPos.y);
                int d5 = GetTexDepth(imgPos.x + 5, imgPos.y);
                int d6 = GetTexDepth(imgPos.x + 6, imgPos.y);
                int d7 = GetTexDepth(imgPos.x + 7, imgPos.y);

                uint4 outDepth = uint4((d1 << 16) | d0, (d3 << 16) | d2, (d5 << 16) | d4, (d7 << 16) | d6);

                return outDepth;
            }

            ENDCG
        }
    }
}
