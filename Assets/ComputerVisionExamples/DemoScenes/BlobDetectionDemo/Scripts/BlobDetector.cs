using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using com.rfilkov.kinect;


namespace com.rfilkov.components
{
    public class BlobDetector : MonoBehaviour
    {
        [Tooltip("Depth sensor index - 0 is the 1st one, 1 - the 2nd one, etc.")]
        public int sensorIndex = 0;

        [Tooltip("Camera used to estimate the overlay positions of 3D-objects over the background. By default it is the main camera.")]
        public Camera foregroundCamera;

        [Tooltip("Blob prefab, used to represent the blob in the 3D space.")]
        public GameObject blobPrefab;

        [Tooltip("The blobs root object.")]
        public GameObject blobsRootObj;

        [Range(0, 500)]
        [Tooltip("Max X and Y distance to blob, in pixels, to consider a pixel part of it.")]
        public int xyDistanceToBlob = 10;

        [Range(0, 500)]
        [Tooltip("Max Z-distance to blob, in mm, to consider a pixel part of it.")]
        public int zDistanceToBlob = 100;

        [Range(0, 100)]
        [Tooltip("Average depth values across number of frames.")]
        public int smoothAcrossFrames = 10;

        [Range(1, 10)]
        [Tooltip("Increment in X & Y directions, when analyzing the raw depth image.")]
        public int xyIncrement = 3;

        [Range(0, 500)]
        [Tooltip("Minimum amount of pixels in a blob.")]
        public int minPixelsInBlob = 50;

        [Range(0, 5)]
        [Tooltip("Time between the checks for blobs, in seconds.")]
        public float timeBetweenChecks = 0.1f;

        [Tooltip("UI-Text to display info messages.")]
        public Text infoText;

        [Tooltip("Background depth image, used to scale the blob indicators on screen.")]
        public RawImage backgroundImage;

        [Tooltip("UI-Text to display the maximum distance.")]
        public Text maxDistanceText;


        // reference to KM
        private KinectManager kinectManager = null;
        private KinectInterop.SensorData sensorData = null;

        // depth image resolution
        private int depthImageWidth;
        private int depthImageHeight;

        // depth scale
        private Vector3 depthScale = Vector3.one;

        // min & max distance tracked by the sensor
        private float minDistance = 0f;
        private float maxDistance = 0f;

        // screen rectangle taken by the foreground image (in pixels)
        private Rect foregroundImgRect;

        // last depth frame time
        private ulong lastDepthFrameTime = 0;
        private float lastCheckTime = 0;

        // list of blobs
        private List<Blob> blobs = new List<Blob>();
        // list of cubes
        private List<GameObject> blobObjects = new List<GameObject>();

        // saved depth frames
        private ushort[][] savedFrames = null;
        private int frameIndex, frameCount = 0;


        /// <summary>
        /// Gets the number of detected blobs.
        /// </summary>
        /// <returns>Number of blobs.</returns>
        public int GetBlobsCount()
        {
            return blobs.Count;
        }


        /// <summary>
        /// Gets the blob with the given index.
        /// </summary>
        /// <param name="i">Blob index.</param>
        /// <returns>The blob.</returns>
        public Blob GetBlob(int i)
        {
            if(i >= 0 && i < blobs.Count)
            {
                return blobs[i];
            }

            return null;
        }


        /// <summary>
        /// Gets distance to the blob with the given index.
        /// </summary>
        /// <param name="i">Blob index.</param>
        /// <returns>Distance to the blob.</returns>
        public float GetBlobDistance(int i)
        {
            if (i >= 0 && i < blobs.Count)
            {
                Vector3 blobCenter = blobs[i].GetBlobCenter();
                return blobCenter.z / 1000f;

            }

            return 0f;
        }


        /// <summary>
        /// Gets position on the depth image of the given blob. 
        /// </summary>
        /// <param name="i">Blob index.</param>
        /// <returns>Depth image position of the blob.</returns>
        public Vector2 GetBlobImagePos(int i)
        {
            if (i >= 0 && i < blobs.Count)
            {
                Vector3 blobCenter = blobs[i].GetBlobCenter();
                return (Vector2)blobCenter;

            }

            return Vector2.zero;
        }


