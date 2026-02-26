using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using com.rfilkov.providers;
using com.rfilkov.devices;


namespace com.rfilkov.kinect
{
    /// <summary>
    /// Universal Camera Interface - works with different devices and stream providers.
    /// </summary>
    public class UniCamInterface : DepthSensorBase
    {
        [Tooltip("Whether to loop the playback, when end of recording is reached.")]
        public bool loopPlayback = false;

        [Tooltip("Whether the body tracker should detect multiple users or a single user only.")]
        public bool detectMultipleUsers = true;

        [Tooltip("Whether to try to recalibrate the camera on the next run, or not.")]
        public bool recalibrateCamera = false;

        [Tooltip("UI text to display debug messages.")]
        public UnityEngine.UI.Text debugText;

        //[Tooltip("UI raw-image to be used for debugging purposes.")]
        //public UnityEngine.UI.RawImage debugImage;

        //[Tooltip("UI raw-image 2 to be used for debugging purposes.")]
        //public UnityEngine.UI.RawImage debugImage2;

        // sensor data
        private KinectInterop.SensorData _sensorData = null;

        // stream providers
        [HideInInspector]
        public IColorStreamProvider _colorStreamProvider = null;
        [HideInInspector]
        public IDepthStreamProvider _depthStreamProvider = null;
        [HideInInspector]
        public IIRStreamProvider _irStreamProvider = null;
        [HideInInspector]
        public IBodyStreamProvider _bodyStreamProvider = null;
        [HideInInspector]
        public IObjectStreamProvider _objectStreamProvider = null;
        [HideInInspector]
        public IPoseStreamProvider _poseStreamProvider = null;
        [HideInInspector]
        public ICamIntrStreamProvider _camIntrStreamProvider = null;

        [HideInInspector]
        public static ulong _frameIndex = 0;
        [HideInInspector]
        public static float _frameTs = 0f;

        // list of device managers & stream providers
        private List<IDeviceManager> _deviceManagers = new List<IDeviceManager>();
        private List<IStreamProvider> _streamProviders = new List<IStreamProvider>();

        // stream providers with delayed start (because of the device)
        private List<IStreamProvider> _delayedProviders = new List<IStreamProvider>();

        // dictionaties
        private Dictionary<string, Component> _devStrComponent = new Dictionary<string, Component>();

        // frameset manager
        private FramesetManager _fsManager = null;
        private bool _dontOpenMoreFrames = false;

        // scaled frame properties
        private int _scaledColorWidth = 0;
        private int _scaledColorHeight = 0;
        private int _scaledDepthWidth = 0;
        private int _scaledDepthHeight = 0;
        private int _scaledIrWidth = 0;
        private int _scaledIrHeight = 0;
        private int _scaledBodyIndexWidth = 0;
        private int _scaledBodyIndexHeight = 0;
        private int _scaledObjIndexWidth = 0;
        private int _scaledObjIndexHeight = 0;


        //// transformed image props
        //private Material colorFlippedImageMaterial = null;
        //private ComputeBuffer colorDepthBuffer = null;
        //private ComputeBuffer colorBodyIndexBuffer = null;
        //private ulong lastColorDepthBufferTime = 0;
        //private ulong lastColorBodyIndexBufferTime = 0;

        // UniCam capabilities
        private const KinectInterop.FrameSource UNICAM_CAPS = KinectInterop.FrameSource.TypeColor | KinectInterop.FrameSource.TypeDepth |
            KinectInterop.FrameSource.TypeBody | KinectInterop.FrameSource.TypeBodyIndex |
            KinectInterop.FrameSource.TypeObject | KinectInterop.FrameSource.TypeObjectIndex;  // | KinectInterop.FrameSource.TypePose;


        // depth sensor settings
        [System.Serializable]
        public class UniCamSettings : BaseSensorSettings
        {
            public bool loopPlayback;
        }


        public override KinectInterop.DepthSensorPlatform GetSensorPlatform()
        {
            return KinectInterop.DepthSensorPlatform.UniCam;
        }


        public override System.Type GetSensorSettingsType()
        {
            return typeof(UniCamSettings);
        }


        public override BaseSensorSettings GetSensorSettings(BaseSensorSettings settings)
        {
            if (settings == null)
            {
                settings = new UniCamSettings();
            }

            UniCamSettings extSettings = (UniCamSettings)base.GetSensorSettings(settings);

            extSettings.loopPlayback = loopPlayback;

            return settings;
        }

        public override void SetSensorSettings(BaseSensorSettings settings)
        {
            if (settings == null)
                return;

            base.SetSensorSettings(settings);

            UniCamSettings extSettings = (UniCamSettings)settings;
            loopPlayback = extSettings.loopPlayback;
        }

        public override List<KinectInterop.SensorDeviceInfo> GetAvailableSensors()
        {
            // create stream providers and device managers, as needed
            if (_streamProviders.Count == 0)
            {
                CreateStreamProviders();
            }

            // get available devices
            List<KinectInterop.SensorDeviceInfo> alSensorInfo = new List<KinectInterop.SensorDeviceInfo>();

            foreach (var deviceMgr in _deviceManagers)
            {
                var devices = deviceMgr.GetAvailableDevices();
                alSensorInfo.AddRange(devices);
            }

            //if (alSensorInfo.Count == 0)
            //{
            //    Debug.Log("  No sensor devices found.");
            //}

            return alSensorInfo;
        }

        // creates the configured stream providers and device managers
        private void CreateStreamProviders()
        {
            KinectManager kinectManager = KinectManager.Instance;
            var uniCamConfig = (UniCamConfig)Resources.Load("UniCamConfig");

            if (uniCamConfig == null)
                throw new Exception("UniCamConfig not found. Please check if the asset exists in resources.");

            // color stream provider
            if (string.IsNullOrEmpty(uniCamConfig.colorStreamProvider))
                Debug.LogWarning("Color stream provider is not configured. Please check UniCamConfig settings.");
            else if (_colorStreamProvider == null)
            {
                Type providerType = Type.GetType(uniCamConfig.colorStreamProvider);

                var providerComponent = gameObject.GetComponent(providerType);
                if (providerComponent == null)
                    providerComponent = gameObject.AddComponent(providerType);
                _colorStreamProvider = (IColorStreamProvider)providerComponent;

                _streamProviders.Add(_colorStreamProvider);
                _devStrComponent[uniCamConfig.colorStreamProvider] = providerComponent;
            }

            // check for depth-camera
            if (uniCamConfig.colorStreamProvider == "com.rfilkov.providers.WebcamColorStreamProvider" &&
                uniCamConfig.depthStreamProvider == "com.rfilkov.providers.WebcamDepthStreamProvider")
            {
                var webcamDevices = WebCamTexture.devices;

                if(deviceIndex >= 0 && deviceIndex < webcamDevices.Length &&
                    !string.IsNullOrEmpty(webcamDevices[deviceIndex].depthCameraName))
                {
                    uniCamConfig.depthStreamProvider = "com.rfilkov.providers.DepthcamStreamProvider";
                    Debug.Log($"D{deviceIndex} DepthStreamProvider updated to: {uniCamConfig.depthStreamProvider}");
                }
            }

            // depth stream provider
            if (string.IsNullOrEmpty(uniCamConfig.depthStreamProvider))
                Debug.LogWarning("Depth stream provider is not configured. Please check UniCamConfig settings.");
            else if (_depthStreamProvider == null)
            {
                Type providerType = Type.GetType(uniCamConfig.depthStreamProvider);

                var providerComponent = gameObject.GetComponent(providerType);
                if (providerComponent == null)
                    providerComponent = gameObject.AddComponent(providerType);
                _depthStreamProvider = (IDepthStreamProvider)providerComponent;

                _streamProviders.Add(_depthStreamProvider);
                _devStrComponent[uniCamConfig.depthStreamProvider] = providerComponent;
            }

            // body stream provider
            //if (string.IsNullOrEmpty(uniCamConfig.bodyStreamProvider))
            //    Debug.LogWarning("Body stream provider is not configured. Please check UniCamConfig settings.");
            //else if (_bodyStreamProvider == null)
            if (kinectManager != null && kinectManager.getBodyFrames != KinectManager.BodyTextureType.None)
            {
                if (_bodyStreamProvider == null && !string.IsNullOrEmpty(uniCamConfig.bodyStreamProvider))
                {
                    Type providerType = Type.GetType(uniCamConfig.bodyStreamProvider);
                    //Debug.Log($"Body provider: {uniCamConfig.bodyStreamProvider}\nobj: {gameObject.name}, comp: {gameObject.GetComponent(providerType)}");

                    var providerComponent = gameObject.GetComponent(providerType);
                    if (providerComponent == null)
                        providerComponent = gameObject.AddComponent(providerType);
                    _bodyStreamProvider = (IBodyStreamProvider)providerComponent;

                    _streamProviders.Add(_bodyStreamProvider);
                    _devStrComponent[uniCamConfig.bodyStreamProvider] = providerComponent;
                }
            }

            // object stream provider
            if (kinectManager != null && kinectManager.getObjectFrames != KinectManager.ObjectTrackingType.None)
            {
                if (_objectStreamProvider == null && !string.IsNullOrEmpty(uniCamConfig.objectStreamProvider))
                {
                    Type providerType = Type.GetType(uniCamConfig.objectStreamProvider);

                    var providerComponent = gameObject.GetComponent(providerType);
                    if (providerComponent == null)
                        providerComponent = gameObject.AddComponent(providerType);
                    _objectStreamProvider = (IObjectStreamProvider)providerComponent;

                    _streamProviders.Add(_objectStreamProvider);
                    _devStrComponent[uniCamConfig.objectStreamProvider] = providerComponent;
                }
            }

            // IR stream provider
            if (kinectManager != null && kinectManager.getInfraredFrames != KinectManager.InfraredTextureType.None)
            {
                if (_irStreamProvider == null && !string.IsNullOrEmpty(uniCamConfig.IRStreamProvider))
                {
                    Type providerType = Type.GetType(uniCamConfig.IRStreamProvider);

                    var providerComponent = gameObject.GetComponent(providerType);
                    if (providerComponent == null)
                        providerComponent = gameObject.AddComponent(providerType);
                    _irStreamProvider = (IIRStreamProvider)providerComponent;

                    _streamProviders.Add(_irStreamProvider);
                    _devStrComponent[uniCamConfig.IRStreamProvider] = providerComponent;
                }
            }

            // pose stream provider
            if (kinectManager != null && kinectManager.getPoseFrames != KinectManager.PoseUsageType.None)
            {
                if (_poseStreamProvider == null && !string.IsNullOrEmpty(uniCamConfig.poseStreamProvider))
                {
                    Type providerType = Type.GetType(uniCamConfig.poseStreamProvider);

                    var providerComponent = gameObject.GetComponent(providerType);
                    if (providerComponent == null)
                        providerComponent = gameObject.AddComponent(providerType);
                    _poseStreamProvider = (IPoseStreamProvider)providerComponent;

                    _streamProviders.Add(_poseStreamProvider);
                    _devStrComponent[uniCamConfig.poseStreamProvider] = providerComponent;
                }
            }

            // create the device managers
            foreach (var provider in _streamProviders)
            {
                string deviceMgrClass = provider.GetDeviceManagerClass();

                if (!string.IsNullOrEmpty(deviceMgrClass) && !_devStrComponent.ContainsKey(deviceMgrClass))
                {
                    Type deviceMgrType = Type.GetType(deviceMgrClass);

                    var deviceMgrComponent = gameObject.GetComponent(deviceMgrType);
                    if (deviceMgrComponent == null)
                        deviceMgrComponent = gameObject.AddComponent(deviceMgrType);
                    var deviceMgr = (IDeviceManager)deviceMgrComponent;

                    _deviceManagers.Add(deviceMgr);
                    _devStrComponent[deviceMgrClass] = deviceMgrComponent;
                }
            }
        }


