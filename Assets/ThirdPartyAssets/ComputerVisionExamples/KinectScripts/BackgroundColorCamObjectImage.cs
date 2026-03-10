using UnityEngine;
using System.Collections;
using com.rfilkov.kinect;
using System;

namespace com.rfilkov.components
{
    /// <summary>
    /// BackgroundColorCamObjectImage is component that displays the color camera aligned object-index image on RawImage texture, usually the scene background.
    /// </summary>
    public class BackgroundColorCamObjectImage : MonoBehaviour
    {
        [Tooltip("Depth sensor index - 0 is the 1st one, 1 - the 2nd one, etc.")]
        public int sensorIndex = 0;

        [Tooltip("Index of the object, tracked by this component. -1 means all objects, 0 - the 1st object, 1 - the 2nd one, 2 - the 3rd one, etc.")]
        public int objectIndex = -1;

        [Tooltip("RawImage used to display the color camera feed.")]
        public UnityEngine.UI.RawImage backgroundImage;

        [Tooltip("RenderTexture to render the image.")]
        public RenderTexture backgroundTexture;

        [Tooltip("Camera used to display the background image. Set it, if you'd like to allow background image to resize, to match the color image's aspect ratio.")]
        public Camera backgroundCamera;

        [Tooltip("Opaqueness factor of the raw-image.")]
        [Range(0f, 1f)]
        public float opaqueness = 1.0f;


        // last camera rect width & height
        private float lastCamRectW = 0;
        private float lastCamRectH = 0;

        // reference to the kinectManager
        private KinectManager kinectManager = null;
        private KinectInterop.SensorData sensorData = null;
        private Vector2 initialAnchorPos = Vector2.zero;

        // color-camera aligned frames
        //private ulong lastColorCamDepthFrameTime = 0;
        private ulong lastColorCamObjectIndexFrameTime = 0;

        // color-camera aligned texture and buffers
        private RenderTexture objectImageTexture = null;
        private Material objectImageMaterial = null;

        //private Texture2D objectIndexBuffer = null;
        private ComputeBuffer objectIndexBuffer = null;
        //private ComputeBuffer depthImageBuffer = null;
        //private ComputeBuffer objectHistBuffer = null;

        //// body image hist data
        //protected int[] depthObjBufferData = null;
        //protected int[] equalObjBufferData = null;
        //protected int objHistTotalPoints = 0;


