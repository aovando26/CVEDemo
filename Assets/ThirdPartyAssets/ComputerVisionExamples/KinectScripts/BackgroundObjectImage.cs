using UnityEngine;
using System.Collections;
using com.rfilkov.kinect;
using System;

namespace com.rfilkov.components
{
    /// <summary>
    /// Background object image is component that displays the object image on RawImage texture, usually the scene background.
    /// </summary>
    public class BackgroundObjectImage : MonoBehaviour
    {
        [Tooltip("Depth sensor index - 0 is the 1st one, 1 - the 2nd one, etc.")]
        public int sensorIndex = 0;

        [Tooltip("Index of the object, tracked by this component. -1 means all objects, 0 - the 1st object, 1 - the 2nd one, 2 - the 3rd one, etc.")]
        public int objectIndex = -1;

        [Tooltip("RawImage used to display the user-body image.")]
        public UnityEngine.UI.RawImage backgroundImage;

        [Tooltip("RenderTexture to render the image.")]
        public RenderTexture backgroundTexture;

        [Tooltip("Camera used to display the background image. Set it, if you'd like to allow background image to resize, to match the depth image's aspect ratio.")]
        public Camera backgroundCamera;

        [Tooltip("Opaqueness factor of the raw-image.")]
        [Range(0f, 1f)]
        public float opaqueness = 1.0f;


        // last camera rect width & height
        private float lastCamRectW = 0;
        private float lastCamRectH = 0;

        // references
        private KinectManager kinectManager = null;
        private KinectInterop.SensorData sensorData = null;
        private Vector2 initialAnchorPos = Vector2.zero;

        // color-camera aligned frames
        //private ulong lastDepthFrameTime = 0;
        private ulong lastObjectIndexFrameTime = 0;

        // color-camera aligned texture and buffers
        private RenderTexture objectImageTexture = null;
        private Material objImageMaterial = null;

        private ComputeBuffer objectIndexBuffer = null;
        //private ComputeBuffer depthImageBuffer = null;
        //private ComputeBuffer objectHistBuffer = null;

        //// body image hist data
        //protected int[] depthBodyBufferData = null;
        //protected int[] equalBodyBufferData = null;
        //protected int bodyHistTotalPoints = 0;


        void Start()
        {
            if (backgroundImage == null)
            {
                backgroundImage = GetComponent<UnityEngine.UI.RawImage>();
            }

            kinectManager = KinectManager.Instance;
            sensorData = kinectManager != null ? kinectManager.GetSensorData(sensorIndex) : null;

            if (sensorData != null)
            {
                // create the user texture and needed buffers
                //bodyImageTexture = KinectInterop.CreateRenderTexture(bodyImageTexture, sensorData.depthImageWidth, sensorData.depthImageHeight);
                objImageMaterial = new Material(Shader.Find("Kinect/UserBodyImageShader"));  // UserHistImageShader

                //objectHistBuffer = KinectInterop.CreateComputeBuffer(objectHistBuffer, DepthSensorBase.MAX_DEPTH_DISTANCE_MM + 1, sizeof(int));

                //depthBodyBufferData = new int[DepthSensorBase.MAX_DEPTH_DISTANCE_MM + 1];
                //equalBodyBufferData = new int[DepthSensorBase.MAX_DEPTH_DISTANCE_MM + 1];
            }
        }


        void OnDestroy()
        {
            if (objectImageTexture)
            {
                objectImageTexture.Release();
                objectImageTexture = null;
            }

            if (objectIndexBuffer != null)
            {
                objectIndexBuffer.Dispose();
                objectIndexBuffer = null;
            }

            //if (depthImageBuffer != null)
            //{
            //    depthImageBuffer.Dispose();
            //    depthImageBuffer = null;
            //}

            //if (objectHistBuffer != null)
            //{
            //    objectHistBuffer.Dispose();
            //    objectHistBuffer = null;
            //}
        }


