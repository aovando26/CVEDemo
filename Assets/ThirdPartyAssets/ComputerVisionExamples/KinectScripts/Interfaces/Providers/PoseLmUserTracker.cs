using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using com.rfilkov.kinect;


namespace com.rfilkov.providers
{
    /// <summary>
    /// PoseLmUserTracker tracks user data consistently across frames.
    /// </summary>
    public class PoseLmUserTracker
    {

        public class UserTrackingJointData
        {
            public float jointScore;

            public Vector2 imagePos;
            public Vector3 kinectPos;
            public Vector3 spacePos;
            public Vector3 smoothPos;


            public UserTrackingJointData(float jointScore, Vector2 imagePos, Vector3 kinectPos, Vector3 spacePos, Vector3 smoothPos)
            {
                this.jointScore = jointScore;

                this.imagePos = imagePos;
                this.kinectPos = kinectPos;
                this.spacePos = spacePos;
                this.smoothPos = smoothPos;
            }

            public override string ToString()
            {
                return $"score: {jointScore:F3}, uv: {imagePos:F2}, kin: {kinectPos:F2}, pos: {spacePos:F2}, smooth: {smoothPos:F2}";
            }
        }


        // user tracking body data (for internal use
        public class UserTrackingBodyData
        {
            public ulong userId;
            public int bodyIndex;

            //public trackers.TrackState trackState;
            //public bool isActivated;

            public long timestamp;
            public long prevTimestamp;

            //public float regionTs;
            //public float updateTs;
            //public float startTs;

            public float bodyDepthRaw;
            public float bodyDepth;
            //public float bodyDepthDX;

            private const int DEPTH_COUNT = 10;
            private float[] depths;
            private float depthSum;
            private int depthIndex;
            private int depthCount;

            //private float bodyDepthX;
            //private float bodyDepthK;
            //private float bodyDepthP;

            //public Vector2 lastImagePos;
            //public Vector2 imagePos;
            //public Vector2 imagePosDX;

            //public float userScore;
            //public float lastUserScore;

            // score history
            //public float userScoreDX;

            //public Vector4 lastPoseBox;

            public UserTrackingJointData[] joints;

            // smooth position
            //private const int SMOOTH_COUNT = 10;
            public Vector4[] smoothPos;

            private Vector3[] smoothDx;
            //private float[,,] smoothRaw;
            //private float[,] smoothSum;
            //private int smoothIndex;
            //private int smoothCount;

            //// K-params
            //private const float KParamQ = 0.001f;
            //private const float KParamR = 0.0015f;


            public UserTrackingBodyData(float depth, int lmCount)
            {
                //// body image position
                //imagePos = lastImagePos = bodyImagePos;

                // depth
                this.bodyDepth = bodyDepthRaw = depth;
                //this.bodyDepthX = depth;

                depths = new float[DEPTH_COUNT];
                depths[0] = depthSum = depth;
                depthIndex = depthCount = 1;

                //// pose box
                //lastPoseBox = poseBox;

                //// score
                //userScore = lastUserScore = score;

                // smoothed 3d-lm
                smoothPos = new Vector4[lmCount];
                smoothDx = new Vector3[lmCount];

                //smoothRaw = new float[lmCount, 3, SMOOTH_COUNT];
                //smoothSum = new float[lmCount, 3];
                //smoothIndex = smoothCount = 0;
            }

            public override string ToString()
            {
                return $"id: {userId}, bi: {bodyIndex}, depth: {bodyDepth:F3}";
            }


            //public float Prob
            //{
            //    get => userScore;
            //}