        void Start()
        {
            if (backgroundImage == null)
            {
                backgroundImage = GetComponent<UnityEngine.UI.RawImage>();
            }

            kinectManager = KinectManager.Instance;
            sensorData = kinectManager != null ? kinectManager.GetSensorData(sensorIndex) : null;

            if(sensorData != null)
            {
                // enable color camera aligned depth & object-index frames 
                //sensorData.sensorInterface.EnableColorCameraDepthFrame(sensorData, true);
                sensorData.sensorInterface.EnableColorCameraObjectIndexFrame(sensorData, true);

                // create the object-index texture and needed buffers
                objectImageMaterial = new Material(Shader.Find("Kinect/UserBodyImageShader"));  // UserHistImageShader

                //objectHistBuffer = KinectInterop.CreateComputeBuffer(objectHistBuffer, DepthSensorBase.MAX_DEPTH_DISTANCE_MM + 1, sizeof(int));

                //depthObjBufferData = new int[DepthSensorBase.MAX_DEPTH_DISTANCE_MM + 1];
                //equalObjBufferData = new int[DepthSensorBase.MAX_DEPTH_DISTANCE_MM + 1];
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
                //KinectInterop.Destroy(objectIndexBuffer);
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

            if (sensorData != null)
            {
                // disable color camera aligned depth & object-index frames 
                //sensorData.sensorInterface.EnableColorCameraDepthFrame(sensorData, false);
                sensorData.sensorInterface.EnableColorCameraObjectIndexFrame(sensorData, false);
            }
        }


        void Update()
        {
            if (kinectManager && kinectManager.IsInitialized())
            {
                float cameraWidth = backgroundCamera ? backgroundCamera.pixelRect.width : 0f;
                float cameraHeight = backgroundCamera ? backgroundCamera.pixelRect.height : 0f;

                // check for new color camera aligned frames
                UpdateTextureWithNewFrame();

                if (backgroundImage && objectImageTexture != null && (backgroundImage.texture == null || 
                    backgroundImage.texture.width != objectImageTexture.width || backgroundImage.texture.height != objectImageTexture.height ||
                    lastCamRectW != cameraWidth || lastCamRectH != cameraHeight))
                {
                    lastCamRectW = cameraWidth;
                    lastCamRectH = cameraHeight;

                    // enable color camera aligned depth & object-index frames 
                    sensorData = kinectManager.GetSensorData(sensorIndex);  // sensor data may be re-created after sensor-int restart
                    //sensorData.sensorInterface.EnableColorCameraDepthFrame(sensorData, true);
                    sensorData.sensorInterface.EnableColorCameraObjectIndexFrame(sensorData, true);

                    backgroundImage.texture = objectImageTexture;
                    backgroundImage.rectTransform.localScale = sensorData.colorImageScale;
                    backgroundImage.color = new Color(1f, 1f, 1f, opaqueness);  // Color.white;

                    if (backgroundCamera != null)
                    {
                        // adjust image's size and position to match the stream aspect ratio
                        int colorImageWidth = sensorData.colorImageWidth;
                        int colorImageHeight = sensorData.colorImageHeight;
                        if (colorImageWidth == 0 || colorImageHeight == 0)
                            return;

                        RectTransform rectImage = backgroundImage.rectTransform;
                        float rectWidth = (rectImage.anchorMin.x != rectImage.anchorMax.x) ? cameraWidth * (rectImage.anchorMax.x - rectImage.anchorMin.x) : rectImage.sizeDelta.x;
                        float rectHeight = (rectImage.anchorMin.y != rectImage.anchorMax.y) ? cameraHeight * (rectImage.anchorMax.y - rectImage.anchorMin.y) : rectImage.sizeDelta.y;

                        if (colorImageWidth >= colorImageHeight)
                            rectWidth = rectHeight * colorImageWidth / colorImageHeight;
                        else
                            rectHeight = rectWidth * colorImageHeight / colorImageWidth;

                        Vector2 pivotOffset = (rectImage.pivot - new Vector2(0.5f, 0.5f)) * 2f;
                        Vector2 imageScale = sensorData.colorImageScale;
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
                    Vector2 imageScale = sensorData.colorImageScale;
                    KinectInterop.TransformTexture(objectImageTexture, backgroundTexture, 0, imageScale.x < 0f, imageScale.y < 0f, backgroundCamera == null);
                }

            }
            else
            {
                // reset the background texture, if needed
                if (backgroundImage && backgroundImage.texture != null)
                {
                    backgroundImage.texture = null;

                    if (sensorData != null)
                    {
                        // disable color camera aligned depth & object-index frames 
                        //sensorData.sensorInterface.EnableColorCameraDepthFrame(sensorData, false);
                        sensorData.sensorInterface.EnableColorCameraObjectIndexFrame(sensorData, false);
                    }
                }
            }

            //RectTransform rectTransform = backgroundImage.rectTransform;
            //Debug.Log("pivot: " + rectTransform.pivot + ", anchorPos: " + rectTransform.anchoredPosition + ", \nanchorMin: " + rectTransform.anchorMin + ", anchorMax: " + rectTransform.anchorMax);
        }


        // checks for new color-camera aligned frames, and composes an updated body-index texture, if needed
        private void UpdateTextureWithNewFrame()
        {
            if (sensorData == null || sensorData.sensorInterface == null || sensorData.colorCamObjectIndexImage == null)  // || sensorData.colorCamDepthImage == null)
                return;
            if (sensorData.colorImageWidth == 0 || sensorData.colorImageHeight == 0 || sensorData.lastColorCamObjectIndexFrameTime == 0)  // || sensorData.lastColorCamDepthFrameTime == 0)
                return;

            // get object index frame
            if (/**lastColorCamDepthFrameTime != sensorData.lastColorCamDepthFrameTime ||*/ lastColorCamObjectIndexFrameTime != sensorData.lastColorCamObjectIndexFrameTime)
            {
                //lastColorCamDepthFrameTime = sensorData.lastColorCamDepthFrameTime;
                lastColorCamObjectIndexFrameTime = sensorData.lastColorCamObjectIndexFrameTime;

                if(objectImageTexture == null || objectImageTexture.width != sensorData.colorImageWidth || objectImageTexture.height != sensorData.colorImageHeight)
                {
                    objectImageTexture = KinectInterop.CreateRenderTexture(objectImageTexture, sensorData.colorImageWidth, sensorData.colorImageHeight);
                }

                //// get configured min & max distances 
                //float minDistance = ((DepthSensorBase)sensorData.sensorInterface).minDepthDistance;
                //float maxDistance = ((DepthSensorBase)sensorData.sensorInterface).maxDepthDistance;

                //int depthMinDistance = (int)(minDistance * 1000f);
                //int depthMaxDistance = (int)(maxDistance * 1000f);

                //Array.Clear(depthObjBufferData, 0, depthObjBufferData.Length);
                //Array.Clear(equalObjBufferData, 0, equalObjBufferData.Length);
                //objHistTotalPoints = 0;

                //int frameLen = sensorData.colorCamDepthImage.Length;
                //for (int i = 0; i < frameLen; i++)
                //{
                //    int depth = sensorData.colorCamDepthImage[i];
                //    int limDepth = (depth >= depthMinDistance && depth <= depthMaxDistance) ? depth : 0;

                //    if (/**rawBodyIndexImage[i] != 255 &&*/ limDepth > 0)
                //    {
                //        depthObjBufferData[limDepth]++;
                //        objHistTotalPoints++;
                //    }
                //}

                //if (objHistTotalPoints > 0)
                //{
                //    equalObjBufferData[0] = depthObjBufferData[0];
                //    for (int i = 1; i < depthObjBufferData.Length; i++)
                //    {
                //        equalObjBufferData[i] = equalObjBufferData[i - 1] + depthObjBufferData[i];
                //    }
                //}

                int objectIndexBufferLength = sensorData.colorCamObjectIndexImage.Length >> 2;
                if (objectIndexBuffer == null || objectIndexBuffer.count != objectIndexBufferLength)  // objectIndexBuffer.width != (sensorData.colorImageWidth >> 4) || objectIndexBuffer.height != sensorData.colorImageHeight)
                {
                    objectIndexBuffer = KinectInterop.CreateComputeBuffer(objectIndexBuffer, objectIndexBufferLength, sizeof(uint));
                    //objectIndexBuffer = KinectInterop.CreateTexture2D(objectIndexBuffer, sensorData.colorImageWidth >> 4, sensorData.colorImageHeight, TextureFormat.RGBAFloat);
                }

                KinectInterop.SetComputeBufferData(objectIndexBuffer, sensorData.colorCamObjectIndexImage, objectIndexBufferLength, sizeof(uint));
                //objectIndexBuffer.LoadRawTextureData(sensorData.colorCamObjectIndexImage);
                //objectIndexBuffer.Apply();

                //int depthBufferLength = sensorData.colorCamDepthImage.Length >> 1;
                //if(depthImageBuffer == null || depthImageBuffer.count != depthBufferLength)
                //{
                //    depthImageBuffer = KinectInterop.CreateComputeBuffer(depthImageBuffer, depthBufferLength, sizeof(uint));
                //}

                //KinectInterop.SetComputeBufferData(depthImageBuffer, sensorData.colorCamDepthImage, depthBufferLength, sizeof(uint));

                //if (objectHistBuffer != null)
                //{
                //    KinectInterop.SetComputeBufferData(objectHistBuffer, equalObjBufferData, equalObjBufferData.Length, sizeof(int));
                //}

                //float minDist = minDistance;  // kinectManager.minUserDistance != 0f ? kinectManager.minUserDistance : minDistance;
                //float maxDist = maxDistance;  // kinectManager.maxUserDistance != 0f ? kinectManager.maxUserDistance : maxDistance;

                objectImageMaterial.SetInt("_TexResX", sensorData.colorImageWidth);
                objectImageMaterial.SetInt("_TexResY", sensorData.colorImageHeight);
                //objectImageMaterial.SetInt("_MinDepth", (int)(minDist * 1000f));
                //objectImageMaterial.SetInt("_MaxDepth", (int)(maxDist * 1000f));

                objectImageMaterial.SetBuffer("_BodyIndexMap", objectIndexBuffer);
                //objectImageMaterial.SetBuffer("_DepthMap", depthImageBuffer);
                //objectImageMaterial.SetBuffer("_HistMap", objectHistBuffer);
                //objectImageMaterial.SetInt("_TotalPoints", objHistTotalPoints);

                Color[] objectIndexColors = kinectManager.GetObjectIndexColors();
                if(objectIndex >= 0)
                {
                    ulong objId = kinectManager.GetObjectIdByIndex(objectIndex);
                    int objIndex = kinectManager.GetTrackingIndexByObjectId(objId);

                    int numObjIndices = objectIndexColors.Length;
                    Color clrNone = new Color(0f, 0f, 0f, 0f);

                    for (int i = 0; i < numObjIndices; i++)
                    {
                        if (i != objIndex)
                            objectIndexColors[i] = clrNone;
                    }
                }

                objectImageMaterial.SetColorArray("_BodyIndexColors", objectIndexColors);

                Graphics.Blit(null, objectImageTexture, objectImageMaterial);
            }
        }

    }
}

