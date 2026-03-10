using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using com.rfilkov.kinect;


namespace com.rfilkov.providers
{
    /// <summary>
    /// YoloObjectTracker tracks object data consistently across frames.
    /// </summary>
    public class YoloObjectTracker
    {

        public class UserTrackingJointData
        {
            public float jointScore;
            public Vector2 imagePosRaw;
            public Vector3 spacePosRaw;

            public Vector2 imagePos;
            public Vector3 spacePos;
            public Vector3 smoothPos;


            // lp filter
            public Vector3[] prevSpacePos = new Vector3[6];

            // kalman filter
            public Vector3 posK;
            public Vector3 posP;
            public Vector3 posX;


            public UserTrackingJointData(float jointScore, Vector2 imagePos, Vector3 spacePos, Vector3 smoothPos)
            {
                this.jointScore = jointScore;

                this.imagePos = imagePosRaw = imagePos;
                this.spacePos = spacePosRaw = spacePos;
                this.smoothPos = smoothPos;
            }

            public override string ToString()
            {
                return $"score: {jointScore:F3}, uv: {imagePos:F2}, pos: {spacePos:F2}, smooth: {smoothPos:F2}";
            }
        }


        // object tracking data (for internal use)
        public class ObjTrackingData
        {
            public ulong objId;
            public int objIndex;
            public KinectInterop.ObjectType objType;
            public float objScore;

            public long timestamp;
            public long prevTimestamp;

            //public float regionTs;
            public float updateTs;
            public float startTs;

            public float objDepthRaw;
            public float objDepth;
            //public float objDepthDX;

            private float objDepthX;
            private float objDepthK;
            private float objDepthP;

            public Vector2 lastImagePos;
            public Vector2 objImagePos;
            public Vector2 imagePosDX;

            public Vector2 lastImageSize;
            public Vector2 objImageSize;
            public Vector2 imageSizeDX;

            public float lastObjScore;
            public float objScoreDX;


            // K-params
            private const float KParamQ = 0.001f;
            private const float KParamR = 0.0015f;


            public ObjTrackingData(Vector2 objImagePos, Vector2 objImageSize, float objDepth, float objScore)
            {
                // object image position & size
                this.objImagePos = lastImagePos = objImagePos;
                this.objImageSize = lastImageSize = objImageSize;

                // depth
                this.objDepth = objDepthRaw = objDepth;
                //depthX = bodyDepth;

                // score
                this.objScore = lastObjScore = objScore;
            }

            public override string ToString()
            {
                return $"id: {objId}, oi: {objIndex}, score: {objScore:F3}, uv: {objImagePos:F2}, rawUv: {lastImagePos:F2}\ndepth: {objDepth:F3}, raw: {objDepthRaw:F3}";
            }


            public float Prob
            {
                get => objScore;
            }