            /// <summary>
            /// Sets the latest body parameters.
            /// </summary>
            /// <param name="bodyIndex">Body index</param>
            /// <param name="lastTimestamp">Last timestamp</param>
            /// <param name="bodyImagePos">Body image position</param>
            /// <param name="score">Score</param>
            public float SetBodyData(int bodyIndex, long lastTimestamp, float depth)  // , float regionTs)
            {
                // update body-index and timestamp
                this.bodyIndex = bodyIndex;

                this.prevTimestamp = timestamp;
                this.timestamp = lastTimestamp;
                float deltaTime = (timestamp - prevTimestamp) * 0.0000001f;

                //// image pos
                //Vector3 imagePosParams = new Vector3(2f, 1.5f, deltaTime);
                //this.imagePosDX = KinectInterop.OneEuroStepDX(bodyImagePos, imagePos, imagePosDX, imagePosParams);
                //this.imagePos = KinectInterop.OneEuroStepX(bodyImagePos, imagePos, imagePosDX, imagePosParams);
                //this.lastImagePos = bodyImagePos;
                ////Debug.Log($"    imagePos: {imagePos:F2}, lastImagePos: {lastImagePos:F2}\nimagePosDX: {imagePosDX:F2}, params: {imagePosParams:F3}");

                //// score
                //Vector3 userScoreParams = new Vector3(2f, 1.5f, deltaTime);
                //this.userScoreDX = KinectInterop.OneEuroStepDX(score, userScore, userScoreDX, userScoreParams);
                //this.userScore = KinectInterop.OneEuroStepX(score, userScore, userScoreDX, userScoreParams);
                //this.lastUserScore = score;

                // depth (allow up to 1/2 meter changes)
                if (depth > 0f)
                {
                    //if(Mathf.Abs(depth - bodyDepthRaw) <= 0.5f)
                    //{
                    //    //Vector3 bodyDepthParams = userScoreParams;
                    //    //this.bodyDepthDX = KinectInterop.OneEuroStepDX(depth, bodyDepth, bodyDepthDX, bodyDepthParams);
                    //    //this.bodyDepth = KinectInterop.OneEuroStepX(depth, bodyDepth, bodyDepthDX, bodyDepthParams);
                    //    //this.bodyDepth = Mathf.Lerp(bodyDepth, depth, 5f * deltaTime);

                    //    this.bodyDepthRaw = depth;
                    //    bodyDepthK = (bodyDepthP + KParamQ) / (bodyDepthP + KParamQ + KParamR);
                    //    bodyDepthP = KParamR * (bodyDepthP + KParamQ) / (KParamR + bodyDepthP + KParamQ);
                    //    bodyDepth = bodyDepthX = bodyDepthX + (depth - bodyDepthX) * bodyDepthK;
                    //    //bodyDepth = Mathf.Lerp(bodyDepth, bodyDepthX, 5f * deltaTime);
                    //}
                    //else
                    //{
                    //    this.bodyDepthRaw = Mathf.Lerp(bodyDepthRaw, depth, 5f * deltaTime);
                    //}

                    // depth
                    if(depthCount < DEPTH_COUNT)
                    {
                        depthSum += depth;
                        depthCount++;
                    }
                    else
                    {
                        depthSum -= depths[depthIndex];
                        depthSum += depth;
                    }

                    // mean, var, dev
                    depths[depthIndex] = bodyDepthRaw = depth;
                    depthIndex = (depthIndex + 1) % DEPTH_COUNT;

                    float depthMean = depthSum / depthCount;
                    float depthVar = 0f;

                    for (int i = 0; i < depthCount; i++)
                    {
                        float dd = depths[i] - depthMean;
                        depthVar += dd * dd;
                    }

                    depthVar /= depthCount;
                    float depthDev = Mathf.Sqrt(depthVar);

                    // min, max, body-depth
                    float depthMin = depthMean - depthDev;
                    float depthMax = depthMean + depthDev;
                    bodyDepth = 0f; int bodyDepthCount = 0;

                    for (int i = 0; i < depthCount; i++)
                    {
                        float d = depths[i];
                        if (d >= depthMin && d <= depthMax)
                        {
                            bodyDepth += d;
                            bodyDepthCount++;
                        }
                    }

                    bodyDepth /= bodyDepthCount;
                    //Debug.Log($"  bi: {bodyIndex}, rawDepth: {bodyDepthRaw:F3}, bodyDepth: {bodyDepth:F3}, dIdx: {depthIndex}, dCnt: {depthCount}\ndMean: {depthMean:F3}, dDev: {depthDev:F3}, bdCount: {bodyDepthCount}");
                }

                return deltaTime;
            }

            // "One Euro" low pass filter
            private float lpf_Alpha(float cutoff, float t_e)
            {
                float r = 2f * Mathf.PI * cutoff * t_e;
                return r / (r + 1);
            }