        /// <summary>
        /// Gets position in the 3d space of the given blob.
        /// </summary>
        /// <param name="i">Blob index.</param>
        /// <returns>Space position of the blob.</returns>
        public Vector3 GetBlobSpacePos(int i)
        {
            if (i >= 0 && i < blobs.Count)
            {
                Vector3 blobCenter = blobs[i].GetBlobCenter();
                Vector3 spacePos = kinectManager.MapDepthPointToSpaceCoords(sensorIndex, (Vector2)blobCenter, (ushort)blobCenter.z, true);

                return spacePos;

            }

            return Vector3.zero;
        }


        /// <summary>
        /// Sets the minimum distance, in meters.
        /// </summary>
        /// <param name="fMinDist">Min distance.</param>
        public void SetMinDistance(float fMinDist)
        {
            if (sensorData != null && sensorData.sensorInterface != null)
            {
                ((DepthSensorBase)sensorData.sensorInterface).minDepthDistance = fMinDist;
            }
        }


        /// <summary>
        /// Sets the maximum distance, in meters.
        /// </summary>
        /// <param name="fMaxDist">Max distance.</param>
        public void SetMaxDistance(float fMaxDist)
        {
            if(sensorData != null && sensorData.sensorInterface != null)
            {
                ((DepthSensorBase)sensorData.sensorInterface).maxDepthDistance = fMaxDist;
            }
        }


        // internal methods

        void Start()
        {
            if (blobsRootObj == null)
            {
                blobsRootObj = gameObject;  // new GameObject("BlobsRoot");
            }

            if (foregroundCamera == null)
            {
                // by default use the main camera
                foregroundCamera = Camera.main;
            }

            // calculate the foreground rectangle
            //foregroundImgRect = kinectManager.GetForegroundRectDepth(sensorIndex, foregroundCamera);
        }

        void Update()
        {
            if (kinectManager == null || !kinectManager.IsInitialized())
            {
                kinectManager = KinectManager.Instance;
                sensorData = kinectManager != null ? kinectManager.GetSensorData(sensorIndex) : null;
            }

            if (kinectManager == null || !kinectManager.IsInitialized())
                return;

            // get depth image resolution
            depthImageWidth = sensorData.depthImageWidth;  // kinectManager.GetDepthImageWidth(sensorIndex);
            depthImageHeight = sensorData.depthImageHeight;  // kinectManager.GetDepthImageHeight(sensorIndex);
            depthScale = sensorData.depthImageScale;  // kinectManager.GetDepthImageScale(sensorIndex);

            minDistance = ((DepthSensorBase)sensorData.sensorInterface).minDepthDistance;  // kinectManager.GetSensorMinDistance(sensorIndex);
            maxDistance = ((DepthSensorBase)sensorData.sensorInterface).maxDepthDistance;  // kinectManager.GetSensorMaxDistance(sensorIndex);

            if (maxDistanceText)
            {
                maxDistanceText.text = string.Format("{0:F2} m", maxDistance);
            }

            // calculate the foreground rectangle
            foregroundImgRect = kinectManager.GetForegroundRectDepth(sensorIndex, foregroundCamera);  //, scaleX, scaleY);

            // apply the back-image anchor position
            Vector2 anchorPos = backgroundImage ? backgroundImage.rectTransform.anchoredPosition : Vector2.zero;
            foregroundImgRect.position = foregroundImgRect.position + anchorPos;

            if (lastDepthFrameTime != sensorData.lastDepthFrameTime)
            {
                lastDepthFrameTime = sensorData.lastDepthFrameTime;

                if ((Time.time - lastCheckTime) >= timeBetweenChecks)
                {
                    lastCheckTime = Time.time;

                    if (savedFrames == null && smoothAcrossFrames > 0 && depthImageWidth > 0 && depthImageHeight > 0)
                    {
                        // create frame array
                        savedFrames = new ushort[smoothAcrossFrames][];
                        for(int i = 0; i < savedFrames.Length; i++)
                        {
                            savedFrames[i] = new ushort[depthImageWidth * depthImageHeight];
                        }

                        frameIndex = 0; frameCount = 0;
                    }

                    // copy frame if needed
                    if(savedFrames != null && sensorData != null && sensorData.depthImage != null)
                    {
                        KinectInterop.CopyBytes(sensorData.depthImage, sizeof(ushort), savedFrames[frameIndex], sizeof(ushort));

                        if (frameCount < savedFrames.Length)
                            frameCount++;
                        frameIndex = (frameIndex + 1) % savedFrames.Length;
                        //Debug.Log($"Saved depth frame to index {frameIndex}, count: {frameCount}/{savedFrames.Length}");
                    }

                    // detect blobs of pixel in the raw depth image
                    DetectBlobsInRawDepth();
                }
            }

            if(blobPrefab)
            {
                // instantiates representative blob objects for each blog
                InstantiateBlobObjects();
            }

            if (infoText)
            {
                string sMessage = blobs.Count + " blobs detected.\n";

                for (int i = 0; i < blobs.Count; i++)
                {
                    Blob b = blobs[i];
                    //sMessage += string.Format("x1: {0}, y1: {1}, x2: {2}, y2: {3}\n", b.minx, b.miny, b.maxx, b.maxy);
                    sMessage += $"Blob {i} at {GetBlobSpacePos(i)} - {b.pixels} pixels\n";
                }

                //Debug.Log(sMessage);
                infoText.text = sMessage;
            }
        }


