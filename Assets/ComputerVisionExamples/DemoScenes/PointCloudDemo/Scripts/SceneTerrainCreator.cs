using UnityEngine;
using System.Collections;
using com.rfilkov.kinect;

namespace com.rfilkov.components
{
    /// <summary>
    /// Sets terrain heights depending on the depth map.
    /// @author Marc Teuber
    /// </summary>
    public class SceneTerrainCreator : MonoBehaviour
    {
        [Tooltip("Depth sensor index used for color camera overlay - 0 is the 1st one, 1 - the 2nd one, etc.")]
        public int sensorIndex = 0;

        [Tooltip("Reference to the terrain-component")]
        public Terrain terrain;

        [Tooltip("Whether to flip on X axis or not")]
        public bool flipX = true;

        [Tooltip("Maximum distance value in the depth image, in meters")]
        [Range(1f, 10f)]
        public float maxDistance = 10f;

        [Tooltip("Adjust this value to control the height of the terrain")]
        [Range(0.1f, 100f)]
        public float heightMultiplier = 10f;

        [Tooltip("Whether to inverse the distance to height or not")]
        public bool inverseHeight = false;

        [Tooltip("Time interval between terrain updates, in seconds. 0 means no wait.")]
        public float updateInterval = 2f;


        // references
        private KinectManager kinectManager;
        private KinectInterop.SensorData sensorData;

        // last update time
        private float lastUpdateTime = 0f;


        void Start()
        {
            kinectManager = KinectManager.Instance;
            sensorData = (kinectManager != null && kinectManager.IsInitialized()) ? kinectManager.GetSensorData(sensorIndex) : null;

            if(terrain == null)
            {
                terrain = GetComponent<Terrain>();
            }

            lastUpdateTime = -updateInterval + 0.5f;
        }

        void Update()
        {
            if (sensorData != null && sensorData.depthImage != null && (Time.time - lastUpdateTime) >= updateInterval)
            {
                lastUpdateTime = Time.time;

                ApplyHeightMapToTerrain(sensorData.depthImage, sensorData.depthImageWidth, sensorData.depthImageHeight);
            }
        }

        // Apply heightmap to the terrain
        void ApplyHeightMapToTerrain(ushort[] depthImage, int imageWidth, int imageHeight)
        {
            if (terrain != null && terrain.terrainData != null)
            {
                // Get terrain data
                TerrainData terrainData = terrain.terrainData;

                //// Set heightmap resolution
                //terrainData.heightmapResolution = imageWidth;

                // Set terrain size
                terrainData.size = new Vector3(imageWidth, maxDistance * heightMultiplier, imageHeight);

                // Convert texture to heightmap
                int maxHeightmapXY = terrainData.heightmapResolution - 1;
                float[,] heights = new float[maxHeightmapXY, maxHeightmapXY];

                int startX = (maxHeightmapXY - imageWidth) / 2;
                int startY = (maxHeightmapXY - imageHeight) / 2;
                Debug.Log($"time: {lastUpdateTime:F3}, maxXY: {maxHeightmapXY}, startX: {startX}, startY: {startY}");

                for (int y = 0; y < imageHeight; y++)
                {
                    for (int x = 0; x < imageWidth; x++)
                    {
                        if((startX + x) >= 0 && (startX + x) < maxHeightmapXY && (startY + y) >= 0 && (startY + y) < maxHeightmapXY)
                        {
                            float dist = depthImage[y * imageWidth + (!flipX ? x : (imageWidth - x - 1))] * 0.001f;
                            if (dist == 0f)
                                continue;

                            float height = (!inverseHeight ? dist : maxDistance - dist) / maxDistance;
                            heights[startX + x, startY + y] = height;
                        }
                    }
                }

                // Apply the heights to the terrain
                terrainData.SetHeights(0, 0, heights);

                // center the terrain on screen (feel free to change this)
                terrain.transform.position = new Vector3(-imageWidth * 0.5f, 0f, -imageHeight * 0.5f);
            }
        }
    }
}
