using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;

namespace com.rfilkov.providers
{
    public static class YoloProviderJobs
    {
        // constants - detection image size in pixels
        public const int DET_IMAGE_SIZE = 640;
        // detection size in bytes
        public const int DETECTION_SIZE = 116;
        // number of detected classes
        public const int NUM_CLASSES = 80;
        // size of the class mask
        public const int MASK_SIZE = 32;


        // job to convert anchor boxes to pose detections
        [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard)]
        public struct JobProcessDetections : IJob
        {
            [ReadOnly]
            public NativeArray<float> detInput;  // input detections

            [ReadOnly]
            public NativeArray<int> detDisabledTypeBuf;  // disabled object types

            [ReadOnly]
            public Vector2 lboxScale;  // letterbox scale

            [ReadOnly]
            public float scoreThreshold;  // score threshold

            // output
            public NativeList<ObjectDetection> detOutput;  // output detections


            public void Execute()
            {
                const float scale = 1f / DET_IMAGE_SIZE;

                for (int detOffset = 0; detOffset < detInput.Length; detOffset += DETECTION_SIZE)
                {
                    float maxScore = 0f;
                    int maxClass = -1;

                    for (int c = 0; c < NUM_CLASSES; c++)
                    {
                        float score = detInput[detOffset + 4 + c];

                        if (score > maxScore)
                        {
                            maxScore = score;
                            maxClass = c;
                        }
                    }

                    if (maxScore >= scoreThreshold && maxClass >= 0 && detDisabledTypeBuf[maxClass] == 0)
                    {
                        ObjectDetection od = new ObjectDetection();

                        od.center = new Vector2((detInput[detOffset] * scale - 0.5f) * lboxScale.x + 0.5f, (detInput[detOffset + 1] * scale - 0.5f) * lboxScale.y + 0.5f);
                        od.size = new Vector2(detInput[detOffset + 2] * scale * lboxScale.x, detInput[detOffset + 3] * scale * lboxScale.y);

                        od.objType = maxClass;
                        od.score = maxScore;

                        od.m0 = detInput[detOffset + 4 + NUM_CLASSES];
                        od.m1 = detInput[detOffset + 4 + NUM_CLASSES + 1];
                        od.m2 = detInput[detOffset + 4 + NUM_CLASSES + 2];
                        od.m3 = detInput[detOffset + 4 + NUM_CLASSES + 3];
                        od.m4 = detInput[detOffset + 4 + NUM_CLASSES + 4];
                        od.m5 = detInput[detOffset + 4 + NUM_CLASSES + 5];
                        od.m6 = detInput[detOffset + 4 + NUM_CLASSES + 6];
                        od.m7 = detInput[detOffset + 4 + NUM_CLASSES + 7];
                        od.m8 = detInput[detOffset + 4 + NUM_CLASSES + 8];
                        od.m9 = detInput[detOffset + 4 + NUM_CLASSES + 9];
                        od.m10 = detInput[detOffset + 4 + NUM_CLASSES + 10];

                        od.m11 = detInput[detOffset + 4 + NUM_CLASSES + 11];
                        od.m12 = detInput[detOffset + 4 + NUM_CLASSES + 12];
                        od.m13 = detInput[detOffset + 4 + NUM_CLASSES + 13];
                        od.m14 = detInput[detOffset + 4 + NUM_CLASSES + 14];
                        od.m15 = detInput[detOffset + 4 + NUM_CLASSES + 15];
                        od.m16 = detInput[detOffset + 4 + NUM_CLASSES + 16];
                        od.m17 = detInput[detOffset + 4 + NUM_CLASSES + 17];
                        od.m18 = detInput[detOffset + 4 + NUM_CLASSES + 17];
                        od.m19 = detInput[detOffset + 4 + NUM_CLASSES + 19];
                        od.m20 = detInput[detOffset + 4 + NUM_CLASSES + 20];

                        od.m21 = detInput[detOffset + 4 + NUM_CLASSES + 21];
                        od.m22 = detInput[detOffset + 4 + NUM_CLASSES + 22];
                        od.m23 = detInput[detOffset + 4 + NUM_CLASSES + 23];
                        od.m24 = detInput[detOffset + 4 + NUM_CLASSES + 24];
                        od.m25 = detInput[detOffset + 4 + NUM_CLASSES + 25];
                        od.m26 = detInput[detOffset + 4 + NUM_CLASSES + 26];
                        od.m27 = detInput[detOffset + 4 + NUM_CLASSES + 27];
                        od.m28 = detInput[detOffset + 4 + NUM_CLASSES + 28];
                        od.m29 = detInput[detOffset + 4 + NUM_CLASSES + 29];
                        od.m30 = detInput[detOffset + 4 + NUM_CLASSES + 30];
                        od.m31 = detInput[detOffset + 4 + NUM_CLASSES + 31];

                        detOutput.Add(od);
                    }
                }
            }
        }