        // detects blobs of pixel in the raw depth image
        private void DetectBlobsInRawDepth()
        {
            blobs.Clear();

            if (sensorData == null || sensorData.depthImage == null)
                return;

            ushort[] rawDepth = sensorData.depthImage;

            ushort minDistanceMm = (ushort)(minDistance * 1000f);
            ushort maxDistanceMm = (ushort)(maxDistance * 1000f);

            for (int y = 0; y < depthImageHeight; y += xyIncrement)
            {
                int di = y * depthImageWidth;

                for (int x = 0; x < depthImageWidth; x += xyIncrement, di += xyIncrement)
                {
                    ushort depth = 0;

                    if (savedFrames == null)
                    {
                        // last frame
                        depth = rawDepth[di];
                    }
                    else
                    {
                        // average across frames
                        int depthSum = 0, validCount = 0;

                        for(int f = 0; f < frameCount; f++)
                        {
                            ushort frameDepth = savedFrames[f][di];

                            if(frameDepth != 0)
                            {
                                depthSum += frameDepth;
                                validCount++;
                            }

                            if (validCount == frameCount)  // should be in all frames
                                depth = (ushort)(depthSum / validCount);
                        }
                    }

                    depth = (depth >= minDistanceMm && depth <= maxDistanceMm) ? depth : (ushort)0;

                    if (depth != 0)
                    {
                        bool blobFound = false;
                        foreach (var b in blobs)
                        {
                            if (b.IsNearOrInside(x, y, depth, xyDistanceToBlob, zDistanceToBlob))
                            {
                                b.AddDepthPixel(x, y, depth);
                                blobFound = true;
                                break;
                            }
                        }

                        if (!blobFound)
                        {
                            Blob b = new Blob(x, y, depth);
                            blobs.Add(b);
                        }
                    }
                }
            }

            // remove small and inside blobs
            var blobsToRemove = new List<Blob>();
            foreach (Blob b in blobs)
            {
                if(b.pixels < minPixelsInBlob && !blobsToRemove.Contains(b))
                {
                    blobsToRemove.Add(b);
                    continue;
                }

                foreach (Blob b2 in blobs)
                {
                    if(b == b2)
                        continue;

                    if (b.IsInside(b2) && !blobsToRemove.Contains(b))
                    {
                        blobsToRemove.Add(b);
                        continue;
                    }
                }
            }

            for (int i = 0; i < blobsToRemove.Count; i++)
            {
                Blob b = blobsToRemove[i];

                if (blobs.Contains(b))
                    blobs.Remove(b);
            }
        }