        /// <summary>
        /// Returns the FS-manager instance.
        /// </summary>
        public FramesetManager FramesetManager
        {
            get => _fsManager;
        }


        public override KinectInterop.SensorData OpenSensor(KinectManager kinectManager, KinectInterop.FrameSource dwFlags, bool bSyncDepthAndColor, bool bSyncBodyAndDepth)
        {
            // save initial parameters
            base.OpenSensor(kinectManager, dwFlags, bSyncDepthAndColor, bSyncBodyAndDepth);

            // create stream providers and device managers, as needed
            if (_streamProviders.Count == 0)
            {
                CreateStreamProviders();
            }

            // create sensor data
            _sensorData = new KinectInterop.SensorData();
            _frameIndex = 0;
            _frameTs = 0f;

            // create frameset manager
            _fsManager = FramesetManager.Instance;
            _fsManager.ResetFramesetIndices();
            // clear first frame flags
            _fsManager.GetLastOpenFrameset().ClearReadyFlags();

            // start all stream providers
            sensorDeviceId = string.Empty;

            foreach (var provider in _streamProviders)
            {
                string devMgrClass = provider.GetDeviceManagerClass();

                if(!string.IsNullOrEmpty(devMgrClass))
                {
                    var devMgr = (IDeviceManager)_devStrComponent[devMgrClass];
                    var devState = devMgr.GetDeviceState();
                    string deviceId = string.Empty;

                    if (devState == DeviceState.NotStarted || devState == DeviceState.Stopped || devState == DeviceState.Unknown)
                    {
                        int devIndex = provider.GetDeviceIndex(this, devMgr.GetAvailableDevices());

                        deviceId = devMgr.OpenDevice(this, deviceIndex, kinectManager, DeviceManagerReady);
                        if (string.IsNullOrEmpty(deviceId))
                            throw new Exception($"D{devIndex}: Sorry, can't open device of type: {devMgrClass}");

                        devState = devMgr.GetDeviceState();
                    }

                    if (devState == DeviceState.Working)
                        provider.StartProvider(this, devMgr, kinectManager, _sensorData);
                    else if (devState != DeviceState.ErrorOccured)
                        _delayedProviders.Add(provider);

                    sensorDeviceId += deviceId + " ";
                }
                else
                {
                    // start stream provider directly
                    provider.StartProvider(this, null, kinectManager, _sensorData);
                }
            }

            // try to open the sensor or play back the recording
            if (_delayedProviders.Count == 0)
            {
                DoOpenSensor(_sensorData, dwFlags, bSyncDepthAndColor, bSyncBodyAndDepth);
            }

            return _sensorData;
        }

        // invoked when the device manager gets started
        private void DeviceManagerReady(string deviceClass, KinectManager kinectManager)
        {
            // start delayed providers
            foreach(var provider in _delayedProviders)
            {
                string devMgrClass = provider.GetDeviceManagerClass();

                if (devMgrClass == deviceClass)
                {
                    IDeviceManager devMgr = _devStrComponent[devMgrClass] as IDeviceManager;
                    provider.StartProvider(this, devMgr, kinectManager, _sensorData);
                }
            }

            // remove delayed providers
            for (int i = _delayedProviders.Count - 1; i >= 0; i--)
            {
                var provider = _delayedProviders[i];
                string devMgrClass = provider.GetDeviceManagerClass();

                if(devMgrClass == deviceClass)
                {
                    _delayedProviders.RemoveAt(i);
                }

            }

            // check if all devices and providers are started
            if(_delayedProviders.Count == 0)
            {
                DoOpenSensor(_sensorData, frameSourceFlags, isSyncDepthAndColor, isSyncBodyAndDepth);
                
                // invoke InitSensorData() again
                KinectInterop.InitSensorData(_sensorData, KinectManager.Instance);
            }
        }

        // fills in the sensor data, when available
        private void DoOpenSensor(KinectInterop.SensorData sensorData, KinectInterop.FrameSource dwFlags, bool bSyncDepthAndColor, bool bSyncBodyAndDepth)
        {
            sensorPlatform = KinectInterop.DepthSensorPlatform.UniCam;
            sensorData.sensorCaps = UNICAM_CAPS;

            sensorData.sensorIntPlatform = sensorPlatform;
            sensorData.startTimeOffset = DateTime.UtcNow.Ticks;

            sensorDeviceId = sensorDeviceId.Trim();
            sensorData.sensorId = sensorDeviceId;
            sensorData.sensorName = sensorDeviceId;

            // color & depth image scales
            sensorData.colorImageScale = _colorStreamProvider != null ? _colorStreamProvider.GetColorImageScale() : Vector3.one;
            sensorData.depthImageScale = _depthStreamProvider != null ? _depthStreamProvider.GetDepthImageScale() : Vector3.one;
            sensorData.infraredImageScale = _irStreamProvider != null ? _irStreamProvider.GetIRImageScale() : Vector3.one;
            sensorData.sensorSpaceScale = _depthStreamProvider != null ? _depthStreamProvider.GetSensorSpaceScale() : Vector3.one;
            sensorData.unitToMeterFactor = 0.001f;

            // depth camera offset & matrix z-flip
            sensorRotOffset = Vector3.zero;
            sensorRotFlipZ = true;
            sensorRotIgnoreY = true;

            // color camera data & intrinsics
            sensorData.colorImageFormat = _colorStreamProvider != null ? _colorStreamProvider.GetColorImageFormat() : TextureFormat.RGB24;
            sensorData.colorImageStride = _colorStreamProvider != null ? _colorStreamProvider.GetColorImageStride() : 3;  // bytes per pixel

            if ((dwFlags & KinectInterop.FrameSource.TypeColor) != 0 && _colorStreamProvider != null)
            {
                var colorImageSize = _colorStreamProvider.GetColorImageSize();
                sensorData.colorImageWidth = colorImageSize.x;
                sensorData.colorImageHeight = colorImageSize.y;

                sensorData.colorImageTexture = KinectInterop.CreateRenderTexture((RenderTexture)sensorData.colorImageTexture, sensorData.colorImageWidth, sensorData.colorImageHeight); // new Texture2D(sensorData.colorImageWidth, sensorData.colorImageHeight, TextureFormat.RGB24, false);

                //_colorStreamProvider.SetColorImageProps(sensorData.colorImageTexture, colorFrameLock, sensorData);
            }

            // depth camera data & intrinsics
            if ((dwFlags & KinectInterop.FrameSource.TypeDepth) != 0 && _depthStreamProvider != null)
            {
                var depthImageSize = _depthStreamProvider.GetDepthImageSize();
                sensorData.depthImageWidth = depthImageSize.x;
                sensorData.depthImageHeight = depthImageSize.y;

                //rawDepthImage = new ushort[sensorData.depthImageWidth * sensorData.depthImageHeight];
                sensorData.depthImage = new ushort[sensorData.depthImageWidth * sensorData.depthImageHeight];

                //_depthStreamProvider.SetDepthFrameProps(rawDepthImage, depthFrameLock, sensorData);
            }

            // infrared data
            if ((dwFlags & KinectInterop.FrameSource.TypeInfrared) != 0 && _irStreamProvider != null)
            {
                var irImageSize = _irStreamProvider.GetIRImageSize();

                //rawInfraredImage = new ushort[sensorData.depthImageWidth * sensorData.depthImageHeight];
                sensorData.infraredImage = new ushort[irImageSize.x * irImageSize.y];

                minInfraredValue = _irStreamProvider.GetMinIRValue();  // 0f;
                maxInfraredValue = _irStreamProvider.GetMaxIRValue();  // 1000f;

                //if (debugImage != null)
                //{
                //    _irStreamProvider.SetDebugImage(debugImage);
                //}
            }

            // body & body-index data
            if ((dwFlags & (KinectInterop.FrameSource.TypeBody | KinectInterop.FrameSource.TypeBodyIndex)) != 0 && _bodyStreamProvider != null)
            {
                // init basic body variables
                base.InitBodyTracking(dwFlags, sensorData);
                _bodyStreamProvider.SetDetectMultipleUsers(detectMultipleUsers);

                var bodyIndexImageSize = _bodyStreamProvider.GetBodyIndexImageSize();
                //rawBodyIndexImage = new byte[bodyIndexImageSize.x * bodyIndexImageSize.y];
                sensorData.bodyIndexImage = new byte[bodyIndexImageSize.x * bodyIndexImageSize.y];

                //_bodyStreamProvider.SetBodyIndexFrameProps(rawBodyIndexImage, bodyTrackerLock, sensorData);
            }

            // object & object-index data
            if ((dwFlags & (KinectInterop.FrameSource.TypeObject | KinectInterop.FrameSource.TypeObjectIndex)) != 0 && _objectStreamProvider != null)
            {
                // init basic object variables
                base.InitObjectTracking(dwFlags, sensorData);

                var objIndexImageSize = _objectStreamProvider.GetObjectIndexImageSize();
                //rawObjectIndexImage = new byte[objIndexImageSize.x * objIndexImageSize.y];
                sensorData.objectIndexImage = new byte[objIndexImageSize.x * objIndexImageSize.y];

                //_objectStreamProvider.SetObjectIndexFrameProps(rawObjectIndexImage, objectTrackerLock, sensorData);
            }

            // get camera intrinsics & extrinsics
            if ((dwFlags & KinectInterop.FrameSource.TypeColor) != 0 && _colorStreamProvider != null)
                sensorData.colorCamIntr = _colorStreamProvider.GetCameraIntrinsics();
            if ((dwFlags & KinectInterop.FrameSource.TypeDepth) != 0 && _depthStreamProvider != null)
                sensorData.depthCamIntr = _depthStreamProvider.GetCameraIntrinsics();
            if(_depthStreamProvider != null)
                sensorData.depth2ColorExtr = _depthStreamProvider.GetCameraExtrinsics();
            if(_colorStreamProvider != null)
                sensorData.color2DepthExtr = _colorStreamProvider.GetCameraExtrinsics();

            // if invalid, try to estimate them
            if(sensorData.colorCamIntr == null || sensorData.depthCamIntr == null || 
                sensorData.depth2ColorExtr == null || sensorData.color2DepthExtr == null)
            {
                TryEstimateCameraIntrExtr(sensorData, fsData: null, KinectManager.Instance);  // no frameset yet, at this point
            }

            // don't get all frames
            getAllSensorFrames = false;

            if (consoleLogMessages)
                Debug.Log($"D{deviceIndex} UniCam-sensor opened: {sensorDeviceId}");
        }

