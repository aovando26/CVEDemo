using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using com.rfilkov.kinect;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace com.rfilkov.devices
{
    /// <summary>
    /// DepthcamDeviceManager - manages webcam depth devices.
    /// </summary>
    public class DepthcamDeviceManager : MonoBehaviour, IDeviceManager
    {
        [Tooltip("Requested web-camera resolution.")]
        public Vector2Int requestedResolution = Vector2Int.zero;

        [Tooltip("Camera orientation in space (normal, turned or flipped over).")]
        public CameraOrientationEnum cameraOrientation = CameraOrientationEnum.Default;
        public enum CameraOrientationEnum { Default = 0, Clockwise90 = 90, CounterClockwise90 = 270, Flip180 = 180 }

        [Tooltip("Whether to flip camera image horizontally or not.")]
        public bool flipHorizontally = true;

        [Tooltip("Whether to flip camera image vertically or not.")]
        public bool flipVertically = false;

        [Tooltip("Raw image for debugging purposes.")]
        public UnityEngine.UI.RawImage debugImage = null;

        //[Tooltip("Raw image 2 for debugging purposes.")]
        //public UnityEngine.UI.RawImage debugImage2 = null;


        // cached sensor info and update time
        private static List<KinectInterop.SensorDeviceInfo> alSensorInfo = null;
        private static float sensorListUpdateTime = 0f;


        // saved references and device parameters
        private UniCamInterface _unicamInt = null;
        private KinectManager _kinectManager = null;
        private int _deviceIndex = -1;
        private KinectInterop.DeviceStreamingMode _streamMode;
        private string _recordingFile;
        private Action<string, KinectManager> _deviceOpenedCallback = null;

        private string _deviceId = null;
        private DeviceState _deviceState = DeviceState.NotStarted;
        private bool _isFrontFacing = false;

        // web camera, texture & image props
        private WebCamTexture _depthcam = null;

        // conversion shader params
        private ComputeShader _tex2BufShader;
        //private Material _tex2BufMaterial;

        private ComputeBuffer _depthBuf;
        //private Tensor<int> _depthTensor;
        //private TextureTensorData _depthTensorData;
        //private ulong _depthBufTimestamp = 0;

        // web-camera capabilities
        private const KinectInterop.FrameSource WEBCAM_CAPS = KinectInterop.FrameSource.TypeDepth;

        private ulong _lastFrameTime = 0;


        // horizontal flip
        public void SetHorizontalFlip(bool flip)
        {
            flipHorizontally = flip;
        }

        // vertical flip
        public void SetVerticalFlip(bool flip)
        {
            flipVertically = flip;
        }

        // camera orientation
        public void SetCameraOrientation(int ori)
        {
            switch(ori)
            {
                case 0:
                    cameraOrientation = CameraOrientationEnum.Default;
                    break;
                case 1:
                    cameraOrientation = CameraOrientationEnum.Clockwise90;
                    break;
                case 2:
                    cameraOrientation = CameraOrientationEnum.CounterClockwise90;
                    break;
                case 3:
                    cameraOrientation = CameraOrientationEnum.Flip180;
                    break;
            }

            Debug.Log($"CameraOrientation: {cameraOrientation}");
        }


        /// <summary>
        /// Returns the device type, controlled by this device manager.
        /// </summary>
        /// <returns>Supported device type</returns>
        public string GetDeviceType()
        {
            return "Depthcam";
        }

        /// <summary>
        /// Returns the list of available devices, controlled by this device manager.
        /// </summary>
        /// <returns>List of available devices</returns>
        public List<KinectInterop.SensorDeviceInfo> GetAvailableDevices()
        {
            if (alSensorInfo != null && (Time.time - sensorListUpdateTime) < 30f)
            {
                return alSensorInfo;
            }

            alSensorInfo = new List<KinectInterop.SensorDeviceInfo>();
            sensorListUpdateTime = Time.time;

            var webcamDevices = WebCamTexture.devices;
            int deviceCount = webcamDevices.Length;

            for (int i = 0; i < deviceCount; i++)
            {
                var device = webcamDevices[i];
                KinectInterop.SensorDeviceInfo sensorInfo = new KinectInterop.SensorDeviceInfo();

                sensorInfo.deviceIndex = i;
                sensorInfo.sensorId = i.ToString();
                sensorInfo.sensorName = device.depthCameraName;
                sensorInfo.sensorCaps = WEBCAM_CAPS;
                sensorInfo.isFrontFacing = device.isFrontFacing;
                sensorInfo.isDepthCam = !string.IsNullOrEmpty(device.depthCameraName);

                alSensorInfo.Add(sensorInfo);
            }

            return alSensorInfo;
        }

        /// <summary>
        /// Opens the specified device.
        /// </summary>
        /// <param name="unicamInt">UniCam sensor interface</param>
        /// <param name="deviceIndex">Device index</param>
        /// <param name="kinectManager">Kinect manager</param>
        /// <returns>Device ID, if the device is opened successfully, null otherwise.</returns>
        public string OpenDevice(UniCamInterface unicamInt, int deviceIndex, KinectManager kinectManager, System.Action<string, KinectManager> deviceOpenedCallback = null)
        {
            // save parameters
            _unicamInt = unicamInt;
            _deviceIndex = deviceIndex;
            _streamMode = unicamInt.deviceStreamingMode;
            _recordingFile = unicamInt.recordingFile;
            _kinectManager = kinectManager;

            _deviceOpenedCallback = deviceOpenedCallback;
            _deviceState = DeviceState.NotStarted;
            _isFrontFacing = false;

            _lastFrameTime = 0;

            if (KinectInterop.IsSupportsComputeShaders())
            {
                _tex2BufShader = Resources.Load("DepthcamTexToBufShader") as ComputeShader;
            }

            if (unicamInt.deviceStreamingMode != KinectInterop.DeviceStreamingMode.Disabled)
            {
                if (HasCameraPermission())
                {
                    _deviceOpenedCallback = null;
                    return DoOpenDevice();
                }
                else
                {
                    // request camera permission
                    RequestCameraPermission();
                    _deviceId = $"dcam{_deviceIndex}_req_perm";

                    return _deviceId;
                }
            }

            return string.Empty;
        }

        // checks if the camera permission has been granted by the user
        private bool HasCameraPermission()
        {
#if UNITY_IOS || UNITY_WEBGL
            return Application.HasUserAuthorization(UserAuthorization.WebCam);
#elif UNITY_ANDROID
            Permission.HasUserAuthorizedPermission(Permission.Camera)
#else
            return true;
#endif
        }

        // requests camera permission and then initializes the camera
        private void RequestCameraPermission()
        {
            Debug.Log("Requesting depthcam permission...");
#if UNITY_IOS || UNITY_WEBGL
            StartCoroutine(AskForPermissionIfRequired(UserAuthorization.WebCam, () => { DoOpenDevice(); }));
            return;
#elif UNITY_ANDROID
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                AskCameraPermission();
                return;
            }
            DoOpenDevice();
#else
            DoOpenDevice();
#endif
        }

#if UNITY_IOS || UNITY_WEBGL
        private bool CheckPermissionAndRaiseCallbackIfGranted(UserAuthorization authenticationType, Action authenticationGrantedAction)
        {
            if (Application.HasUserAuthorization(authenticationType))
            {
                if (authenticationGrantedAction != null)
                    authenticationGrantedAction();

                return true;
            }
            return false;
        }

        private IEnumerator AskForPermissionIfRequired(UserAuthorization authenticationType, Action authenticationGrantedAction)
        {
            if (!CheckPermissionAndRaiseCallbackIfGranted(authenticationType, authenticationGrantedAction))
            {
                yield return Application.RequestUserAuthorization(authenticationType);
                yield return null;

                if (!CheckPermissionAndRaiseCallbackIfGranted(authenticationType, authenticationGrantedAction))
                    Debug.LogError($"Depthcam permission {authenticationType} denied");
                else
                    Debug.Log($"Depthcam permission {authenticationType} granted");
            }
        }
#elif UNITY_ANDROID
        private void PermissionCallbacksPermissionGranted(string permissionName)
        {
            Debug.LogError($"Depthcam permission {permissionName} granted");
            StartCoroutine(DelayedCameraInitialization());
        }

        private IEnumerator DelayedCameraInitialization()
        {
            yield return null;
            DoOpenDevice();
        }

        private void PermissionCallbacksPermissionDenied(string permissionName)
        {
            Debug.LogError($"Depthcam permission {permissionName} denied");
        }

        private void AskCameraPermission()
        {
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionDenied += PermissionCallbacksPermissionDenied;
            callbacks.PermissionGranted += PermissionCallbacksPermissionGranted;
            Permission.RequestUserPermission(Permission.Camera, callbacks);
        }
#endif

        // opens webcam device, image or video
        private string DoOpenDevice()
        {
            if (_unicamInt.deviceStreamingMode == KinectInterop.DeviceStreamingMode.PlayRecording ||
                _unicamInt.deviceStreamingMode == KinectInterop.DeviceStreamingMode.SaveRecording)
            {
                Debug.LogWarning("Depthcam doesn't support playing or saving recordings!");
            }
            else
            {
                var alSensors = GetAvailableDevices();

                if (_kinectManager.consoleLogMessages)
                {
                    // display the list of available devices
                    System.Text.StringBuilder sbDevices = new System.Text.StringBuilder();
                    sbDevices.AppendLine("Available depthcam devices:");

                    foreach (var alSensor in alSensors)
                        sbDevices.AppendLine(alSensor.ToString());
                    Debug.Log(sbDevices.ToString());
                }

                if (_deviceIndex >= alSensors.Count || string.IsNullOrEmpty(alSensors[_deviceIndex].sensorName))
                {
                    Debug.LogError("  D" + _deviceIndex + " is not available.");
                    return null;
                }

                KinectInterop.SensorDeviceInfo deviceInfo = alSensors[_deviceIndex];

                if (requestedResolution != Vector2Int.zero)
                {
                    _depthcam = new WebCamTexture(deviceInfo.sensorName, requestedResolution.x, requestedResolution.y);
                }
                else
                {
                    _depthcam = new WebCamTexture(deviceInfo.sensorName);
                }

                // try to open the sensor
                _depthcam.Play();

                _deviceId = deviceInfo.sensorName;
                _deviceState = DeviceState.Working;
                _isFrontFacing = deviceInfo.isFrontFacing;

                Debug.Log($"D{_unicamInt.deviceIndex} Opened depthcam ID: {_deviceId}\nresolution: {_depthcam.width}x{_depthcam.height}");  // +
                    // $"\nrequested: {requestedResolution}, rotAngle: {_depthcam.videoRotationAngle}, vMirror: {_depthcam.videoVerticallyMirrored}, ori: {Screen.orientation}");
            }

            // invoke the callback
            if (_deviceOpenedCallback != null)
            {
                _deviceOpenedCallback(this.GetType().FullName, _kinectManager);
                _deviceOpenedCallback = null;
            }

            return _deviceId;
        }

        /// <summary>
        /// Closes the device, if it's open.
        /// </summary>
        public void CloseDevice()
        {
            _deviceState = DeviceState.Stopped;

            _depthBuf?.Release();
            _depthBuf = null;

            if (_depthcam)
            {
                if(_depthcam.isPlaying)
                {
                    _depthcam.Stop();
                }

                KinectInterop.Destroy(_depthcam);
                _depthcam = null;
            }
        }

        /// <summary>
        /// Returns the current device state.
        /// </summary>
        /// <returns>Current device state</returns>
        public DeviceState GetDeviceState()
        {
            return _deviceState;
        }

        /// <summary>
        /// Returns the device-index of the currently opened device.
        /// </summary>
        /// <returns>Device index, or -1 if no device is opened</returns>
        public int GetDeviceIndex()
        {
            return _deviceIndex;
        }

        /// <summary>
        /// Returns the device-id of the currently opened device.
        /// </summary>
        /// <returns>Device ID, or null if no device is opened</returns>
        public string GetDeviceId()
        {
            return _deviceId;
        }

        /// <summary>
        /// Returns device property. The result, if not null, should be casted appropriately. 
        /// </summary>
        /// <param name="propId">Property ID</param>
        /// <returns>Property value or null</returns>
        public object GetDeviceProperty(DeviceProp propId)
        {
            switch(propId)
            {
                case DeviceProp.Resolution:
                    return GetImageSize();
            }

            Debug.LogWarning($"Unknown device property requested: {propId}");
            return null;
        }


        // returns the image size
        public Vector2Int GetImageSize()
        {
            if (Application.isMobilePlatform)
            {
                // sets camera orientation & h/v flip, according to screen orientation
                SetMobileCameraProps();
            }

            int w = 0, h = 0;  // , camW = 0, camH = 0, scrW = Screen.width, srcH = Screen.height;
            bool isVertical = cameraOrientation == CameraOrientationEnum.Clockwise90 || cameraOrientation == CameraOrientationEnum.CounterClockwise90;

            if(_depthcam != null)
            {
                //camW = _depthcam.width; camH = _depthcam.height;
                w = (!isVertical ? _depthcam.width : _depthcam.height);
                h = (!isVertical ? _depthcam.height : _depthcam.width);
            }

            //Debug.Log($"tex/cam w: {camW}, h: {camH}\n" +
            //    $"image w: {w}, h: {h}, camOri: {cameraOrientation}, flipH: {flipHorizontally}, flipV: {flipVertically}, vert: {isVertical}\n" +
            //    $"screen: w: {scrW}, h: {srcH}, ori: {Screen.orientation}\nmobile: {Application.isMobilePlatform}");

            return new Vector2Int(w, h);
        }

        // sets the camera orientation and h/v flip on mobile platforms, according to screen orientation
        private void SetMobileCameraProps()
        {
            switch(Application.platform)
            {
                case RuntimePlatform.IPhonePlayer:
                    switch (Screen.orientation)
                    {
                        case ScreenOrientation.Portrait:
                            cameraOrientation = CameraOrientationEnum.CounterClockwise90;
                            flipHorizontally = !_isFrontFacing;
                            flipVertically = false;
                            break;

                        case ScreenOrientation.LandscapeLeft:
                            cameraOrientation = CameraOrientationEnum.Default;
                            flipHorizontally = false;
                            flipVertically = _isFrontFacing;
                            break;

                        case ScreenOrientation.LandscapeRight:
                            cameraOrientation = CameraOrientationEnum.Default;
                            flipHorizontally = true;
                            flipVertically = !_isFrontFacing;
                            break;

                        case ScreenOrientation.PortraitUpsideDown:
                            cameraOrientation = CameraOrientationEnum.Clockwise90;
                            flipHorizontally = _isFrontFacing;
                            flipVertically = true;
                            break;
                    }

                    break;

                case RuntimePlatform.Android:
                    flipHorizontally = false;
                    flipVertically = false;

                    switch (Screen.orientation)
                    {
                        case ScreenOrientation.Portrait:
                            cameraOrientation = CameraOrientationEnum.CounterClockwise90;
                            break;

                        case ScreenOrientation.LandscapeLeft:
                            cameraOrientation = CameraOrientationEnum.Default;
                            break;

                        case ScreenOrientation.LandscapeRight:
                            cameraOrientation = CameraOrientationEnum.Flip180;
                            break;

                        case ScreenOrientation.PortraitUpsideDown:
                            cameraOrientation = CameraOrientationEnum.Clockwise90;
                            break;
                    }

                    break;
            }
        }

        /// <summary>
        /// Polls the device frames in a thread.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="fsData">Frameset data</param>
        public void PollDeviceFrames(KinectInterop.SensorData sensorData, providers.FramesetData fsData)
        {
            // does nothing
        }

        /// <summary>
        /// Updates the device data in the main thread.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="fsData">Frameset data</param>
        public void UpdateDeviceData(KinectInterop.SensorData sensorData, providers.FramesetData fsData)
        {
            if (fsData._procFrameTimestamp == fsData._rawFrameTimestamp)
                return;

            if(_unicamInt.deviceStreamingMode != _streamMode || _unicamInt.deviceIndex != _deviceIndex || _unicamInt.recordingFile != _recordingFile)
            {
                CloseDevice();
                OpenDevice(_unicamInt, _unicamInt.deviceIndex, _kinectManager);
            }

            // update the buffer texture, if needed
            Vector2Int imageSize = GetImageSize();
            if (imageSize.x == 0 || imageSize.y == 0)
                return;

            // restart the camera if needed
            if (_depthcam != null && !_depthcam.isPlaying)
            {
                try
                {
                    Debug.LogWarning("Depthcam stopped playing. Trying to restart it...");
                    _depthcam.Stop();
                    _depthcam.Play();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            // debug image
            if(debugImage != null)
            {
                debugImage.texture = _depthcam;

                float debugH = debugImage.rectTransform.sizeDelta.y;
                debugImage.rectTransform.sizeDelta = new Vector2(debugH * debugImage.texture.width / debugImage.texture.height, debugH);
            }

            int bufLength = imageSize.x * imageSize.y;
            if (_depthBuf == null || _depthBuf.count != bufLength)
            {
                _depthBuf = KinectInterop.CreateComputeBuffer(_depthBuf, bufLength, sizeof(float));
                Debug.Log($"D{_unicamInt.deviceIndex} Updated depthcam resolution to: {imageSize.x}x{imageSize.y}, fs: {fsData._fsIndex}");  // +
                    // (_depthcam ? $"\nrotAngle: {_depthcam.videoRotationAngle}, vMirror: {_depthcam.videoVerticallyMirrored}, ori: {Screen.orientation}" : string.Empty));
            }

            if (_lastFrameTime != 0 && fsData._rawFrameTimestamp >= _lastFrameTime && (fsData._rawFrameTimestamp - _lastFrameTime) < KinectInterop.MIN_TIME_BETWEEN_IN_FRAMES)
            {
                //Debug.Log($"    fs: {fsData._fsIndex} DepthCamFrame skipped. ts: {fsData._rawFrameTimestamp}, dTs: {fsData._rawFrameTimestamp - _lastFrameTime}\nframeReady: {fsData._isRawFrameReady}");
                return;
            }

            // texture to buffer
            _tex2BufShader.SetTexture(0, "_DepthTex", _depthcam);
            _tex2BufShader.SetBuffer(0, "_DepthBuf", _depthBuf);

            _tex2BufShader.SetInt("_TexW", _depthcam.width);
            _tex2BufShader.SetInt("_TexH", _depthcam.height);
            _tex2BufShader.SetInt("_Angle", (int)cameraOrientation);
            _tex2BufShader.SetInt("_FlipH", flipHorizontally ? 1 : 0);
            _tex2BufShader.SetInt("_FlipV", flipVertically ? 1 : 0);

            //Debug.Log($"fs: {fsData._fsIndex} - DepthcamTexToBuf - ts: {fsData._procFrameTimestamp}\ndW: {_depthcam.width}, dH: {_depthcam.height}, ori: {(int)cameraOrientation}, flH: {flipHorizontally}, flV: {flipVertically}");
            _tex2BufShader.Dispatch(0, _depthcam.width >> 3, _depthcam.height >> 3, 1);
            fsData._procFrameTimestamp = fsData._rawFrameTimestamp;
            //Debug.Log($"fs: {fsData._fsIndex} - DepthcamTexToBuf started, ts: {fsData._procFrameTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
        }


        /// <summary>
        /// Returns depth compute buffer.
        /// </summary>
        /// <returns>Depth compute buffer</returns>
        public ComputeBuffer GetDepthBuffer()
        {
            return _depthBuf;
        }

        /// <summary>
        /// Returns the last timestamp of the depth compute buffer.
        /// </summary>
        /// <returns>Last timestamp</returns>
        public ulong GetDepthBufTimestamp(providers.FramesetData fsData)
        {
            return fsData._procFrameTimestamp;
        }


        /// <summary>
        /// Sets or clears the device frame-ready flag.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        /// <param name="isReady">true if the frame is ready, false otherwise</param>
        public void SetFrameReady(providers.FramesetData fsData, DeviceFrame frameType, bool isReady)
        {
            // do nothing
        }

        /// <summary>
        /// Whether the streams can use processed frames or not. 
        /// </summary>
        public bool IsUsingProcFrames { get; set; } = false;

        /// <summary>
        /// Sets or clears the processed-frame-ready flag.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        /// <param name="isReady">true if the frame is ready, false otherwise</param>
        public void SetProcFrameReady(providers.FramesetData fsData, DeviceFrame frameType, bool isReady)
        {
            // do nothing
        }

        /// <summary>
        /// Sets processed frame and timestamp, as well as the proc-frame-ready flag.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        /// <param name="frameType">Frame type</param>
        /// <param name="frame">Frame</param>
        /// <param name="timestamp">Timestamp</param>
        public void SetProcFrame(providers.FramesetData fsData, DeviceFrame frameType, object frame, ulong timestamp)
        {
            // do nothing
        }

    }
}