        // instantiates representative blob objects for each blob
        private void InstantiateBlobObjects()
        {
            int bi = 0;
            foreach (var b in blobs)
            {
                while (bi >= blobObjects.Count)
                {
                    var cub = Instantiate(blobPrefab, new Vector3(0, 0, -10), Quaternion.identity);
                    //cub.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);  // to match the dimensions of a ball

                    blobObjects.Add(cub);
                    cub.transform.parent = blobsRootObj.transform;
                }

                Vector3 blobCenter = b.GetBlobCenter();
                Vector3 blobSpacePos = kinectManager.GetPosDepthOverlay((int)blobCenter.x, (int)blobCenter.y, (ushort)blobCenter.z, sensorIndex, foregroundCamera, foregroundImgRect);

                blobObjects[bi].transform.position = blobSpacePos;
                blobObjects[bi].name = "Blob" + bi;

                bi++;
            }

            // remove the extra cubes
            for (int i = blobObjects.Count - 1; i >= bi; i--)
            {
                Destroy(blobObjects[i]);
                blobObjects.RemoveAt(i);
            }
        }


        void OnRenderObject()
        {
            int rectX = (int)foregroundImgRect.xMin;
            //int rectY = (int)foregroundImgRect.yMax;
            int rectY = (int)foregroundImgRect.yMin;

            float scaleX = foregroundImgRect.width / depthImageWidth;
            float scaleY = foregroundImgRect.height / depthImageHeight;

            // draw grid
            //DrawGrid();

            // display blob rectangles
            int bi = 0;

            foreach (var b in blobs)
            {
                float x = (depthScale.x >= 0f ? b.minx : depthImageWidth - b.maxx) * scaleX;  // b.minx * scaleX;
                float y = (depthScale.y >= 0f ? b.miny : depthImageHeight - b.maxy) * scaleY;  // b.maxy * scaleY;

                Rect rectBlob = new Rect(rectX + x, rectY + y, (b.maxx - b.minx) * scaleX, (b.maxy - b.miny) * scaleY);
                KinectInterop.DrawRect(rectBlob, 2, Color.white);

                Vector3 blobCenter = b.GetBlobCenter();
                x = (depthScale.x >= 0f ? blobCenter.x : depthImageWidth - blobCenter.x) * scaleX;  // blobCenter.x * scaleX;
                y = (depthScale.y >= 0f ? blobCenter.y : depthImageHeight - blobCenter.y) * scaleY;  // blobCenter.y* scaleY; // 

                Vector3 blobPos = new Vector3(rectX + x, rectY + y, 0);
                KinectInterop.DrawPoint(blobPos, 3, Color.green);

                bi++;
            }
        }


        // draws coordinate grid on screen
        private void DrawGrid()
        {
            int rectX = (int)foregroundImgRect.xMin;
            int rectY = (int)foregroundImgRect.yMin;

            float scaleX = foregroundImgRect.width / depthImageWidth;
            float scaleY = foregroundImgRect.height / depthImageHeight;

            // draw grid
            float c = 0.3f;
            for (int x = 0; x < depthImageWidth; x += 100)
            {
                int sX = (int)(x * scaleX);
                int sMaxY = (int)((depthImageHeight - 1) * scaleY);

                Color clrLine = new Color(c, 0, 0, 1);
                KinectInterop.DrawLine(rectX + sX, rectY, rectX + sX, rectY + sMaxY, 1, clrLine);
                c += 0.1f;
            }

            c = 0.3f;
            for (int y = 0; y < depthImageHeight; y += 100)
            {
                int sY = (int)((depthImageHeight - y) * scaleY);
                int sMaxX = (int)((depthImageWidth - 1) * scaleX);

                Color clrLine = new Color(0, c, 0, 1);
                KinectInterop.DrawLine(rectX, rectY + sY, rectX + sMaxX, rectY + sY, 1, clrLine);
                c += 0.1f;
            }
        }

    }
}
