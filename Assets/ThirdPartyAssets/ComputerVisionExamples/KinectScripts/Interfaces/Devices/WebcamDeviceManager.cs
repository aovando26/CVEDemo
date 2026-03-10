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
    /// WebcamDeviceManager - manages webcam devices.
    /// </summary>
    public class WebcamDeviceManager : MonoBehaviour, IDeviceManager
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

        [Tooltip("Playback position in seconds.")]
        [Range(-1f, 300f)]
        public float playbackPosSeconds = -1f;

        [Tooltip("Whether to include audio in the playback or not.")]
        public bool playbackAudio = true;  // false;

        [Tooltip("External texture to be used instead of the web-camera as image input.")]
        public Texture externalTexture = null;

        [Tooltip("Raw image for debugging purposes.")]
        public UnityEngine.UI.RawImage debugImage = null;

        [Tooltip("Raw image 2 for debugging purposes.")]
        public UnityEngine.UI.RawImage debugImage2 = null;


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
        private WebCamTexture _webcam = null;
        private UnityEngine.Video.VideoPlayer _videoPlayer = null;
        private bool isExtTextureCreated = false;
        private ulong _deviceStartTime = 0;

        private ulong _lastFrameTime = 0;
        //private RenderTexture _lastFrameImage;

        // saved horizontal flip
        private bool _savedFlipHoriz = false;

        //private RenderTexture _colorTex = null;
        //private ulong _lastUpdateTime = 0;
        //private bool _colorFrameReady = false;

        // web-camera capabilities
        private const KinectInterop.FrameSource WEBCAM_CAPS = KinectInterop.FrameSource.TypeColor | KinectInterop.FrameSource.TypeDepth |
            KinectInterop.FrameSource.TypeBody | KinectInterop.FrameSource.TypeBodyIndex |
            KinectInterop.FrameSource.TypeObject | KinectInterop.FrameSource.TypeObjectIndex;  // | KinectInterop.FrameSource.TypePose;


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
            return "Webcam";
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
                sensorInfo.sensorName = device.name;
                sensorInfo.sensorCaps = WEBCAM_CAPS;
                sensorInfo.isFrontFacing = device.isFrontFacing;
                sensorInfo.isDepthCam = !string.IsNullOrEmpty(device.depthCameraName);

                //if (consoleLogMessages)
                //    Debug.Log(string.Format("  D{0}: {1}, id: {2}", i, sensorInfo.sensorName, sensorInfo.sensorId));

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
            //_lastFrameImage = null;

            if (externalTexture == null && unicamInt.deviceStreamingMode == KinectInterop.DeviceStreamingMode.PlayRecording)
            {
                if (string.IsNullOrEmpty(unicamInt.recordingFile))
                {
                    Debug.LogError("Playback selected, but the path to recording file is missing.");
                    _deviceState = DeviceState.ErrorOccured;
                    return null;
                }

                if (!System.IO.File.Exists(unicamInt.recordingFile))
                {
                    Debug.LogError("PlayRecording selected, but the recording file cannot be found: " + unicamInt.recordingFile);
                    _deviceState = DeviceState.ErrorOccured;
                    return null;
                }

                if (kinectManager.consoleLogMessages)
                    Debug.Log("Playing back: " + unicamInt.recordingFile);

                _videoPlayer = gameObject.AddComponent<UnityEngine.Video.VideoPlayer>();
                _videoPlayer.playOnAwake = false;
                _videoPlayer.isLooping = unicamInt.loopPlayback;

                if (playbackAudio)
                {
                    _videoPlayer.timeUpdateMode = UnityEngine.Video.VideoTimeUpdateMode.DSPTime;
                    _videoPlayer.SetDirectAudioVolume(0, 0f);
                }
                else
                {
                    _videoPlayer.timeUpdateMode = UnityEngine.Video.VideoTimeUpdateMode.GameTime;
                    _videoPlayer.SetDirectAudioMute(0, true);
                }

                _videoPlayer.url = "file://" + unicamInt.recordingFile.Replace('\\', '/');
                _videoPlayer.prepareCompleted += VideoPlayer_PrepareCompleted;
                _videoPlayer.Prepare();

                _deviceId = KinectInterop.GetFileName(unicamInt.recordingFile, false);
                _deviceState = DeviceState.Starting;

                return _deviceId;
            }
            else if(unicamInt.deviceStreamingMode != KinectInterop.DeviceStreamingMode.Disabled)
            {
                if(HasCameraPermission())
                {
                    _deviceOpenedCallback = null;
                    return DoOpenDevice();
                }
                else
                {
                    // request camera permission
                    RequestCameraPermission();

                    _deviceId = $"wcam{_deviceIndex}_req_perm";
                    return _deviceId;
                }
            }

            return string.Empty;
        }

        // fades in the audio track
        private float _currentFadeTime = 0f;
        private IEnumerator FadeInVideoAudio(ushort trackIndex, float fadeInTime)
        {
            _currentFadeTime = 0f;
            while (_currentFadeTime < fadeInTime)
            {
                float audioVolume = _currentFadeTime / fadeInTime;
                _videoPlayer.SetDirectAudioVolume(trackIndex, audioVolume);
                //Debug.Log($"audioVolume: {audioVolume:F3}, currentTime: {_currentFadeTime:F3}, fadeInTime: {fadeInTime:F3}");

                _currentFadeTime += Time.deltaTime;
                yield return null;
            }
        }


        // invoked when the video is prepared
        private void VideoPlayer_PrepareCompleted(UnityEngine.Video.VideoPlayer source)
        {
            //Debug.Log("video w: " + source.width + ", h: " + source.height);
            externalTexture = KinectInterop.CreateRenderTexture(null, (int)source.width, (int)source.height);
            externalTexture.name = KinectInterop.GetFileName(_unicamInt.recordingFile, false);
            isExtTextureCreated = true;

            source.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture;
            source.targetTexture = (RenderTexture)externalTexture;
            source.Play();

            // fade-in audio
            StartCoroutine(FadeInVideoAudio(0, 2f));

            DoOpenDevice();
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
            Debug.Log("Requesting webcam permission...");
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
                    Debug.LogError($"Webcam permission {authenticationType} denied");
                else
                    Debug.Log($"Webcam permission {authenticationType} granted");
            }
        }