        void Update()
        {
            if (kinectManager && kinectManager.IsInitialized() && sensorData != null)
            {
                float cameraWidth = backgroundCamera ? backgroundCamera.pixelRect.width : 0f;
                float cameraHeight = backgroundCamera ? backgroundCamera.pixelRect.height : 0f;

                // check for new depth and body-index frames
                UpdateTextureWithNewFrame();

                //Texture bodyImageTexture = kinectManager.GetUsersImageTex(sensorIndex);
                if (backgroundImage && objectImageTexture != null && (backgroundImage.texture == null ||
                    backgroundImage.texture.width != objectImageTexture.width || backgroundImage.texture.height != objectImageTexture.height ||
                    lastCamRectW != cameraWidth || lastCamRectH != cameraHeight))
                {
                    lastCamRectW = cameraWidth;
                    lastCamRectH = cameraHeight;

                    backgroundImage.texture = objectImageTexture;
                    backgroundImage.rectTransform.localScale = sensorData.depthImageScale;  // kinectManager.GetDepthImageScale(sensorIndex);
                    backgroundImage.color = new Color(1f, 1f, 1f, opaqueness);  // Color.white;

                    if (backgroundCamera != null)
                    {
                        // adjust image's size and position to match the stream aspect ratio
                        int depthImageWidth = sensorData.depthImageWidth;  // kinectManager.GetDepthImageWidth(sensorIndex);
                        int depthImageHeight = sensorData.depthImageHeight;  // kinectManager.GetDepthImageHeight(sensorIndex);
                        if (depthImageWidth == 0 || depthImageHeight == 0)
                            return;

                        RectTransform rectImage = backgroundImage.rectTransform;
                        float rectWidth = (rectImage.anchorMin.x != rectImage.anchorMax.x) ? cameraWidth * (rectImage.anchorMax.x - rectImage.anchorMin.x) : rectImage.sizeDelta.x;
                        float rectHeight = (rectImage.anchorMin.y != rectImage.anchorMax.y) ? cameraHeight * (rectImage.anchorMax.y - rectImage.anchorMin.y) : rectImage.sizeDelta.y;

                        if (depthImageWidth >= depthImageHeight)
                            rectWidth = rectHeight * depthImageWidth / depthImageHeight;
                        else
                            rectHeight = rectWidth * depthImageHeight / depthImageWidth;

                        Vector2 pivotOffset = (rectImage.pivot - new Vector2(0.5f, 0.5f)) * 2f;
                        Vector2 imageScale = sensorData.depthImageScale;  // (Vector2)kinectManager.GetDepthImageScale(sensorIndex);
                        Vector2 anchorPos = rectImage.anchoredPosition + pivotOffset * imageScale * new Vector2(rectWidth, rectHeight);

                        if (rectImage.anchorMin.x != rectImage.anchorMax.x)
                        {
                            rectWidth = -(cameraWidth - rectWidth);
                        }

                        if (rectImage.anchorMin.y != rectImage.anchorMax.y)
                        {
                            rectHeight = -(cameraHeight - rectHeight);
                        }

                        rectImage.sizeDelta = new Vector2(rectWidth, rectHeight);
                        rectImage.anchoredPosition = initialAnchorPos = anchorPos;
                    }
                }

                //if (backgroundImage)
                //{
                //    // update the anchor position, if needed
                //    if (sensorData != null && sensorData.sensorInterface != null)
                //    {
                //        Vector2 updatedAnchorPos = initialAnchorPos + sensorData.sensorInterface.GetBackgroundImageAnchorPos(sensorData);
                //        if (backgroundImage.rectTransform.anchoredPosition != updatedAnchorPos)
                //        {
                //            backgroundImage.rectTransform.anchoredPosition = updatedAnchorPos;
                //        }
                //    }
                //}

                if (objectImageTexture != null && backgroundTexture != null)
                {
                    Vector2 imageScale = sensorData.depthImageScale;
                    KinectInterop.TransformTexture(objectImageTexture, backgroundTexture, 0, imageScale.x < 0f, imageScale.y < 0f, backgroundCamera == null);
                }

            }
            else
            {
                // reset the background texture, if needed
                if (backgroundImage && backgroundImage.texture != null)
                {
                    backgroundImage.texture = null;
                }
            }

            //RectTransform rectTransform = backgroundImage.rectTransform;
            //Debug.Log("pivot: " + rectTransform.pivot + ", anchorPos: " + rectTransform.anchoredPosition + ", \nanchorMin: " + rectTransform.anchorMin + ", anchorMax: " + rectTransform.anchorMax);
        }


