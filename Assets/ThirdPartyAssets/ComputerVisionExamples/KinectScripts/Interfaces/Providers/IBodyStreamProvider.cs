using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using com.rfilkov.kinect;

namespace com.rfilkov.providers
{
    public interface IBodyStreamProvider : IStreamProvider
    {
        /// <summary>
        /// Returns the body-index image scale.
        /// </summary>
        /// <returns>Body-index image scale</returns>
        Vector3 GetBodyIndexImageScale();

        /// <summary>
        /// Returns the body-index image size.
        /// </summary>
        /// <returns>Body-index image size (width, height)</returns>
        Vector2Int GetBodyIndexImageSize();

        /// <summary>
        /// Instructs the body-stream provider to stop body tracking.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        void StopBodyTracking(KinectInterop.SensorData sensorData);

        /// <summary>
        /// Determines whether to detect multiple users or not
        /// </summary>
        /// <param name="isDetectMultipleUsers">Whether to detect multiple users or not</param>
        void SetDetectMultipleUsers(bool isDetectMultipleUsers);

        /// <summary>
        /// Gets the maximum number of users that could be detected by the body stream provider.
        /// </summary>
        /// <returns>The maximum number of users</returns>
        uint GetMaxUsersCount();

        /// <summary>
        /// Sets scaled frame properties.
        /// </summary>
        /// <param name="isEnabled">Whether the scaling is enabled or not</param>
        /// <param name="scaledWidth">Scaled image width</param>
        /// <param name="scaledHeight">Scaled image height</param>
        void SetScaledFrameProps(bool isEnabled, int scaledWidth, int scaledHeight);

        /// <summary>
        /// Checks if the frame scaling is enabled or not.
        /// </summary>
        /// <returns>true if scaling is enabled, false otherwise</returns>
        bool IsFrameScalingEnabled();

    }
}