            private Vector3 lpf_Step_dx(Vector3 x, Vector3 p_x, Vector3 p_dx, Vector3 f_params)
            {
                Vector3 dx = (x - p_x) / f_params.z;
                return Vector3.Lerp(p_dx, dx, lpf_Alpha(1f, f_params.z));
            }

            private Vector3 lpf_Step_x(Vector3 x, Vector3 p_x, Vector3 dx, Vector3 f_params)
            {
                float cutoff = f_params.y + f_params.x * dx.magnitude;
                return Vector3.Lerp(p_x, x, lpf_Alpha(cutoff, f_params.z));
            }

            /// <summary>
            /// Sets all body joints data by using the given raw values.
            /// </summary>
            /// <param name="imagePos">Raw image (uv) positions</param>
            /// <param name="spacePos">Raw space position</param>
            public void SetJointsData(Vector4[] imagePos, Vector4[] kinectPos, Vector4[] spacePos, Vector3 lpfParams, int[] poseJointTypes)
            {
                if (imagePos == null || kinectPos == null || spacePos == null)
                    throw new Exception("Image & space positions can't be null.");
                if (imagePos.Length != spacePos.Length || imagePos.Length != kinectPos.Length)
                    throw new Exception($"Image and space positions have different lengths ({imagePos.Length}!={spacePos.Length})");

                // smooth 3d-lm
                for(int i = 0; i < spacePos.Length; i++)
                {
                    Vector4 pos = spacePos[i];

                    //if(pos.z != 0f)
                    {
                        float score = pos.w;

                        Vector3 p_pos = smoothPos[i];
                        Vector3 p_pos_dx = smoothDx[i];

                        Vector3 pos_dx = lpf_Step_dx(pos, p_pos, p_pos_dx, lpfParams);
                        pos = lpf_Step_x(pos, p_pos, pos_dx, lpfParams);
                        pos.w = score;

                        smoothPos[i] = pos;
                        smoothDx[i] = pos_dx;
                        

                        //// smooth pos for each coord
                        //float[] posRaw = { pos.x, pos.y, pos.z };
                        //float[] posOut = { 0f, 0f, 0f };

                        //for (int c = 0; c < 3; c++)
                        //{
                        //    if (smoothCount < SMOOTH_COUNT)
                        //    {
                        //        smoothSum[i, c] += posRaw[c];
                        //    }
                        //    else
                        //    {
                        //        smoothSum[i, c] -= smoothRaw[i, c, smoothIndex];
                        //        smoothSum[i, c] += posRaw[c];
                        //    }

                        //    // mean, var, dev
                        //    smoothRaw[i, c, smoothIndex] = posRaw[c];

                        //    int sc = smoothCount < SMOOTH_COUNT ? smoothCount + 1 : smoothCount;
                        //    float smoothMean = smoothSum[i, c] / sc;
                        //    float smoothVar = 0f;

                        //    for (int s = 0; s < sc; s++)
                        //    {
                        //        float dd = smoothRaw[i, c, s] - smoothMean;
                        //        smoothVar += dd * dd;
                        //    }

                        //    smoothVar /= sc;
                        //    float smoothDev = Mathf.Sqrt(smoothVar);

                        //    // min, max, pos-comp
                        //    float smoothMin = smoothMean - smoothDev;
                        //    float smoothMax = smoothMean + smoothDev;
                        //    float posFilt = 0f; int posFiltCount = 0;

                        //    for (int s = 0; s < sc; s++)
                        //    {
                        //        float p = smoothRaw[i, c, s];
                        //        if (p >= smoothMin && p <= smoothMax)
                        //        {
                        //            posFilt += p;
                        //            posFiltCount++;
                        //        }
                        //    }

                        //    posOut[c] = posFilt / posFiltCount;
                        //}

                        //smoothPos[i] = new Vector4(posOut[0], posOut[1], posOut[2], score);
                    }
                }

                //smoothIndex = (smoothIndex + 1) % SMOOTH_COUNT;
                //if (smoothCount < SMOOTH_COUNT)
                //    smoothCount++;

                int jointLen = imagePos.Length;
                if(joints == null)
                {
                    // create joints data
                    joints = new UserTrackingJointData[jointLen];
                    for (int i = 0; i < jointLen; i++)
                    {
                        if (poseJointTypes[i] < 0)
                            continue;

                        joints[i] = new UserTrackingJointData(spacePos[i].w, imagePos[i], kinectPos[i], spacePos[i], smoothPos[i]);
                    }
                }
                else
                {
                    // update joints data
                    if (imagePos.Length != joints.Length)
                        throw new Exception($"Image/space positions and joints have different lengths ({imagePos.Length}!={joints.Length})");

                    if (CheckBoneOrientations(ref smoothPos, ref kinectPos, joints))
                    {
                        for (int i = 0; i < jointLen; i++)
                        {
                            if (poseJointTypes[i] < 0)
                                continue;

                            UserTrackingJointData joint = joints[i];
                            joint.jointScore = spacePos[i].w;

                            joint.imagePos = imagePos[i];
                            joint.kinectPos = kinectPos[i];
                            joint.spacePos = spacePos[i];
                            joint.smoothPos = smoothPos[i];
                        }
                    }
                    else
                    {
                        // revert the timestamp
                        timestamp = prevTimestamp;
                    }

                }
            }

