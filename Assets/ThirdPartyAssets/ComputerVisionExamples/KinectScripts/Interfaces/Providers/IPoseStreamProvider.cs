using com.rfilkov.kinect;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.rfilkov.providers
{
    public interface IPoseStreamProvider : IStreamProvider
    {

        /// <summary>
        /// Checks if the stream frame is ready or not. 
        /// </summary>
        /// <returns>true if the frame is ready, false otherwise</returns>
        bool IsFrameReady();

        /// <summary>
        /// Sets or clears the stream frame-ready flag.
        /// </summary>
        /// <param name="isReady">true if the frame is ready, false otherwise</param>
        void SetFrameReady(bool isReady);

        /// <summary>
        /// Enables or disables the pose stream after the start up.
        /// </summary>
        /// <param name="isEnable"></param>
        void EnablePoseStream(bool isEnable);

        /// <summary>
        /// Gets the estimated sensor position, in meters.
        /// </summary>
        /// <returns>Sensor position, in meters</returns>
        Vector3 GetSensorPosition();

        /// <summary>
        /// Gets the estimated sensor rotation.
        /// </summary>
        /// <returns>Sensor rotation</returns>
        Quaternion GetSensorRotation();

        /// <summary>
        /// Returns the timestamp of the current pose frame.
        /// </summary>
        /// <returns>IR frame timestamp</returns>
        ulong GetPoseFrameTimestamp();

        /// <summary>
        /// Sets raw-image to display the debug texture.
        /// </summary>
        /// <param name="debugImage">UI raw-image</param>
        void SetDebugImage(UnityEngine.UI.RawImage debugImage);

    }
}
