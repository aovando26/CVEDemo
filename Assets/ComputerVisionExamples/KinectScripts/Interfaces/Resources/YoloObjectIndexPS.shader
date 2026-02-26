Shader "Hidden/Kinect/YoloObjectIndexPS"
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


            uint _maskTexWidth4;
            uint _objIndexImgWidth4;

            int _maskTexWidth;
            int _maskTexHeight;
            int _maskObjCount;

            uint _objIndexImgWidth;
            uint _objIndexImgHeight;

            float2 _invScale;
            float _depthScaleX;

            float4 _objBoxes[100];


            inline uint GetMaskObjIndex(uint imgPosX, uint imgPosY)
            {
	            float2 imgUv = float2((float)(_depthScaleX >= 0.0 ? imgPosX : _objIndexImgWidth - imgPosX - 1) / _objIndexImgWidth, (float)imgPosY / _objIndexImgHeight);
	            float2 srcUv = (imgUv - 0.5) * _invScale + 0.5;

                int2 srcPos = int2(srcUv.x * _maskTexWidth, srcUv.y * _maskTexHeight);  // src-tensor position
		        uint maxOI = 0xff;

                if(srcPos.x >= 0 && srcPos.x < _maskTexWidth && srcPos.y >= 0 && srcPos.y < _maskTexHeight)
                {
                    uint texIndex = srcPos.y * _maskTexWidth + srcPos.x;
		            float maxMask = 0.0;

		            for(int o = 0; o < _maskObjCount; o++)
		            {
                        float4 objBox = _objBoxes[o];

                        if(imgUv.x >= objBox.x && imgUv.x <= objBox.z && imgUv.y >= objBox.y && imgUv.y <= objBox.w)
                        {
			                int maskIdx = o * _maskTexWidth4 + (texIndex >> 2);
			                float mask = SampleFloatElementX(maskIdx, texIndex & 3);
			
			                if (mask > maxMask)
			                {
				                maxMask = mask;
				                maxOI = o;
			                }
                        }
                    }
                }

                return maxOI;
            }


            int4 frag(v2f i, UNITY_VPOS_TYPE screenPos : VPOS) : SV_Target
            {
                uint outIndex = GetBlockIndexO(screenPos);
                uint2 imgPos = uint2((outIndex % _objIndexImgWidth4) << 4, outIndex / _objIndexImgWidth4);  // out-tensor position (4 pixels per block, 4 values per element)

                int oi0 = GetMaskObjIndex(imgPos.x, imgPos.y);
                int oi1 = GetMaskObjIndex(imgPos.x + 1, imgPos.y);
                int oi2 = GetMaskObjIndex(imgPos.x + 2, imgPos.y);
                int oi3 = GetMaskObjIndex(imgPos.x + 3, imgPos.y);
                int oi4 = GetMaskObjIndex(imgPos.x + 4, imgPos.y);
                int oi5 = GetMaskObjIndex(imgPos.x + 5, imgPos.y);
                int oi6 = GetMaskObjIndex(imgPos.x + 6, imgPos.y);
                int oi7 = GetMaskObjIndex(imgPos.x + 7, imgPos.y);

                int oi8 = GetMaskObjIndex(imgPos.x + 8, imgPos.y);
                int oi9 = GetMaskObjIndex(imgPos.x + 9, imgPos.y);
                int oi10 = GetMaskObjIndex(imgPos.x + 10, imgPos.y);
                int oi11 = GetMaskObjIndex(imgPos.x + 11, imgPos.y);
                int oi12 = GetMaskObjIndex(imgPos.x + 12, imgPos.y);
                int oi13 = GetMaskObjIndex(imgPos.x + 13, imgPos.y);
                int oi14 = GetMaskObjIndex(imgPos.x + 14, imgPos.y);
                int oi15 = GetMaskObjIndex(imgPos.x + 15, imgPos.y);

                uint4 oiOut = uint4((oi3 << 24) | (oi2 << 16) | (oi1 << 8) | oi0,
                    (oi7 << 24) | (oi6 << 16) | (oi5 << 8) | oi4,
                    (oi11 << 24) | (oi10 << 16) | (oi9 << 8) | oi8,
                    (oi15 << 24) | (oi14 << 16) | (oi13 << 8) | oi12);

                return oiOut;
            }

            ENDCG
        }
    }
}
