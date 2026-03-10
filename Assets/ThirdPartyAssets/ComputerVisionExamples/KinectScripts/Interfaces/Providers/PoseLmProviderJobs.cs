using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;

namespace com.rfilkov.providers
{
    public static class PoseLmProviderJobs
    {
        // landmark image size in pixels
        public const int LM_IMAGE_SIZE = 256;

        // landmark tilt (5 deg back)
        public const float LM_TILT = 5f * Mathf.Deg2Rad;


        // job to convert raw landmarks to normalized landmarks and to Unity coordinates
        //[BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard)]
        public struct JobProcessLandmarks : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> poseFlag;  // pose flag

            [ReadOnly]
            public NativeArray<float> landmarks2d;  // 2d landmarks

            [ReadOnly]
            public NativeArray<float> landmarks3d;  // 3d landmarks

            [ReadOnly]
            public int landmarkCount;  // landmark count

            [ReadOnly]
            public int poseIndex;  // pose index

            [ReadOnly]
            public NativeArray<PoseRegion> poseRegions;  // pose regions

            [ReadOnly]
            public Vector2 lboxScale;  // letterbox scaler

            [ReadOnly]
            public NativeArray<ushort> depthMapBuf;

            [ReadOnly]
            public int depthMapWidth;

            [ReadOnly]
            public int depthMapHeight;

            [ReadOnly]
            public Vector2 intrPpt;

            [ReadOnly]
            public Vector2 intrFlen;

            [ReadOnly]
            public int segmWidth;  // segmentation width

            [ReadOnly]
            public int segmHeight;  // segmentation height

            [ReadOnly]
            public NativeArray<float> segmBuffer;  // segmentation buffer

            // output
            public NativeArray<Vector4> outLandmarks2d;  // 2d landmarks

            public NativeArray<Vector4> outLandmarks3d;  // 3d landmarks

            public NativeArray<Vector4> outLandmarksKin;  // sensor landmarks


            // get segm map value for the 2d lm position
            private float GetSegmValue(Vector2 lmPos)
            {
                int ix = (int)(lmPos.x * segmWidth);
                int iy = (int)((1f - lmPos.y) * segmHeight);
                int ind = ix + iy * segmWidth;

                if (ix < 0 || ix >= segmWidth || iy < 0 || iy >= segmHeight)
                    return -1f;
                else
                    return segmBuffer[ind];
            }


            public void Execute(int i)
            {
                PoseRegion region = poseRegions[poseIndex];

                if (i >= landmarkCount)
                {
                    float poseConfidence = poseFlag[0];

                    //outLandmarks2d[landmarkCount] = new Vector4(poseConfidence, 0f, 0f, 0f);
                    float boxX = (region.box.x - 0.5f) * lboxScale.x + 0.5f;
                    float boxY = (region.box.y - 0.5f) * lboxScale.y + 0.5f;
                    outLandmarks2d[i] = new Vector4(boxX, boxY, region.size.x * lboxScale.x, region.size.y * lboxScale.y);

                    outLandmarks3d[i] = new Vector4(poseConfidence, 0f, 0f, 0f);
                    outLandmarksKin[i] = new Vector4(poseConfidence, 0f, 0f, 0f);
                }
                else
                {
                    int li2 = i * 5;
                    int li3 = i * 3;

                    float x = landmarks2d[li2] / LM_IMAGE_SIZE;
                    float y = landmarks2d[li2 + 1] / LM_IMAGE_SIZE;
                    //float z = landmarks2d[li2 + 2] / LM_IMAGE_SIZE;

                    float visibility = landmarks2d[li2 + 3];
                    float presence = landmarks2d[li2 + 4];

                    float scoreV = kinect.KinectInterop.Sigmoid(visibility);
                    float scoreP = kinect.KinectInterop.Sigmoid(presence);
                    float score = i <= 32 ? Mathf.Min(scoreV, scoreP) : scoreP;

                    // lm2d
                    Vector3 pos = new Vector3(x, 1f - y, 0f);
                    float segm = GetSegmValue(pos);

                    pos = region.cropMatrix.MultiplyPoint3x4(pos);
                    pos.x = (pos.x - 0.5f) * lboxScale.x + 0.5f;
                    pos.y = (pos.y - 0.5f) * lboxScale.y + 0.5f;

                    Vector4 lm2d = pos;
                    lm2d.z = segm;
                    lm2d.w = score;
                    outLandmarks2d[i] = lm2d;

                    int dx = (int)(pos.x * depthMapWidth);
                    int dy = (int)((1f - pos.y) * depthMapHeight);

                    // lm3d
                    pos = new Vector3(landmarks3d[li3], -landmarks3d[li3 + 1], landmarks3d[li3 + 2]);

                    Matrix4x4 matRotation = kinect.KinectInterop.MakeRotationMatrixZ(region.box.w) * kinect.KinectInterop.MakeRotationMatrixX(LM_TILT);
                    pos = matRotation.MultiplyPoint3x4(pos);

                    Vector4 lm3d = pos;
                    lm3d.w = score;
                    outLandmarks3d[i] = lm3d;

                    // lmKin
                    Vector4 lmPos = Vector4.zero;
                    if (intrFlen.x != 0f && intrFlen.y != 0f)
                    {
                        float kx = (dx - intrPpt.x) / intrFlen.x;  // * pos.z;
                        float ky = (dy - intrPpt.y) / intrFlen.y;  // * pos.z;
                        lmPos = new Vector4(kx, ky, 1f, score);  // pos.z, score);
                    }
                    else
                    {
                        lmPos.w = score;
                    }

                    outLandmarksKin[i] = lmPos;

                    if (i == 33)
                    {
                        int di = Mathf.Clamp(dx, 0, depthMapWidth - 1) + Mathf.Clamp(dy, 0, depthMapHeight - 1) * depthMapWidth;

                        uint depthMm = depthMapBuf[di];
                        float depth = depthMm * 0.001f;

                        Vector4 hipPos = Vector4.zero;
                        if (intrFlen.x != 0f && intrFlen.y != 0f)
                        {
                            float hx = (dx - intrPpt.x) / intrFlen.x * depth;
                            float hy = (dy - intrPpt.y) / intrFlen.y * depth;
                            hipPos = new Vector4(hx, -hy, depth, score);
                        }
                        else
                        {
                            hipPos.w = score;
                        }

                        outLandmarks3d[i] = hipPos;
                    }
                }
            }
        }


    }
}