        public override void CloseSensor(KinectInterop.SensorData sensorData)
        {
            base.CloseSensor(sensorData);

            // release frame resources
            int numFramesets = FramesetManager.FRAMESETS_COUNT;
            for (int i = 0; i < numFramesets; i++)
            {
                FramesetData fsData = _fsManager.GetFramesetAt(i);

                foreach (var provider in _streamProviders)
                {
                    provider.ReleaseFrameData(fsData);
                }
            }

            // stop all stream providers
            foreach (var provider in _streamProviders)
            {
                provider.StopProvider();
            }

            // close all devices
            foreach(var devMgr in _deviceManagers)
            {
                devMgr.CloseDevice();
            }

            _streamProviders.Clear();
            _deviceManagers.Clear();

            _delayedProviders.Clear();
            _devStrComponent.Clear();

            _colorStreamProvider = null;
            _depthStreamProvider = null;
            _irStreamProvider = null;
            _bodyStreamProvider = null;
            _objectStreamProvider = null;
            _poseStreamProvider = null;
            _camIntrStreamProvider = null;

            // stop sentis manager
            if(SentisManager.Instance != null)
            {
                SentisManager.Instance.StopManager();
                Destroy(SentisManager.Instance);
            }

            if (consoleLogMessages)
                Debug.Log($"D{deviceIndex} UniCam-sensor closed: {sensorDeviceId}");
        }


        /// <summary>
        /// Returns the first open frameset (w), or null.
        /// </summary>
        /// <returns></returns>
        public FramesetData GetFirstOpenFrameset()
        {
            int openFsCount = _fsManager.OpenFramesetsCount;

            for (int i = 0; i < openFsCount; i++)
            {
                FramesetData fsData = _fsManager.GetOpenFramesetAt(i);
                if (!fsData._isFramesetReady)
                    return fsData;
            }

            return null;
        }

        /// <summary>
        /// Returns the last open frameset (w), or null.
        /// </summary>
        /// <returns></returns>
        public FramesetData GetLastOpenFrameset()
        {
            return _fsManager.GetLastOpenFrameset();
        }


        private ulong _curFrameCount = 0;
        private long _curFrameTs = DateTime.Now.Ticks;
        private long _lastFrameTs = DateTime.Now.Ticks;


        public override bool UpdateSensorData(KinectInterop.SensorData sensorData, KinectManager kinectManager, bool isPlayMode)
        {
            // frame index
            _frameTs = Time.unscaledDeltaTime;
            if (_frameIndex != ulong.MaxValue)
                _frameIndex++;
            else
                _frameIndex = 0;

            // check if device managers and stream providers are working
            foreach (var devMgr in _deviceManagers)
                if (devMgr.GetDeviceState() != DeviceState.Working)
                    return false;

            foreach (var provider in _streamProviders)
                if (provider.GetProviderState() != StreamState.Working)
                    return false;

            // check if providers are ready for the next frame
            FramesetData lastOpenFrame = _fsManager.GetLastOpenFrameset();
            //FramesetData firstOpenFrame = _fsManager.GetOpenFramesetAt(0);
            bool isReadyForNextFrame = true;  // firstOpenFrame != null ? firstOpenFrame.IsAnyFrameReady() : true;

            if(lastOpenFrame != null && isReadyForNextFrame)
            {
                foreach (var provider in _streamProviders)
                {
                    isReadyForNextFrame &= provider.IsReadyForNextFrame(sensorData, lastOpenFrame);
                    if (!isReadyForNextFrame)
                    {
                        //Debug.Log($"    fs: {lastOpenFrame._fsIndex}, {provider.GetType().Name} is not ready for the next frame.");
                        break;
                    }
                }
            }

            if (isReadyForNextFrame && !_dontOpenMoreFrames && _fsManager.IncWriteIndex())
            {
                lastOpenFrame = _fsManager.GetLastOpenFrameset();  // the new frameset
                lastOpenFrame._fsPrev = _fsManager.GetPrevOpenFrameset();

                foreach (var provider in _streamProviders)
                {
                    provider.ReleaseFrameData(lastOpenFrame);
                }

                lastOpenFrame.ClearReadyFlags();
                //System.Threading.Thread.Sleep(1);  // sleep for 1 ms before processing the frame
                //Debug.LogWarning($"Next open frame: {lastOpenFrame._fsIndex}, prev: {lastOpenFrame._fsPrev._fsIndex}");
            }

            // update all stream data
            int openFsCount = _fsManager.OpenFramesetsCount;
            //bool isFirstFrame = true;

            for (int i = 0; i < openFsCount; i++)
            {
                FramesetData fsData = _fsManager.GetOpenFramesetAt(i);

                if (fsData != null)
                {
                    //Debug.Log($"  processing open frame: {fsData._fsIndex} - {i + 1}/{openFsCount}, ready: {fsData._isFramesetReady}");
                    bool isLastFrame = lastOpenFrame != null && fsData._fsIndex == lastOpenFrame._fsIndex;

                    if (!fsData._isFramesetReady)
                    {
                        if (!isSyncDepthAndColor && isLastFrame)
                        {
                            if (_colorStreamProvider != null && !isSyncBodyAndDepth)
                                fsData.CopyPrevColorData();

                            if (_depthStreamProvider != null)
                                fsData.CopyPrevDepthData();
                            if (_irStreamProvider != null)
                                fsData.CopyPrevIrData();
                        }

                        if (!isSyncBodyAndDepth && isLastFrame)
                        {
                            //if (_colorStreamProvider != null && !isSyncDepthAndColor)  // check for copy in previous block
                            //    fsData.CopyPrevColorData();
                            if (_bodyStreamProvider != null)
                                fsData.CopyPrevBodyData();
                            if (_objectStreamProvider != null)
                                fsData.CopyPrevObjData();
                        }

                        // update all device data
                        foreach (var devMgr in _deviceManagers)
                        {
                            devMgr.UpdateDeviceData(sensorData, fsData);
                        }

                        foreach (var provider in _streamProviders)
                        {
                            bool allowUpdate = true;
                            if(!isSyncDepthAndColor)
                            {
                                if (provider is IDepthStreamProvider || provider is IIRStreamProvider)
                                    allowUpdate = isLastFrame;
                            }
                            if (!isSyncBodyAndDepth)
                            {
                                if (provider is IBodyStreamProvider || provider is IObjectStreamProvider)
                                    allowUpdate = isLastFrame;
                            }

                            if(allowUpdate)
                            {
                                //Debug.Log($"    fs: {fsData._fsIndex} - updating stream data for: {provider.GetType().Name}");
                                provider.UpdateStreamData(sensorData, fsData);
                            }
                        }

                        //isFirstFrame = false;
                    }
                }
            }

            // check whether to change the current frame
            FramesetData currentFrame = _fsManager.GetCurrentFrameset();
            if (currentFrame != null && currentFrame._isFramesetReady)
            {
                // go to next frameset
                _fsManager.IncReadIndex();
                currentFrame = _fsManager.GetCurrentFrameset();
            }

            // check if the current frame is ready
            //if (currentFrame != null)
            //    Debug.LogWarning($"Current frame: {currentFrame._fsIndex}, ready: {currentFrame._isFramesetReady}");
            if (currentFrame != null && !currentFrame._isFramesetReady)
            {
                // check for frame ready, according to the selected sync
                bool bFramesReady = IsFramesetReady(currentFrame);
                if (debugText != null)
                    debugText.text = $"{currentFrame._fsIndex}" + (bFramesReady ? " - ready" : "");

                if (bFramesReady)
                {
                    _curFrameCount = 0; _curFrameTs = DateTime.Now.Ticks;
                    currentFrame._isFramesetReady = true;

                    DoUpdateSensorData(sensorData, currentFrame, kinectManager, isPlayMode);

                    // clear device frame-ready flags
                    foreach (IDeviceManager devMgr in _deviceManagers)
                    {
                        bool useProcFrames = devMgr.IsUsingProcFrames;  // currentFrame._isUsingProcFrames;

                        if (useProcFrames && currentFrame._isProcFrameReady)
                            devMgr.SetProcFrameReady(currentFrame, DeviceFrame.All, false);
                        if (!useProcFrames && currentFrame._isRawFrameReady)
                            devMgr.SetFrameReady(currentFrame, DeviceFrame.All, false);
                    }

                    //Debug.Log($"D{deviceIndex}, fs: {currentFrame._fsIndex} Cleared frame-ready & proc-frame-ready flags");
                }
            }

            // update sensor-data arrays
            bool bSuccess = base.UpdateSensorData(sensorData, kinectManager, isPlayMode);

            // sleep for 1 ms to keep the webcam running
            System.Threading.Thread.Sleep(1);  // 200  // 1

            return bSuccess;
        }

