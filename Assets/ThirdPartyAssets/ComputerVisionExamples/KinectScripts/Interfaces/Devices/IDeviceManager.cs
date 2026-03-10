using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using com.rfilkov.kinect;


namespace com.rfilkov.devices
{
    /// <summary>
    /// Device states.
    /// </summary>
    public enum DeviceState : int { NotStarted = 0, Starting = 1, Working = 2, Stopping = 3, Stopped = 4, ErrorOccured = 4, Unknown = 9 };

    /// <summary>
    /// Device properties.
    /// </summary>
    public enum DeviceProp : int { Default = 0, Resolution = 1, Frame = 2, FrameTime = 3, ProcFrame = 4, ProcFrameTime = 5, ImuFrame = 6, ImuFrameTime = 7 };

    /// <summary>
    /// Device frame types.
    /// </summary>
    public enum DeviceFrame : int { All = 0, Color = 1, Depth = 2, Infrared = 3, BodyData = 4, BodyIndex = 5, Pose = 6 };


    public interface IDeviceManager
    {
        /// <summary>
        /// Returns the device type, controlled by this device manager.
        /// </summary>
        /// <returns>Supported device type</returns>
        string GetDeviceType();

        /// <summary>
        /// Returns the list of available devices, controlled by this device manager.
        /// </summary>
        /// <returns>List of available devices</returns>
        List<KinectInterop.SensorDeviceInfo> GetAvailableDevices();

        /// <summary>
        /// Opens the specified device.
        /// </summary>
        /// <param name="unicamInt">UniCam sensor interface</param>
        /// <param name="deviceIndex">Device index</param>
        /// <param name="kinectManager">Kinect manager</param>
        /// <returns>Device ID, if the device is opened successfully, null otherwise.</returns>
        string OpenDevice(UniCamInterface unicamInt, int deviceIndex, KinectManager kinectManager, System.Action<string, KinectManager> deviceOpenedCallback = null);

        /// <summary>
        /// Closes the device, if it's open.
        /// </summary>
        void CloseDevice();

        /// <summary>
        /// Returns the current device state.
        /// </summary>
        /// <returns>Current device state</returns>
        DeviceState GetDeviceState();

        /// <summary>
        /// Returns the device-index of the currently opened device.
        /// </summary>
        /// <returns>Device index, or -1 if no device is opened</returns>

        int GetDeviceIndex();

        /// <summary>
        /// Returns the device-id of the currently opened device.
        /// </summary>
        /// <returns>Device ID, or null if no device is opened</returns>
        string GetDeviceId();

        /// <summary>
        /// Returns device property. The result, if not null, should be casted appropriately. 
        /// </summary>
        /// <param name="propId">Property ID</param>
        /// <returns>Property value or null</returns>
        object GetDeviceProperty(DeviceProp propId);

        /// <summary>
        /// Polls the device frames in a thread.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="fsData">Frameset data</param>
        void PollDeviceFrames(KinectInterop.SensorData sensorData, providers.FramesetData fsData);

        /// <summary>
        /// Updates the device data in the main thread.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="fsData">Frameset data</param>
        void UpdateDeviceData(KinectInterop.SensorData sensorData, providers.FramesetData fsData);

        /// <summary>
        /// Sets or clears the device frame-ready flag.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        /// <param name="isReady">true if the frame is ready, false otherwise</param>
        void SetFrameReady(providers.FramesetData fsData, DeviceFrame frameType, bool isReady);

        /// <summary>
        /// Whether the streams can use processed frames or not. 
        /// </summary>
        bool IsUsingProcFrames { get; set; }

        /// <summary>
        /// Sets or clears the device processed-frame-ready flag.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        /// <param name="isReady">true if the frame is ready, false otherwise</param>
        void SetProcFrameReady(providers.FramesetData fsData, DeviceFrame frameType, bool isReady);

        /// <summary>
        /// Sets processed frame and timestamp.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        /// <param name="frameType">Frame type</param>
        /// <param name="frame">Frame</param>
        /// <param name="timestamp">Timestamp</param>
        void SetProcFrame(providers.FramesetData fsData, DeviceFrame frameType, object frame, ulong timestamp);
    }
}