            // checks for bone orientation issues
            private bool CheckBoneOrientations(ref Vector4[] spacePos, ref Vector4[] kinectPos, UserTrackingJointData[] prevSpacePos)
            {
                // check hips orientation
                Vector3 hips1 = GetLmBone(LmBoneType.Hips);
                Vector3 hips2 = GetLmBone(LmBoneType.Hips, spacePos);
                if (Vector3.Angle(hips1.normalized, hips2.normalized) > MAX_ORI_DIFF)
                {
                    //Debug.LogWarning($"Hips rotate too sharp: {Vector3.Angle(hips1.normalized, hips2.normalized):F1} deg\nRestoring previous pose - id: {userId}, bi : {bodyIndex}");
                    RestorePrevBoneType(BoneType.All, ref spacePos, ref kinectPos, prevSpacePos);
                    return true;
                }

                // check shoulders orientation
                Vector3 sh1 = GetLmBone(LmBoneType.Shoulders);
                Vector3 sh2 = GetLmBone(LmBoneType.Shoulders, spacePos);
                if (Vector3.Angle(sh1.normalized, sh2.normalized) > MAX_ORI_DIFF)
                {
                    //Debug.LogWarning($"Shoulders rotate too sharp: {Vector3.Angle(sh1.normalized, sh2.normalized):F1} deg\nRestoring previous pose - id: {userId}, bi : {bodyIndex}");
                    RestorePrevBoneType(BoneType.All, ref spacePos, ref kinectPos, prevSpacePos);
                    return true;
                }

                // check spine orientation
                Vector3 spine1 = GetLmBone(LmBoneType.Spine);
                Vector3 spine2 = GetLmBone(LmBoneType.Spine, spacePos);
                if (Vector3.Angle(spine1.normalized, spine2.normalized) > MAX_ORI_DIFF)
                {
                    //Debug.LogWarning($"Spine rotates too sharp: {Vector3.Angle(spine1.normalized, spine2.normalized):F1}  deg\nRestoring previous pose - id: {userId}, bi : {bodyIndex}");
                    RestorePrevBoneType(BoneType.All, ref spacePos, ref kinectPos, prevSpacePos);
                    return true;
                }

                // check shoulders-to-knees difference
                Vector3 kneesDir = GetLmBone(LmBoneType.Knees, spacePos);
                if (Vector3.Angle(sh2.normalized, kneesDir.normalized) > MAX_ORI_DIFF)
                {
                    //Debug.LogWarning($"Sh-to-knees dir differs too much: {Vector3.Angle(sh2.normalized, kneesDir.normalized):F1}  deg\nRestoring previous pose - id: {userId}, bi : {bodyIndex}");
                    RestorePrevBoneType(BoneType.All, ref spacePos, ref kinectPos, prevSpacePos);
                    return true;
                }

                return true;
            }


            // restores previous positions of the body bones with given bone types
            private void RestorePrevBoneType(BoneType boneType, ref Vector4[] spacePos, ref Vector4[] kinectPos, UserTrackingJointData[] prevSpacePos)
            {
                int jointLen = spacePos.Length;

                for (int i = 0; i < jointLen; i++)
                {
                    if ((PoseLm2BoneType[i] & boneType) != BoneType.None &&
                        prevSpacePos[i] != null)
                    {
                        spacePos[i] = prevSpacePos[i].smoothPos;
                        kinectPos[i] = prevSpacePos[i].kinectPos;
                    }
                }
            }