        // checks if frameset is ready, according to the selected sync
        private bool IsFramesetReady(FramesetData currentFrame)
        {
            ulong devFrameTs = currentFrame._procFrameTimestamp;
            if (devFrameTs == 0L)
                devFrameTs = currentFrame._rawFrameTimestamp;

            if (devFrameTs == 0L)
                return false;

            bool isColorStreamOn = /**(frameSourceFlags & KinectInterop.FrameSource.TypeColor) != 0 &&*/ _colorStreamProvider != null;
            bool isDepthStreamOn = (frameSourceFlags & KinectInterop.FrameSource.TypeDepth) != 0 && _depthStreamProvider != null;
            bool isIrStreamOn = (frameSourceFlags & KinectInterop.FrameSource.TypeInfrared) != 0 && _irStreamProvider != null;
            bool isBodyDataStreamOn = (frameSourceFlags & KinectInterop.FrameSource.TypeBody) != 0 && _bodyStreamProvider != null;
            bool isBodyIndexStreamOn = (frameSourceFlags & KinectInterop.FrameSource.TypeBodyIndex) != 0 && _bodyStreamProvider != null;
            bool isObjDataStreamOn = (frameSourceFlags & KinectInterop.FrameSource.TypeObject) != 0 && _objectStreamProvider != null;
            bool isObjIndexStreamOn = (frameSourceFlags & KinectInterop.FrameSource.TypeObjectIndex) != 0 && _objectStreamProvider != null;

            bool isColorFrameReady = isColorStreamOn ? currentFrame._isColorImageReady && (!(isSyncDepthAndColor || isSyncBodyAndDepth) || currentFrame._colorImageTimestamp == devFrameTs) :
                (isSyncDepthAndColor || isSyncBodyAndDepth);
            bool isDepthFrameReady = isDepthStreamOn ? currentFrame._isDepthFrameReady && (!isSyncDepthAndColor || currentFrame._depthFrameTimestamp == devFrameTs) : isSyncDepthAndColor;
            bool isIrFrameReady = isIrStreamOn ? currentFrame._isIrFrameReady && (!isSyncDepthAndColor || currentFrame._irFrameTimestamp == devFrameTs) : isSyncDepthAndColor;
            bool isBodyDataFrameReady = isBodyDataStreamOn ? currentFrame._isBodyDataFrameReady && (!isSyncBodyAndDepth || currentFrame._bodyDataTimestamp == (devFrameTs + currentFrame._trackedBodiesCount)) : isSyncBodyAndDepth;
            bool isBodyIndexFrameReady = isBodyIndexStreamOn ? currentFrame._isBodyIndexFrameReady && (!isSyncBodyAndDepth || currentFrame._bodyIndexTimestamp == devFrameTs) : isSyncBodyAndDepth;
            bool isObjDataFrameReady = isObjDataStreamOn ? currentFrame._isObjDataFrameReady && (!isSyncBodyAndDepth || currentFrame._objDataTimestamp == (devFrameTs + currentFrame._trackedObjCount)) : isSyncBodyAndDepth;
            bool isObjIndexFrameReady = isObjIndexStreamOn ? currentFrame._isObjIndexFrameReady && (!isSyncBodyAndDepth || currentFrame._objIndexTimestamp == devFrameTs) : isSyncBodyAndDepth;

            bool isScaledColorFrameReady = isColorStreamOn && _colorStreamProvider.IsImageScalingEnabled() ?
                currentFrame._isScaledColorImageReady && (!(isSyncDepthAndColor || isSyncBodyAndDepth) || currentFrame._scaledColorImageTimestamp == devFrameTs) : (isSyncDepthAndColor || isSyncBodyAndDepth);
            bool isScaledDepthFrameReady = isDepthStreamOn && _depthStreamProvider.IsFrameScalingEnabled() ?
                currentFrame._isScaledDepthFrameReady && (!isSyncDepthAndColor || currentFrame._scaledDepthFrameTimestamp == devFrameTs) : isSyncDepthAndColor;
            bool isScaledIRFrameReady = isIrStreamOn && _irStreamProvider.IsFrameScalingEnabled() ?
                currentFrame._isScaledIrFrameReady && (!isSyncDepthAndColor || currentFrame._scaledIrFrameTimestamp == devFrameTs) : isSyncDepthAndColor;
            bool isScaledBodyIndexFrameReady = isBodyIndexStreamOn && _bodyStreamProvider.IsFrameScalingEnabled() ?
                currentFrame._isScaledBodyIndexFrameReady && (!isSyncBodyAndDepth || currentFrame._scaledBodyIndexFrameTimestamp == devFrameTs) : isSyncBodyAndDepth;
            bool isScaledObjIndexFrameReady = isObjIndexStreamOn && _objectStreamProvider.IsFrameScalingEnabled() ?
                currentFrame._isScaledObjIndexFrameReady && (!isSyncBodyAndDepth || currentFrame._scaledObjIndexFrameTimestamp == devFrameTs) : isSyncBodyAndDepth;

            bool isColorDepthSyncReady = isSyncDepthAndColor ?
                isColorFrameReady && isScaledColorFrameReady && isDepthFrameReady && isScaledDepthFrameReady && isIrFrameReady && isScaledIRFrameReady :
                isColorFrameReady || isScaledColorFrameReady || isDepthFrameReady || isScaledDepthFrameReady || isIrFrameReady || isScaledIRFrameReady;
            bool isBodyDepthSyncReady = isSyncBodyAndDepth ?
                isColorFrameReady && isScaledColorFrameReady && isBodyDataFrameReady && isBodyIndexFrameReady && isScaledBodyIndexFrameReady && isObjDataFrameReady && isObjIndexFrameReady && isScaledObjIndexFrameReady :
                isColorFrameReady || isScaledColorFrameReady || isBodyDataFrameReady || isBodyIndexFrameReady || isScaledBodyIndexFrameReady || isObjDataFrameReady || isObjIndexFrameReady || isScaledObjIndexFrameReady;

            bool bFramesReady = (isSyncDepthAndColor && isSyncBodyAndDepth) ? (isColorDepthSyncReady && isBodyDepthSyncReady) :
                 isSyncDepthAndColor ? isColorDepthSyncReady : isSyncBodyAndDepth ? isBodyDepthSyncReady : (isColorDepthSyncReady || isBodyDepthSyncReady);

            long deltaTimeFs = DateTime.Now.Ticks - _curFrameTs;
            bool bFrameTimeOK = deltaTimeFs >= KinectInterop.MIN_TIME_BETWEEN_OUT_FRAMES;

            _curFrameCount++;
            // Debug.LogWarning($"Current fs: {currentFrame._fsIndex}, devTs: {devFrameTs}, allReady: {bFramesReady}, timeOk: {bFrameTimeOK}\nfn: {_curFrameCount} / {(deltaTimeFs * 0.0001f):F2} ms, frame: {_frameIndex} / {(_frameTs * 1000f):F2} ms" +
            //     $"\ncReady: {isColorFrameReady}, dReady: {isDepthFrameReady}, iReady: {isIrFrameReady}, bdReady: {isBodyDataFrameReady}, biReady: {isBodyIndexFrameReady}, odReady: {isObjDataFrameReady}, oiReady: {isObjIndexFrameReady}" +
            //     $"\nscReady: {isScaledColorFrameReady}, sdReady: {isScaledDepthFrameReady}, siReady: {isScaledIRFrameReady}, sbiReady: {isScaledBodyIndexFrameReady}, soiReady: {isScaledObjIndexFrameReady}" +
            //     $"\ncdSyncReady: {isColorDepthSyncReady}, bdSyncReady: {isBodyDepthSyncReady}");
            _lastFrameTs = DateTime.Now.Ticks;

            return bFramesReady && bFrameTimeOK;
        }