#elif UNITY_ANDROID
        private void PermissionCallbacksPermissionGranted(string permissionName)
        {
            Debug.LogError($"Webcam permission {permissionName} granted");
            StartCoroutine(DelayedCameraInitialization());
        }

        private IEnumerator DelayedCameraInitialization()
        {
            yield return null;
            DoOpenDevice();
        }

        private void PermissionCallbacksPermissionDenied(string permissionName)
        {
            Debug.LogError($"Webcam permission {permissionName} denied");
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
            if (externalTexture != null)
            {
                // don't h-flip static images
                _savedFlipHoriz = flipHorizontally;

                if (flipHorizontally)
                {
                    //Debug.LogWarning("  D" + _deviceIndex + " static image or video. Setting horizontal flipping to false.");
                    flipHorizontally = false;
                }

                _deviceId = externalTexture.name;
                _deviceState = DeviceState.Working;
                Debug.Log($"D{_unicamInt.deviceIndex} Opened texture ID: {_deviceId}, resolution: {externalTexture.width} x {externalTexture.height}");
            }
            else
            {
                var alSensors = GetAvailableDevices();

                if (_kinectManager && _kinectManager.consoleLogMessages)
                {
                    // display the list of available devices
                    System.Text.StringBuilder sbBuf = new System.Text.StringBuilder();
                    sbBuf.AppendLine("Available webcam devices:");

                    for (int i = 0; i < alSensors.Count; i++)
                    {
                        sbBuf.Append($"D{i}: ").AppendLine(alSensors[i].ToString());
                    }

                    Debug.Log(sbBuf.ToString());
                }

                if (_deviceIndex >= alSensors.Count)
                {
                    Debug.LogError("  D" + _deviceIndex + " is not available.");
                    return null;
                }

                KinectInterop.SensorDeviceInfo deviceInfo = alSensors[_deviceIndex];

                if (requestedResolution != Vector2Int.zero)
                {
                    _webcam = new WebCamTexture(deviceInfo.sensorName, requestedResolution.x, requestedResolution.y);
                }
                else
                {
                    _webcam = new WebCamTexture(deviceInfo.sensorName);
                }

                // try to open the sensor
                _webcam.Play();

                //// don't h-flip back-facing cameras
                //bool isBackFacing = Application.isMobilePlatform && !WebCamTexture.devices[_deviceIndex].isFrontFacing;
                //if (flipHorizontally && isBackFacing)
                //{
                //    Debug.LogWarning("  D" + _deviceIndex + " back facing. Setting horizontal flipping to false.");
                //    flipHorizontally = false;
                //}

                _deviceId = deviceInfo.sensorName;
                _deviceState = DeviceState.Working;
                _isFrontFacing = deviceInfo.isFrontFacing;

                Debug.Log($"D{_unicamInt.deviceIndex} Opened webcam ID: {_deviceId}\nresolution: {_webcam.width}x{_webcam.height}");  // +
                    // $"\nrequested: {requestedResolution}, rotAngle: {_webcam.videoRotationAngle}, vMirror: {_webcam.videoVerticallyMirrored}, ori: {Screen.orientation}");
            }

            // create recording if needed
            if (_unicamInt.deviceStreamingMode == KinectInterop.DeviceStreamingMode.SaveRecording)
            {
                Debug.LogWarning("Webcam doesn't support saving recordings!");
            }

            // set device start time
            _deviceStartTime = (ulong)(Time.time * 10000000f);  // (ulong)DateTime.UtcNow.Ticks;
            //Debug.Log($"D{_unicamInt.deviceIndex} Webcam device started at ts: {_deviceStartTime}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));

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

            if (_webcam)
            {
                if(_webcam.isPlaying)
                {
                    _webcam.Stop();
                }

                KinectInterop.Destroy(_webcam);
                _webcam = null;
            }

            if(_videoPlayer != null)
            {
                Destroy(_videoPlayer);
                _videoPlayer = null;

                //StopCoroutine(FadeInVideoAudio(0, 2f));
            }

            if(externalTexture != null)
            {
                // restore h-flip
                flipHorizontally = _savedFlipHoriz;
            }

            if (isExtTextureCreated && externalTexture)
            {
                KinectInterop.Destroy(externalTexture);
                externalTexture = null;
            }

            //_lastFrameImage?.Release();
            //_lastFrameImage = null;

            //if (_colorTex)
            //{
            //    KinectInterop.Destroy(_colorTex);
            //    _colorTex = null;
            //}
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

            if (externalTexture != null)
            {
                //camW = externalTexture.width; camH = externalTexture.height;
                w = (!isVertical ? externalTexture.width : externalTexture.height) & 0xFFF0;
                h = (!isVertical ? externalTexture.height : externalTexture.width) & 0xFFFC;
            }
            else if(_webcam != null)
            {
                //camW = _webcam.width; camH = _webcam.height;
                w = (!isVertical ? _webcam.width : _webcam.height) & 0xFFF0;
                h = (!isVertical ? _webcam.height : _webcam.width) & 0xFFFC;
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
                            flipVertically = !_isFrontFacing;
                            break;

                        case ScreenOrientation.LandscapeRight:
                            cameraOrientation = CameraOrientationEnum.Default;
                            flipHorizontally = true;
                            flipVertically = _isFrontFacing;
                            break;

                        case ScreenOrientation.PortraitUpsideDown:
                            cameraOrientation = CameraOrientationEnum.Clockwise90;
                            flipHorizontally = !_isFrontFacing;
                            flipVertically = false;
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
            if (fsData._isRawFrameReady)
                return;

            if(_unicamInt.deviceStreamingMode != _streamMode || _unicamInt.deviceIndex != _deviceIndex || _unicamInt.recordingFile != _recordingFile)
            {
                Debug.LogWarning($"Restarting webcam device for: {_unicamInt.deviceStreamingMode}, devIndex: {_unicamInt.deviceIndex}, recFile: {_unicamInt.recordingFile}");
                CloseDevice();
                OpenDevice(_unicamInt, _unicamInt.deviceIndex, _kinectManager);
            }

            // update the buffer texture, if needed
            Vector2Int imageSize = GetImageSize();
            if (imageSize.x == 0 || imageSize.y == 0)
                return;

            RenderTexture colorImage = (RenderTexture)fsData._rawFrame;
            if (colorImage == null || colorImage.width != imageSize.x || colorImage.height != imageSize.y)
            {
                fsData._rawFrame = colorImage = KinectInterop.CreateRenderTexture(colorImage, imageSize.x, imageSize.y, RenderTextureFormat.ARGB32);

                //if(fsData._fsIndex == 0)
                //    Debug.Log($"D{_unicamInt.deviceIndex} Updated device resolution to: {imageSize.x}x{imageSize.y}, fs: {fsData._fsIndex}");  // +
                //        // (_webcam ? $"\nrotAngle: {_webcam.videoRotationAngle}, vMirror: {_webcam.videoVerticallyMirrored}, ori: {Screen.orientation}" : string.Empty));
            }

            //if(_lastFrameImage == null || _lastFrameImage.width != colorImage.width || _lastFrameImage.height != colorImage.height)
            //{
            //    _lastFrameImage = KinectInterop.CreateRenderTexture(_lastFrameImage, colorImage.width, colorImage.height, colorImage.format);
            //}

            // restart the camera if needed
            if (_webcam != null && !_webcam.isPlaying)
            {
                try
                {
                    Debug.LogWarning("Webcam stopped playing. Trying to restart it...");
                    _webcam.Stop();
                    _webcam.Play();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            // set the playback time, in seconds
            if (_videoPlayer != null && _videoPlayer.canSetTime && playbackPosSeconds >= 0f &&
                playbackPosSeconds < _videoPlayer.length)
            {
                _videoPlayer.time = playbackPosSeconds;
            }

            ulong frameTs = (ulong)(Time.time * 10000000f) - _deviceStartTime + 1;  // (ulong)DateTime.UtcNow.Ticks - _deviceStartTime;;
            //Debug.Log($"fs: {fsData._fsIndex} GetRawFrame - frameTs: {frameTs}, lastTs: {_lastFrameTime}\ndTs: {frameTs - _lastFrameTime}");

            if (_lastFrameTime != 0 && frameTs >= _lastFrameTime && (frameTs - _lastFrameTime) < KinectInterop.MIN_TIME_BETWEEN_IN_FRAMES)
            {
                //Graphics.CopyTexture(_lastFrameImage, colorImage);
                //fsData._rawFrameTimestamp = frameTs;
                //fsData._isRawFrameReady = fsData._rawFrameTimestamp > 0;
                ////Debug.Log($"\n    fs: {fsData._fsIndex} RawFrame ts: {frameTs} will copy frame from ts: {_lastFrameTime}\ndTs: {frameTs - _lastFrameTime}");

                //Debug.Log($"    fs: {fsData._fsIndex} WebCamFrame skipped. ts: {frameTs}, dTs: {frameTs - _lastFrameTime}\nframeReady: {fsData._isRawFrameReady}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                return;
            }

            // buffer the texture
            Texture colorTex = externalTexture != null ? externalTexture : _webcam;  // (_webcam != null && _webcam.didUpdateThisFrame ? _webcam : null);
            if (colorTex != null)
            {
                if (cameraOrientation != CameraOrientationEnum.Default || flipHorizontally || flipVertically)
                    KinectInterop.TransformTexture(colorTex, colorImage, (int)cameraOrientation, flipHorizontally, flipVertically);
                else
                    Graphics.Blit(colorTex, colorImage);

                //ulong prevRawFrameTimestamp = fsData._rawFrameTimestamp;
                fsData._rawFrameTimestamp = frameTs;
                fsData._isRawFrameReady = fsData._rawFrameTimestamp > 0 && fsData._rawFrameTimestamp != _lastFrameTime;

                // debug image
                if (debugImage != null)
                {
                    debugImage.texture = colorTex;

                    float debugH = debugImage.rectTransform.sizeDelta.y;
                    debugImage.rectTransform.sizeDelta = new Vector2(debugH * debugImage.texture.width / debugImage.texture.height, debugH);
                }

                // debug image 2
                if (debugImage2 != null)
                {
                    debugImage2.texture = colorImage;

                    float debugH = debugImage2.rectTransform.sizeDelta.y;
                    debugImage2.rectTransform.sizeDelta = new Vector2(debugH * debugImage2.texture.width / debugImage2.texture.height, debugH);
                }

                // if (fsData._isRawFrameReady)
                //     Debug.Log($"fs: {fsData._fsIndex}, WebCamFrame ready - ts: {fsData._rawFrameTimestamp}, dTs: {fsData._rawFrameTimestamp - _lastFrameTime}\nframeReady: {fsData._isRawFrameReady}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));

                //_lastFrameImage = colorImage;
                //Graphics.CopyTexture(colorImage, _lastFrameImage);
                _lastFrameTime = fsData._rawFrameTimestamp;
            }
        }

        /// <summary>
        /// Sets or clears the device frame-ready flag.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        /// <param name="isReady">true if the frame is ready, false otherwise</param>
        public void SetFrameReady(providers.FramesetData fsData, DeviceFrame frameType, bool isReady)
        {
            if (frameType == DeviceFrame.All)
            {
                fsData._isRawFrameReady = isReady;
                //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsData._fsIndex}, " + (!isReady ? " Cleared" : " Set") + $" frame-ready flag - ts: {fsData._rawFrameTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }
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
            fsData._isProcFrameReady = isReady;
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