            private const float MAX_ORI_DIFF = 120f;  // max allowed difference - 120 degrees
            private enum LmBoneType : int { Hips, Shoulders, Spine, Nose, Knees, Ankles, LFoot, RFoot, LLeg, RLeg, LArm, RArm }

            // returns the lm-bone estimated from space-pos array
            private Vector3 GetLmBone(LmBoneType boneType, Vector4[] spacePos)
            {
                switch(boneType)
                {
                    case LmBoneType.Hips:
                        return (Vector3)(spacePos[23] - spacePos[24]);

                    case LmBoneType.Shoulders:
                        return (Vector3)(spacePos[11] - spacePos[12]);

                    case LmBoneType.Spine:
                        Vector3 hc = (Vector3)spacePos[33];  // ((spacePos[23] + spacePos[24]) * 0.5f);
                        Vector3 shc = (Vector3)((spacePos[11] + spacePos[12]) * 0.5f);
                        return (shc - hc);

                    case LmBoneType.Nose:
                        shc = (Vector3)((spacePos[11] + spacePos[12]) * 0.5f);
                        return ((Vector3)spacePos[0] - shc);

                    case LmBoneType.Knees:
                        return (Vector3)(spacePos[25] - spacePos[26]);

                    case LmBoneType.Ankles:
                        return (Vector3)(spacePos[27] - spacePos[28]);

                    case LmBoneType.LFoot:
                        return (Vector3)(spacePos[32] - spacePos[28]);

                    case LmBoneType.RFoot:
                        return (Vector3)(spacePos[31] - spacePos[27]);

                    case LmBoneType.LLeg:
                        return (Vector3)(spacePos[26] - spacePos[24]);

                    case LmBoneType.RLeg:
                        return (Vector3)(spacePos[25] - spacePos[23]);

                    case LmBoneType.LArm:
                        return (Vector3)(spacePos[14] - spacePos[12]);

                    case LmBoneType.RArm:
                        return (Vector3)(spacePos[13] - spacePos[11]);
                }

                throw new Exception($"Unknown bone type: {boneType}");
            }

            // returns the lm-bone estimated from the joints array
            private Vector3 GetLmBone(LmBoneType boneType)
            {
                switch (boneType)
                {
                    case LmBoneType.Hips:
                        return (joints[23].spacePos - joints[24].spacePos);

                    case LmBoneType.Shoulders:
                        return (joints[11].spacePos - joints[12].spacePos);

                    case LmBoneType.Spine:
                        Vector3 hc = joints[33].spacePos;  // (joints[23].spacePos + joints[24].spacePos) * 0.5f;
                        Vector3 shc = (joints[11].spacePos + joints[12].spacePos) * 0.5f;
                        return (shc - hc);

                    case LmBoneType.Nose:
                        shc = (Vector3)((joints[11].spacePos + joints[12].spacePos) * 0.5f);
                        return ((Vector3)joints[0].spacePos - shc);

                    case LmBoneType.Knees:
                        return (joints[25].spacePos - joints[26].spacePos);

                    case LmBoneType.Ankles:
                        return (joints[27].spacePos - joints[28].spacePos);

                    case LmBoneType.LFoot:
                        return (joints[32].spacePos - joints[28].spacePos);

                    case LmBoneType.RFoot:
                        return (joints[31].spacePos - joints[27].spacePos);

                    case LmBoneType.LLeg:
                        return (joints[26].spacePos - joints[24].spacePos);

                    case LmBoneType.RLeg:
                        return (joints[25].spacePos - joints[23].spacePos);

                    case LmBoneType.LArm:
                        return (joints[14].spacePos - joints[12].spacePos);

                    case LmBoneType.RArm:
                        return (joints[13].spacePos - joints[11].spacePos);
                }

                throw new Exception($"Unknown bone type: {boneType}");
            }