        // perform the sensor-data update
        private void DoUpdateSensorData(KinectInterop.SensorData sensorData, FramesetData fsData, KinectManager kinectManager, bool isPlayMode)
        {
            // color frame
            if (_colorStreamProvider != null && fsData._isColorImageReady && !isPlayMode)
            {
                //lock (colorFrameLock)
                if ((frameSourceFlags & KinectInterop.FrameSource.TypeColor) != 0 && fsData._colorImage != null)
                {
                    // check for resolution change
                    //Vector2Int imageRes = _colorStreamProvider.GetColorImageSize();
                    if (sensorData.colorImageTexture == null || sensorData.colorImageTexture.width != fsData._colorImage.width || sensorData.colorImageTexture.height != fsData._colorImage.height)
                    {
                        sensorData.colorImageWidth = fsData._colorImage.width;
                        sensorData.colorImageHeight = fsData._colorImage.height;

                        //RenderTextureFormat colorTexFormat = sensorData.colorImageFormat == TextureFormat.BGRA32 ? RenderTextureFormat.BGRA32 : RenderTextureFormat.Default;
                        sensorData.colorImageTexture = KinectInterop.CreateRenderTexture((RenderTexture)sensorData.colorImageTexture, sensorData.colorImageWidth, sensorData.colorImageHeight); // new Texture2D(sensorData.colorImageWidth, sensorData.colorImageHeight, TextureFormat.RGB24, false);
                        sensorData.colorCamIntr = null;
                    }

                    Graphics.Blit(fsData._colorImage, (RenderTexture)sensorData.colorImageTexture);
                    sensorData.lastColorFrameTime = currentColorTimestamp = rawColorTimestamp = fsData._colorImageTimestamp;
                    //Debug.Log("D" + deviceIndex + " UpdateColorTimestamp: " + sensorData.lastColorFrameTime + ", Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));

                    //if (pointCloudAlignedColorTex != null)
                    //{
                    //    // depth-cam color image
                    //    lastDepthCamColorFrameTime = sensorData.lastColorFrameTime;
                    //    //Debug.Log("LastDepthCamColorFrameTime: " + lastDepthCamColorFrameTime);
                    //}
                }

                //_colorStreamProvider.SetFrameReady(false);
            }

            // depth frame
            if (_depthStreamProvider != null && fsData._isDepthFrameReady && !isPlayMode)
            {
                //lock (depthFrameLock)
                if ((frameSourceFlags & KinectInterop.FrameSource.TypeDepth) != 0)
                {
                    // check for resolution change
                    Vector2Int imageRes = _depthStreamProvider.GetDepthImageSize();
                    int imageLen = imageRes.x * imageRes.y;

                    if (sensorData.depthImageWidth != imageRes.x || sensorData.depthImageHeight != imageRes.y ||
                        sensorData.depthImage == null || sensorData.depthImage.Length != imageLen)
                    {
                        sensorData.depthImageWidth = imageRes.x;
                        sensorData.depthImageHeight = imageRes.y;

                        sensorData.depthImage = new ushort[imageLen];
                        sensorData.depthCamIntr = null;
                    }

                    if (fsData._depthFrame != null && fsData._depthFrame.Length == sensorData.depthImage.Length)
                        KinectInterop.CopyBytes(fsData._depthFrame, sizeof(ushort), sensorData.depthImage, sizeof(ushort));
                    sensorData.lastDepthFrameTime = currentDepthTimestamp = rawDepthTimestamp = fsData._depthFrameTimestamp;
                    //Debug.Log("D" + deviceIndex + " UpdateDepthTimestamp: " + sensorData.lastDepthFrameTime + ", Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                }

                //_depthStreamProvider.SetFrameReady(false);
            }

            if (sensorData.colorCamIntr == null || sensorData.depthCamIntr == null)
            {
                // try to estimate intrinsic & extrinsic parameters
                TryEstimateCameraIntrExtr(sensorData, fsData, kinectManager);
            }

            // infrared frame
            if (_irStreamProvider != null && fsData._isIrFrameReady && !isPlayMode)
            {
                if ((frameSourceFlags & KinectInterop.FrameSource.TypeInfrared) != 0)
                {
                    // check for resolution change
                    Vector2Int imageRes = _irStreamProvider.GetIRImageSize();
                    if (sensorData.infraredImage == null || sensorData.infraredImage.Length != (imageRes.x * imageRes.y))
                    {
                        sensorData.infraredImage = new ushort[imageRes.x * imageRes.y];
                    }

                    if (fsData._irFrame.Length == sensorData.infraredImage.Length)
                        KinectInterop.CopyBytes(fsData._irFrame, sizeof(ushort), sensorData.infraredImage, sizeof(ushort));
                    sensorData.lastInfraredFrameTime = currentInfraredTimestamp = rawInfraredTimestamp = fsData._irFrameTimestamp;
                    //Debug.Log("D" + deviceIndex + " UpdateInfraredTimestamp: " + sensorData.lastInfraredFrameTime + ", Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                }

                //_irStreamProvider.SetFrameReady(false);
            }

            // body-index frame
            if (_bodyStreamProvider != null && fsData._isBodyIndexFrameReady)
            {
                //lock (bodyTrackerLock)
                if ((frameSourceFlags & KinectInterop.FrameSource.TypeBodyIndex) != 0)
                {
                    var imageRes = _bodyStreamProvider.GetBodyIndexImageSize();
                    if (sensorData.bodyIndexImage == null || sensorData.bodyIndexImage.Length != (imageRes.x * imageRes.y))
                    {
                        sensorData.bodyIndexImage = new byte[imageRes.x * imageRes.y];
                    }

                    if(fsData._bodyIndexFrame != null && fsData._bodyIndexFrame.Length == sensorData.bodyIndexImage.Length)
                        KinectInterop.CopyBytes(fsData._bodyIndexFrame, sizeof(byte), sensorData.bodyIndexImage, sizeof(byte));
                    sensorData.lastBodyIndexFrameTime = currentBodyIndexTimestamp = rawBodyIndexTimestamp = fsData._bodyIndexTimestamp;
                    //Debug.Log("D" + deviceIndex + " UpdateBodyIndexTimestamp: " + sensorData.lastBodyIndexFrameTime + ", Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                }

                //_bodyStreamProvider.SetBodyIndexFrameReady(false);
            }

            // body frame
            if (_bodyStreamProvider != null && fsData._isBodyDataFrameReady)
            {
                //if(sensorData.alTrackedBodies != null)
                if ((frameSourceFlags & KinectInterop.FrameSource.TypeBody) != 0)
                {
                    // number & list of bodies
                    sensorData.trackedBodiesCount = trackedBodiesCount = fsData._trackedBodiesCount;;
                    List<KinectInterop.BodyData> trackedBodies = fsData._alTrackedBodies;

                    // create the needed slots
                    if (sensorData.alTrackedBodies.Length < trackedBodiesCount)
                    {
                        Array.Resize<KinectInterop.BodyData>(ref sensorData.alTrackedBodies, (int)trackedBodiesCount);

                        for (int i = 0; i < trackedBodiesCount; i++)
                        {
                            sensorData.alTrackedBodies[i] = new KinectInterop.BodyData((int)KinectInterop.JointType.Count);
                        }
                    }

                    //alTrackedBodies.CopyTo(sensorData.alTrackedBodies);
                    for (int i = 0; i < trackedBodiesCount; i++)
                    {
                        trackedBodies[i].CopyTo(ref sensorData.alTrackedBodies[i]);

                        //KinectInterop.BodyData bodyData = sensorData.alTrackedBodies[i];
                        //Debug.Log($"  (U)User ID: {bodyData.liTrackingID}, body: {i}, bi: {bodyData.iBodyIndex}, pos: {bodyData.joint[0].kinectPos}, rot: {bodyData.joint[0].normalRotation.eulerAngles}");
                    }

                    sensorData.lastBodyFrameTime = currentBodyTimestamp = rawBodyTimestamp = fsData._bodyDataTimestamp + trackedBodiesCount;
                    //Debug.Log($"D{deviceIndex} UpdateBodyTimestamp: {sensorData.lastBodyFrameTime}, BodyCount: {trackedBodiesCount}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                }

                //_bodyStreamProvider.SetBodyDataFrameReady(false);
            }

            // object-index frame
            if (_objectStreamProvider != null && fsData._isObjIndexFrameReady)
            {
                //lock (objectTrackerLock)
                if ((frameSourceFlags & KinectInterop.FrameSource.TypeObjectIndex) != 0)
                {
                    var imageRes = _objectStreamProvider.GetObjectIndexImageSize();
                    if (sensorData.objectIndexImage == null || sensorData.objectIndexImage.Length != (imageRes.x * imageRes.y))
                    {
                        sensorData.objectIndexImage = new byte[imageRes.x * imageRes.y];
                    }

                    if (fsData._objIndexFrame != null && fsData._objIndexFrame.Length == sensorData.objectIndexImage.Length)
                        KinectInterop.CopyBytes(fsData._objIndexFrame, sizeof(byte), sensorData.objectIndexImage, sizeof(byte));
                    sensorData.lastObjectIndexFrameTime = currentObjectIndexTimestamp = rawObjectIndexTimestamp = fsData._objIndexTimestamp;
                    //Debug.Log("D" + deviceIndex + " UpdateObjectIndexTimestamp: " + sensorData.lastObjectIndexFrameTime + ", Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                }

                //_objectStreamProvider.SetObjectIndexFrameReady(false);
            }

            // object frame
            if (_objectStreamProvider != null && fsData._isObjDataFrameReady)
            {
                //if(sensorData.alTrackedObjects != null)
                if ((frameSourceFlags & KinectInterop.FrameSource.TypeObject) != 0)
                {
                    // number & list of objects
                    sensorData.trackedObjectsCount = trackedObjectsCount = fsData._trackedObjCount; ;
                    List<KinectInterop.ObjectData> trackedObjects = fsData._alTrackedObjects;

                    // create the needed slots
                    if (sensorData.alTrackedObjects.Length < trackedObjectsCount)
                    {
                        Array.Resize<KinectInterop.ObjectData>(ref sensorData.alTrackedObjects, (int)trackedObjectsCount);

                        for (int i = 0; i < trackedObjectsCount; i++)
                        {
                            sensorData.alTrackedObjects[i] = new KinectInterop.ObjectData();
                        }
                    }

                    //alTrackedBodies.CopyTo(sensorData.alTrackedObjects);
                    for (int i = 0; i < trackedObjectsCount; i++)
                    {
                        trackedObjects[i].CopyTo(ref sensorData.alTrackedObjects[i]);

                        KinectInterop.ObjectData objData = sensorData.alTrackedObjects[i];
                        //Debug.Log($"  Object {i}/{trackedObjectsCount} id: {objData.trackingID} - {objData.objType}, oi: {objData.objIndex}, pos: {objData.kinectPos}\nscore: {objData.score:F3}");
                    }

                    sensorData.lastObjectFrameTime = currentObjectTimestamp = rawObjectTimestamp = fsData._objDataTimestamp + trackedObjectsCount;
                    //Debug.Log($"D{deviceIndex} UpdateObjectTimestamp: {sensorData.lastObjectFrameTime}, ObjCount: {trackedObjectsCount}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                }

                //_objectStreamProvider.SetObjectDataFrameReady(false);
            }

            // pose frame
            if (_poseStreamProvider != null && _poseStreamProvider.IsFrameReady())
            {
                if ((frameSourceFlags & KinectInterop.FrameSource.TypePose) != 0)
                {
                    rawPosePosition = _poseStreamProvider.GetSensorPosition();
                    rawPoseRotation = _poseStreamProvider.GetSensorRotation();

                    currentPoseTimestamp = rawPoseTimestamp = _poseStreamProvider.GetPoseFrameTimestamp();
                    //Debug.Log($"D{deviceIndex} RawPoseTimestamp: {rawPoseTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff") + $"\nPos: {rawPosePosition:F2}, Rot: {rawPoseRotation.eulerAngles:F1}");
                }

                _poseStreamProvider.SetFrameReady(false);
            }
        }


        // displays the info or debug message on screen, and optionally in the console
        private void ShowInfoMessage(KinectManager kinectManager, string message, bool isError = false)
        {
            if (kinectManager != null && kinectManager.statusInfoText != null)
            {
                kinectManager.statusInfoText.text = message;
            }
            else if(isError)
            {
                Debug.LogError(message);
            }
        }

        // tries to estimate color & depth intrinsic and extrinsic parameters
        private bool TryEstimateCameraIntrExtr(KinectInterop.SensorData sensorData, FramesetData fsData, KinectManager kinectManager)
        {
            if (_colorStreamProvider == null || _depthStreamProvider == null)
            {
                ShowInfoMessage(kinectManager, "Both color and depth stream providers must be set up! See UniCamConfig.", true);
                return false;
            }

            // color camera resolution
            Vector2Int imageRes = _colorStreamProvider.GetColorImageSize();
            if (sensorData.colorImageWidth != imageRes.x || sensorData.colorImageHeight != imageRes.y)
            {
                sensorData.colorImageWidth = imageRes.x;
                sensorData.colorImageHeight = imageRes.y;
            }

            // depth camera resolution
            imageRes = _depthStreamProvider.GetDepthImageSize();
            if (sensorData.depthImageWidth != imageRes.x || sensorData.depthImageHeight != imageRes.y)
            {
                sensorData.depthImageWidth = imageRes.x;
                sensorData.depthImageHeight = imageRes.y;
            }

            if(sensorData.colorImageWidth < 100 || sensorData.colorImageHeight < 100)
            {
                // don't do calculations for resolutions like 16x16
                return false;
            }

            // check for saved intr/extr parameters
            if (!recalibrateCamera && KinectInterop.LoadSensorIntrExtr(sensorData))
                return false;

            //if (kinectManager != null && (kinectManager.getDepthFrames == KinectManager.DepthTextureType.None ||
            //    kinectManager.getColorFrames == KinectManager.ColorTextureType.None))
            //{
            //    ShowInfoMessage(kinectManager, "Both color and depth frames must be enabled! See the KinectManager's settings.", true);
            //    return false;
            //}

            // create the cam-intr provider if needed
            if (_camIntrStreamProvider == null)
            {
                var uniCamConfig = (UniCamConfig)Resources.Load("UniCamConfig");

                if (!string.IsNullOrEmpty(uniCamConfig.camIntrStreamProvider))
                {
                    Type providerType = Type.GetType(uniCamConfig.camIntrStreamProvider);

                    var providerComponent = gameObject.GetComponent(providerType);
                    if (providerComponent == null)
                        providerComponent = gameObject.AddComponent(providerType);

                    _camIntrStreamProvider = (ICamIntrStreamProvider)providerComponent;
                    //Debug.Log($"Created cam-intr provider: {_camIntrStreamProvider}");

                    // set full sync settings
                    _dontOpenMoreFrames = true;
                    StartCoroutine(SetFullSyncSettings(kinectManager));
                }
                else
                {
                    ShowInfoMessage(kinectManager, "The camera intrinsics provider is not set up. See UniCamConfig.", true);
                    return false;
                }
            }

            // try to estimate intrinsic & extrinsic parameters
            if (fsData == null || _dontOpenMoreFrames)
                return false;

            // color camera intr
            Vector2 focalLen = _camIntrStreamProvider.TryEstimateFocalLen(PointCloudResolution.ColorCameraResolution, sensorData, fsData, out float percentReady);
            if (focalLen != Vector2.zero)
            {
                _colorStreamProvider.FocalLength = focalLen;
                sensorData.colorCamIntr = _colorStreamProvider.GetCameraIntrinsics();
            }

            // depth camera intr
            focalLen = _camIntrStreamProvider.TryEstimateFocalLen(PointCloudResolution.DepthCameraResolution, sensorData, fsData, out percentReady);
            if (focalLen != Vector2.zero)
            {
                _depthStreamProvider.FocalLength = focalLen;
                sensorData.depthCamIntr = _depthStreamProvider.GetCameraIntrinsics();
            }

            ShowInfoMessage(kinectManager, $"Please stand in front of the camera, to calibrate it - {percentReady * 100f:F0}%");
            if (sensorData.colorCamIntr != null && sensorData.depthCamIntr != null)
            {
                // save intr & extr patameters for later use
                KinectInterop.SaveSensorIntrExtr(sensorData);

                if (kinectManager != null && kinectManager.statusInfoText != null)
                {
                    kinectManager.statusInfoText.text = string.Empty;
                }

                // restore sync settings
                _dontOpenMoreFrames = true;
                StartCoroutine(RestoreSyncSettings(kinectManager));

                return true;
            }

            return false;
        }


        private KinectManager.DepthTextureType _kmGetDepthFrames;
        private KinectManager.ColorTextureType _kmGetColorFrames;
        private KinectInterop.FrameSource _frameSourceFlags;

        // waits for a second, and sets full sync (d/c & b/d)
        private IEnumerator SetFullSyncSettings(KinectManager kinectManager)
        {
            // wait for all open frames to be displayed
            while(_fsManager == null || _fsManager.GetReadIndex() != _fsManager.GetWriteIndex() ||
                !IsFramesetReady(_fsManager.GetFramesetAt(_fsManager.GetWriteIndex())))
            {
                //Debug.Log($"SetFullSyncSettings - ri: {_fsManager.GetReadIndex()}, wi: {_fsManager.GetWriteIndex()}");
                yield return null;
            }

            // wait for any unfinished model
            while(SentisManager.Instance != null && SentisManager.Instance.IsAnyRtModelUnfinished())
            {
                yield return null;
            }

            // avoid further frame-ready mistake
            _fsManager.GetFramesetAt(_fsManager.GetWriteIndex())._isFramesetReady = true;

            // save depth & color flags
            _kmGetDepthFrames = kinectManager.getDepthFrames;
            _kmGetColorFrames = kinectManager.getColorFrames;
            _frameSourceFlags = frameSourceFlags;

            // turn on color and depth frames, to make camera calibration possible
            if (kinectManager != null && (_kmGetDepthFrames == KinectManager.DepthTextureType.None ||
                _kmGetColorFrames == KinectManager.ColorTextureType.None))
            {
                kinectManager.getDepthFrames = KinectManager.DepthTextureType.DepthTexture;
                kinectManager.getColorFrames = KinectManager.ColorTextureType.ColorTexture;
                frameSourceFlags |= KinectInterop.FrameSource.TypeColor | KinectInterop.FrameSource.TypeDepth;
            }

            // set full sync modes
            SetDepthAndColorSync(bSyncDepthAndColor: true, bIgnoreStreams: true);
            SetBodyAndDepthSync(bSyncBodyAndDepth: true, bIgnoreStreams: true);

            if(_fsManager != null && SentisManager.Instance != null)
            {
                // update frame indices of runtime models
                int fsIndex = _fsManager.GetNextWriteIndex();  //.GetFirstOpenFrameIndex();
                SentisManager.Instance.UpdateRtModelsFrame(isSyncDepthAndColor, isSyncBodyAndDepth, fsIndex);
            }

            //Debug.Log($"Set sync d/c: {isSyncDepthAndColor}, b/d: {isSyncDepthAndColor}");
            _dontOpenMoreFrames = false;

            // start the provider
            string devMgrClass = _camIntrStreamProvider.GetDeviceManagerClass();
            if (!string.IsNullOrEmpty(devMgrClass))
            {
                var devMgr = (IDeviceManager)_devStrComponent[devMgrClass];
                var devState = devMgr.GetDeviceState();
                string deviceId = string.Empty;

                if (devState == DeviceState.Working)
                    _camIntrStreamProvider.StartProvider(this, devMgr, kinectManager, _sensorData);
                else
                {
                    Debug.LogError("The camera intrinsics provider can't be started!");
                }
            }
            else
            {
                // start stream provider directly
                _camIntrStreamProvider.StartProvider(this, null, kinectManager, _sensorData);
            }

            _streamProviders.Add(_camIntrStreamProvider);
            //Debug.Log($"Started cam-intr provider: {_camIntrStreamProvider}, state: {_camIntrStreamProvider.GetProviderState()}");
        }

        // restores sync settings in 1 second
        private IEnumerator RestoreSyncSettings(KinectManager kinectManager)
        {

            // stop cam-intr provider & restore body provider
            _camIntrStreamProvider.StopProvider();
            _streamProviders.Remove(_camIntrStreamProvider);
            _camIntrStreamProvider = null;
            //Debug.Log($"Stopped cam-intr provider: {_camIntrStreamProvider}");

            // wait for all open frames to be displayed
            while (_fsManager == null || _fsManager.GetReadIndex() != _fsManager.GetWriteIndex() ||
                !IsFramesetReady(_fsManager.GetFramesetAt(_fsManager.GetWriteIndex())))
            {
                //Debug.Log($"RestoreSyncSettings - ri: {_fsManager.GetReadIndex()}, wi: {_fsManager.GetWriteIndex()}");
                yield return null;
            }

            // wait for any unfinished model
            while (SentisManager.Instance != null && SentisManager.Instance.IsAnyRtModelUnfinished())
            {
                yield return null;
            }

            // avoid further frame-ready mistake
            _fsManager.GetFramesetAt(_fsManager.GetWriteIndex())._isFramesetReady = true;

            // restore depth & color flags
            kinectManager.getDepthFrames = _kmGetDepthFrames;
            kinectManager.getColorFrames = _kmGetColorFrames;
            frameSourceFlags = _frameSourceFlags;

            // restore sync modes
            SetDepthAndColorSync(kinectManager.syncDepthAndColor);
            SetBodyAndDepthSync(kinectManager.syncBodyAndDepth);

            if (_fsManager != null && SentisManager.Instance != null)
            {
                // update frame indices of runtime models
                int fsIndex = _fsManager.GetNextWriteIndex();  //.GetFirstOpenFrameIndex();
                SentisManager.Instance.UpdateRtModelsFrame(isSyncDepthAndColor, isSyncBodyAndDepth, fsIndex);
            }

            //Debug.Log($"Restored sync d/c: {isSyncDepthAndColor}, b/d: {isSyncDepthAndColor}");
            _dontOpenMoreFrames = false;
        }


        public override void PollSensorFrames(KinectInterop.SensorData sensorData)
        {
            try
            {
                int openFsCount = _fsManager.OpenFramesetsCount;

                for (int i = 0; i < openFsCount; i++)
                {
                    FramesetData fsData = _fsManager.GetOpenFramesetAt(i);
                    if (fsData == null || fsData._isFramesetReady)
                        continue;

                    // poll all device frames
                    foreach (var devMgr in _deviceManagers)
                    {
                        devMgr.PollDeviceFrames(sensorData, fsData);
                    }

                    // poll all stream frames
                    foreach (var provider in _streamProviders)
                    {
                        if (provider.GetProviderState() == StreamState.Working)
                        {
                            provider.PollStreamFrames(sensorData, fsData);
                        }
                    }
                }

                //// transformation data frames
                //ulong depthFrameTime2 = _depthStreamProvider != null ? _depthStreamProvider.GetDepthFrameTimestamp() : 0;
                //ProcessTransformedFrames(sensorData, depthFrameTime2);
            }
            catch (System.TimeoutException)
            {
                // do nothing
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }
        }


        public override void EnablePoseStream(KinectInterop.SensorData sensorData, bool bEnable)
        {
            base.EnablePoseStream(sensorData, bEnable);

            if(_poseStreamProvider != null)
            {
                _poseStreamProvider.EnablePoseStream(bEnable);
            }
        }


        // enables or disables depth camera color frame processing
        public override void EnableDepthCameraColorFrame(KinectInterop.SensorData sensorData, bool isEnable)
        {
            if (isEnable && pointCloudColorTexture == null)
            {
                pointCloudColorTexture = KinectInterop.CreateRenderTexture(pointCloudColorTexture, sensorData.depthImageWidth, sensorData.depthImageHeight, RenderTextureFormat.ARGB32);
                sensorData.depthCamColorImageTexture = KinectInterop.CreateRenderTexture((RenderTexture)sensorData.depthCamColorImageTexture, sensorData.depthImageWidth, sensorData.depthImageHeight, RenderTextureFormat.ARGB32);
            }
            else if (!isEnable && pointCloudColorTexture != null)
            {
                pointCloudColorTexture.Release();
                pointCloudColorTexture = null;

                ((RenderTexture)sensorData.depthCamColorImageTexture).Release();
                sensorData.depthCamColorImageTexture = null;
            }
        }

        // creates the point-cloud color shader and its respective buffers, as needed
        protected override bool CreatePointCloudColorShader(KinectInterop.SensorData sensorData)
        {
            pointCloudColorRes = GetPointCloudTexResolution(sensorData);

            if (pointCloudResolution == PointCloudResolution.DepthCameraResolution)
            {
                if (_colorStreamProvider == null)
                    return false;

                // enable color image scaling
                _colorStreamProvider.SetScaledImageProps(true, sensorData.depthImageWidth, sensorData.depthImageHeight);
                _scaledColorWidth = sensorData.depthImageWidth;
                _scaledColorHeight = sensorData.depthImageWidth;

                if (pointCloudAlignedColorTex == null)
                {
                    pointCloudAlignedColorTex = KinectInterop.CreateRenderTexture((RenderTexture)pointCloudAlignedColorTex, sensorData.depthImageWidth, sensorData.depthImageHeight, RenderTextureFormat.ARGB32);
                }
            }

            return true;
        }

        // disposes the point-cloud color shader and its respective buffers
        protected override void DisposePointCloudColorShader(KinectInterop.SensorData sensorData)
        {
            if(_colorStreamProvider != null)
            {
                // disable color image scaling
                _colorStreamProvider.SetScaledImageProps(false, sensorData.depthImageWidth, sensorData.depthImageHeight);
            }

            base.DisposePointCloudColorShader(sensorData);
        }

        // updates the point-cloud color shader with the actual data
        protected override bool UpdatePointCloudColorShader(KinectInterop.SensorData sensorData)
        {
            if (pointCloudResolution == PointCloudResolution.DepthCameraResolution)
            {
                if (pointCloudAlignedColorTex != null)
                {
                    if (_scaledColorWidth != sensorData.depthImageWidth || _scaledColorHeight != sensorData.depthImageWidth)
                    {
                        _scaledColorWidth = sensorData.depthImageWidth;
                        _scaledColorHeight = sensorData.depthImageWidth;

                        // set new scaled frame size 
                        _colorStreamProvider.SetScaledImageProps(true, sensorData.depthImageWidth, sensorData.depthImageHeight);
                        pointCloudAlignedColorTex = KinectInterop.CreateRenderTexture((RenderTexture)pointCloudAlignedColorTex, sensorData.depthImageWidth, sensorData.depthImageHeight, RenderTextureFormat.ARGB32);
                    }

                    FramesetData currentFrame = _fsManager.GetCurrentFrameset();
                    if (currentFrame != null && currentFrame._isScaledColorImageReady && lastDepthCamColorFrameTime != currentFrame._scaledColorImageTimestamp)
                    {
                        Graphics.Blit(currentFrame._scaledColorImage, (RenderTexture)pointCloudAlignedColorTex);
                        lastDepthCamColorFrameTime = currentFrame._scaledColorImageTimestamp;
                        //Debug.Log("LastDepthCamColorFrameTime: " + lastDepthCamColorFrameTime);
                    }

                    if (sensorData.lastDepthCamColorFrameTime != lastDepthCamColorFrameTime && pointCloudAlignedColorTex != null)
                    {
                        sensorData.lastDepthCamColorFrameTime = lastDepthCamColorFrameTime;

                        if (sensorData.depthCamColorImageTexture != null)
                        {
                            if (sensorData.depthCamColorImageTexture.width != sensorData.depthImageWidth || sensorData.depthCamColorImageTexture.height != sensorData.depthImageHeight)
                            {
                                sensorData.depthCamColorImageTexture = KinectInterop.CreateRenderTexture((RenderTexture)sensorData.depthCamColorImageTexture, sensorData.depthImageWidth, sensorData.depthImageHeight, RenderTextureFormat.ARGB32);
                            }

                            Graphics.CopyTexture(pointCloudAlignedColorTex, sensorData.depthCamColorImageTexture);
                        }

                        if (pointCloudColorRT != null)
                        {
                            if (pointCloudColorRT.width != sensorData.depthImageWidth || pointCloudColorRT.height != sensorData.depthImageHeight)
                            {
                                pointCloudColorRT = KinectInterop.CreateRenderTexture(pointCloudColorRT, sensorData.depthImageWidth, sensorData.depthImageHeight, RenderTextureFormat.ARGB32);
                            }

                            Graphics.CopyTexture(pointCloudAlignedColorTex, pointCloudColorRT);
                        }

                        if (pointCloudColorTexture != null)
                        {
                            //if (pointCloudColorTexture.width != sensorData.depthImageWidth || pointCloudColorTexture.height != sensorData.depthImageHeight)
                            if (pointCloudColorTexture.width == 0 || pointCloudColorTexture.height == 0)
                            {
                                pointCloudColorTexture = KinectInterop.CreateRenderTexture(pointCloudColorTexture, sensorData.depthImageWidth, sensorData.depthImageHeight, RenderTextureFormat.ARGB32);
                            }

                            Graphics.Blit(pointCloudAlignedColorTex, pointCloudColorTexture);  // CopyTexture
                        }
                    }

                    return true;
                }
            }
            else
            {
                return base.UpdatePointCloudColorShader(sensorData);
            }

            return false;
        }

        // creates the point-cloud vertex shader and its respective buffers, as needed
        protected override bool CreatePointCloudVertexShader(KinectInterop.SensorData sensorData)
        {
            if (_depthStreamProvider == null)
                return false;

            bool bResult = base.CreatePointCloudVertexShader(sensorData);
            
            if(pointCloudResolution == PointCloudResolution.ColorCameraResolution)
            {
                // enable depth frame scaling
                _depthStreamProvider.SetScaledFrameProps(true, sensorData.colorImageWidth, sensorData.colorImageHeight);
                colorCamDepthDataFrame = new ushort[sensorData.colorImageWidth * sensorData.colorImageHeight];

                _scaledDepthWidth = sensorData.colorImageWidth;
                _scaledDepthHeight = sensorData.colorImageHeight;
            }

            return bResult;
        }

        // disposes the point-cloud vertex shader and its respective buffers
        protected override void DisposePointCloudVertexShader(KinectInterop.SensorData sensorData)
        {
            if(_depthStreamProvider != null)
            {
                // disable depth frame scaling
                _depthStreamProvider.SetScaledFrameProps(false, sensorData.colorImageWidth, sensorData.colorImageHeight);
            }

            base.DisposePointCloudVertexShader(sensorData);
        }

        // updates the point-cloud vertex shader with the actual data
        protected override bool UpdatePointCloudVertexShader(KinectInterop.SensorData sensorData, KinectManager kinectManager)
        {
            if (pointCloudVertexShader != null && pointCloudVertexRT != null && _depthStreamProvider != null) //&& 
                //(sensorData.lastDepth2SpaceFrameTime != lastColorCamDepthFrameTime))
            {
                if (pointCloudResolution == PointCloudResolution.ColorCameraResolution)
                {
                    if(_scaledDepthWidth != sensorData.colorImageWidth || _scaledDepthHeight != sensorData.colorImageHeight)
                    {
                        _scaledDepthWidth = sensorData.colorImageWidth;
                        _scaledDepthHeight = sensorData.colorImageHeight;

                        // set new scaled frame size 
                        _depthStreamProvider.SetScaledFrameProps(true, sensorData.colorImageWidth, sensorData.colorImageHeight);
                        colorCamDepthDataFrame = new ushort[sensorData.colorImageWidth * sensorData.colorImageHeight];
                    }

                    FramesetData currentFrame = _fsManager.GetCurrentFrameset();
                    if(currentFrame != null && currentFrame._isScaledDepthFrameReady && lastColorCamDepthFrameTime != currentFrame._scaledDepthFrameTimestamp)
                    {
                        KinectInterop.CopyBytes(currentFrame._scaledDepthFrame, sizeof(ushort), colorCamDepthDataFrame, sizeof(ushort));
                        lastColorCamDepthFrameTime = currentFrame._scaledDepthFrameTimestamp;
                        //Debug.Log("LastColorCamDepthFrameTime: " + lastColorCamDepthFrameTime);
                    }
                }
            }

            return base.UpdatePointCloudVertexShader(sensorData, kinectManager);
        }

        // creates the color-depth shader and its respective buffers, as needed
        protected override bool CreateColorDepthShader(KinectInterop.SensorData sensorData)
        {
            if (_depthStreamProvider == null)
                return false;

            bool bResult = base.CreateColorDepthShader(sensorData);

            // enable depth frame scaling
            _depthStreamProvider.SetScaledFrameProps(true, sensorData.colorImageWidth, sensorData.colorImageHeight);
            colorCamDepthDataFrame = new ushort[sensorData.colorImageWidth * sensorData.colorImageHeight];

            _scaledDepthWidth = sensorData.colorImageWidth;
            _scaledDepthHeight = sensorData.colorImageHeight;

            //if (sensorData.colorDepthTexture == null)
            //{
            //    sensorData.colorDepthTexture = new RenderTexture(sensorData.colorImageWidth, sensorData.colorImageHeight, 0, RenderTextureFormat.ARGB32);
            //    //sensorData.colorDepthTexture.enableRandomWrite = true;
            //    sensorData.colorDepthTexture.Create();
            //}

            //if(colorDepthBuffer == null)
            //{
            //    int bufLength = (sensorData.colorImageWidth * sensorData.colorImageHeight) >> 1;
            //    colorDepthBuffer = KinectInterop.CreateComputeBuffer(colorDepthBuffer, bufLength, sizeof(uint));
            //}

            //colorDepthShaderInited = true;

            return bResult;
        }

        // disposes the color-depth shader and its respective buffers
        protected override void DisposeColorDepthShader(KinectInterop.SensorData sensorData)
        {
            if (_depthStreamProvider != null)
            {
                // disable depth frame scaling
                _depthStreamProvider.SetScaledFrameProps(false, sensorData.colorImageWidth, sensorData.colorImageHeight);
            }

            base.DisposeColorDepthShader(sensorData);
        }

        // updates the color-depth shader with the actual data
        protected override bool UpdateColorDepthShader(KinectInterop.SensorData sensorData)
        {
            if (colorCamDepthDataFrame != null && _depthStreamProvider != null)
            {
                if (_scaledDepthWidth != sensorData.colorImageWidth || _scaledDepthHeight != sensorData.colorImageHeight)
                {
                    _scaledDepthWidth = sensorData.colorImageWidth;
                    _scaledDepthHeight = sensorData.colorImageHeight;

                    // set new scaled frame size 
                    _depthStreamProvider.SetScaledFrameProps(true, sensorData.colorImageWidth, sensorData.colorImageHeight);
                    colorCamDepthDataFrame = new ushort[sensorData.colorImageWidth * sensorData.colorImageHeight];
                }

                FramesetData currentFrame = _fsManager.GetCurrentFrameset();
                if (currentFrame != null && currentFrame._isScaledDepthFrameReady && lastColorCamDepthFrameTime != currentFrame._scaledDepthFrameTimestamp)
                {
                    KinectInterop.CopyBytes(currentFrame._scaledDepthFrame, sizeof(ushort), colorCamDepthDataFrame, sizeof(ushort));
                    lastColorCamDepthFrameTime = currentFrame._scaledDepthFrameTimestamp;
                    //Debug.Log("LastColorCamDepthFrameTime: " + lastColorCamDepthFrameTime);
                }
            }

            return base.UpdateColorDepthShader(sensorData);
        }

        //// invoked when the color-cam depth image is ready
        //private void ColorCamDepthImageReady(KinectInterop.SensorData sensorData)
        //{
        //    if (sensorData != null)
        //    {
        //        sensorData.lastColorDepthBufferTime = sensorData.lastColorCamDepthFrameTime = lastColorCamDepthFrameTime;
        //        //Debug.Log("D" + deviceIndex + " ColorCamDepthFrameTime: " + lastColorCamDepthFrameTime + ", Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
        //    }
        //}

        //// enables or disables color camera depth frame processing
        //public override void EnableColorCameraDepthFrame(KinectInterop.SensorData sensorData, bool isEnable)
        //{
        //    if (isEnable && sensorData.colorCamDepthImage == null)
        //    {
        //        sensorData.colorCamDepthImage = new ushort[sensorData.colorImageWidth * sensorData.colorImageHeight];
        //    }
        //    else if (!isEnable && sensorData.colorCamDepthImage != null)
        //    {
        //        sensorData.colorCamDepthImage = null;
        //    }
        //}

        // creates the color-infrared shader and its respective buffers, as needed
        protected override bool CreateColorInfraredShader(KinectInterop.SensorData sensorData)
        {
            if (_irStreamProvider == null)
                return false;

            bool bResult = base.CreateColorInfraredShader(sensorData);

            // enable IR frame scaling
            _irStreamProvider.SetScaledFrameProps(true, sensorData.colorImageWidth, sensorData.colorImageHeight);
            colorCamDepthDataFrame = new ushort[sensorData.colorImageWidth * sensorData.colorImageHeight];

            _scaledIrWidth = sensorData.colorImageWidth;
            _scaledIrHeight = sensorData.colorImageHeight;

            return bResult;
        }

        // disposes the color-infrared shader and its respective buffers
        protected override void DisposeColorInfraredShader(KinectInterop.SensorData sensorData)
        {
            if (_irStreamProvider != null)
            {
                // disable IR frame scaling
                _irStreamProvider.SetScaledFrameProps(false, sensorData.colorImageWidth, sensorData.colorImageHeight);
            }

            base.DisposeColorInfraredShader(sensorData);
        }

        // updates the color-infrared shader with the actual data
        protected override bool UpdateColorInfraredShader(KinectInterop.SensorData sensorData)
        {
            if (colorCamInfraredDataFrame != null && _irStreamProvider != null)
            {
                if (_scaledIrWidth != sensorData.colorImageWidth || _scaledIrHeight != sensorData.colorImageHeight)
                {
                    _scaledIrWidth = sensorData.colorImageWidth;
                    _scaledIrHeight = sensorData.colorImageHeight;

                    // set new scaled frame size 
                    _irStreamProvider.SetScaledFrameProps(true, sensorData.colorImageWidth, sensorData.colorImageHeight);
                    colorCamInfraredDataFrame = new ushort[sensorData.colorImageWidth * sensorData.colorImageHeight];
                }

                FramesetData currentFrame = _fsManager.GetCurrentFrameset();
                if (currentFrame != null && currentFrame._isScaledIrFrameReady && lastColorCamInfraredFrameTime != currentFrame._scaledIrFrameTimestamp)
                {
                    KinectInterop.CopyBytes(currentFrame._scaledIrFrame, sizeof(ushort), colorCamInfraredDataFrame, sizeof(ushort));
                    lastColorCamInfraredFrameTime = currentFrame._scaledIrFrameTimestamp;
                    //Debug.Log("LastColorCamInfraredFrameTime: " + lastColorCamDepthFrameTime);
                }
            }

            return base.UpdateColorInfraredShader(sensorData);
        }

        // creates the color-camera body index shader and its respective buffers, as needed
        protected override bool CreateColorBodyIndexShader(KinectInterop.SensorData sensorData)
        {
            if (_bodyStreamProvider == null)
                return false;

            bool bResult = base.CreateColorBodyIndexShader(sensorData);

            // enable body-index frame scaling
            _bodyStreamProvider.SetScaledFrameProps(true, sensorData.colorImageWidth, sensorData.colorImageHeight);
            colorCamBodyIndexFrame = new byte[sensorData.colorImageWidth * sensorData.colorImageHeight];

            _scaledBodyIndexWidth = sensorData.colorImageWidth;
            _scaledBodyIndexHeight = sensorData.colorImageHeight;

            //if (sensorData.colorBodyIndexTexture == null)
            //{
            //    sensorData.colorBodyIndexTexture = new RenderTexture(sensorData.colorImageWidth, sensorData.colorImageHeight, 0, RenderTextureFormat.ARGB32);
            //    //sensorData.colorBodyIndexTexture.enableRandomWrite = true;
            //    sensorData.colorBodyIndexTexture.Create();
            //}

            //if (colorBodyIndexBuffer == null)
            //{
            //    int bufLength = (sensorData.colorImageWidth * sensorData.colorImageHeight) >> 2;
            //    colorBodyIndexBuffer = KinectInterop.CreateComputeBuffer(colorBodyIndexBuffer, bufLength, sizeof(uint));
            //}

            //colorBodyIndexShaderInited = true;

            return bResult;
        }


        protected override void DisposeColorBodyIndexShader(KinectInterop.SensorData sensorData)
        {
            if(_bodyStreamProvider != null)
            {
                // disable body-index frame scaling
                _bodyStreamProvider.SetScaledFrameProps(false, sensorData.colorImageWidth, sensorData.colorImageHeight);
            }

            base.DisposeColorBodyIndexShader(sensorData);
        }

        // updates the color-camera body index shader with the actual data
        protected override bool UpdateColorBodyIndexShader(KinectInterop.SensorData sensorData)
        {
            if (colorCamBodyIndexFrame != null && _bodyStreamProvider != null)
            {
                if (_scaledBodyIndexWidth != sensorData.colorImageWidth || _scaledBodyIndexHeight != sensorData.colorImageHeight)
                {
                    _scaledBodyIndexWidth = sensorData.colorImageWidth;
                    _scaledBodyIndexHeight = sensorData.colorImageHeight;

                    // set new scaled frame size 
                    _bodyStreamProvider.SetScaledFrameProps(true, sensorData.colorImageWidth, sensorData.colorImageHeight);
                    colorCamBodyIndexFrame = new byte[sensorData.colorImageWidth * sensorData.colorImageHeight];
                }

                FramesetData currentFrame = _fsManager.GetCurrentFrameset();
                if (currentFrame != null && currentFrame._isScaledBodyIndexFrameReady && currentFrame._scaledBodyIndexFrame != null &&
                    lastColorCamBodyIndexFrameTime != currentFrame._scaledBodyIndexFrameTimestamp)
                {
                    KinectInterop.CopyBytes(currentFrame._scaledBodyIndexFrame, sizeof(byte), colorCamBodyIndexFrame, sizeof(byte));
                    lastColorCamBodyIndexFrameTime = currentFrame._scaledBodyIndexFrameTimestamp;
                    //Debug.Log("LastColorCamBodyIndexFrameTime: " + lastColorCamBodyIndexFrameTime);
                }
            }

            return base.UpdateColorBodyIndexShader(sensorData);
        }

        // creates the color-camera object index shader and its respective buffers, as needed
        protected override bool CreateColorObjectIndexShader(KinectInterop.SensorData sensorData)
        {
            if (_objectStreamProvider == null)
                return false;

            bool bResult = base.CreateColorObjectIndexShader(sensorData);

            // enable object-index frame scaling
            _objectStreamProvider.SetScaledFrameProps(true, sensorData.colorImageWidth, sensorData.colorImageHeight);
            colorCamObjIndexFrame = new byte[sensorData.colorImageWidth * sensorData.colorImageHeight];

            _scaledObjIndexWidth = sensorData.colorImageWidth;
            _scaledObjIndexHeight = sensorData.colorImageHeight;

            return bResult;
        }


        protected override void DisposeColorObjectIndexShader(KinectInterop.SensorData sensorData)
        {
            if (_objectStreamProvider != null)
            {
                // disable object-index frame scaling
                _objectStreamProvider.SetScaledFrameProps(false, sensorData.colorImageWidth, sensorData.colorImageHeight);
            }

            base.DisposeColorObjectIndexShader(sensorData);
        }

        // updates the color-camera object index shader with the actual data
        protected override bool UpdateColorObjectIndexShader(KinectInterop.SensorData sensorData)
        {
            if (colorCamObjIndexFrame != null && _objectStreamProvider != null)
            {
                if (_scaledObjIndexWidth != sensorData.colorImageWidth || _scaledObjIndexHeight != sensorData.colorImageHeight)
                {
                    _scaledObjIndexWidth = sensorData.colorImageWidth;
                    _scaledObjIndexHeight = sensorData.colorImageHeight;

                    // set new scaled frame size 
                    _objectStreamProvider.SetScaledFrameProps(true, sensorData.colorImageWidth, sensorData.colorImageHeight);
                    colorCamObjIndexFrame = new byte[sensorData.colorImageWidth * sensorData.colorImageHeight];
                }

                FramesetData currentFrame = _fsManager.GetCurrentFrameset();
                if (currentFrame != null && currentFrame._isScaledObjIndexFrameReady && lastColorCamObjIndexFrameTime != currentFrame._scaledObjIndexFrameTimestamp)
                {
                    if (currentFrame._scaledObjIndexFrame.Length == colorCamObjIndexFrame.Length)
                        KinectInterop.CopyBytes(currentFrame._scaledObjIndexFrame, sizeof(byte), colorCamObjIndexFrame, sizeof(byte));
                    lastColorCamObjIndexFrameTime = currentFrame._scaledObjIndexFrameTimestamp;
                    //Debug.Log("LastColorCamObjIndexFrameTime: " + lastColorCamObjIndexFrameTime);
                }
            }

            return base.UpdateColorObjectIndexShader(sensorData);
        }

        //// invoked when the color-cam body-index image is ready
        //private void ColorCamBodyIndexImageReady(KinectInterop.SensorData sensorData)
        //{
        //    if (sensorData != null)
        //    {
        //        sensorData.lastColorBodyIndexBufferTime = sensorData.lastColorCamBodyIndexFrameTime = lastColorCamBodyIndexFrameTime;
        //        //Debug.Log("D" + deviceIndex + " ColorCamBodyIndexFrameTime: " + lastColorCamBodyIndexFrameTime + ", Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
        //    }
        //}

        //// enables or disables color camera body-index frame processing
        //public override void EnableColorCameraBodyIndexFrame(KinectInterop.SensorData sensorData, bool isEnable)
        //{
        //    if (isEnable && sensorData.colorCamBodyIndexImage == null)
        //    {
        //        sensorData.colorCamBodyIndexImage = new byte[sensorData.colorImageWidth * sensorData.colorImageHeight];
        //    }
        //    else if (!isEnable && sensorData.colorCamBodyIndexImage != null)
        //    {
        //        sensorData.colorCamBodyIndexImage = null;
        //    }
        //}


        //// creates the transformed sensor frames, if needed
        //private void ProcessTransformedFrames(KinectInterop.SensorData sensorData, ulong depthFrameTime)
        //{
        //    if ((pointCloudAlignedColorTex != null || pointCloudDepthBuffer != null || colorDepthBuffer != null ||
        //        sensorData.colorCamDepthImage != null || sensorData.colorInfraredBuffer != null || sensorData.colorCamInfraredImage != null) &&
        //        (!isSyncDepthAndColor || (rawColorTimestamp == depthFrameTime && rawDepthTimestamp == depthFrameTime)))
        //    {
        //        //ulong depthFrameDeviceTimestamp = (ulong)capture.Depth.DeviceTimestamp.Ticks;
        //        if (pointCloudAlignedColorTex != null && lastDepthCamColorFrameTime != rawDepthTimestamp)
        //        {
        //            //lock (depthCamColorFrameLock)
        //            {
        //                lastDepthCamColorFrameTime = rawDepthTimestamp;
        //                //Debug.Log("D" + deviceIndex + " DepthCamColorFrameTime: " + lastDepthCamColorFrameTime + ", Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
        //            }
        //        }

        //        if (((pointCloudDepthBuffer != null && pointCloudResolution == PointCloudResolution.ColorCameraResolution) ||
        //            colorDepthBuffer != null || sensorData.colorCamDepthImage != null) &&
        //            lastColorCamDepthFrameTime != rawDepthTimestamp)
        //        {
        //            //lock (colorCamDepthFrameLock)
        //            {
        //                lastColorCamDepthFrameTime = rawDepthTimestamp;
        //                //Debug.Log("D" + deviceIndex + " ColorCamDepthFrameTime: " + lastColorCamDepthFrameTime + ", Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
        //            }
        //        }

        //        if ((sensorData.colorInfraredBuffer != null || sensorData.colorCamInfraredImage != null) &&
        //            lastColorCamInfraredFrameTime != rawDepthTimestamp)
        //        {
        //            //lock (colorCamDepthFrameLock)
        //            {
        //                lastColorCamInfraredFrameTime = rawDepthTimestamp;
        //                //Debug.Log("D" + deviceIndex + " ColorCamInfraredFrameTime: " + lastColorCamInfraredFrameTime + ", Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
        //            }
        //        }
        //    }

        //    // transformed body index frame
        //    //Debug.Log("  depthFrameTime: " + depthFrameTime + ", rawBodyIndexTimestamp: " + rawBodyIndexTimestamp + ", lastColorCamBodyIndexFrameTime: " + lastColorCamBodyIndexFrameTime);
        //    if ((sensorData.colorCamBodyIndexImage != null || colorBodyIndexBuffer != null) &&
        //        //bodyIndexImage != null &&
        //        (!isSyncBodyAndDepth || (rawBodyIndexTimestamp == depthFrameTime)) && (rawBodyIndexTimestamp != lastColorCamBodyIndexFrameTime))
        //    {
        //        //lock (colorCamBodyIndexFrameLock)
        //        {
        //            lock (bodyTrackerLock)
        //            {
        //                lastColorCamBodyIndexFrameTime = rawBodyIndexTimestamp;
        //                //Debug.Log("D" + deviceIndex + " ColorCamBodyIndexFrameTime: " + lastColorCamBodyIndexFrameTime + ", Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
        //            }
        //        }
        //    }

        //}


        //// stops the body tracker and releases its data
        //public override void StopBodyTracking(KinectInterop.SensorData sensorData)
        //{
        //    // invoke the base method
        //    base.StopBodyTracking(sensorData);
        //}


        // unprojects plane point into the space
        public override Vector3 UnprojectPoint(KinectInterop.CameraIntrinsics intr, Vector2 pixel, float depth)
        {
            if (depth <= 0f || _depthStreamProvider == null)
                return Vector3.zero;

            return _depthStreamProvider.UnprojectPoint(intr, pixel, depth);
        }


        // projects space point onto a plane
        public override Vector2 ProjectPoint(KinectInterop.CameraIntrinsics intr, Vector3 point)
        {
            if (point == Vector3.zero || _depthStreamProvider == null)
                return Vector2.zero;

            return _depthStreamProvider.ProjectPoint(intr, point);
        }


        // transforms a point from one space to another
        public override Vector3 TransformPoint(KinectInterop.CameraExtrinsics extr, Vector3 point)
        {
            if (point == Vector3.zero || _depthStreamProvider == null)
                return Vector2.zero;

            return _depthStreamProvider.TransformPoint(extr, point);
        }

    }
}
