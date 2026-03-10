#include "UnityCG.cginc"

//#pragma vertex vert
//#pragma fragment frag


struct v2f
{
};

v2f vert(float4 vertex : POSITION, out float4 outpos : SV_POSITION)
{
    v2f o;
    outpos = UnityObjectToClipPos(vertex);
    return o;
}


uint WidthShiftO;
//uint StrideAxisO, DimAxisO, DimBlockedO;


inline uint GetBlockIndexO(UNITY_VPOS_TYPE screenPos)
{
    uint2 tid = (uint2)(screenPos.xy - 0.5f);
    return (tid.y << WidthShiftO) + tid.x;
}


Texture2D<float4> Xfptr;
Texture2D<int4> Xiptr;
uint WidthShiftX, WidthMaskX;


inline uint GetBlockIndexX(UNITY_VPOS_TYPE screenPos)
{
    uint2 tid = (uint2)(screenPos.xy - 0.5f);
    return (tid.y << WidthShiftX) + tid.x;
}

inline float4 SampleFloatBlock(Texture2D ptr, uint widthMask, uint widthShift, uint blockIndex)
{
    return ptr.Load(uint3(blockIndex & widthMask, blockIndex >> widthShift, 0));
}

float4 SampleFloatBlockX(uint blockIndex)
{
    return SampleFloatBlock(Xfptr, WidthMaskX, WidthShiftX, blockIndex);
}


inline float SampleFloatElement(Texture2D ptr, uint widthMask, uint widthShift, uint blockIndex, uint c)
{
    uint x = blockIndex & widthMask;
    uint y = blockIndex >> widthShift;

    return ptr.Load(uint3(x, y, 0))[c];
}

float SampleFloatElementX(uint blockIndex, uint c)
{
    return SampleFloatElement(Xfptr, WidthMaskX, WidthShiftX, blockIndex, c);
}


inline float4 SampleFloatElements(Texture2D ptr, uint widthMask, uint widthShift, uint4 blockIndex4, uint4 c4)
{
    float4 v = 0;
    uint4 x4 = blockIndex4 & widthMask;
    uint4 y4 = blockIndex4 >> widthShift;

    v.x = ptr.Load(uint3(x4.x, y4.x, 0))[c4.x];
    v.y = ptr.Load(uint3(x4.y, y4.y, 0))[c4.y];
    v.z = ptr.Load(uint3(x4.z, y4.z, 0))[c4.z];
    v.w = ptr.Load(uint3(x4.w, y4.w, 0))[c4.w];

    return v;
}

float4 SampleFloatElementsX(uint4 blockIndex4, uint4 c4)
{
    return SampleFloatElements(Xfptr, WidthMaskX, WidthShiftX, blockIndex4, c4);
}


inline int4 SampleIntBlock(Texture2D<int4> ptr, uint widthMask, uint widthShift, uint blockIndex)
{
    return ptr.Load(uint3(blockIndex & widthMask, blockIndex >> widthShift, 0));
}

int4 SampleIntBlockX(uint blockIndex)
{
    return SampleIntBlock(Xiptr, WidthMaskX, WidthShiftX, blockIndex);
}


inline int SampleIntElement(Texture2D<int4> ptr, uint widthMask, uint widthShift, uint blockIndex, uint c)
{
    uint x = blockIndex & widthMask;
    uint y = blockIndex >> widthShift;

    return ptr.Load(uint3(x, y, 0))[c];
}

int SampleIntElementX(uint blockIndex, uint c)
{
    return SampleIntElement(Xiptr, WidthMaskX, WidthShiftX, blockIndex, c);
}


inline int4 SampleIntElements(Texture2D<int4> ptr, uint widthMask, uint widthShift, uint4 blockIndex4, uint4 c4)
{
    int4 v = 0;
    uint4 x4 = blockIndex4 & widthMask;
    uint4 y4 = blockIndex4 >> widthShift;

    v.x = ptr.Load(uint3(x4.x, y4.x, 0))[c4.x];
    v.y = ptr.Load(uint3(x4.y, y4.y, 0))[c4.y];
    v.z = ptr.Load(uint3(x4.z, y4.z, 0))[c4.z];
    v.w = ptr.Load(uint3(x4.w, y4.w, 0))[c4.w];

    return v;
}

int4 SampleIntElementsX(uint4 blockIndex4, uint4 c4)
{
    return SampleIntElements(Xiptr, WidthMaskX, WidthShiftX, blockIndex4, c4);
}