            /// <summary>
            /// Sets the latest object parameters.
            /// </summary>
            /// <param name="objIndex">Object index</param>
            /// <param name="lastTimestamp">Last timestamp</param>
            /// <param name="objImagePos">Object position</param>
            /// <param name="objImageSize">Object size</param>
            /// <param name="objScore">Confidence score</param>
            public void SetObjectData(int objIndex, int iObjType, long lastTimestamp, Vector2 objImagePos, Vector2 objImageSize, float objScore, float depth)
            {
                // update body-index and timestamp
                this.objIndex = objIndex;
                this.objType = (KinectInterop.ObjectType)iObjType;

                this.prevTimestamp = timestamp;
                this.timestamp = lastTimestamp;
                float deltaTime = (timestamp - prevTimestamp) * 0.0000001f;

                // object image pos
                Vector3 imagePosParams = new Vector3(2f, 1.5f, deltaTime);
                this.imagePosDX = KinectInterop.OneEuroStepDX(objImagePos, this.objImagePos, imagePosDX, imagePosParams);
                this.objImagePos = KinectInterop.OneEuroStepX(objImagePos, this.objImagePos, imagePosDX, imagePosParams);
                this.lastImagePos = objImagePos;
                //Debug.Log($"    objPos: {objImagePos:F2}, lastImagePos: {lastImagePos:F2}\nimagePosDX: {imagePosDX:F2}, params: {imagePosParams:F3}");

                // object image size
                Vector3 imageSizeParams = imagePosParams;
                this.imageSizeDX = KinectInterop.OneEuroStepDX(objImageSize, this.objImageSize, imageSizeDX, imageSizeParams);
                this.objImageSize = KinectInterop.OneEuroStepX(objImageSize, this.objImageSize, imageSizeDX, imageSizeParams);
                this.lastImageSize = objImageSize;
                //Debug.Log($"    objSize: {objImageSize:F2}, lastImageSize: {lastImageSize:F2}\nimagePosDX: {imageSizeDX:F2}, params: {imageSizeParams:F3}");

                // confidence score
                Vector3 userScoreParams = imagePosParams;
                this.objScoreDX = KinectInterop.OneEuroStepDX(objScore, this.objScore, objScoreDX, userScoreParams);
                this.objScore = KinectInterop.OneEuroStepX(objScore, this.objScore, objScoreDX, userScoreParams);
                this.lastObjScore = objScore;

                // object depth
                if (depth > 0f)
                {
                    if (Mathf.Abs(depth - objDepthRaw) <= 0.5f)
                    {
                        //Vector3 objDepthParams = imagePosParams;
                        //this.objDepthDX = KinectInterop.OneEuroStepDX(depth, objDepth, objDepthDX, objDepthParams);
                        //this.objDepth = KinectInterop.OneEuroStepX(depth, objDepth, objDepthDX, objDepthParams);

                        this.objDepthRaw = depth;
                        objDepthK = (objDepthP + KParamQ) / (objDepthP + KParamQ + KParamR);
                        objDepthP = KParamR * (objDepthP + KParamQ) / (KParamR + objDepthP + KParamQ);
                        objDepth = objDepthX = objDepthX + (depth - objDepthX) * objDepthK;
                    }
                    else
                    {
                        this.objDepthRaw = Mathf.Lerp(objDepthRaw, depth, 5f * deltaTime);
                    }
                }

                //Debug.Log($"  oi: {objIndex}, rawDepth: {objDepthRaw:F3}, objDepth: {objDepth:F3}\nlastScore: {objScore:F3}, userScore: {this.objScore:F3}");
            }

        }


        public YoloObjectTracker(float minObjScore)
        {
            //_minObjScore = minObjScore;
        }

        /// <summary>
        /// Releases the allocated resources.
        /// </summary>
        public void StopObjectTracking()
        {
        }


        /// <summary>
        /// Updates user body with the latest body and joint data.
        /// </summary>
        /// <param name="objIndex">Object index</param>
        /// <param name="objScore">Confidence score</param>
        /// <param name="objImagePos">Object position (uv)</param>
        /// <param name="objImageSize">Object size</param>
        /// <param name="objDepth">Object depth</param>
        /// <param name="lastTimestamp">Last body timestamp</param>
        public void UpdateObjectData(int objIndex, trackers.STrack detTrack, ref ObjectDetection objDet, float objDepth, long lastTimestamp, Dictionary<ulong, trackers.STrack> idToTrack)
        {
            //if (detTrack.State != trackers.TrackState.Tracked)
            //    return;
            if (detTrack.TrackId == 0 || !idToTrack.ContainsKey(detTrack.TrackId))
                return;

            trackers.STrack objTrack = idToTrack[detTrack.TrackId];
            ObjTrackingData obj = (ObjTrackingData)objTrack._dataObj;

            float lastTs = (float)(lastTimestamp * 0.0001f);
            bool bAdded = false;

            if (obj == null)
            {
                // add object to the list
                obj = new ObjTrackingData(objDet.center, objDet.size, objDepth, objDet.score);
                obj.startTs = lastTs;

                obj.objId = detTrack.TrackId;
                bAdded = true;
            }

            // set the latest values
            obj.SetObjectData(objIndex, objDet.objType, lastTimestamp, objDet.center, objDet.size, objDet.score, objDepth);

            if(obj.updateTs == 0f && detTrack._isActivated)
            {
                obj.updateTs = lastTs;
            }

            if (bAdded)
            {
                //Debug.Log($"  added object {obj.objIndex} '{obj.objType}' - id: {obj.objId}, score: {objDet.score:F3}/{obj.objScore:F3}, ts: {lastTs}/{obj.updateTs}\ndepth: {objDepth:F3}/{obj.objDepth:F3}, uv: {objDet.center}/{obj.objImagePos}, ts: {obj.timestamp}");
            }
            else
            {
                //Debug.Log($"  updated object {obj.objIndex} '{obj.objType}' - id: {obj.objId}, score: {objDet.score:F3}/{obj.objScore:F3}, ts: {lastTs}/{obj.updateTs}\ndepth: {objDepth:F3}/{obj.objDepth:F3}, uv: {objDet.center}/{obj.objImagePos}, ts: {obj.timestamp}");
            }

            objTrack._dataObj = obj;
        }

    }
}
