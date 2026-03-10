// detection struct size
#define DET_STRUCT_SIZE 56

// keypoint count
#define KP_COUNT 17


// pose detection structure
struct PoseDetection
{
    //float score;
    //float2 center;
    //float2 extent;
    //float2 keyPoints[4];

    float2 center;
    float2 size;
    float score;
    float3 keypoints[KP_COUNT];
};

// pose region structure
struct PoseRegion
{
	float4 box;
    float4 dBox;
    float4 size;
    float4 par;

	float4x4 cropMatrix;
	float4x4 invMatrix;

    float3 keypoints[KP_COUNT];
};

