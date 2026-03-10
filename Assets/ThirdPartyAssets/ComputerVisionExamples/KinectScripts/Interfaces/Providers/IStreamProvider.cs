using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using com.rfilkov.devices;
using com.rfilkov.kinect;

namespace com.rfilkov.providers
{
    /// <summary>
    /// Stream states.
    /// </summary>
    public enum StreamState : int { NotStarted = 0, Starting = 1, Working = 2, Stopping = 3, Stopped = 4, ErrorOccured = 4, Unknown = 9 };

    public interface IStreamProvider
    {
        /// <summary>
        /// Returns the required device manager class name for this stream, or null if no device is required.
        /// </summary>
        /// <returns>Device manager class name, or null</returns>
        string GetDeviceManagerClass();

        /// <summary>
        /// Returns the required device index for this stream, or -1 if no device is required
        /// </summary>
        /// <param name="unicamInt">UniCam interface</param>
        /// <param name="devices">List of available devices</param>
        /// <returns>Device index, or -1</returns>
        int GetDeviceIndex(UniCamInterface unicamInt, List<KinectInterop.SensorDeviceInfo> devices);

        /// <summary>
        /// Starts the stream provider. Returns true on success, false on failure.
        /// </summary>
        /// <param name="unicamInt">UniCam interface</param>
        /// <param name="deviceManager">Device manager</param>
        /// <param name="kinectManager">Kinect manager</param>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="streamStartedCallback">Stream-started callback (optional)</param>
        /// <returns></returns>
        string StartProvider(UniCamInterface unicamInt, IDeviceManager deviceManager, KinectManager kinectManager, KinectInterop.SensorData sensorData, 
            System.Action<bool> streamStartedCallback = null);

        /// <summary>
        /// Stops the stream provider.
        /// </summary>
        void StopProvider();

        /// <summary>
        /// Releases stream resources allocated in the frameset.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        void ReleaseFrameData(FramesetData fsData);

        /// <summary>
        /// Returns the current state of the stream provider.
        /// </summary>
        /// <returns>Stream provider state.</returns>
        StreamState GetProviderState();

        /// <summary>
        /// Returns the provider-id of the currently opened stream provider.
        /// </summary>
        /// <returns>Provider ID, or null if the provider is not started</returns>
        string GetProviderId();

        /// <summary>
        /// Polls the stream frames in a thread.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="fsData">Frameset data</param>
        void PollStreamFrames(KinectInterop.SensorData sensorData, FramesetData fsData);

        /// <summary>
        /// Updates the stream data in the main thread.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="fsData">Frameset data</param>
        void UpdateStreamData(KinectInterop.SensorData sensorData, FramesetData fsData);

        /// <summary>
        /// Checks if the stream is ready to start processing next frame.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="fsData">Frameset data</param>
        /// <returns>true if the stream is ready for next frame, false otherwise</returns>
        bool IsReadyForNextFrame(KinectInterop.SensorData sensorData, FramesetData fsData);

    }
}