            // pose landmarks to bone-type conversion
            private enum BoneType : int { None = 0, Face = 1, ArmLeft = 2, ArmRight = 4, LegLeft = 8, LegRight = 0x10, All = 0xFF }
            private static readonly BoneType[] PoseLm2BoneType =
            {
                /** 00 */ BoneType.Face,  // nose
                /** 01 */ BoneType.None,  // left eye inner
                /** 02 */ BoneType.Face,  // eye right
                /** 03 */ BoneType.None,  // left eye outer
                /** 04 */ BoneType.None,  // right eye inner
                /** 05 */ BoneType.Face,  // eye left
                /** 06 */ BoneType.None,  // right eye outer
                /** 07 */ BoneType.Face,  // ear right
                /** 08 */ BoneType.Face,  // ear left
                /** 09 */ BoneType.None,  // mouth left
                /** 10 */ BoneType.None,  // mouth right
                /** 11 */ BoneType.ArmRight,  // shoulder right
                /** 12 */ BoneType.ArmLeft,  // shoulder left
                /** 13 */ BoneType.ArmRight,  // elbow right
                /** 14 */ BoneType.ArmLeft,  // elbow left
                /** 15 */ BoneType.ArmRight,  // wrist right
                /** 16 */ BoneType.ArmLeft,  // wrist left
                /** 17 */ BoneType.None,  // left pinky
                /** 18 */ BoneType.None,  // right pinky
                /** 19 */ BoneType.ArmRight,  // handtip right
                /** 20 */ BoneType.ArmLeft,  // handtip left
                /** 21 */ BoneType.ArmRight,  // thumb Right
                /** 22 */ BoneType.ArmLeft,  // thumb left
                /** 23 */ BoneType.LegRight,  // hip right
                /** 24 */ BoneType.LegLeft,  // hip left
                /** 25 */ BoneType.LegRight,  // knee right
                /** 26 */ BoneType.LegLeft,  // knee left
                /** 27 */ BoneType.LegRight,  // ankle right
                /** 28 */ BoneType.LegLeft,  // ankle left
                /** 29 */ BoneType.None,  // left heel
                /** 30 */ BoneType.None,  // right heel
                /** 31 */ BoneType.LegRight,  // foot right
                /** 32 */ BoneType.LegLeft,  // foot left
                /** 33 */ BoneType.None,
                /** 34 */ BoneType.None,
                /** 35 */ BoneType.None,
                /** 36 */ BoneType.None,
                /** 37 */ BoneType.None,
                /** 38 */ BoneType.None,
                /** 39 */ BoneType.None
            };

        }


        // pose joints to joint types
        private int[] _poseJointTypes;

        // reference to KM
        private KinectManager _kinectManager;
        private bool _isDepthSynched = false;

        //// min user score of valid users
        //private float _minUserScore = 0.5f;


        public PoseLmUserTracker(int[] poseJointTypes, KinectManager kinectManager)
        {
            _poseJointTypes = poseJointTypes;

            _kinectManager = kinectManager;
            _isDepthSynched = _kinectManager.getDepthFrames != KinectManager.DepthTextureType.None && _kinectManager.syncDepthAndColor;

            //_minUserScore = minUserScore;
            //nextUserId = 1;
        }

        /// <summary>
        /// Releases the allocated resources.
        /// </summary>
        public void StopUserTracking()
        {
        }


        // calculates and returns joint pair depth
        private float GetPairDepth(int bodyIndex, int j1, int j2, Vector4[] jointImagePos, Vector4[] jointSpacePos, KinectInterop.CameraIntrinsics camIntr, float bodyDepth)
        {
            if (camIntr != null && jointImagePos[j2].w >= MIN_SCORE && jointImagePos[j1].w >= MIN_SCORE && jointSpacePos[j2].w >= MIN_SCORE && jointSpacePos[j1].w >= MIN_SCORE)
            {
                Vector2 pn2d = jointImagePos[j2] - jointImagePos[j1];
                Vector2 pn3d = jointSpacePos[j2] - jointSpacePos[j1];

                float pn3x = pn2d.x * camIntr.width / camIntr.fx;
                float pn3y = pn2d.y * camIntr.height / camIntr.fy;

                float depth = Mathf.Sqrt(pn3d.sqrMagnitude / (pn3x * pn3x + pn3y * pn3y));
                //Debug.Log($"    bi: {bodyIndex}, j1: {j1}, j2: {j2}, pn2d: {pn2d:F2}, pn3d: {pn3d:F2}\nfx: {camIntr.fx:F2}, fy: {camIntr.fy:F2}, w: {camIntr.width}, h: {camIntr.height}, dx: {pn3x:F2}, dy: {pn3y:F2}, d: {depth:F3}\nbDepth: {bodyDepth:F3}");

                return depth;
            }

            return bodyDepth;
        }

