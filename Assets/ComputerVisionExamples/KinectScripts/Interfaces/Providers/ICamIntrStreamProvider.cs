using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using com.rfilkov.kinect;

namespace com.rfilkov.providers
{
    public interface ICamIntrStreamProvider : IStreamProvider
    {
        /// <summary>
        /// Tries to check the camera intrinsics on the detected raw body data.
        /// </summary>
        /// <param name="cameraRes">Camera resolution (color or depth)</param>
        /// <param name="sensorData">Sensor data</param>
        /// <returns>true if the camera intrinsics seem right, false otherwise</returns>
        bool TryCheckCamIntr(DepthSensorBase.PointCloudResolution cameraRes, KinectInterop.SensorData sensorData, FramesetData fsData);

        /// <summary>
        /// Tries to estimate focal length out of the detected raw body data.
        /// </summary>
        /// <param name="cameraRes">Camera resolution (color or depth)</param>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="percentReady">(out) Percentage of readiness, in case of multi-step procedure (0-1)</param>
        /// <returns>Focal length, or Vector2.zero</returns>
        Vector2 TryEstimateFocalLen(DepthSensorBase.PointCloudResolution cameraRes, KinectInterop.SensorData sensorData, FramesetData fsData, out float percentReady);
    }
}