        // job to do weighted NMS on the raw pose detections
        [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard)]
        public struct JobBatchedNMS : IJob
        {
            public NativeList<ObjectDetection> detObjects;  // raw object detections

            [ReadOnly]
            public float iouThreshold;  // IOU threshold

            [ReadOnly]
            public int maxObjectCount;  // maximum number of tracked objects

            // output
            public NativeArray<ObjectDetection> nmsObjects;  // nms object detections

            public NativeArray<int> nmsObjectCount;  // nms object count


            // caluculate IOU between boxes
            private float CalcIOU(in ObjectDetection pd1, in ObjectDetection pd2)
            {
                float pd1Area = pd1.size.x * pd1.size.y;
                float pd2Area = pd2.size.x * pd2.size.y;

                float p0x = Mathf.Max(pd1.center.x - pd1.size.x * 0.5f, pd2.center.x - pd2.size.x * 0.5f);
                float p0y = Mathf.Max(pd1.center.y - pd1.size.y * 0.5f, pd2.center.y - pd2.size.y * 0.5f);

                float p1x = Mathf.Min(pd1.center.x + pd1.size.x * 0.5f, pd2.center.x + pd2.size.x * 0.5f);
                float p1y = Mathf.Min(pd1.center.y + pd1.size.y * 0.5f, pd2.center.y + pd2.size.y * 0.5f);

                float innerArea = Mathf.Max(0, p1x - p0x) * Mathf.Max(0, p1y - p0y);

                return innerArea / (pd1Area + pd2Area - innerArea);
            }


            public void Execute()
            {
                int detObjectCount = detObjects.Length;
                NativeArray<bool> nmsKeepArray = new NativeArray<bool>(detObjectCount, Allocator.Temp, NativeArrayOptions.ClearMemory);

                for (int i2 = 0; i2 < detObjectCount; i2++)
                {
                    bool isKeep = true;

                    for (int j = 0; j < i2; j++)
                    {
                        if (nmsKeepArray[j])
                        {
                            float iou = CalcIOU(detObjects[i2], detObjects[j]);

                            if (iou >= iouThreshold)
                            {
                                if (detObjects[i2].score > detObjects[j].score)
                                {
                                    nmsKeepArray[j] = false;
                                }
                                else
                                {
                                    isKeep = false;
                                    break;
                                }
                            }
                        }
                    }

                    nmsKeepArray[i2] = isKeep;
                }

                int nmsCount = 0;
                for (int i3 = 0; (i3 < detObjectCount) && (nmsCount < maxObjectCount); i3++)
                {
                    if (nmsKeepArray[i3])
                    {
                        nmsObjects[nmsCount] = detObjects[i3];
                        nmsCount++;
                    }
                }

                nmsObjectCount[0] = nmsCount;
                nmsKeepArray.Dispose();
            }
        }


        // job to convert pose detections to normalized coordinates
        //[BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard)]
        public struct JobProcessMasks : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<ObjectDetection> nmsObjects;  // nms object detections

            [ReadOnly]
            public int nmsObjectCount;  // nms object count

            [ReadOnly]
            public NativeArray<float> maskInput;  // mask input

            [ReadOnly]
            public int maskImgWidth;  // mask-image width

            [ReadOnly]
            public int maskImgHeight;  // mask-image height

            // output
            public NativeArray<float> masksBuf;  // masks buffer


            public void Execute(int i)
            {
                int maxImgSize = maskImgWidth * maskImgHeight;
                int oi = i / maxImgSize;
                int ixy = i % maxImgSize;

                //int iy = ixy / maskImgWidth;
                //int ix = ixy % maskImgWidth;

                //for (int o = 0; o < nmsObjectCount; o++)
                //if(oi == 4)
                {
                    ObjectDetection od = nmsObjects[oi];
                    //int mofs = (iy * maskImgWidth + ix) * MASK_SIZE;
                    int mofs = ixy * MASK_SIZE;

                    float mdot = od.m0 * maskInput[mofs];
                    mdot += od.m1 * maskInput[mofs + 1];
                    mdot += od.m2 * maskInput[mofs + 2];
                    mdot += od.m3 * maskInput[mofs + 3];
                    mdot += od.m4 * maskInput[mofs + 4];
                    mdot += od.m5 * maskInput[mofs + 5];
                    mdot += od.m6 * maskInput[mofs + 6];
                    mdot += od.m7 * maskInput[mofs + 7];
                    mdot += od.m8 * maskInput[mofs + 8];
                    mdot += od.m9 * maskInput[mofs + 9];
                    mdot += od.m10 * maskInput[mofs + 10];

                    mdot += od.m11 * maskInput[mofs + 11];
                    mdot += od.m12 * maskInput[mofs + 12];
                    mdot += od.m13 * maskInput[mofs + 13];
                    mdot += od.m14 * maskInput[mofs + 14];
                    mdot += od.m15 * maskInput[mofs + 15];
                    mdot += od.m16 * maskInput[mofs + 16];
                    mdot += od.m17 * maskInput[mofs + 17];
                    mdot += od.m18 * maskInput[mofs + 18];
                    mdot += od.m19 * maskInput[mofs + 19];
                    mdot += od.m20 * maskInput[mofs + 20];

                    mdot += od.m21 * maskInput[mofs + 21];
                    mdot += od.m22 * maskInput[mofs + 22];
                    mdot += od.m23 * maskInput[mofs + 23];
                    mdot += od.m24 * maskInput[mofs + 24];
                    mdot += od.m25 * maskInput[mofs + 25];
                    mdot += od.m26 * maskInput[mofs + 26];
                    mdot += od.m27 * maskInput[mofs + 27];
                    mdot += od.m28 * maskInput[mofs + 28];
                    mdot += od.m29 * maskInput[mofs + 29];
                    mdot += od.m30 * maskInput[mofs + 30];
                    mdot += od.m31 * maskInput[mofs + 31];

                    //int oofs = o * maskImgWidth * maskImgHeight + (iy * maskImgWidth + ix);
                    masksBuf[i] = mdot;
                }
            }
        }


    }
}