        private const float MIN_SCORE = 0.2f;
        private static readonly int[] BD_JINDEX1 = { 12, 11, 24, 23, 8,  7 };   // lsh, rsh, lh, rh, lear, rear
        private static readonly int[] BD_JINDEX2 = { 23, 24, 27, 28, 15, 16 };  // rh,  lh,  ra, la, rwri, lwri

        // calculates the body depth from 2d & 3d positions
        private float GetBodyDepth(int bodyIndex, Vector4[] jointImagePos, Vector4[] jointSpacePos, KinectInterop.CameraIntrinsics camIntr,
            UserTrackingBodyData body, float bodyDepth)
        {
            if(_isDepthSynched)
            {
                // return measured depth
                return bodyDepth;
            }

            float depthSum = 0f;
            int depthCnt = 0;

            int numPairs = BD_JINDEX1.Length;
            for (int j = 0; j < numPairs; j++)
            {
                int j1 = BD_JINDEX1[j];
                int j2 = BD_JINDEX2[j];

                float depth = GetPairDepth(bodyIndex, j1, j2, jointImagePos, jointSpacePos, camIntr, body != null ? body.bodyDepth : 0f);

                if(depth > 0f)
                {
                    depthSum += depth;
                    depthCnt++;
                }
            }

            float depthAvg = depthCnt > 0 ? depthSum / depthCnt : bodyDepth;
            //Debug.Log($"bi: {bodyIndex}, depth: {depthAvg:F3} dCount: {depthCnt}/{numPairs}, bDepth: {bodyDepth:F3}");

            return depthAvg;
        }

        //private string _savedIds = string.Empty;

        /// <summary>
        /// Updates user body with the latest body and joint data.
        /// </summary>
        /// <param name="bodyIndex">Body index</param>
        /// <param name="score">Body score</param>
        /// <param name="bodyImagePos">Body image position (uv)</param>
        /// <param name="bodyDepth">Body distance</param>
        /// <param name="lastTimestamp">Last body timestamp</param>
        /// <param name="jointImagePos">Joint image positions (uv)</param>
        /// <param name="jointKinectPos">Joint sensor positions</param>
        /// <param name="jointSpacePos">Joint space positions</param>
        public void UpdateUserBodyData(int bodyIndex, float bodyDepth, long lastTimestamp, KinectInterop.CameraIntrinsics camIntr, PoseRegion poseRegion, 
            Vector4[] jointImagePos, Vector4[] jointKinectPos, Vector4[] jointSpacePos, trackers.STrack detTrack, Dictionary<ulong, trackers.STrack> idToTrack)
        {
            //if (detTrack.State != trackers.TrackState.Tracked)
            //    return;
            if (detTrack.TrackId == 0 || !idToTrack.ContainsKey(detTrack.TrackId))
                return;

            trackers.STrack objTrack = idToTrack[detTrack.TrackId];
            UserTrackingBodyData body = (UserTrackingBodyData)objTrack._dataObj;

            //float lastTs = (float)(lastTimestamp * 0.0001f);
            bodyDepth = GetBodyDepth(bodyIndex, jointImagePos, jointSpacePos, camIntr, body, bodyDepth);
            //score = GetLmScoreReliability(score, jointImagePos, poseRegion);
            bool bAdded = false;

            if (body == null)
            {
                // add the body to the list
                body = new UserTrackingBodyData(bodyDepth, jointSpacePos.Length);
                //body.startTs = lastTs;

                body.userId = detTrack.TrackId;  // (ulong)posePar.z;  // nextUserId;
                bAdded = true;
            }

            // set the latest values
            float deltaTime = body.SetBodyData(bodyIndex, lastTimestamp, bodyDepth);  // , posePar.x);

            //string currentIds = string.Join(", ", idToTrack.Keys);
            //if(_savedIds != currentIds)
            //{
            //    Debug.Log($"ids: {currentIds}\nprev: {_savedIds}");
            //    _savedIds = currentIds;
            //}

            //if (body.trackState != detTrack.State || body.isActivated != detTrack.IsActivated)
            //{
            //    Debug.Log($"body {body.userId}/{detTrack.TrackId}, state: {body.trackState}/{detTrack.State}, act: {body.isActivated}/{detTrack.IsActivated}");
            //}

            //body.trackState = detTrack.State;
            //body.isActivated = detTrack.IsActivated;

            //if(body.updateTs == 0f && detTrack._isActivated)  // && score >= _minUserScore)
            //{
            //    body.updateTs = lastTs;
            //}

            // get smoothed positions
            Vector3 lpfParams = new Vector3(2f, 1.5f, deltaTime);

            // joints data
            body.SetJointsData(jointImagePos, jointKinectPos, jointSpacePos, lpfParams, _poseJointTypes);

            if (bAdded)
            {
                //Debug.Log($"  added body id: {body.userId}, score: {score:F3}/{body.userScore:F3}, ts: {lastTs}/{body.updateTs}\ndepth: {bodyDepth:F3}/{body.bodyDepth:F3}, uv: {bodyImagePos}/{body.imagePos}, poseBox: {poseBox}, ts: {body.timestamp}");
            }
            else
            {
                //Debug.Log($"  updated body id: {body.userId}, score: {score:F3}/{body.userScore:F3}, ts: {lastTs}/{body.updateTs}\ndepth: {bodyDepth:F3}/{body.bodyDepth:F3}, uv: {bodyImagePos}/{body.imagePos}, poseBox: {poseBox}, ts: {body.timestamp}");
            }

            objTrack._dataObj = body;
        }


