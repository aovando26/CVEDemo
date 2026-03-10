using com.rfilkov.components;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace com.rfilkov.kinect
{
    /// <summary>
    /// BackgroundRemovalByBodyBounds filters user silhouettes, according to the bounds determined by the positions of the body joints.
    /// </summary>
    public class BackgroundRemovalByBodyBounds : MonoBehaviour
    {
        [Tooltip("Offset from the lowest body joint to the floor.")]
        [Range(-0.1f, 0.5f)]
        public float bottomOffset = 0.05f;

        [Tooltip("Offset from the highest body joint to the top of the body.")]
        [Range(-0.1f, 0.5f)]
        public float topOffset = 0.2f;

        [Tooltip("Offset from the leftmost body joint to the left end of the body.")]
        [Range(-0.1f, 0.5f)]
        public float leftOffset = 0.2f;

        [Tooltip("Offset from the rightmost body joint to the right end of the body.")]
        [Range(-0.1f, 0.5f)]
        public float rightOffset = 0.2f;

        [Tooltip("Offset from the frontmost body joint to the front end of the body.")]
        [Range(-0.1f, 0.5f)]
        public float frontOffset = 0.2f;

        [Tooltip("Offset from the backmost body joint to the back end of the body.")]
        [Range(-0.1f, 0.5f)]
        public float backOffset = 0.2f;


        // foreground filter shader
        private ComputeShader foregroundFilterShader = null;
        private int foregroundFilterKernel = -1;

        //private Vector4[] foregroundFilterPos = null;
        private Vector4[] bodyPosMin = null;
        private Vector4[] bodyPosMaxX = null;
        private Vector4[] bodyPosMaxY = null;
        private Vector4[] bodyPosMaxZ = null;
        private Vector4[] bodyPosDot = null;


        // initializes background removal with shaders
        public bool InitBackgroundRemoval(KinectInterop.SensorData sensorData, int maxBodyCount)
        {
            foregroundFilterShader = Resources.Load("ForegroundFiltBodyShader") as ComputeShader;
            foregroundFilterKernel = foregroundFilterShader != null ? foregroundFilterShader.FindKernel("FgFiltBody") : -1;

            bodyPosMin = new Vector4[maxBodyCount];
            bodyPosMaxX = new Vector4[maxBodyCount];
            bodyPosMaxY = new Vector4[maxBodyCount];
            bodyPosMaxZ = new Vector4[maxBodyCount];
            bodyPosDot = new Vector4[maxBodyCount];

            return true;
        }


        // releases background removal shader resources
        public void FinishBackgroundRemoval(KinectInterop.SensorData sensorData)
        {
            if (foregroundFilterShader != null)
            {
                foregroundFilterShader = null;
            }

            bodyPosMin = null;
            bodyPosMaxX = null;
            bodyPosMaxY = null;
            bodyPosMaxZ = null;
            bodyPosDot = null;
        }


        /// <summary>
        /// Applies foreground filter by body bounds.
        /// </summary>
        public void ApplyForegroundFilterByBody(Texture vertexTexture, RenderTexture alphaTexture, int playerIndex, int sensorIndex, int maxBodyCount,
            Matrix4x4 matKinectToWorld, KinectManager kinectManager, Camera foregroundCamera)
        {
            //Matrix4x4 matWorldToKinect = matKinectToWorld.inverse;
            if (kinectManager != null && kinectManager.userManager != null)
            {
                List<ulong> alUserIds = null;

                if (playerIndex < 0)
                {
                    alUserIds = kinectManager.userManager.alUserIds;
                }
                else
                {
                    alUserIds = new List<ulong>();

                    ulong userId = kinectManager.GetUserIdByIndex(playerIndex);
                    if (userId != 0)
                        alUserIds.Add(userId);
                }

                int uCount = Mathf.Min(alUserIds.Count, maxBodyCount);
                foregroundFilterShader.SetInt("_NumBodies", uCount);

                //if (uCount > 0)
                //{
                //    Debug.Log("playerIndex: " + playerIndex + ", uCount: " + uCount + ", userId: " + (uCount > 0 ? alUserIds[0] : 0));
                //}

                // get the background rectangle (use the portrait background, if available)
                Rect backgroundRect = foregroundCamera.pixelRect;
                PortraitBackground portraitBack = PortraitBackground.Instance;

                if (portraitBack && portraitBack.enabled)
                {
                    backgroundRect = portraitBack.GetBackgroundRect();
                }

                int jCount = kinectManager.GetJointCount();
                for (int i = 0; i < uCount; i++)
                {
                    ulong userId = alUserIds[i];

                    bool bSuccess = kinectManager.GetUserBoundingBox(userId, /**foregroundCamera*/ null, sensorIndex, backgroundRect,
                        out Vector3 pMin, out Vector3 pMax);
                    //Debug.Log($"pMin: {pMin:F2}, pMax: {pMax:F2}, success: {bSuccess}");

                    if (bSuccess)
                    {
                        Vector3 posMin = new Vector3(pMin.x - leftOffset, pMin.y - bottomOffset, pMin.z - frontOffset);
                        Vector3 posMaxX = new Vector3(pMax.x + rightOffset, posMin.y, posMin.z);
                        Vector3 posMaxY = new Vector3(posMin.x, pMax.y + topOffset, posMin.z);
                        Vector3 posMaxZ = new Vector3(posMin.x, posMin.y, pMax.z + backOffset + 1f);  // add 1m behind user, because of the depth imprecision

                        bodyPosMin[i] = posMin;  // matWorldToKinect.MultiplyPoint3x4(posMin);
                        bodyPosMaxX[i] = posMaxX - posMin;  // matWorldToKinect.MultiplyPoint3x4(posMaxX) - (Vector3)bodyPosMin[i];
                        bodyPosMaxY[i] = posMaxY - posMin;  // matWorldToKinect.MultiplyPoint3x4(posMaxY) - (Vector3)bodyPosMin[i];
                        bodyPosMaxZ[i] = posMaxZ - posMin;  // matWorldToKinect.MultiplyPoint3x4(posMaxZ) - (Vector3)bodyPosMin[i];

                        bodyPosDot[i] = new Vector3(Vector3.Dot(bodyPosMaxX[i], bodyPosMaxX[i]), Vector3.Dot(bodyPosMaxY[i], bodyPosMaxY[i]), Vector3.Dot(bodyPosMaxZ[i], bodyPosMaxZ[i]));
                        //Debug.Log($"pMin: {(Vector3)bodyPosMin[i]}, pMaxX: {(Vector3)bodyPosMaxX[i]}\npMaxY: {(Vector3)bodyPosMaxY[i]}, pMaxZ: {(Vector3)bodyPosMaxZ[i]}, pDot: {(Vector3)bodyPosDot[i]}");
                    }

                    //string sMessage2 = string.Format("Xmin: {0:F1}; Xmax: {1:F1}", bodyPosMin[i].x, bodyPosMaxX[i].x);
                    //Debug.Log(sMessage2);
                }
            }

            foregroundFilterShader.SetMatrix("_Transform", matKinectToWorld);
            foregroundFilterShader.SetVectorArray("_BodyPosMin", bodyPosMin);
            foregroundFilterShader.SetVectorArray("_BodyPosMaxX", bodyPosMaxX);
            foregroundFilterShader.SetVectorArray("_BodyPosMaxY", bodyPosMaxY);
            foregroundFilterShader.SetVectorArray("_BodyPosMaxZ", bodyPosMaxZ);
            foregroundFilterShader.SetVectorArray("_BodyPosDot", bodyPosDot);

            foregroundFilterShader.SetTexture(foregroundFilterKernel, "_VertexTex", vertexTexture);
            foregroundFilterShader.SetTexture(foregroundFilterKernel, "_AlphaTex", alphaTexture);
            foregroundFilterShader.Dispatch(foregroundFilterKernel, vertexTexture.width / 8, vertexTexture.height / 8, 1);
        }

    }
}