        // checks for new color-camera aligned frames, and composes an updated object-index texture, if needed
        private void UpdateTextureWithNewFrame()
        {
            if (sensorData == null || sensorData.sensorInterface == null || sensorData.objectIndexImage == null || sensorData.depthImage == null)
                return;
            if (sensorData.depthImageWidth == 0 || sensorData.depthImageHeight == 0 || sensorData.lastDepthFrameTime == 0 || sensorData.lastObjectIndexFrameTime == 0)
                return;

            // get object-index frame
            if (/**lastDepthFrameTime != sensorData.lastDepthFrameTime ||*/ lastObjectIndexFrameTime != sensorData.lastObjectIndexFrameTime)
            {
                //lastDepthFrameTime = sensorData.lastDepthFrameTime;
                lastObjectIndexFrameTime = sensorData.lastObjectIndexFrameTime;

                if (objectImageTexture == null || objectImageTexture.width != sensorData.depthImageWidth || objectImageTexture.height != sensorData.depthImageHeight)
                {
                    objectImageTexture = KinectInterop.CreateRenderTexture(objectImageTexture, sensorData.depthImageWidth, sensorData.depthImageHeight);
                }

                //// get configured min & max distances 
                //float minDistance = ((DepthSensorBase)sensorData.sensorInterface).minDepthDistance;
                //float maxDistance = ((DepthSensorBase)sensorData.sensorInterface).maxDepthDistance;

                //int depthMinDistance = (int)(minDistance * 1000f);
                //int depthMaxDistance = (int)(maxDistance * 1000f);

                //Array.Clear(depthBodyBufferData, 0, depthBodyBufferData.Length);
                //Array.Clear(equalBodyBufferData, 0, equalBodyBufferData.Length);
                //bodyHistTotalPoints = 0;

                //int frameLen = sensorData.depthImage.Length;
                //for (int i = 0; i < frameLen; i++)
                //{
                //    int depth = sensorData.depthImage[i];
                //    int limDepth = (depth >= depthMinDistance && depth <= depthMaxDistance) ? depth : 0;

                //    if (/**rawBodyIndexImage[i] != 255 &&*/ limDepth > 0)
                //    {
                //        depthBodyBufferData[limDepth]++;
                //        bodyHistTotalPoints++;
                //    }
                //}

                //if (bodyHistTotalPoints > 0)
                //{
                //    equalBodyBufferData[0] = depthBodyBufferData[0];
                //    for (int i = 1; i < depthBodyBufferData.Length; i++)
                //    {
                //        equalBodyBufferData[i] = equalBodyBufferData[i - 1] + depthBodyBufferData[i];
                //    }
                //}

                int objectIndexBufferLength = sensorData.objectIndexImage.Length >> 2;
                if (objectIndexBuffer == null || objectIndexBuffer.count != objectIndexBufferLength)
                {
                    objectIndexBuffer = KinectInterop.CreateComputeBuffer(objectIndexBuffer, objectIndexBufferLength, sizeof(uint));
                }

                KinectInterop.SetComputeBufferData(objectIndexBuffer, sensorData.objectIndexImage, objectIndexBufferLength, sizeof(uint));

                //int depthBufferLength = sensorData.depthImage.Length >> 1;
                //if (depthImageBuffer == null || depthImageBuffer.count != depthBufferLength)
                //{
                //    depthImageBuffer = KinectInterop.CreateComputeBuffer(depthImageBuffer, depthBufferLength, sizeof(uint));
                //}

                //KinectInterop.SetComputeBufferData(depthImageBuffer, sensorData.depthImage, depthBufferLength, sizeof(uint));

                //if (bodyHistBuffer != null)
                //{
                //    KinectInterop.SetComputeBufferData(bodyHistBuffer, equalBodyBufferData, equalBodyBufferData.Length, sizeof(int));
                //}

                //float minDist = minDistance;  // kinectManager.minUserDistance != 0f ? kinectManager.minUserDistance : minDistance;
                //float maxDist = maxDistance;  // kinectManager.maxUserDistance != 0f ? kinectManager.maxUserDistance : maxDistance;

                objImageMaterial.SetInt("_TexResX", sensorData.depthImageWidth);
                objImageMaterial.SetInt("_TexResY", sensorData.depthImageHeight);
                //objImageMaterial.SetInt("_MinDepth", (int)(minDist * 1000f));
                //objImageMaterial.SetInt("_MaxDepth", (int)(maxDist * 1000f));

                objImageMaterial.SetBuffer("_BodyIndexMap", objectIndexBuffer);
                //objImageMaterial.SetBuffer("_DepthMap", depthImageBuffer);
                //objImageMaterial.SetBuffer("_HistMap", objectHistBuffer);
                //bodyImageMaterial.SetInt("_TotalPoints", bodyHistTotalPoints);

                Color[] objIndexColors = kinectManager.GetObjectIndexColors();
                if (objectIndex >= 0)
                {
                    ulong objId = kinectManager.GetObjectIdByIndex(objectIndex);
                    int objIndex = kinectManager.GetTrackingIndexByObjectId(objId);

                    int numObjIndices = objIndexColors.Length;
                    Color clrNone = new Color(0f, 0f, 0f, 0f);

                    for (int i = 0; i < numObjIndices; i++)
                    {
                        if (i != objIndex)
                            objIndexColors[i] = clrNone;
                    }
                }

                objImageMaterial.SetColorArray("_BodyIndexColors", objIndexColors);

                Graphics.Blit(null, objectImageTexture, objImageMaterial);
            }
        }

    }
}