        // maximum distance between lm image position and pose region position
        private const float MAX_2D_DIST = 0.05f;

        // checks the landmark score reliability
        public float GetLmScoreReliability(float score, Vector4[] jointImagePos, PoseRegion poseRegion)
        {
            if(poseRegion.par != Vector4.zero)
            {
                Vector2 hipsPos = jointImagePos[33];
                Vector2 neckPos = (jointImagePos[11] + jointImagePos[12]) * 0.5f;
                //Debug.LogWarning($"Dist dHips: ({hipsPos.x - poseRegion.par.x:F2}, {hipsPos.y - poseRegion.par.y:F2}), dNeck: ({neckPos.x - poseRegion.par.z:F2}, {neckPos.y - poseRegion.par.w:F2})\nhips: {hipsPos:F2}, neck: {neckPos:F2}, pose: {poseRegion.par:F2}");

                if (Mathf.Abs(hipsPos.x - poseRegion.par.x) > MAX_2D_DIST || Mathf.Abs(hipsPos.y - poseRegion.par.y) > MAX_2D_DIST ||
                    Mathf.Abs(neckPos.x - poseRegion.par.z) > MAX_2D_DIST || Mathf.Abs(neckPos.y - poseRegion.par.w) > MAX_2D_DIST)
                {
                    // lm-pose hips or neck don't match
                    //Debug.LogWarning($"Hips or neck don't match. User score set to 0.101\ndHips: ({hipsPos.x - poseRegion.par.x:F2}, {hipsPos.y - poseRegion.par.y:F2}), dNeck: ({neckPos.x - poseRegion.par.z:F2}, {neckPos.y - poseRegion.par.w:F2})");
                    return 0.101f;
                }
            }

            float pelSegm = jointImagePos[33].z;
            float lmOutPart = GetLmPartOutOfSilhouette(jointImagePos);

            if (pelSegm < 0 || lmOutPart > 0.5f)
            {
                // pose out of segm
                //Debug.LogWarning($"User score set to 0.102, prevScore: {score:F3}\npelSegm: {pelSegm:F3}, outPart: {lmOutPart:F3}");
                return 0.102f;
            }

            return score;
        }

        // gets the part of lm that are out of the silhouette
        private float GetLmPartOutOfSilhouette(Vector4[] jointImagePos)
        {
            int count = 0;
            int outSil = 0;

            for (uint j = 11; j < 33; j++)
            {
                if(jointImagePos[j].z < 0f)
                    outSil++;
                count++;
            }

            return (float)outSil / count;
        }

    }
}
