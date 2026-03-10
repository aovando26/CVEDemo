Shader "Hidden/Kinect/PoseLmBodyIndexPS"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
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

            Texture2D<uint4> _MainTex;
            //SamplerState _MainTexSampler;

            uint _segmTexWidth4;
            uint _bodyIndexImgWidth4;

            int _segmTexWidth;
            int _segmTexHeight;

            uint _bodyIndexImgWidth;
            uint _bodyIndexImgHeight;

            uint _poseIndex;
            float4x4 _invMatrix;

            float2 _invScale;
            float _depthScaleX;


            inline uint GetBodyIndexOf(uint imgPosX, uint imgPosY, uint shift, uint mask)
            {
	            float2 imgUv = float2((float)(_depthScaleX >= 0.0 ? imgPosX : _bodyIndexImgWidth - imgPosX - 1) / _bodyIndexImgWidth, (float)(_bodyIndexImgHeight - imgPosY - 1) / _bodyIndexImgHeight);
                float2 texUv = (imgUv - 0.5) * _invScale + 0.5;
                texUv = mul(_invMatrix, float4(texUv, 0, 1)).xy;

                int2 srcPos = int2(texUv.x * _segmTexWidth, (1 - texUv.y) * _segmTexHeight);  // src-tensor position
		        uint bi = 0xff;

                if(srcPos.x >= 0 && srcPos.x < _segmTexWidth && srcPos.y >= 0 && srcPos.y < _segmTexHeight)
                {
                    uint texIndex = srcPos.y * _segmTexWidth + srcPos.x;
			        float segm = SampleFloatElementX(texIndex, 0);
			
	                if (segm > 0)
	                {
                        bi = _poseIndex;
	                }
                }

                return (bi << shift) | mask;
            }


            int4 frag(v2f i, UNITY_VPOS_TYPE screenPos : VPOS) : SV_Target
            {
                uint outIndex = GetBlockIndexO(screenPos);
                uint2 imgPos = uint2((outIndex % _bodyIndexImgWidth4) << 4, outIndex / _bodyIndexImgWidth4);  // out-tensor position (4 pixels per block, 4 values per element)

                uint bi0 = GetBodyIndexOf(imgPos.x, imgPos.y, 0, 0xffffff00);
                uint bi1 = GetBodyIndexOf(imgPos.x + 1, imgPos.y, 8, 0xffff00ff);
                uint bi2 = GetBodyIndexOf(imgPos.x + 2, imgPos.y, 16, 0xff00ffff);
                uint bi3 = GetBodyIndexOf(imgPos.x + 3, imgPos.y, 24, 0x00ffffff);
                uint bi4 = GetBodyIndexOf(imgPos.x + 4, imgPos.y, 0, 0xffffff00);
                uint bi5 = GetBodyIndexOf(imgPos.x + 5, imgPos.y, 8, 0xffff00ff);
                uint bi6 = GetBodyIndexOf(imgPos.x + 6, imgPos.y, 16, 0xff00ffff);
                uint bi7 = GetBodyIndexOf(imgPos.x + 7, imgPos.y, 24, 0x00ffffff);

                uint bi8 = GetBodyIndexOf(imgPos.x + 8, imgPos.y, 0, 0xffffff00);
                uint bi9 = GetBodyIndexOf(imgPos.x + 9, imgPos.y, 8, 0xffff00ff);
                uint bi10 = GetBodyIndexOf(imgPos.x + 10, imgPos.y, 16, 0xff00ffff);
                uint bi11 = GetBodyIndexOf(imgPos.x + 11, imgPos.y, 24, 0x00ffffff);
                uint bi12 = GetBodyIndexOf(imgPos.x + 12, imgPos.y, 0, 0xffffff00);
                uint bi13 = GetBodyIndexOf(imgPos.x + 13, imgPos.y, 8, 0xffff00ff);
                uint bi14 = GetBodyIndexOf(imgPos.x + 14, imgPos.y, 16, 0xff00ffff);
                uint bi15 = GetBodyIndexOf(imgPos.x + 15, imgPos.y, 24, 0x00ffffff);

                uint2 scrPos = (uint2)(screenPos.xy - 0.5f);
                uint4 biPrev = _MainTex.Load(uint3(screenPos.xy, 0));  // 0xffffffff;  // 

                uint bi0p = (biPrev.x & 0xff) | 0xffffff00;
                uint bi1p = (biPrev.x & 0xff00) | 0xffff00ff;
                uint bi2p = (biPrev.x & 0xff0000) | 0xff00ffff;
                uint bi3p = (biPrev.x & 0xff000000) | 0x00ffffff;
                uint bi4p = (biPrev.y & 0xff) | 0xffffff00;
                uint bi5p = (biPrev.y & 0xff00) | 0xffff00ff;
                uint bi6p = (biPrev.y & 0xff0000) | 0xff00ffff;
                uint bi7p = (biPrev.y & 0xff000000) | 0x00ffffff;

                uint bi8p = (biPrev.z & 0xff) | 0xffffff00;
                uint bi9p = (biPrev.z & 0xff00) | 0xffff00ff;
                uint bi10p = (biPrev.z & 0xff0000) | 0xff00ffff;
                uint bi11p = (biPrev.z & 0xff000000) | 0x00ffffff;
                uint bi12p = (biPrev.w & 0xff) | 0xffffff00;
                uint bi13p = (biPrev.w & 0xff00) | 0xffff00ff;
                uint bi14p = (biPrev.w & 0xff0000) | 0xff00ffff;
                uint bi15p = (biPrev.w & 0xff000000) | 0x00ffffff;

                bi0 = bi0p != 0xffffffff ? bi0p : bi0;
                bi1 = bi1p != 0xffffffff ? bi1p : bi1;
                bi2 = bi2p != 0xffffffff ? bi2p : bi2;
                bi3 = bi3p != 0xffffffff ? bi3p : bi3;
                bi4 = bi4p != 0xffffffff ? bi4p : bi4;
                bi5 = bi5p != 0xffffffff ? bi5p : bi5;
                bi6 = bi6p != 0xffffffff ? bi6p : bi6;
                bi7 = bi7p != 0xffffffff ? bi7p : bi7;

                bi8 = bi8p != 0xffffffff ? bi8p : bi8;
                bi9 = bi9p != 0xffffffff ? bi9p : bi9;
                bi10 = bi10p != 0xffffffff ? bi10p : bi10;
                bi11 = bi11p != 0xffffffff ? bi11p : bi11;
                bi12 = bi12p != 0xffffffff ? bi12p : bi12;
                bi13 = bi13p != 0xffffffff ? bi13p : bi13;
                bi14 = bi14p != 0xffffffff ? bi14p : bi14;
                bi15 = bi15p != 0xffffffff ? bi15p : bi15;

                uint4 biOut = uint4(bi0 & bi1 & bi2 & bi3, bi4 & bi5 & bi6 & bi7,
                    bi8 & bi9 & bi10 & bi11, bi12 & bi13 & bi14 & bi15);

                return biOut;
            }

            ENDCG
        }
    }
}
