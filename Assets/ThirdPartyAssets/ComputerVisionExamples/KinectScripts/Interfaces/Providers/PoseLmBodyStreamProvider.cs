using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using com.rfilkov.kinect;
using com.rfilkov.devices;
using System.Linq;


namespace com.rfilkov.providers
{
    // pose detection
    public struct PoseDetection
    {
        //public float score;
        //public Vector2 center;
        //public Vector2 extent;

        //public Vector2 hipCenter;
        //public Vector2 roi_full;
        //public Vector2 shoulderCenter;
        //public Vector2 roi_upper;
        //public const int SIZE = 13 * sizeof(float);

        //public override string ToString()
        //{
        //    var vMin = center - extent / 2;
        //    var vMax = center + extent / 2;
        //    return $"score: {score:F3} min: {vMin:F3}; max: {vMax:F3}; center: {center:F3};\nhips: {hipCenter:F3}; roi1: {roi_full:F3}; shc: {shoulderCenter:F3}; roi2: {roi_upper:F3}";
        //}

        public Vector2 center;
        public Vector2 size;
        public float score;

        public Vector3 kp0, kp1, kp2, kp3, kp4;
        public Vector3 kp5, kp6, kp7, kp8;
        public Vector3 kp9, kp10, kp11, kp12;
        public Vector3 kp13, kp14, kp15, kp16;

        public const int SIZE = (5 + 17 * 3) * sizeof(float);

        public override string ToString()
        {
            return $"score: {score:F3} center: {center:F2}; size: {size:F2}\nkp5: {kp5}; kp11: {kp11}";
        }
    }

    // pose region
    public struct PoseRegion
    {
        public Vector4 box;
        public Vector4 dBox;
        public Vector4 size;
        public Vector4 par;

        public Matrix4x4 cropMatrix;
        public Matrix4x4 invMatrix;

        public Vector3 kp0, kp1, kp2, kp3, kp4;
        public Vector3 kp5, kp6, kp7, kp8;
        public Vector3 kp9, kp10, kp11, kp12;
        public Vector3 kp13, kp14, kp15, kp16;

        public const int SIZE = (48 + 17 * 3) * sizeof(float);

        public override string ToString()
        {
            //Matrix4x4 invCropMat = cropMatrix.inverse;
            float angle = box.w * Mathf.Rad2Deg;
            return $"box: {box}, angle: {angle:F1} deg\nkp5: {kp5}, kp11: {kp11}";  // \ncropMat - T: {cropMatrix.GetPosition()}, R: {cropMatrix.rotation.eulerAngles}, S: {cropMatrix.lossyScale}, \ninvMat - T: {invMatrix.GetPosition()}, R: {invMatrix.rotation.eulerAngles}, S: {invMatrix.lossyScale}\n";
            //return $"box: {box}, angle: {angle:F1} deg\ncropMat:\n{cropMatrix}\ninvMat:\n{invMatrix}\n";
        }
    }


    /// <summary>
    /// Per-frame settings of PoseLm-provider. 
    /// </summary>
    public class PoseLmProviderData
    {
        // pose landmarks
        //public ComputeBuffer[] _landmarkBuffers;
        //public ComputeBuffer[] _worldLandmarkBuffers;

        public Vector4[][] _poseLandmarks, _poseWorldLandmarks, _poseKinectLandmarks;

        // pose detections and region
        //public ComputeBuffer _nmsCountBuffer;
        //public ComputeBuffer _nmsOutputBuffer;
        public ComputeBuffer _poseRegionBuffer;
        //public NativeArray<PoseRegion> _poseRegionData;
        public PoseRegion[] _poseRegions;
        public uint _poseRegionCount;
        public bool _isPoseRegionBufferReady;

        public uint[] _poseCounts;
        //public PoseDetection[] _poseDetections;

        // pose count and current index
        public uint _poseCount;
        public uint _poseIndex;

        // landmark regions and counts
        public PoseRegion[] _lmRegions;
        public uint _lmRegionCount;
        public uint _lmPoseCount;
        public ulong _lmRegionTimestamp;

        // body-index image buffer
        public ComputeBuffer _biImageBuffer;
    }

    /// <summary>
    /// PoseLM body stream provider.
    /// </summary>
    public class PoseLmBodyStreamProvider : MonoBehaviour, IBodyStreamProvider
    {
        [Tooltip("Detection score threshold")]
        [Range(0, 1.0f)]
        public float detectScoreThreshold = 0.5f;  // 0.75f;  // 

        [Tooltip("Landmark score threshold")]
        [Range(0, 1.0f)]
        public float landmarkScoreThreshold = 0.5f;  // 0.6f;  // 

        [Tooltip("NMS IoU threshold")]
        [Range(0, 1.0f)]
        public float iouThreshold = 0.3f;

        [Tooltip("Joint tracked threshold")]
        [Range(0, 1.0f)]
        //[HideInInspector]
        internal float jointTrackedThreshold = 0.5f;  // 0.75f;  // 

        [Tooltip("Joint inferred threshold")]
        [Range(0, 1.0f)]
        //[HideInInspector]
        internal float jointInferredThreshold = 0.2f;  // 0.1f;

        [Tooltip("Raw image for debugging purposes.")]
        public UnityEngine.UI.RawImage _debugImage = null;

        [Tooltip("Raw image 2 for debugging purposes.")]
        public UnityEngine.UI.RawImage _debugImage2 = null;

        [Tooltip("Raw image 3 for debugging purposes.")]
        public UnityEngine.UI.RawImage _debugImage3 = null;


        // whether or not to use pose regions from previous frames
        private static readonly bool USE_PREV_REGIONS = false;


        // constants
        public const int MAX_DETECTIONS = 100;
        public const int MAX_BODIES = 3;

        public const int BODY_LM_COUNT = 39;
        public const int SEGMENTATION_SIZE = 256;

        //private const int RVF_MAX_QUEUE_LENGTH = 5;


        // references
        private UniCamInterface _unicamInt;
        private IDeviceManager _deviceManager;
        private KinectManager _kinectManager;
        //private UnityEngine.UI.RawImage _debugImage;

        // stream provider properties
        private string _providerId = null;
        private StreamState _streamState = StreamState.NotStarted;

        // detect multiple users or not
        private bool _isDetectMultipleUsers = true;

        // original image
        //private RenderTexture _sourceTex = null;
        private Vector3 _srcImageScale = Vector3.one;

        // scaled frame props
        private bool _isScalingEnabled = false;
        private int _scaledFrameWidth = 0;
        private int _scaledFrameHeight = 0;

        private ComputeBuffer _scaledBodyIndexBuf = null;

        // scaling shader params
        private ComputeShader _scaleBufShader;
        private Material _scaleBufMaterial;
        //private ComputeBuffer _scaleSrcBuf;
        private int _scaleBufKernel;

        private string _inputNameDetect;
        private int _inputWidthDetect = 0;
        private int _inputHeightDetect = 0;
        private int _inputChannelsDetect = 0;

        private string _inputNameLandmark;
        private int _inputWidthLandmark = 0;
        private int _inputHeightLandmark = 0;
        private int _inputChannelsLandmark = 0;

        // reference to SM
        private SentisManager _sentisManager = null;
        private RuntimeModel _modelDetect = null;
        private RuntimeModel _modelLandmark = null;

        private const string _dmodelName = "bdet";
        private const string _lmodelName = "blm";

        //private IWorker _workerDetect = null;
        //private IWorker _workerLandmark = null;

        // provider state
        private enum ProviderState : int { NotStarted, DetectBodies, ProcessDetection, GetDetection, LandmarkBody, ProcessLandmarks, Ready, CopyFrame }
        //private ProviderState _providerState = ProviderState.NotStarted;

        // letterbox scale and pad
        private Vector2 _lboxScale = Vector2.one;
        private Vector2 _lboxPad = Vector2.zero;

        // shader for data processing
        private ComputeShader _processShader;
        private Material _processIndexMaterial;
        private Material _clearIndexMaterial;

        // detector shaders and buffers
        private ComputeShader _texToTensorShader;

        //private Tensor<float> _detTexTensor;
        //private TextureTransform _detTexTrans;
        ////private ComputeBuffer _detTexBuffer;

        //private Tensor<float> _lmTexTensor;
        //private TextureTransform _lmTexTrans;

        private ComputeBuffer _detCountBuffer;
        private ComputeBuffer _detOutputBuffer;

        private ComputeBuffer _nmsCountBuffer;
        private ComputeBuffer _nmsOutputBuffer;

        // landmark shaders & buffers
        private ComputeBuffer _rawLandmarks2dBuf;
        private ComputeBuffer _rawLandmarks3dBuf;

        private ComputeBuffer _landmarks2dBuf;
        private ComputeBuffer _landmarks3dBuf;
        private ComputeBuffer _landmarksKinBuf;
        //private ComputeBuffer _smoothLandmarks3dBuf;

        // mask material and texture
        private Material _segmMaskMaterial;
        private RenderTexture _segmMaskTex;

        // depth image properties
        private IColorStreamProvider _colorProvider = null;
        private IIRStreamProvider _irProvider = null;
        private IDepthStreamProvider _depthProvider = null;
        private int _depthImageWidth = 0;
        private int _depthImageHeight = 0;

        // references to the data buffer & lock object
        //private byte[] _bodyIndexFrame = null;
        //private object _bodyTrackerLock = null;
        private KinectInterop.SensorData _sensorData = null;

        // last inference time & framesets
        private ulong _lastInfTime = 0;
        private Queue<ulong> _lastInfTs = new Queue<ulong>();
        private Dictionary<ulong, FramesetData> _lastInfBtFs = new Dictionary<ulong, FramesetData>();
        private Dictionary<ulong, FramesetData> _lastInfBiFs = new Dictionary<ulong, FramesetData>();

        // detection textures
        private RenderTexture _detectionTex;
        private RenderTexture _landmarkTex;
        //private Material _detectionMat;
        //private Material _landmarkMat;

        // user tracking manager
        private PoseLmUserTracker _userTracker = null;
        private trackers.ByteTracker _byteTracker = null;

        // action dispatchers
        private const int MAX_QUEUED_FRAMES = FramesetManager.FRAMESETS_COUNT;
        private Queue<FramesetData> _bodyProcessDispatcher = new Queue<FramesetData>(MAX_QUEUED_FRAMES);
        private Queue<FramesetData> _bodySaveDispatcher = new Queue<FramesetData>(MAX_QUEUED_FRAMES);

        // hand states
        protected float leftHandFingerAngle = 0f;
        protected float rightHandFingerAngle = 0f;
        protected ulong lastHandStatesTimestamp = 0;

        private UnityEngine.UI.Text debugText;
        private UnityEngine.UI.Text debugText2;
        private UnityEngine.UI.Text debugText3;


        /// <summary>
        /// Returns the required device manager class name for this stream, or null if no device is required.
        /// </summary>
        /// <returns>Device manager class name, or null</returns>
        public string GetDeviceManagerClass()
        {
            return null;
        }

        /// <summary>
        /// Returns the required device index for this stream, or -1 if no device is required
        /// </summary>
        /// <param name="unicamInt">UniCam interface</param>
        /// <param name="devices">List of available devices</param>
        /// <returns>Device index, or -1</returns>
        public int GetDeviceIndex(UniCamInterface unicamInt, List<KinectInterop.SensorDeviceInfo> devices)
        {
            return -1;
        }

        /// <summary>
        /// Starts the stream provider. Returns true on success, false on failure.
        /// </summary>
        /// <param name="unicamInt">UniCam interface</param>
        /// <param name="deviceManager">Device manager</param>
        /// <param name="kinectManager">Kinect manager</param>
        /// <param name="streamStartedCallback">Stream-started callback (optional)</param>
        /// <returns></returns>
        public string StartProvider(UniCamInterface unicamInt, IDeviceManager deviceManager, KinectManager kinectManager, KinectInterop.SensorData sensorData, 
            Action<bool> streamStartedCallback = null)
        {
            _unicamInt = unicamInt;
            _deviceManager = deviceManager;
            _kinectManager = kinectManager;
            _sensorData = sensorData;
            _streamState = StreamState.NotStarted;

            // get color image provider
            _colorProvider = unicamInt._colorStreamProvider;
            _irProvider = unicamInt._irStreamProvider;
            if (_colorProvider == null && _irProvider == null)
                throw new Exception("Source stream is not available. Please enable either color or IR stream.");

            // get depth image size
            _depthProvider = unicamInt._depthStreamProvider;
            if (_depthProvider == null)
                throw new Exception("Depth stream provider is not available.");

            var depthImageSize = _depthProvider.GetDepthImageSize();
            _depthImageWidth = depthImageSize.x;
            _depthImageHeight = depthImageSize.y;

            //// create body-index frame
            //int bodyIndexFrameSize = _depthImageWidth * _depthImageHeight;
            //_bodyIndexFrame = new byte[bodyIndexFrameSize];

            // get source image scale
            _srcImageScale = //_irProvider != null ? _irProvider.GetBTSourceImageScale() : 
                _colorProvider != null ? _colorProvider.GetBTSourceImageScale() : Vector3.one;

            // instantiate SM
            _sentisManager = SentisManager.Instance;
            if (_sentisManager == null)
            {
                _sentisManager = gameObject.AddComponent<SentisManager>();
            }
            else
            {
                _sentisManager.StartManager();
            }

            // load det model
            string bdetModelName = "bdet_1.2";
            _modelDetect = _sentisManager.AddModel(_dmodelName, bdetModelName);  // , 2
            if (_modelDetect == null)
            {
                // model not found
                return null;
            }

            _inputNameDetect = _modelDetect.inputNames[0];  // _modelDetect.inputs[0].name;
            var inShapeDetect = _modelDetect.inputShapes[_inputNameDetect];  // _modelDetect.inputs[0].shape;

            _inputWidthDetect = (int)inShapeDetect[3];  // inShapeDetect.Get(3);
            _inputHeightDetect = (int)inShapeDetect[2];  // inShapeDetect.Get(2);
            _inputChannelsDetect = (int)inShapeDetect[1];  // inShapeDetect.Get(1);
            //Debug.Log($"BDet model loaded - iW: {_inputWidthDetect}, iH: {_inputHeightDetect}, iC: {_inputChannelsDetect}, name: {_inputNameDetect}, dt: {DateTime.Now.ToString("HH:mm:ss.fff")}");

            // load lm model
            string blmModelName = "blm_1.1";
            _modelLandmark = _sentisManager.AddModel(_lmodelName, blmModelName);
            if (_modelLandmark == null)
            {
                // model not found
                return null;
            }

            _inputNameLandmark = _modelLandmark.inputNames[0];  // _modelLandmark.inputs[0].name;
            var inShapeLandmark = _modelLandmark.inputShapes[_inputNameLandmark];  // _modelLandmark.inputs[0].shape;

            _inputWidthLandmark = (int)inShapeLandmark[2];  // inShapeLandmark.Get(2);
            _inputHeightLandmark = (int)inShapeLandmark[1];  // inShapeLandmark.Get(1);
            _inputChannelsLandmark = (int)inShapeLandmark[3];  // inShapeLandmark.Get(3);
            //Debug.Log($"BLm model loaded - iW: {_inputWidthLandmark}, iH: {_inputHeightLandmark}, iC: {_inputChannelsLandmark}, name: {_inputNameLandmark}, dt: {DateTime.Now.ToString("HH:mm:ss.fff")}");

            // init textures
            _detectionTex = KinectInterop.CreateRenderTexture(_detectionTex, _inputWidthDetect, _inputHeightDetect, RenderTextureFormat.ARGB32);
            _landmarkTex = KinectInterop.CreateRenderTexture(_landmarkTex, _inputWidthLandmark, _inputHeightLandmark, RenderTextureFormat.ARGB32);

            //// init shaders
            //Shader lboxTexShader = Shader.Find("Kinect/LetterboxTexShader");
            //_detectionMat = new Material(lboxTexShader);
            //Shader lmarkTexShader = Shader.Find(KinectInterop.IsSupportsComputeShaders() ? "Kinect/CropTexShader" : "Kinect/CropTexMatShader");
            //_landmarkMat = new Material(lmarkTexShader);

            // create detTex-to-tensor shader, tensor & buffer
            _texToTensorShader = Resources.Load("TexToTensorShader") as ComputeShader;

            //// transforms & tensors
            //_detTexTrans = new TextureTransform().SetDimensions(_inputWidthDetect, _inputHeightDetect, _inputChannelsDetect).SetTensorLayout(TensorLayout.NCHW);
            //_detTexTensor = new Tensor<float>(new TensorShape(1, _inputChannelsDetect, _inputHeightDetect, _inputWidthDetect));
            //_lmTexTrans = new TextureTransform().SetDimensions(_inputWidthLandmark, _inputHeightLandmark, _inputChannelsLandmark).SetTensorLayout(TensorLayout.NHWC);
            //_lmTexTensor = new Tensor<float>(new TensorShape(1, _inputHeightLandmark, _inputWidthLandmark, _inputChannelsLandmark));

            if (KinectInterop.IsSupportsComputeShaders())
            {
                // processing shader
                _processShader = Resources.Load("PoseLmCompute") as ComputeShader;
                //_processShader.SetInt("_currentRegCount", 0);

                // scale shader
                _scaleBufShader = Resources.Load("BodyIndexScaleShader") as ComputeShader;
                _scaleBufKernel = _scaleBufShader != null ? _scaleBufShader.FindKernel("ScaleBodyIndexImage") : -1;
            }

            //// init pose-region buffer
            //PoseRegion[] poseRegions = new PoseRegion[MAX_BODIES];
            //for (int i = 0; i < MAX_BODIES; i++)
            //{
            //    poseRegions[i] = new PoseRegion()
            //    {
            //        box = new Vector4(0.5f, 0.5f, 1f, 0f),
            //        dBox = Vector4.zero,

            //        size = new Vector4(0.5f, 0.5f, 1f, 1f),
            //        par = new Vector4(0f, i == 0 ? 1 : 0, 1, 0),

            //        cropMatrix = Matrix4x4.identity,
            //        invMatrix = Matrix4x4.identity
            //    };
            //}

            //_rvfCurrentTimes = new float[MAX_BODIES];
            //_rvfQueueLength = 0;

            Vector4[] lm2dArray = new Vector4[BODY_LM_COUNT + 1];
            Vector4[] lm3dArray = new Vector4[BODY_LM_COUNT + 1];
            Vector4[] lmKinArray = new Vector4[BODY_LM_COUNT + 1];

            if (KinectInterop.IsSupportsComputeShaders())
            {
                // compute shaders and buffers
                _rawLandmarks2dBuf = new ComputeBuffer(BODY_LM_COUNT + 1, sizeof(float) * 4);  // new ComputeBuffer[MAX_BODIES];
                _rawLandmarks3dBuf = new ComputeBuffer(BODY_LM_COUNT + 1, sizeof(float) * 4);  // new ComputeBuffer[MAX_BODIES];

                _landmarks2dBuf = new ComputeBuffer(BODY_LM_COUNT + 1, sizeof(float) * 4);  // new ComputeBuffer[MAX_BODIES];
                _landmarks3dBuf = new ComputeBuffer(BODY_LM_COUNT + 1, sizeof(float) * 4);  // new ComputeBuffer[MAX_BODIES];
                _landmarksKinBuf = new ComputeBuffer(BODY_LM_COUNT + 1, sizeof(float) * 4);  // new ComputeBuffer[MAX_BODIES];
                //_smoothLandmarks3dBuf = new ComputeBuffer[MAX_BODIES];

                _landmarks2dBuf.SetData(lm2dArray);
                _landmarks3dBuf.SetData(lm3dArray);
                _landmarksKinBuf.SetData(lmKinArray);

                //_rvfQueueBuffers = new ComputeBuffer[MAX_BODIES];
                //_rvfScaleBuffers = new ComputeBuffer[MAX_BODIES];

                //for (int i = 0; i < MAX_BODIES; i++)
                //{
                //    _rawLandmarks2dBuf[i] = new ComputeBuffer(BODY_LM_COUNT + 1, sizeof(float) * 4);
                //    _rawLandmarks3dBuf[i] = new ComputeBuffer(BODY_LM_COUNT + 1, sizeof(float) * 4);

                //    _landmarks2dBuf[i] = new ComputeBuffer(BODY_LM_COUNT + 1, sizeof(float) * 4);
                //    _landmarks3dBuf[i] = new ComputeBuffer(BODY_LM_COUNT + 1, sizeof(float) * 4);
                //    _smoothLandmarks3dBuf[i] = new ComputeBuffer(BODY_LM_COUNT, sizeof(float) * 4);

                //    //_rvfQueueBuffers[i] = new ComputeBuffer(RVF_MAX_QUEUE_LENGTH * BODY_LM_COUNT, sizeof(float) * 4);
                //    _rvfScaleBuffers[i] = new ComputeBuffer(BODY_LM_COUNT, sizeof(float) * 3);
                //}

                // create detect-output buffers
                _detCountBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
                _detOutputBuffer = new ComputeBuffer(MAX_DETECTIONS, PoseDetection.SIZE, ComputeBufferType.Append);

                _nmsCountBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
                _nmsOutputBuffer = new ComputeBuffer(MAX_BODIES, PoseDetection.SIZE, ComputeBufferType.Append);

                //_nmsPoseBuffer = new ComputeBuffer(MAX_DETECTIONS, PoseDetection.SIZE);
                //_nmsKeepBuffer = new ComputeBuffer(MAX_DETECTIONS, sizeof(uint));
                //_nmsIouBuffer = new ComputeBuffer(MAX_DETECTIONS * MAX_DETECTIONS, sizeof(float));
                //_nmsScoreBuffer = new ComputeBuffer(MAX_DETECTIONS * MAX_DETECTIONS, sizeof(uint));

                //_poseRegionBuffer = new ComputeBuffer(MAX_BODIES, PoseRegion.SIZE);  // 24
                //_regionCountBuffer = new ComputeBuffer(1, sizeof(uint));
                //_poseRegionCurrentTime = _poseRegionStartTime = Time.time;

                //_poseRegionBuffer.SetData(poseRegions);

                //// init region-count buffer
                //uint[] _regionCount = new uint[1];
                //_regionCount[0] = 1;  // count
                ////_regionCount[1] = 1;  // lastId
                ////_regionCountBuffer.SetData(_regionCount);
            }

            // create user tracker
            _userTracker = new PoseLmUserTracker(PoseJoint2JointType, _kinectManager);  // landmarkScoreThreshold);
            _byteTracker = new trackers.ByteTracker();
            _byteTracker.trackHighThresh = jointTrackedThreshold;
            _byteTracker.trackLowThresh = jointInferredThreshold;
            _byteTracker.newTrackThresh = jointTrackedThreshold;

            // last inference time & framesets
            _lastInfTime = 0;
            _lastInfTs.Clear();
            _lastInfBtFs.Clear();
            _lastInfBiFs.Clear();

            _providerId = deviceManager != null ? deviceManager.GetDeviceId() + "_bs" : "poselm_bs";
            _streamState = StreamState.Working;

            if (_debugImage3 != null)
            {
                Shader segmMashShader = Shader.Find("Kinect/SegmMaskShader");
                _segmMaskMaterial = new Material(segmMashShader);
                _segmMaskTex = KinectInterop.CreateRenderTexture(_segmMaskTex, SEGMENTATION_SIZE, SEGMENTATION_SIZE);

                _debugImage3.texture = _segmMaskTex;  // _detectionTex;  // _landmarkTex;  // _segmMaskTex;  // _sourceTex;  // 
            }

            //// set provider state
            //_providerState = ProviderState.DetectBodies;

            if (streamStartedCallback != null)
            {
                streamStartedCallback(true);
            }

            return _providerId;
        }

        /// <summary>
        /// Stops the stream provider.
        /// </summary>
        public void StopProvider()
        {
            //// dispose det-tex & lm-tex tensors
            //_detTexTensor?.Dispose();
            //_detTexTensor = null;
            ////_detTexBuffer = null;

            //_lmTexTensor?.Dispose();
            //_lmTexTensor = null;

            _detectionTex?.Release();
            _detectionTex = null;

            _landmarkTex?.Release();
            _landmarkTex = null;

            if (KinectInterop.IsSupportsComputeShaders())
            {
                // compute buffers
                _rawLandmarks2dBuf?.Dispose();
                _rawLandmarks2dBuf = null;

                _rawLandmarks3dBuf?.Dispose();
                _rawLandmarks3dBuf = null;

                _landmarks2dBuf?.Dispose();
                _landmarks2dBuf = null;

                _landmarks3dBuf?.Dispose();
                _landmarks3dBuf = null;

                _landmarksKinBuf?.Dispose();
                _landmarksKinBuf = null;

                // detection buffers
                _detCountBuffer?.Dispose();
                _detCountBuffer = null;

                _detOutputBuffer?.Dispose();
                _detOutputBuffer = null;

                _nmsCountBuffer?.Dispose();
                _nmsCountBuffer = null;

                _nmsOutputBuffer?.Dispose();
                _nmsOutputBuffer = null;
            }

            // scaling props
            _isScalingEnabled = false;

            _scaledBodyIndexBuf?.Release();
            _scaledBodyIndexBuf = null;

            _segmMaskTex?.Release();
            _segmMaskTex = null;
            _segmMaskMaterial = null;

            KinectInterop.Destroy(_processIndexMaterial);
            KinectInterop.Destroy(_clearIndexMaterial);
            KinectInterop.Destroy(_scaleBufMaterial);

            _userTracker.StopUserTracking();
            //_userMgr = null;

            // last inference time & framesets
            _lastInfTs.Clear();
            _lastInfBtFs.Clear();
            _lastInfBiFs.Clear();

            _modelDetect = null;
            _modelLandmark = null;
            _streamState = StreamState.Stopped;
        }

        // creates provider settings, as needed
        private PoseLmProviderData GetProviderData(FramesetData fsData)
        {
            if (fsData == null || _streamState != StreamState.Working)
                return null;
            if (fsData._bodyProviderSettings != null)
                return (PoseLmProviderData)fsData._bodyProviderSettings;

            // create and initialize plm data
            PoseLmProviderData plmData = new PoseLmProviderData();

            plmData._poseLandmarks = new Vector4[MAX_BODIES][];
            plmData._poseWorldLandmarks = new Vector4[MAX_BODIES][];
            plmData._poseKinectLandmarks = new Vector4[MAX_BODIES][];

            for (int i = 0; i < MAX_BODIES; i++)
            {
                plmData._poseLandmarks[i] = new Vector4[BODY_LM_COUNT + 1];
                plmData._poseWorldLandmarks[i] = new Vector4[BODY_LM_COUNT + 1];
                plmData._poseKinectLandmarks[i] = new Vector4[BODY_LM_COUNT + 1];
            }

            // init pose-region buffer
            PoseRegion[] poseRegions = new PoseRegion[MAX_BODIES];
            plmData._lmRegions = new PoseRegion[MAX_BODIES];

            for (int i = 0; i < MAX_BODIES; i++)
            {
                poseRegions[i] = new PoseRegion()
                {
                    box = new Vector4(0.5f, 0.5f, 1f, 0f),
                    dBox = Vector4.zero,

                    size = new Vector4(0.5f, 0.5f, 1f, 1f),
                    par = Vector4.zero,  // new Vector4(0f, i == 0 ? 1 : 0, 1, 0),

                    cropMatrix = Matrix4x4.identity,
                    invMatrix = Matrix4x4.identity
                };

                plmData._lmRegions[i] = new PoseRegion();
            }

            // pose detections and regions
            if (KinectInterop.IsSupportsComputeShaders())
            {
                // compute shaders and buffers
                //plmData._nmsCountBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
                //plmData._nmsOutputBuffer = new ComputeBuffer(MAX_DETECTIONS, PoseDetection.SIZE, ComputeBufferType.Append);
                plmData._poseRegionBuffer = new ComputeBuffer(MAX_BODIES, PoseRegion.SIZE);
                plmData._poseRegionBuffer.SetData(poseRegions);
            }

            plmData._poseCounts = new uint[1];
            //plmData._poseDetections = new PoseDetection[MAX_BODIES];

            plmData._poseRegions = poseRegions;  // new PoseRegion[MAX_BODIES];

            plmData._poseCount = 0;
            plmData._poseRegionCount = 0;
            plmData._isPoseRegionBufferReady = false;

            plmData._poseIndex = 0;

            fsData._bodyProviderSettings = plmData;
            //Debug.Log($"fs: {fsData._fsIndex}, GetProviderData");

            plmData._lmRegionCount = 0;
            plmData._lmPoseCount = 0;
            plmData._lmRegionTimestamp = 0;

            return plmData;
        }

        /// <summary>
        /// Releases stream resources allocated in the frameset.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        public void ReleaseFrameData(FramesetData fsData)
        {
            if (fsData == null || fsData._isBodyDataCopied)
                return;

            ((RenderTexture)fsData._btSourceImage)?.Release();
            fsData._btSourceImage = null;

            fsData._alTrackedBodies.Clear();
            fsData._bodyIndexFrame = null;
            fsData._scaledBodyIndexFrame = null;

            if(fsData._bodyProviderSettings != null)
            {
                PoseLmProviderData plmData = (PoseLmProviderData)fsData._bodyProviderSettings;

                if(plmData._poseLandmarks != null)
                {
                    for (int i = 0; i < MAX_BODIES; i++)
                    {
                        plmData._poseLandmarks[i] = null;
                        plmData._poseWorldLandmarks[i] = null;
                        plmData._poseKinectLandmarks[i] = null;
                    }
                }

                //plmData._landmarkBuffers = null;
                //plmData._worldLandmarkBuffers = null;

                plmData._poseLandmarks = null;
                plmData._poseWorldLandmarks = null;
                plmData._poseKinectLandmarks = null;

                if (KinectInterop.IsSupportsComputeShaders())
                {
                    plmData._poseRegionBuffer?.Dispose();
                    plmData._poseRegionBuffer = null;
                }

                plmData._poseCounts = null;
                //plmData._poseDetections = null;

                plmData._poseRegions = null;
                plmData._lmRegions = null;

                // body-index image buffer
                plmData._biImageBuffer?.Dispose();
                plmData._biImageBuffer = null;

        		fsData._bodyProviderSettings = null;
                //Debug.Log($"fs: {fsData._fsIndex}, ReleaseFrameData");
            }
        }

        /// <summary>
        /// Returns the current state of the stream provider.
        /// </summary>
        /// <returns>Stream provider state.</returns>
        public StreamState GetProviderState()
        {
            return _streamState;
        }

        /// <summary>
        /// Returns the provider-id of the currently opened stream provider.
        /// </summary>
        /// <returns>Provider ID, or null if the provider is not started</returns>
        public string GetProviderId()
        {
            return _providerId;
        }

        /// <summary>
        /// Polls the stream frames in a thread.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="fsData">Frameset data</param>
        public void PollStreamFrames(KinectInterop.SensorData sensorData, FramesetData fsData)
        {
            //while (_bodyProcessDispatcher.TryDequeue(out FramesetData fsd))
            int queueLen = _bodyProcessDispatcher.Count;
            for(int i = 0; i < queueLen; i++)
            {
                FramesetData fsd = _bodyProcessDispatcher.Dequeue();
                ProcessBodyFrame(sensorData, fsd);
            }
        }

        /// <summary>
        /// Updates the stream data in the main thread.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="fsData">Frameset data</param>
        public void UpdateStreamData(KinectInterop.SensorData sensorData, FramesetData fsData)
        {
            //while (_bodySaveDispatcher.TryDequeue(out FramesetData actionFs))
            int queueLen = _bodySaveDispatcher.Count;
            for (int i = 0; i < queueLen; i++)
            {
                FramesetData fsd = _bodySaveDispatcher.Dequeue();
                SaveFrameBodyData(fsd);
            }

            // get the correct frame
            fsData = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();  //?._fsPrev;
            bool isBodyIndexRequested = (_unicamInt.frameSourceFlags & KinectInterop.FrameSource.TypeBodyIndex) != 0;

            if (!fsData._isBodyTrackerFrameReady || (isBodyIndexRequested && !fsData._isBodyIndexFrameReady))
            {
                ProcessSourceImage(fsData);
            }

            if (fsData._isBodyIndexFrameReady && _isScalingEnabled && !fsData._isScaledBodyIndexFrameReady &&
                fsData._scaledBodyIndexFrameTimestamp != fsData._bodyIndexTimestamp)
            {
                // scale body-index frame
                fsData._scaledBodyIndexFrameTimestamp = fsData._bodyIndexTimestamp;
                ScaleBodyIndexFrame(fsData);
            }
        }

        /// <summary>
        /// Checks if the stream is ready to start processing next frame.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="fsData">Frameset data</param>
        /// <returns>true if the stream is ready for next frame, false otherwise</returns>
        public bool IsReadyForNextFrame(KinectInterop.SensorData sensorData, FramesetData fsData)
        {
            if (!_unicamInt.isSyncBodyAndDepth || fsData._bodyProviderState >= (int)ProviderState.ProcessDetection ||  // LandmarkBody ||
                (_kinectManager != null && _kinectManager.getBodyFrames == KinectManager.BodyTextureType.None))
            {
                return true;
            }

            return false;
        }

        ///// <summary>
        ///// Returns the sensor space scale.
        ///// </summary>
        ///// <returns>Sensor space scale</returns>
        //public Vector3 GetSensorSpaceScale()
        //{
        //    return new Vector3(_srcImageScale.x, -1f, 1f);  // depthScale.x
        //}

        /// <summary>
        /// Returns the body-index image scale.
        /// </summary>
        /// <returns>Body-index image scale</returns>
        public Vector3 GetBodyIndexImageScale()
        {
            return new Vector3(_srcImageScale.x, -1f, 1f);  // depthScale.x
        }

        /// <summary>
        /// Returns the body-index image size.
        /// </summary>
        /// <returns>Body-index image size (width, height)</returns>
        public Vector2Int GetBodyIndexImageSize()
        {
            return new Vector2Int(_depthImageWidth, _depthImageHeight);
        }

        /// <summary>
        /// Instructs the body-stream provider to stop body tracking.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        public void StopBodyTracking(KinectInterop.SensorData sensorData)
        {
            // do nothing
        }

        /// <summary>
        /// Determines whether to detect multiple users or not
        /// </summary>
        /// <param name="isDetectMultipleUsers">Whether to detect multiple users or not</param>
        public void SetDetectMultipleUsers(bool isDetectMultipleUsers)
        {
            _isDetectMultipleUsers = isDetectMultipleUsers;
        }

        /// <summary>
        /// Gets the maximum number of users that could be detected by the body stream provider.
        /// </summary>
        /// <returns>The maximum number of users</returns>
        public uint GetMaxUsersCount()
        {
            return MAX_BODIES;
        }

        /// <summary>
        /// Sets scaled frame properties.
        /// </summary>
        /// <param name="isEnabled">Whether the scaling is enabled or not</param>
        /// <param name="scaledWidth">Scaled image width</param>
        /// <param name="scaledHeight">Scaled image height</param>
        public void SetScaledFrameProps(bool isEnabled, int scaledWidth, int scaledHeight)
        {
            _isScalingEnabled = isEnabled;
            _scaledFrameWidth = scaledWidth;
            _scaledFrameHeight = scaledHeight;
        }

        /// <summary>
        /// Checks if the frame scaling is enabled or not.
        /// </summary>
        /// <returns>true if scaling is enabled, false otherwise</returns>
        public bool IsFrameScalingEnabled()
        {
            return _isScalingEnabled;
        }

        // scales current body-index frame, according to requirements
        private void ScaleBodyIndexFrame(FramesetData fsData)
        {
            PoseLmProviderData plmData = GetProviderData(fsData);
            if (fsData == null || plmData == null)
                return;

            int destFrameLen = _scaledFrameWidth * _scaledFrameHeight;
            if (fsData._scaledBodyIndexFrame == null || fsData._scaledBodyIndexFrame.Length != destFrameLen)
            {
                fsData._scaledBodyIndexFrame = new byte[destFrameLen];
            }

            if (KinectInterop.IsSupportsComputeShaders())
            {
                // compute shaders
                //int srcBufLength = _depthImageWidth * _depthImageHeight >> 2;
                //if (_scaleSrcBuf == null || _scaleSrcBuf.count != srcBufLength)
                //    _scaleSrcBuf = KinectInterop.CreateComputeBuffer(_scaleSrcBuf, srcBufLength, sizeof(uint));

                //if (fsData._bodyIndexFrame != null)
                //{
                //    KinectInterop.SetComputeBufferData(_scaleSrcBuf, fsData._bodyIndexFrame, fsData._bodyIndexFrame.Length, sizeof(byte));
                //}

                int biImageBufLen = (_depthImageWidth * _depthImageHeight) >> 2;
                if (plmData._biImageBuffer == null || plmData._biImageBuffer.count != biImageBufLen)
                {
                    plmData._biImageBuffer?.Dispose();
                    plmData._biImageBuffer = new ComputeBuffer(biImageBufLen, sizeof(uint));
                }

                int destBufLen = destFrameLen >> 2;
                if (_scaledBodyIndexBuf == null || _scaledBodyIndexBuf.count != destBufLen)
                    _scaledBodyIndexBuf = KinectInterop.CreateComputeBuffer(_scaledBodyIndexBuf, destBufLen, sizeof(uint));

                CommandBuffer cmd = new CommandBuffer { name = "ScaleBodyIndexCb" };
                cmd.SetComputeBufferParam(_scaleBufShader, _scaleBufKernel, "_BodyIndexBuf", plmData._biImageBuffer);  // _scaleSrcBuf
                cmd.SetComputeBufferParam(_scaleBufShader, _scaleBufKernel, "_TargetBuf", _scaledBodyIndexBuf);
                cmd.SetComputeIntParam(_scaleBufShader, "_BodyIndexImgWidth", _depthImageWidth);
                cmd.SetComputeIntParam(_scaleBufShader, "_BodyIndexImgHeight", _depthImageHeight);
                cmd.SetComputeIntParam(_scaleBufShader, "_TargetImgWidth", _scaledFrameWidth);
                cmd.SetComputeIntParam(_scaleBufShader, "_TargetImgHeight", _scaledFrameHeight);
                cmd.DispatchCompute(_scaleBufShader, _scaleBufKernel, _scaledFrameWidth / 8, _scaledFrameHeight / 8, 1);

                //Debug.Log($"fs: {fsData._fsIndex} - Started body-index scaling for ts: {fsData._scaledBodyIndexFrameTimestamp}. Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                //Debug.Log($"fs: {fsData._fsIndex}, ScaleBodyIndexImage, frame: {kinect.UniCamInterface._frameIndex}");
                //Debug.Log($"fs: {fsData._fsIndex}, AsyncGPUReadback(_scaledBodyIndexBuf), frame: {kinect.UniCamInterface._frameIndex}");

                cmd.RequestAsyncReadback(_scaledBodyIndexBuf, request =>
                {
                    FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();

                    if (fsd != null && fsd._scaledBodyIndexFrame != null)
                    {
                        if (request.width == fsd._scaledBodyIndexFrame.Length)
                            request.GetData<byte>().CopyTo(fsd._scaledBodyIndexFrame);
                        fsd._scaledBodyIndexFrameTimestamp = fsData._bodyIndexTimestamp;
                        fsd._isScaledBodyIndexFrameReady = true;
                        //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsd._fsIndex} ScaledBodyIndexTime: {fsd._scaledBodyIndexFrameTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                    }
                });

                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();
            }
        }


        // get the available regions in a previous frameset 
        private FramesetData GetAvlRegionsInPrevFs(FramesetData fsData, PoseLmProviderData plmData)
        {
            FramesetData curFsData = fsData;
            PoseLmProviderData curPlmData = plmData;
            int i = FramesetManager.FRAMESETS_COUNT;

            bool isRegionBufferReady = USE_PREV_REGIONS ? true : plmData != null ? plmData._isPoseRegionBufferReady : false;
            while (isRegionBufferReady && plmData != null && plmData._poseRegionCount == 0 && fsData._fsIndex != fsData._fsPrev._fsIndex && i > 0)
            {
                fsData = fsData._fsPrev;
                plmData = GetProviderData(fsData);
                i--;
            }

            if(isRegionBufferReady && plmData != null && plmData._poseRegionCount > 0)
            {
                KinectInterop.CopyBytes(plmData._poseRegions, PoseRegion.SIZE, curPlmData._lmRegions, PoseRegion.SIZE);
                curPlmData._lmRegionCount = plmData._poseRegionCount;
                curPlmData._lmPoseCount = plmData._poseCount;
                curPlmData._lmRegionTimestamp = fsData._btSourceImageTimestamp;
                //Debug.Log($"fs: {curFsData._fsIndex}, Got regions from fs: {fsData._fsIndex}, srcTs: {curPlmData._lmRegionTimestamp}\nregCount: {curPlmData._lmRegionCount}, poseCount: {curPlmData._lmPoseCount}, regBufferReady: {curPlmData._isPoseRegionBufferReady}");

                return fsData;
            }
            else
            {
                //Debug.Log($"fs: {curFsData._fsIndex}, No prev regions found.");
            }

            return null;
        }

        // processes the source image to detect bodies
        private void ProcessSourceImage(FramesetData fsData)
        {
            if (_sentisManager == null || _modelDetect == null || _modelLandmark == null)
                return;

            PoseLmProviderData plmData = GetProviderData(fsData);
            if (fsData == null || plmData == null)
                return;

            if (fsData._bodyProviderState == 0)
            {
                //SetInitialProviderState(fsData);
                fsData._bodyProviderState = (int)ProviderState.DetectBodies;
            }

            int fsIndex = _unicamInt.isSyncBodyAndDepth ? fsData._fsIndex : -1;  // fsData._fsIndex;  // 
            //Debug.Log($"  PlmBT: fs: {fsData._fsIndex} - {(ProviderState)fsData._bodyProviderState}/{fsData._bodyProviderState} - pCount: {plmData._lmPoseCount}, rCount: {plmData._lmRegionCount}\nfsTs: {fsData._rawFrameTimestamp}, srcFs: {fsData._btSourceImageTimestamp}, bdTs: {fsData._bodyDataTimestamp}, biTs: {fsData._bodyIndexTimestamp}");

            switch ((ProviderState)fsData._bodyProviderState)
            {
                case ProviderState.DetectBodies:
                    // check for inference time
                    //Debug.Log($"fs: {fsData._fsIndex} DetectBodies - colorTs: {fsData._rawFrameTimestamp}, lastTs: {_lastInfTime}\ndTs: {fsData._rawFrameTimestamp - _lastInfTime}" + (_lastInfBtFs.ContainsKey(_lastInfTime) ? $";    last fs: {_lastInfBtFs[_lastInfTime]._fsIndex}, ts: {_lastInfBtFs[_lastInfTime]._rawFrameTimestamp}" : ""));
                    if (_lastInfTime != 0 && fsData._rawFrameTimestamp > _lastInfTime && (fsData._rawFrameTimestamp - _lastInfTime) < KinectInterop.MIN_TIME_BETWEEN_INF &&
                        _lastInfBtFs.ContainsKey(_lastInfTime) && _lastInfBtFs[_lastInfTime]._rawFrameTimestamp == _lastInfTime)
                    {
                        // continue w/ copy-frame wait
                        SetCopyLastInfFrame(fsData);
                    }

                    // detect bodies
                    else if (_isDetectMultipleUsers)
                    {
                        if (!_sentisManager.IsModelStarted(fsIndex, _dmodelName))
                        {
                            if (DetectBodiesInImage(fsData, plmData))
                            {
                                fsData._bodyProviderState = (int)ProviderState.ProcessDetection;
                                ProcessSourceImage(fsData);
                            }
                        }
                        //else if (_sentisManager.GetModelFsIndex(_dmodelName) == fsData._fsIndex)
                        //{
                        //    fsData._bodyProviderState = (int)ProviderState.ProcessDetection;
                        //}
                    }
                    else
                    {
                        // single user - skip detection phase
                        ProcessDetectedBodies(fsData, plmData);  // continues w/ ProcessDetection next

                        if(fsData._bodyProviderState != (int)ProviderState.DetectBodies)
                        {
                            ProcessSourceImage(fsData);
                        }
                    }
                    break;

                case ProviderState.ProcessDetection:
                    // process detected bodies
                    bool bDetModelReady = _sentisManager.IsModelReady(fsIndex, _dmodelName);

                    if (bDetModelReady && plmData != null)
                    {
                        ProcessDetectedBodies(fsData, plmData);
                        _sentisManager.ClearModelReady(fsIndex, _dmodelName);
                    }

                    // check for available regions
                    FramesetData fsReg = bDetModelReady ? GetAvlRegionsInPrevFs(fsData, plmData) : null;

                    if (fsReg != null)
                    {
                        // reset pose-index
                        plmData._poseIndex = 0;

                        //if(fsData._btSourceImageTimestamp != plmData._lmRegionTimestamp)
                        {
                            // update the region-buffer
                            if(USE_PREV_REGIONS)
                            {
                                plmData._poseRegionBuffer.SetData(plmData._lmRegions);
                                Debug.Log($"fs: {fsData._fsIndex} - Set region buffer to lmRegions (ProcDet)\nrCount: {plmData._lmRegionCount}, bCount: {plmData._lmPoseCount}, rBufferReady: {plmData._isPoseRegionBufferReady}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                            }

                            //for (int i = 0; i < plmData._lmRegionCount; i++)
                            //{
                            //    var lmRegion = plmData._lmRegions[i];
                            //    Debug.Log($"fs: {fsData._fsIndex}, Lm1Reg{i}/{plmData._poseCount} {lmRegion}\nsize: {lmRegion.size:F2}, par: {lmRegion.par:F2}, ts: {(float)(fsData._btSourceImageTimestamp * 0.0001f):F3}, time: {Time.time:F3}\n");  // +
                            //}
                        }

                        if(fsData._bodyProviderState == (int)ProviderState.ProcessDetection)
                        {
                            fsData._bodyProviderState = (int)ProviderState.GetDetection;
                        }
                    }
                    break;

                case ProviderState.GetDetection:
                    //fsData._bodyProviderState = ProviderState.LandmarkBody;   // moved to ProcessDetectedBodies()
                    break;

                case ProviderState.LandmarkBody:
                    // detect body landmarks
                    if (!_sentisManager.IsModelStarted(fsIndex, _lmodelName) && plmData != null)
                    {
                        if(DetectBodyLandmarks(fsData, plmData, plmData._poseIndex))
                        {
                            fsData._bodyProviderState = (int)ProviderState.ProcessLandmarks;
                        }
                    }
                    break;

                case ProviderState.ProcessLandmarks:
                    // process detected landmarks
                    fsIndex = _unicamInt.isSyncBodyAndDepth ? fsData._fsIndex : -1;  // fsData._fsIndex;  // 
                    if (_sentisManager.IsModelReady(fsIndex, _lmodelName) && plmData != null)
                    {
                        if(ProcessDetectedLandmarks(fsData, plmData, plmData._poseIndex))
                        {
                            _sentisManager.ClearModelReady(fsIndex, _lmodelName);

                            plmData._poseIndex++;
                            if (plmData._poseIndex < plmData._poseCount)
                            {
                                fsData._bodyProviderState = (int)ProviderState.LandmarkBody;
                            }
                            else
                            {
                                fsData._bodyProviderState = (int)ProviderState.Ready;  // (int)ProviderState.DetectBodies;
                                plmData._poseIndex--;
                            }
                        }
                    }
                    break;

                case ProviderState.CopyFrame:
                    // copy data from the last finished frame
                    CopyLastReadyFrameData(fsData, plmData);
                    break;
            }
        }

        // copies data from the last finished frame
        private void CopyLastReadyFrameData(FramesetData fsData, PoseLmProviderData plmData)
        {
            FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();
            PoseLmProviderData pld = _unicamInt.isSyncBodyAndDepth ? plmData : GetProviderData(fsd);

            // body data
            FramesetData lastBtFs = _lastInfBtFs.ContainsKey(fsData._refBtTs) && _lastInfBtFs[fsData._refBtTs]._rawFrameTimestamp == fsData._refBtTs ? _lastInfBtFs[fsData._refBtTs] : null;
            if (lastBtFs == null && _unicamInt.isSyncBodyAndDepth)
                lastBtFs = fsd._fsPrev;

            if (!fsd._isBodyDataFrameReady && (lastBtFs == null || lastBtFs._isBodyDataFrameReady))
            {
                if(lastBtFs != null)
                {
                    uint bodyCount = fsd._trackedBodiesCount = lastBtFs._trackedBodiesCount;

                    // create the needed slots
                    while (fsd._alTrackedBodies.Count < bodyCount)
                    {
                        fsd._alTrackedBodies.Add(new KinectInterop.BodyData((int)KinectInterop.JointType.Count));
                    }

                    for (int i = 0; i < bodyCount; i++)
                    {
                        fsd._alTrackedBodies[i] = lastBtFs._alTrackedBodies[i];
                    }
                }

                // body frame is ready
                fsd._bodyDataTimestamp = fsd._rawFrameTimestamp + fsd._trackedBodiesCount;
                fsd._isBodyDataFrameReady = true;
                //Debug.Log($"    D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, RawCpBodyDataTime: {fsd._bodyDataTimestamp}, bCount: {fsd._trackedBodiesCount}, fsBodies: {fsd._alTrackedBodies.Count}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff") + $"\nfsData: {fsData._fsIndex}, fsTime: {fsData._bodyDataTimestamp}");
            }

            // body-index frame
            FramesetData lastBiFs = _lastInfBiFs.ContainsKey(fsData._refBtTs) && _lastInfBiFs[fsData._refBtTs]._rawFrameTimestamp == fsData._refBtTs ? _lastInfBiFs[fsData._refBtTs] : null;
            if (lastBiFs == null && _unicamInt.isSyncBodyAndDepth)
                lastBiFs = fsd._fsPrev;

            bool isBodyIndexRequested = (_unicamInt.frameSourceFlags & KinectInterop.FrameSource.TypeBodyIndex) != 0;
            if (isBodyIndexRequested && !fsd._isBodyIndexFrameReady && (lastBiFs == null || lastBiFs._isBodyIndexFrameReady))
            {
                if(lastBiFs != null)
                {
                    if (fsd._bodyIndexFrame == null || fsd._bodyIndexFrame.Length != lastBiFs._bodyIndexFrame.Length)
                    {
                        fsd._bodyIndexFrame = new byte[lastBiFs._bodyIndexFrame.Length];
                    }

                    KinectInterop.CopyBytes(lastBiFs._bodyIndexFrame, sizeof(byte), fsd._bodyIndexFrame, sizeof(byte));
                }

                fsd._bodyIndexTimestamp = fsd._rawFrameTimestamp;
                fsd._isBodyIndexFrameReady = true;
                //Debug.Log($"    D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, RawCpBodyIndexTime: {fsd._bodyIndexTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));

                if (_isScalingEnabled && !fsd._isScaledBodyIndexFrameReady &&
                    fsd._scaledBodyIndexFrameTimestamp != fsd._bodyIndexTimestamp)
                {
                    // set data for body scaling
                    int biImageBufLen = (_depthImageWidth * _depthImageHeight) >> 2;
                    if (pld._biImageBuffer == null || pld._biImageBuffer.count != biImageBufLen)
                    {
                        pld._biImageBuffer?.Dispose();
                        pld._biImageBuffer = new ComputeBuffer(biImageBufLen, sizeof(uint));
                    }

                    KinectInterop.SetComputeBufferData(pld._biImageBuffer, fsd._bodyIndexFrame, fsd._bodyIndexFrame.Length, sizeof(byte));
                    //Debug.Log($"    D{_unicamInt.deviceIndex} fs: {fsd._fsIndex} SetCpBodyIndexData for scaling, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                }
            }

            // set provider state to ready, if all data is copied
            if (fsd._isBodyDataFrameReady &&
                (!isBodyIndexRequested || fsd._isBodyIndexFrameReady))
            {
                fsData._bodyProviderState = (int)ProviderState.Ready;
                //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, CopyBodyDet - Provider is ready, Now: " + DateTime.Now.ToString("HH:mm:ss.fff") + $"\nfsData: {fsData._fsIndex}, fsTime: {fsData._rawFrameTimestamp}");
            }
        }

        // sets provider state to copy from last inf-frame
        private void SetCopyLastInfFrame(FramesetData fsData)
        {
            fsData._refBtTs = _lastInfTime;
            fsData._bodyProviderState = (int)ProviderState.CopyFrame;
            //Debug.Log($"fs: {fsData._fsIndex}, BodyDet ts: {fsData._rawFrameTimestamp} will copy frame from ts: {_lastInfTime}\ndTs: {fsData._rawFrameTimestamp - _lastInfTime}" + (_lastInfBtFs.ContainsKey(_lastInfTime) ? $";    last fs: {_lastInfBtFs[_lastInfTime]._fsIndex}, ts: {_lastInfBtFs[_lastInfTime]._rawFrameTimestamp}" : ""));
        }

        // sets this inf-frame as last inf-frame
        private void SetThisAsLastInfFrame(FramesetData fsData)
        {
            //Debug.Log($"fs: {fsData._fsIndex}, BodyDet ts: {fsData._rawFrameTimestamp} will do inference. lastTs: {_lastInfTime}\ndTs: {fsData._rawFrameTimestamp - _lastInfTime}");

            if (_lastInfTs.Count >= KinectInterop.MAX_LASTINF_QUEUE_LENGTH)
            {
                ulong firstTs = _lastInfTs.Dequeue();

                if (_lastInfBtFs.ContainsKey(firstTs))
                    _lastInfBtFs.Remove(firstTs);
                if (_lastInfBiFs.ContainsKey(firstTs))
                    _lastInfBiFs.Remove(firstTs);
            }

            _lastInfTime = fsData._rawFrameTimestamp;
            _lastInfTs.Enqueue(_lastInfTime);
            fsData._refBtTs = _lastInfTime;

            _lastInfBtFs[_lastInfTime] = fsData;
            _lastInfBiFs[_lastInfTime] = fsData;
        }


        // sets a single central region
        private void SetCentralRegion(FramesetData fsData, PoseLmProviderData plmData)
        {
            float sizeX = 1f / _lboxScale.x;
            float sizeY = 1f / _lboxScale.y;

            //if (plmData._poseRegions[0].size.x != sizeX || plmData._poseRegions[0].size.y != sizeY)
            {
                PoseRegion poseRegion = plmData._poseRegions[0];

                float minSize = Mathf.Min(sizeX, sizeY);
                poseRegion.size = new Vector4(sizeX, sizeY, minSize, minSize);
                poseRegion.box.z = poseRegion.size.w;

                Vector3 cropT = new Vector3(poseRegion.box.x - poseRegion.box.z * 0.5f, poseRegion.box.y - poseRegion.box.z * 0.5f, 0f);
                Vector3 cropS = new Vector3(poseRegion.box.z, poseRegion.box.z, 1f);
                poseRegion.cropMatrix.SetTRS(cropT, Quaternion.identity, cropS);

                Vector3 invT = -cropT / poseRegion.box.z;
                Vector3 invS = new Vector3(1f / poseRegion.box.z, 1f / poseRegion.box.z, 1f);
                poseRegion.invMatrix.SetTRS(invT, Quaternion.identity, invS);

                plmData._poseRegions[0] = poseRegion;
                plmData._poseRegionCount = 1;
                plmData._isPoseRegionBufferReady = true;

                if (KinectInterop.IsSupportsComputeShaders())
                {
                    //_poseRegionBuffer.SetData(plmData._poseRegions);
                    plmData._poseRegionBuffer.SetData(plmData._poseRegions);
                }

                //Debug.Log($"fs: {fsData._fsIndex}, SetData(_poseRegionBuffer), frame: {kinect.UniCamInterface._frameIndex}");
                //Debug.Log($"fs: {fsData._fsIndex}, UpdPoseReg{0}: {poseRegion}\nsize: {poseRegion.size:F2}, par: {poseRegion.par:F3}\n" +  // );  //
                //    $"CropMat T: {poseRegion.cropMatrix.GetColumn(3):F2}, R: {poseRegion.cropMatrix.rotation.eulerAngles:F0}, S: {poseRegion.cropMatrix.lossyScale:F2}\n" +
                //    $"InvMat T:{poseRegion.invMatrix.GetColumn(3):F2}, R: {poseRegion.invMatrix.rotation.eulerAngles:F0}, S: {poseRegion.invMatrix.lossyScale:F2}");
            }
        }

        // initializes body detection from the source image
        private bool InitBodyDetection(FramesetData fsData, PoseLmProviderData plmData)
        {
            // check depth image size
            Vector2Int depthImageSize = _depthProvider.GetDepthImageSize();

            if (_depthImageWidth != depthImageSize.x || _depthImageHeight != depthImageSize.y)
            {
                _depthImageWidth = depthImageSize.x;
                _depthImageHeight = depthImageSize.y;
            }

            if((_unicamInt.frameSourceFlags & KinectInterop.FrameSource.TypeBodyIndex) != 0)
            {
                int bodyIndexFrameSize = depthImageSize.x * depthImageSize.y;

                if (fsData._bodyIndexFrame == null || fsData._bodyIndexFrame.Length != bodyIndexFrameSize)
                {
                    fsData._bodyIndexFrame = new byte[bodyIndexFrameSize];
                }
            }

            // get source image time
            ulong imageTime = // _irProvider != null ? fsData._irFrameTimestamp : 
                _colorProvider != null ? fsData._colorImageTimestamp : 0L;
            //if (imageTime != 0 && fsData._btSourceImageTimestamp != imageTime)
            {
                // get source image
                Texture srcImage = //_irProvider != null ? _irProvider.GetBTSourceImage() : 
                    _colorProvider != null ? _colorProvider.GetBTSourceImage(fsData) : null;
                if (srcImage == null || imageTime == 0 || imageTime != fsData._rawFrameTimestamp)
                    return false;

                // letterboxing scale factor
                _lboxScale = new Vector2(Mathf.Max((float)srcImage.height / srcImage.width, 1f), Mathf.Max((float)srcImage.width / srcImage.height, 1f));
                _lboxPad = new Vector2((1f - 1f / _lboxScale.x) * 0.5f, (1f - 1f / _lboxScale.y) * 0.5f);
                //Debug.Log($"LboxScale: {_lboxScale:F3}, pad: {_lboxPad:F3}, srcW: {srcImage.width}, srcH: {srcImage.height}");

                if (fsData._btSourceImage == null || fsData._btSourceImage.width != srcImage.width || fsData._btSourceImage.height != srcImage.height)
                {
                    fsData._btSourceImage = KinectInterop.CreateRenderTexture((RenderTexture)fsData._btSourceImage, srcImage.width, srcImage.height, RenderTextureFormat.ARGB32);
                }

                //Graphics.Blit(srcImage, (RenderTexture)fsData._btSourceImage);

                CommandBuffer cmd = new CommandBuffer { name = "StoreSrcImageCb" };
                cmd.Blit(srcImage, (RenderTexture)fsData._btSourceImage);

                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();
                //Debug.Log($"fs: {fsData._fsIndex}, Blit(_btSourceImage), frame: {kinect.UniCamInterface._frameIndex}");

                fsData._btSourceImageTimestamp = imageTime;
            }

            if (!_isDetectMultipleUsers)
            {
                // single user
                SetCentralRegion(fsData, plmData);
            }

            //if (_debugImage != null && _debugImage.texture == null)
            //{
            //    _debugImage.texture = fsData._btSourceImage;

            //    float debugH = _debugImage.rectTransform.sizeDelta.y;
            //    _debugImage.rectTransform.sizeDelta = new Vector2(debugH * fsData._btSourceImage.width / fsData._btSourceImage.height, debugH);
            //}

            // get body index data, if needed
            if (fsData._bodyIndexFrame != null)
            {
                ClearBodyIndexBuffer(fsData, plmData);
            }

            return true;
        }

        // detects bodies in the source image
        private bool DetectBodiesInImage(FramesetData fsData, PoseLmProviderData plmData)
        {
            // init body detection
            if (!InitBodyDetection(fsData, plmData)) 
                return false;

            // set last inference frame
            SetThisAsLastInfFrame(fsData);

            // make it letterbox image
            //_detectionMat.SetInt("_isLinearColorSpace", QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);
            //_detectionMat.SetInt("_letterboxWidth", _inputWidthDetect);
            //_detectionMat.SetInt("_letterboxHeight", _inputHeightDetect);
            //_detectionMat.SetVector("_lboxScale", _lboxScale);
            //Graphics.Blit(fsData._btSourceImage, _detectionTex, _detectionMat);

            CommandBuffer cmd = new CommandBuffer { name = "ImageToLetterboxCb" };
            cmd.SetComputeIntParam(_texToTensorShader, "_isLinearColorSpace", QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);
            cmd.SetComputeIntParam(_texToTensorShader, "_letterboxWidth", _inputWidthDetect);
            cmd.SetComputeIntParam(_texToTensorShader, "_letterboxHeight", _inputHeightDetect);
            cmd.SetComputeVectorParam(_texToTensorShader, "_lboxScale", _lboxScale);
            cmd.SetComputeTextureParam(_texToTensorShader, 3, "_sourceTex", fsData._btSourceImage);
            cmd.SetComputeTextureParam(_texToTensorShader, 3, "_lboxTex", _detectionTex);
            cmd.DispatchCompute(_texToTensorShader, 3, _detectionTex.width / 8, _detectionTex.height / 8, 1);

            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();

            if (_debugImage != null)  // && _debugImage.texture == null)
            {
                _debugImage.texture = _detectionTex;

                float debugH = _debugImage.rectTransform.sizeDelta.y;
                _debugImage.rectTransform.sizeDelta = new Vector2(debugH * _detectionTex.width / _detectionTex.height, debugH);
            }

            if (debugText == null && _debugImage != null)
                debugText = _debugImage.GetComponentInChildren<UnityEngine.UI.Text>();
            if (debugText != null)
                debugText.text = fsData._fsIndex.ToString();

            // start model inference
            int fsIndex = _unicamInt.isSyncBodyAndDepth ? fsData._fsIndex : -1;  // fsData._fsIndex;  // 
            bool detModelStarted = _sentisManager.StartInference(fsIndex, _dmodelName, _inputNameDetect, _detectionTex);
            //SentisManager.SaveTensorData("input.txt", _detTexTensor, _detectionTex.width);

            //if (detModelStarted)
            //    Debug.LogWarning($"fs: {fsData._fsIndex}, Model {_dmodelName} inference scheduled.");
            //Debug.Log($"fs: {fsData._fsIndex}, StartInferenceBdet:{detModelStarted}, frame: {kinect.UniCamInterface._frameIndex}");

            return detModelStarted;
        }

        // processes detected bodies in image
        private void ProcessDetectedBodies(FramesetData fsData, PoseLmProviderData plmData)
        {
            if (!_isDetectMultipleUsers)
            {
                // single user
                bool bInited = InitBodyDetection(fsData, plmData);
                uint pCount = (uint)(bInited ? 1 : 0);

                plmData._poseCounts[0] = pCount;
                plmData._poseCount = pCount;
                plmData._poseRegionCount = pCount;
                //plmData._isPoseRegionBufferReady = bInited;

                KinectInterop.CopyBytes(plmData._poseRegions, PoseRegion.SIZE, plmData._lmRegions, PoseRegion.SIZE);
                plmData._lmRegionCount = pCount;
                plmData._lmPoseCount = pCount;
                plmData._lmRegionTimestamp = fsData._btSourceImageTimestamp;
                //Debug.Log($"fs: {fsData._fsIndex}, Set regions from fs: {fsData._fsIndex}, srcTs: {plmData._lmRegionTimestamp}\nregCount: {plmData._lmRegionCount}, poseCount: {plmData._lmPoseCount}, regBufferReady: {plmData._isPoseRegionBufferReady}");

                if (bInited)
                {
                    // set last inference frame
                    SetThisAsLastInfFrame(fsData);

                    // continue w/ LandmarkBody next
                    fsData._bodyProviderState = (int)ProviderState.LandmarkBody;
                }

                return;
            }

            // skip inference this frame
            _sentisManager.SkipModelInfThisFrame();

            // multiple users
            if (KinectInterop.IsSupportsComputeShaders())
            {
                // compute shaders
                long[] detShape = _sentisManager.GetOutputShape(_dmodelName, "output_0");

                int fsIndex = _unicamInt.isSyncBodyAndDepth ? fsData._fsIndex : -1;
                ComputeBuffer detections = _sentisManager.OutputAsComputeBuffer(fsIndex, _dmodelName, "output_0");

                // get detections
                CommandBuffer cmd = new CommandBuffer { name = "ProcessBodyDetectionsCb" };
                cmd.SetBufferCounterValue(_detOutputBuffer, 0);
                cmd.SetComputeFloatParam(_processShader, "_scoreThreshold", detectScoreThreshold);
                cmd.SetComputeBufferParam(_processShader, 0, "_detInput", detections);
                cmd.SetComputeBufferParam(_processShader, 0, "_detOutput", _detOutputBuffer);
                cmd.DispatchCompute(_processShader, 0, (int)detShape[0] / 50, 1, 1);
                cmd.CopyCounterValue(_detOutputBuffer, _detCountBuffer, 0);

                //cmd.RequestAsyncReadback(_detCountBuffer, request =>
                //{
                //    if (plmData._poseCounts == null)
                //        return;

                //    request.GetData<uint>().CopyTo(plmData._poseCounts);
                //    //_nmsPoseCount = (int)plmData._poseCounts[0];
                //    Debug.Log($"fs: {fsData._fsIndex}, RawDetCount: {plmData._poseCounts[0]}");
                //});
                //cmd.RequestAsyncReadback(_detOutputBuffer, request =>
                //{
                //    if (plmData._poseCounts == null)
                //        return;

                //    PoseDetection[] poseDetections = new PoseDetection[MAX_DETECTIONS];
                //    request.GetData<PoseDetection>().CopyTo(poseDetections);

                //    for (int i = 0; i < plmData._poseCounts[0] && i < MAX_DETECTIONS; i++)
                //    {
                //        var poseDet = poseDetections[i];
                //        Debug.Log($"  fs: {fsData._fsIndex}, RawPoseDet{i}/{plmData._poseCounts[0]} {poseDet}");
                //    }
                //});

                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();

                // get nms results
                CommandBuffer cmd2 = new CommandBuffer { name = "DetectBodyBatchedNmsCb" };
                cmd2.SetBufferCounterValue(_nmsOutputBuffer, 0);
                cmd2.SetComputeFloatParam(_processShader, "_iouThreshold", iouThreshold);
                cmd2.SetComputeBufferParam(_processShader, 3, "_nmsInputBuf", _detOutputBuffer);
                cmd2.SetComputeBufferParam(_processShader, 3, "_nmsCountBuf", _detCountBuffer);
                cmd2.SetComputeBufferParam(_processShader, 3, "_nmsOutputBuf", _nmsOutputBuffer);
                cmd2.DispatchCompute(_processShader, 3, 1, 1, 1);
                cmd2.CopyCounterValue(_nmsOutputBuffer, _nmsCountBuffer, 0);

                cmd2.RequestAsyncReadback(_nmsCountBuffer, request =>
                {
                    if (plmData._poseCounts == null)
                        return;

                    request.GetData<uint>().CopyTo(plmData._poseCounts);
                    plmData._poseCount = plmData._poseCounts[0];
                    //Debug.Log($"fs: {fsData._fsIndex}, NmsDetCount: {plmData._poseCount}");
                });

                //cmd2.RequestAsyncReadback(_nmsOutputBuffer, request =>
                //{
                //    FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();  //?._fsPrev;
                //    plmData = GetProviderData(fsd);

                //    if (fsd == null || plmData == null)
                //        return;

                //    PoseDetection[] poseDetections = new PoseDetection[MAX_BODIES];
                //    request.GetData<PoseDetection>().CopyTo(poseDetections);

                //    //Debug.Log($"fs: {fsData._fsIndex} - BdetOut2Buffer finished, bCount: {plmData._poseCount}\nNow: " + DateTime.Now.ToString("HH:mm:ss.fff"));

                //    for (int i = 0; i < plmData._poseCount; i++)
                //    {
                //        var poseDet = poseDetections[i];
                //        Debug.Log($"fs: {fsd._fsIndex}, NmsPoseDet{i}/{plmData._poseCount} {poseDet}");
                //    }
                //});

                Graphics.ExecuteCommandBuffer(cmd2);
                cmd2.Release();

                //Debug.Log($"fs: {fsData._fsIndex} - BdetOut2Buffer started\nNow: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                //Debug.Log($"fs: {fsData._fsIndex}, ProcessDetections, DetectBatchedNMS, frame: {kinect.UniCamInterface._frameIndex}");
                //Debug.Log($"fs: {fsData._fsIndex}, AsyncGPUReadback(_nmsOutputBuffer)\nframe: {kinect.UniCamInterface._frameIndex}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));

                // detect pose regions
                CommandBuffer cmd3 = new CommandBuffer { name = "DetectPoseRegionCb" };
                cmd3.SetComputeVectorParam(_processShader, "_lboxScale", _lboxScale);
                cmd3.SetComputeBufferParam(_processShader, 4, "_poseDetections", _nmsOutputBuffer);
                cmd3.SetComputeBufferParam(_processShader, 4, "_poseDetCountBuf", _nmsCountBuffer);
                cmd3.SetComputeBufferParam(_processShader, 4, "_savedRegions", plmData._poseRegionBuffer);
                cmd3.DispatchCompute(_processShader, 4, 1, 1, 1);

                cmd3.RequestAsyncReadback(plmData._poseRegionBuffer, request =>
                {
                    FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();  //?._fsPrev;
                    plmData = GetProviderData(fsd);

                    if (fsd == null || plmData == null || plmData._poseRegions == null)
                        return;

                    var poseRegionData = request.GetData<PoseRegion>();
                    poseRegionData.CopyTo(plmData._poseRegions);
                    plmData._poseRegionCount = plmData._poseCount;

                    if (!USE_PREV_REGIONS)
                    {
                        poseRegionData.CopyTo(plmData._lmRegions);
                        //Debug.Log($"fs: {fsd._fsIndex} - Copied region data to lmRegions\nrCount: {plmData._poseRegionCount}, bCount: {plmData._poseCount}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                    }

                    //if (debugText != null)
                    //{
                    //    for (int i = 0; i < plmData._poseCount; i++)
                    //    {
                    //        debugText.text += $"\n{fsd._fsIndex}/{i} @ {(Vector2)plmData._poseRegions[i].box:F2}";
                    //    }
                    //}

                    if(plmData._poseCount == 0)
                    {
                        // in case of no bodies found
                        SetCentralRegion(fsd, plmData);
                    }

                    //Debug.Log($"fs: {fsd._fsIndex} - BdetReg2Buffer finished, rCount: {plmData._poseRegionCount}, bCount: {plmData._poseCount}\nNow: " + DateTime.Now.ToString("HH:mm:ss.fff"));

                    //for (int i = 0; i < plmData._poseRegionCount; i++)
                    //{
                    //    var poseRegion = plmData._poseRegions[i];
                    //    Debug.Log($"fs: {fsData._fsIndex}, PoseReg{i}/{plmData._poseCount} {poseRegion}\nsize: {poseRegion.size:F2}, par: {poseRegion.par:F2}, ts: {(float)(fsData._btSourceImageTimestamp * 0.0001f):F3}, time: {Time.time:F3}\n");  // +
                    //    //$"CropMat T: {poseRegion.cropMatrix.GetColumn(3):F2}, R: {poseRegion.cropMatrix.rotation.eulerAngles:F0}, S: {poseRegion.cropMatrix.lossyScale:F2}\n" +
                    //    //$"InvMat T:{poseRegion.invMatrix.GetColumn(3):F2}, R: {poseRegion.invMatrix.rotation.eulerAngles:F0}, S: {poseRegion.invMatrix.lossyScale:F2}");
                    //}

                    // check for detections
                    //if (plmData._poseCount > 0)
                    {
                        // go to landmark
                        //FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();  //?._fsPrev;

                        //if (fsd != null)
                        {
                            fsd._bodyProviderState = (int)ProviderState.LandmarkBody;
                        }
                    }
                });

                Graphics.ExecuteCommandBuffer(cmd3);
                cmd3.Release();

                plmData._isPoseRegionBufferReady = true;
                //Debug.Log($"fs: {fsData._fsIndex} - BdetReg2Buffer started\nNow: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                //Debug.Log($"fs: {fsData._fsIndex}, DetectUpdatePoseRegion, frame: {kinect.UniCamInterface._frameIndex}");
                //Debug.Log($"fs: {fsData._fsIndex}, AsyncGPUReadback(_poseRegionBuffer), frame: {kinect.UniCamInterface._frameIndex}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }
        }

        // detects body landmarks in source image
        private bool DetectBodyLandmarks(FramesetData fsData, PoseLmProviderData plmData, uint bi)
        {
            // crop the region from the letterbox image
            //_landmarkMat.SetInt("_poseIndex", (int)bi);
            //_landmarkMat.SetVector("_lboxScale", _lboxScale);
            //_landmarkMat.SetInt("_cropTexWidth", _inputWidthLandmark);
            //_landmarkMat.SetInt("_cropTexHeight", _inputHeightLandmark);
            //_landmarkMat.SetInt("_isLinearColorSpace", QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);

            //if (KinectInterop.IsSupportsComputeShaders())
            //{
            //    _landmarkMat.SetBuffer("_cropRegion", plmData._poseRegionBuffer);  // _poseRegionBuffer);
            //}

            //Graphics.Blit(fsData._btSourceImage, _landmarkTex, _landmarkMat);

            CommandBuffer cmd = new CommandBuffer { name = "_BodyImageToTexCb" };
            cmd.SetComputeIntParam(_texToTensorShader, "_poseIndex", (int)bi);
            cmd.SetComputeIntParam(_texToTensorShader, "_cropTexWidth", _inputWidthLandmark);
            cmd.SetComputeIntParam(_texToTensorShader, "_cropTexHeight", _inputHeightLandmark);
            cmd.SetComputeVectorParam(_texToTensorShader, "_lboxScale", _lboxScale);
            cmd.SetComputeIntParam(_texToTensorShader, "_isLinearColorSpace", QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);
            cmd.SetComputeTextureParam(_texToTensorShader, 4, "_sourceTex", fsData._btSourceImage);
            cmd.SetComputeBufferParam(_texToTensorShader, 4, "_cropRegion", plmData._poseRegionBuffer);  // _poseRegionBuffer);
            cmd.SetComputeTextureParam(_texToTensorShader, 4, "_cropTexture", _landmarkTex);
            cmd.DispatchCompute(_texToTensorShader, 4, _landmarkTex.width / 8, _landmarkTex.height / 8, 1);

            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();

            if (_debugImage2 != null && bi == 0)  // && _debugImage2.texture == null)
            {
                _debugImage2.texture = _landmarkTex;

                float debugH = _debugImage2.rectTransform.sizeDelta.y;
                _debugImage2.rectTransform.sizeDelta = new Vector2(debugH * _landmarkTex.width / _landmarkTex.height, debugH);
            }

            //KinectInterop.RenderTex2Tex2D(_landmarkTex, ref _landmarkTex2d);
            //File.WriteAllBytes($"{fsData._fsIndex}-{bi}-lm.jpg", _landmarkTex2d.EncodeToJPG());

            if (debugText2 == null && _debugImage2 != null)
                debugText2 = _debugImage2.GetComponentInChildren<UnityEngine.UI.Text>();
            if (debugText2 != null && bi == 0)
                debugText2.text = $"{fsData._fsIndex}/{bi}";  // @ {(Vector2)plmData._poseRegions[bi].box:F2}";

            // start model inference
            int fsIndex = _unicamInt.isSyncBodyAndDepth ? fsData._fsIndex : -1;  // fsData._fsIndex;  // 
            bool lmModelStarted = _sentisManager.StartInference(fsIndex, _lmodelName, _inputNameLandmark, _landmarkTex);

            //if (lmModelStarted)
            //    Debug.LogWarning($"fs: {fsData._fsIndex}, bi: {bi}, Body cropped at {plmData._lmRegions[bi].box}\nand model {_lmodelName} inference scheduled.");
            //Debug.Log($"fs: {fsData._fsIndex}, StartInferenceBlm:{lmModelStarted}, frame: {kinect.UniCamInterface._frameIndex}");

            return lmModelStarted;
        }

        // processes detected body landmarks
        private bool ProcessDetectedLandmarks(FramesetData fsData, PoseLmProviderData plmData, uint bi)
        {
            if (KinectInterop.IsSupportsComputeShaders())
            {
                // compute shaders
                ComputeBuffer depthMapBuf = (ComputeBuffer)_depthProvider.GetDepthBuffer(fsData, out bool isDepthPermanent);
                Vector2Int depthImageSize = _depthProvider.GetDepthImageSize();
                KinectInterop.CameraIntrinsics camIntr = _sensorData.depthCamIntr;

                if (depthMapBuf == null)
                {
                    return false;
                }

                // skip inference this frame
                _sentisManager.SkipModelInfThisFrame();

                // get pose landmarks
                int fsIndex = _unicamInt.isSyncBodyAndDepth ? fsData._fsIndex : -1;

                var poseFlagBuffer = _sentisManager.OutputAsComputeBuffer(fsIndex, _lmodelName, "Identity_1");
                var landmarkBuffer = _sentisManager.OutputAsComputeBuffer(fsIndex, _lmodelName, "Identity");
                var worldLandmarkBuffer = _sentisManager.OutputAsComputeBuffer(fsIndex, _lmodelName, "Identity_4");
                var lmSegmBuffer = _sentisManager.OutputAsComputeBuffer(fsIndex, _lmodelName, "Identity_2");

                //var heatmapBuffer = _sentisManager.TensorToComputeBuffer(_lmodelName, "Identity_3");
                //var heatmapShape = _sentisManager.PeekOutputShape(_lmodelName, "Identity_3");

                CommandBuffer cmd = new CommandBuffer { name = "ProcessLandmarksCb" };
                cmd.SetComputeBufferParam(_processShader, 5, "_poseFlag", poseFlagBuffer);
                cmd.SetComputeBufferParam(_processShader, 5, "_landmarkInput", landmarkBuffer);
                cmd.SetComputeBufferParam(_processShader, 5, "_landmarkWorldInput", worldLandmarkBuffer);

                cmd.SetComputeBufferParam(_processShader, 5, "_depthMapBuf", depthMapBuf);
                cmd.SetComputeBufferParam(_processShader, 5, "_poseRegions", plmData._poseRegionBuffer);  // _poseRegionBuffer);

                cmd.SetComputeIntParam(_processShader, "_landmarkCount", BODY_LM_COUNT);
                cmd.SetComputeIntParam(_processShader, "_poseIndex", (int)bi);
                cmd.SetComputeVectorParam(_processShader, "_lboxScale", _lboxScale);

                cmd.SetComputeVectorParam(_processShader, "_intrPpt", camIntr != null ? new Vector2(camIntr.ppx, camIntr.ppy) : Vector2.zero);
                cmd.SetComputeVectorParam(_processShader, "_intrFlen", camIntr != null ? new Vector2(camIntr.fx, camIntr.fy) : Vector2.zero);
                cmd.SetComputeIntParam(_processShader, "_depthMapWidth", depthImageSize.x);  // _sensorData.depthImageWidth);
                cmd.SetComputeIntParam(_processShader, "_depthMapHeight", depthImageSize.y);  // _sensorData.depthImageHeight);

                //cmd.SetComputeBufferParam(_processShader, 5, "_Heatmap", heatmapBuffer);
                //cmd.SetComputeIntParam(_processShader, "_HmHeight", heatmapShape[1]);
                //cmd.SetComputeIntParam(_processShader, "_HmWidth", heatmapShape[2]);
                //cmd.SetComputeIntParam(_processShader, "_KernelSize", 9);
                //cmd.SetComputeFloatParam(_processShader, "_MinConfidence", 0.5f);

                //cmd.SetComputeBufferParam(_processShader, 5, "_OutLandmark", _rawLandmarks2dBuf);
                //cmd.SetComputeBufferParam(_processShader, 5, "_OutLandmarkWorld", _rawLandmarks3dBuf);
                cmd.SetComputeBufferParam(_processShader, 5, "_landmarkOutput", _landmarks2dBuf);
                cmd.SetComputeBufferParam(_processShader, 5, "_landmarkWorldOutput", _landmarks3dBuf);
                cmd.SetComputeBufferParam(_processShader, 5, "_landmarkSensorOutput", _landmarksKinBuf);

                cmd.SetComputeIntParam(_processShader, "_segmInputWidth", SEGMENTATION_SIZE);
                cmd.SetComputeIntParam(_processShader, "_segmInputHeight", SEGMENTATION_SIZE);
                cmd.SetComputeBufferParam(_processShader, 5, "_segmInputBuffer", lmSegmBuffer);
                cmd.DispatchCompute(_processShader, 5, 1, 1, 1);

                // cache pose landmarks to arrays  
                cmd.RequestAsyncReadback(_landmarks2dBuf, request => {
                    FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();
                    plmData = GetProviderData(fsd);

                    if (fsd != null && plmData != null && plmData._poseLandmarks != null && plmData._poseLandmarks[bi] != null)
                    {
                        request.GetData<Vector4>().CopyTo(plmData._poseLandmarks[bi]);
                        //Debug.Log($"  fs: {fsData._fsIndex}, PoseLm2D-{bi} rect: {plmData._poseLandmarks[bi][BODY_LM_COUNT]:F2}, ts: {fsData._btSourceImageTimestamp}\n{string.Join("\n  ", plmData._poseLandmarks[bi].Select((item, index) => index.ToString() + "-" + item.ToString()))}");
                    }
                });
                cmd.RequestAsyncReadback(_landmarksKinBuf, request =>
                {
                    FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();
                    plmData = GetProviderData(fsd);

                    if (fsd != null && plmData != null && plmData._poseKinectLandmarks != null && plmData._poseKinectLandmarks[bi] != null)
                    {
                        request.GetData<Vector4>().CopyTo(plmData._poseKinectLandmarks[bi]);
                        //Debug.Log($"  fs: {fsData._fsIndex}, KinectLm3D-{bi} ts: {fsData._btSourceImageTimestamp}\n{string.Join("\n  ", plmData._poseKinectLandmarks[bi].Select((item, index) => index.ToString() + "-" + item.ToString()))}");
                    }
                });
                cmd.RequestAsyncReadback(_landmarks3dBuf, request => {
                    FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();
                    plmData = GetProviderData(fsd);

                    if (fsd != null && plmData != null && plmData._poseWorldLandmarks != null && plmData._poseWorldLandmarks[bi] != null)
                    {
                        request.GetData<Vector4>().CopyTo(plmData._poseWorldLandmarks[bi]);
                        //Debug.Log($"  fs: {fsData._fsIndex}, PoseLm3D-{bi} score: {plmData._poseWorldLandmarks[bi][BODY_LM_COUNT].x:F3}, ts: {fsData._btSourceImageTimestamp}\n{string.Join("\n  ", plmData._poseWorldLandmarks[bi].Select((item, index) => index.ToString() + "-" + item.ToString()))}");

                        //FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();
                        if ((bi + 1) >= plmData._poseCount)  // && fsd != null)
                        {
                            fsd._bodyTrackerTimestamp = fsData._btSourceImageTimestamp;
                            fsd._isBodyTrackerFrameReady = true;
                            _lastInfBtFs[fsData._refBtTs] = fsd;

                            _bodyProcessDispatcher.Enqueue(fsd);
                            //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, RawBodyTrackerTime: {fsd._bodyTrackerTimestamp}, bCount: {plmData._poseCount}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                        }
                    }
                });

                //cmd.RequestAsyncReadback(_poseRegionBuffer, request =>
                //{
                //    PoseRegion[] glPoseRegions = new PoseRegion[MAX_BODIES];
                //    request.GetData<PoseRegion>().CopyTo(glPoseRegions);

                //    var poseRegion = glPoseRegions[bi];
                //    Debug.Log($"fs: {fsData._fsIndex}, G-PoseReg{bi + 1}/{plmData._poseCount} {poseRegion}\nsize: {poseRegion.size:F2}, bi: {bi}, par: {poseRegion.par:F3}");
                //});

                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();

                if (!isDepthPermanent)
                {
                    depthMapBuf.Dispose();
                }

                //Debug.Log($"fs: {fsData._fsIndex} - BlmOut2Buffer started, bi: {bi}\nNow: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                //Debug.Log($"fs: {fsData._fsIndex}, bi: {bi}, LmProcessLandmarks, frame: {kinect.UniCamInterface._frameIndex}");
                //Debug.Log($"fs: {fsData._fsIndex}, bi: {bi}, AsyncGPUReadback(_landmarks2dBuf, _landmarksKinBuf, _landmarks3dBuf)\nbox: {plmData._poseRegions[bi].box}, frame: {kinect.UniCamInterface._frameIndex}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }

            // get body index data, if needed
            if (fsData._bodyIndexFrame != null)
            {
                ProcessLandmarkSegmentation(fsData, plmData, bi);
            }

            return true;
        }

        // processes body index data for the frame
        private void ProcessLandmarkSegmentation(FramesetData fsData, PoseLmProviderData plmData, uint bi)
        {
            int fsIndex = _unicamInt.isSyncBodyAndDepth ? fsData._fsIndex : -1;
            if (KinectInterop.IsSupportsComputeShaders())
            {
                // compute shaders and buffers
                int biImageBufLen = (_depthImageWidth * _depthImageHeight) >> 2;
                if (plmData._biImageBuffer == null || plmData._biImageBuffer.count != biImageBufLen)
                {
                    plmData._biImageBuffer?.Dispose();
                    plmData._biImageBuffer = new ComputeBuffer(biImageBufLen, sizeof(uint));
                }

                // segm tensor buffer
                long[] segmShape = _sentisManager.GetOutputShape(_lmodelName, "Identity_2");
                ComputeBuffer lmSegmBuffer = _sentisManager.OutputAsComputeBuffer(fsIndex, _lmodelName, "Identity_2");
                Vector3 depthScale = _depthProvider != null ? _depthProvider.GetDepthImageScale() : Vector3.one;

                var invScale = new Vector2(1f / _lboxScale.x, 1f / _lboxScale.y);
                //Debug.Log($"  fs: {fsData._fsIndex}, bi: {bi}; InvScale: {invScale:F3}");

                // copy segm-tex to body-index buffer
                CommandBuffer cmd = new CommandBuffer { name = "SegmToBodyIndexCb" };
                cmd.SetComputeBufferParam(_processShader, 8, "_SegmBuf", lmSegmBuffer);
                cmd.SetComputeBufferParam(_processShader, 8, "_BodyIndexBuf", plmData._biImageBuffer);
                cmd.SetComputeBufferParam(_processShader, 8, "_poseRegions", plmData._poseRegionBuffer);  // _poseRegionBuffer);
                cmd.SetComputeBufferParam(_processShader, 8, "_LandmarkWorldBuf", _landmarks3dBuf);
                cmd.SetComputeIntParam(_processShader, "_poseIndex", (int)bi);
                cmd.SetComputeVectorParam(_processShader, "_InvScale", invScale);
                cmd.SetComputeFloatParam(_processShader, "_DepthScaleX", depthScale.x);
                cmd.SetComputeIntParam(_processShader, "_SegmTexWidth", (int)segmShape[2]);
                cmd.SetComputeIntParam(_processShader, "_SegmTexHeight", (int)segmShape[1]);
                cmd.SetComputeIntParam(_processShader, "_BodyIndexImgWidth", _depthImageWidth);
                cmd.SetComputeIntParam(_processShader, "_BodyIndexImgHeight", _depthImageHeight);
                cmd.SetComputeIntParam(_processShader, "_lmCount", BODY_LM_COUNT);
                //cmd.SetComputeFloatParam(_processShader, "_lmScore", plmData._lmPoseConf[(int)bi]);
                cmd.SetComputeFloatParam(_processShader, "_lmThreshold", landmarkScoreThreshold);
                cmd.DispatchCompute(_processShader, 8, _depthImageWidth / 8, _depthImageHeight / 8, 1);

                if ((bi + 1) >= plmData._poseCount)
                {
                    //Debug.Log($"fs: {fsData._fsIndex} - BdetIndex2Buffer started\nNow: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                    //Debug.Log($"fs: {fsData._fsIndex}, bi: {bi}, AsyncGPUReadback(_biImageBuffer), frame: {kinect.UniCamInterface._frameIndex}");

                    // read-back the body index buffer
                    cmd.RequestAsyncReadback(plmData._biImageBuffer, request => {
                        FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();

                        if (fsd != null && fsd._bodyIndexFrame != null)
                        {
                            if(request.width == fsd._bodyIndexFrame.Length)
                                request.GetData<byte>().CopyTo(fsd._bodyIndexFrame);

                            fsd._bodyIndexTimestamp = fsData._btSourceImageTimestamp;
                            fsd._isBodyIndexFrameReady = true;
                            _lastInfBiFs[fsData._refBtTs] = fsd;
                            //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, RawBodyIndexTime: {fsd._bodyIndexTimestamp}\nNow: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                            //Debug.Log($" sbiEnabled: {_isScalingEnabled}, sbiReady: {fsd._isScaledBodyIndexFrameReady}, sbiTime: {fsd._scaledBodyIndexFrameTimestamp}");

                            // scale body-index image, if needed
                            if (_isScalingEnabled && !fsd._isScaledBodyIndexFrameReady &&
                                fsd._scaledBodyIndexFrameTimestamp != fsd._bodyIndexTimestamp)
                            {
                                fsd._scaledBodyIndexFrameTimestamp = fsd._bodyIndexTimestamp;
                                ScaleBodyIndexFrame(fsd);
                            }
                        }
                    });
                }
 
                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();

                //Debug.Log($"fs: {fsData._fsIndex} Segm-to-body-index bi: {bi}, imgWidth: {_depthImageWidth}, imgHeight: {_depthImageHeight}, _biBufLen: {plmData._biImageBuffer.count} bCount: {plmData._poseCount}\nsegmShape: {segmTensor.shape}, invScale: {invScale:F3}. dScale: {depthScale:F0}");
                //Debug.Log($"fs: {fsData._fsIndex}, bi: {bi}, SegmTexToBodyIndexBuffer, frame: {kinect.UniCamInterface._frameIndex}");

                if (_segmMaskMaterial != null && _segmMaskTex != null)
                {
                    ComputeBuffer landmarkBuffer = _sentisManager.OutputAsComputeBuffer(fsIndex, _lmodelName, "Identity");

                    // segmentation mask
                    _segmMaskMaterial.SetInt("_SegmTexWidth", SEGMENTATION_SIZE);
                    _segmMaskMaterial.SetInt("_SegmTexHeight", SEGMENTATION_SIZE);
                    _segmMaskMaterial.SetBuffer("_SegmBuf", lmSegmBuffer);

                    _segmMaskMaterial.SetInt("_LmCount", BODY_LM_COUNT);
                    _segmMaskMaterial.SetBuffer("_LmBuffer", landmarkBuffer);  // _lmLandmarkBuffers[bi]

                    Graphics.Blit(null, _segmMaskTex, _segmMaskMaterial);
                    //Debug.Log($"  fs: {fsData._fsIndex}, bi: {bi}, Created SegmMaskTex, box: {plmData._poseRegions[bi].box}");

                    if (_debugImage3 != null)
                    {
                        _debugImage3.texture = _segmMaskTex;
                    }

                    //KinectInterop.RenderTex2Tex2D(_segmMaskTex, ref _landmarkSeg2d);
                    //File.WriteAllBytes($"{fsData._fsIndex}-{bi}-seg.jpg", _landmarkSeg2d.EncodeToJPG());
                }

                if (debugText3 == null && _debugImage3 != null)
                    debugText3 = _debugImage3.GetComponentInChildren<UnityEngine.UI.Text>();
                if (debugText3 != null)
                    debugText3.text = $"{fsData._fsIndex}/{bi} @ {(Vector2)plmData._lmRegions[bi].box:F2}";
           }

        }

        // clears the body index buffer
        private void ClearBodyIndexBuffer(FramesetData fsData, PoseLmProviderData plmData)
        {
            if (KinectInterop.IsSupportsComputeShaders())
            {
                // compute shaders and buffers
                int biImageBufLen = (_depthImageWidth * _depthImageHeight) >> 2;
                if (plmData._biImageBuffer == null || plmData._biImageBuffer.count != biImageBufLen)
                {
                    plmData._biImageBuffer?.Dispose();
                    plmData._biImageBuffer = new ComputeBuffer(biImageBufLen, sizeof(uint));
                }

                // clear body-index buffer
                CommandBuffer cmd = new CommandBuffer { name = "ClearBodyIndexCb" };
                cmd.SetComputeBufferParam(_processShader, 7, "_biBuffer", plmData._biImageBuffer);
                cmd.DispatchCompute(_processShader, 7, biImageBufLen / 64, 1, 1);

                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();

                //Debug.Log($"fs: {fsData._fsIndex} Cleared body-index buffer");
                //Debug.Log($"fs: {fsData._fsIndex}, ClearBodyIndexBuffer, frame: {kinect.UniCamInterface._frameIndex}");
            }

        }


        // joint for fl-estimation (shoulders & hips)
        private static readonly int[] FL_JOINT_INDEX = { 12, 23, 24, 11, 24, 27, 28, 23, 8, 13, 14, 7 };  // ls, rh, lh, rs; lh, ra, la, rh, lear, relb, lelb, rear
        private const float MIN_SHOULDER_LEN = 0.15f;

        //private string _savedTracks = string.Empty;

        // processes the current body frame
        private void ProcessBodyFrame(KinectInterop.SensorData sensorData, FramesetData fsData)
        {
            // get sensor-to-world matrix
            Matrix4x4 sensorToWorld = _unicamInt.GetSensorToWorldMatrix();
            //Quaternion sensorRotInv = _unicamInt.GetSensorRotationNotZFlipped(true);
            float scaleX = sensorData.sensorSpaceScale.x;
            float scaleY = sensorData.sensorSpaceScale.y;

            bool isIgnoreZ = _unicamInt.bIgnoreZCoordinates;
            //Vector3 depthScale = _depthProvider != null ? _depthProvider.GetDepthImageScale() : Vector3.one;
            //float depthConfidence = _depthProvider != null ? _depthProvider.GetDepthConfidence() : 0f;

            PoseLmProviderData plmData = (PoseLmProviderData)fsData._bodyProviderSettings;  // GetProviderData(fsData);
            if (plmData != null && sensorData.depthCamIntr != null)
            {
                //Debug.Log($"fs: {fsData._fsIndex}, ProcessBodyFrame - rCount: {plmData._lmRegionCount}, bCount: {plmData._lmPoseCount}\nbtReady: {fsData._isBodyTrackerFrameReady}, btTime: {fsData._bodyTrackerTimestamp}");

                // update byte tracker
                trackers.STrack[] detBodies = new trackers.STrack[plmData._lmPoseCount];
                for (int i = 0; i < plmData._lmPoseCount; i++)
                {
                    Vector4 poseBox = plmData._poseLandmarks[i][BODY_LM_COUNT];  // plmData._poseRegions[i].box;
                    //Vector4 poseCenter = plmData._poseLandmarks[i][33];

                    trackers.RectBox objRect = new trackers.RectBox(poseBox.x, poseBox.y, poseBox.z, poseBox.w);
                    float score = _userTracker.GetLmScoreReliability(plmData._poseWorldLandmarks[i][BODY_LM_COUNT].x, plmData._poseLandmarks[i], plmData._lmRegions[i]);
                    detBodies[i] = new trackers.STrack(objRect, score);
                }

                // update byte tracker
                var idToTrack = _byteTracker.Update(fsData, detBodies, fsData._bodyTrackerTimestamp);

                // update body data of all users
                for (int i = 0; i < plmData._lmPoseCount; i++)
                {
                    //Vector4[] poseLandmarks = plmData._poseLandmarks[i];
                    //Vector4[] poseWorldLandmarks = plmData._poseWorldLandmarks[i];
                    //Vector4[] poseKinectLandmarks = plmData._poseKinectLandmarks[i];

                    //// get hip center
                    //Vector2 vHipImagePos = poseLandmarks[33];  // (poseLandmarks[24] + poseLandmarks[23]) * 0.5f;
                    //float bodyDepth = 0f;

                    ////if (depthConfidence > 0f)
                    //{
                    //    Vector3 vHipSpacePos = GetSpacePosForNormDepthPixel(sensorData, fsData, vHipImagePos, depthScale, getDepthOnly: true);
                    //    bodyDepth = vHipSpacePos.z;
                    //}

                    //// estimate body depth
                    //float estDepth = depthConfidence < 1f ? TryEstimatePoseDepth(i, sensorData, fsData) : 0f;
                    //float bodyDepth = estDepth > 0f ? depthConfidence * provDepth + (1f - depthConfidence) * estDepth : provDepth;
                    ////Debug.Log($"fs: {fsData._fsIndex}, Pose {i} - bodyDepth: {bodyDepth:F3}\nprovDepth: {provDepth:F3}, estDepth: {estDepth:F3}, conf: {(depthConfidence * 100):F0}%");

                    //if (!float.IsNaN(vHipImagePos.x) && !float.IsNaN(vHipImagePos.y) && !float.IsNaN(bodyDepth))
                    {
                        // update user body data
                        _userTracker.UpdateUserBodyData(i, /**bodyDepth*/ plmData._poseWorldLandmarks[i][33].z, (long)fsData._bodyTrackerTimestamp, sensorData.colorCamIntr,
                            plmData._lmRegions[i], plmData._poseLandmarks[i], plmData._poseKinectLandmarks[i], plmData._poseWorldLandmarks[i], detBodies[i], idToTrack);
                    }
                }
            }

            // get tracked bodies
            List<trackers.STrack> trackedBodies = _byteTracker.AllTracks;  // _byteTracker.TrackedTracks;  // 
            fsData._trackedBodiesCount = (uint)trackedBodies.Count;

            //string currentTracks = string.Join(", ", trackedBodies.Select((item) => item.TrackId));
            //string lostTracks = string.Join(", ", _byteTracker.LostTracks.Select((item) => item.TrackId));
            //if (_savedTracks != currentTracks)
            //{
            //    Debug.Log($"tracks: {currentTracks}, prev: {_savedTracks}\nlost: {lostTracks}");
            //    _savedTracks = currentTracks;
            //}

            // create the needed slots
            while (fsData._alTrackedBodies.Count < fsData._trackedBodiesCount)
            {
                fsData._alTrackedBodies.Add(new KinectInterop.BodyData((int)KinectInterop.JointType.Count));
            }

            for (int i = 0; i < fsData._trackedBodiesCount; i++)
            {
                PoseLmUserTracker.UserTrackingBodyData userBody = (PoseLmUserTracker.UserTrackingBodyData)trackedBodies[i]._dataObj;  // userBodies[i];
                KinectInterop.BodyData bodyData = fsData._alTrackedBodies[i];

                if(userBody != null)
                {
                    bodyData.liTrackingID = userBody.userId;
                    bodyData.iBodyIndex = userBody.bodyIndex;

                    bodyData.bIsTracked = trackedBodies[i].IsActivated;  // && trackedBodies[i].State == trackers.TrackState.Tracked;
                    bodyData.bodyTimestamp = fsData._bodyTrackerTimestamp;
                    //Debug.Log($"Body {i} - id: {userBody.userId}, bi: {userBody.bodyIndex}, tracked: {bodyData.bIsTracked}");
                }
                else
                {
                    // consider as not tracked
                    bodyData.bIsTracked = false;
                }

                if (bodyData.bIsTracked)
                {
                    //// estimate body position
                    //Vector2 bodyImagePos = userBody.joints[33].imagePos;
                    //float dx = (depthScale.x >= 0f ? bodyImagePos.x : (1f - bodyImagePos.x)) * _depthImageWidth;
                    //float dy = (depthScale.y >= 0f ? bodyImagePos.y : (1f - bodyImagePos.y)) * _depthImageHeight;

                    ////Vector3 bodySpacePos = _depthProvider.UnprojectPoint(sensorData.depthCamIntr, new Vector2(dx, dy), userBody.joints[33].spacePos.z);
                    //Vector3 bodySpacePos = _depthProvider.UnprojectPoint(sensorData.depthCamIntr, new Vector2(dx, dy), userBody.bodyDepth);

                    Vector3 bodySpacePos = userBody.joints[33].kinectPos * userBody.bodyDepth;
                    bodySpacePos.x *= scaleX;
                    bodySpacePos.y *= scaleY;
                    //Debug.Log($"  fs: {fsData._fsIndex}, bi: {userBody.bodyIndex}, bodySpacePos: {bodySpacePos:F2}\nkPos: {userBody.joints[33].kinectPos}, depth: {userBody.bodyDepth:F3}, spaceScale: {sensorData.sensorSpaceScale}");

                    for (int jBT = 0; jBT < BODY_LM_COUNT; jBT++)
                    {
                        int j = PoseJoint2JointType[jBT];

                        if (j >= 0)
                        {
                            PoseLmUserTracker.UserTrackingJointData userJoint = userBody.joints[jBT];
                            KinectInterop.JointData jointData = bodyData.joint[j];

                            jointData.trackingState = userJoint.jointScore >= jointTrackedThreshold ? KinectInterop.TrackingState.Tracked :
                                userJoint.jointScore >= jointInferredThreshold ? KinectInterop.TrackingState.Inferred : KinectInterop.TrackingState.NotTracked;
                            //jointData.imagePos = userJoint.imagePos;

                            //// unproject image position (keep only z)
                            //dx = (depthScale.x >= 0f ? userJoint.imagePos.x : (1f - userJoint.imagePos.x)) * _depthImageWidth;
                            //dy = (depthScale.y >= 0f ? userJoint.imagePos.y : (1f - userJoint.imagePos.y)) * _depthImageHeight;
                            //Vector3 jointSpacePos = _depthProvider.UnprojectPoint(sensorData.depthCamIntr, new Vector2(dx, dy), bodySpacePos.z + userJoint.spacePos.z);

                            jointData.kinectPos = userJoint.kinectPos * (j != 0 ? userJoint.smoothPos.z + bodySpacePos.z : bodySpacePos.z);
                            Vector3 jointSmoothPos = j != 0 ? userJoint.smoothPos + bodySpacePos : bodySpacePos;

                            if (isIgnoreZ)
                                jointSmoothPos.z = bodySpacePos.z;  // userBody.joints[33].smoothPos.z;

                            jointData.position = sensorToWorld.MultiplyPoint3x4(jointSmoothPos);
                            jointData.orientation = Quaternion.identity;
                            //jointData.orientation = sensorRotInv * jointData.orientation;

                            //if (j == 0)
                            //{
                            //    bodyData.kinectPos = jointData.kinectPos;
                            //    bodyData.position = jointData.position;
                            //    bodyData.orientation = jointData.orientation;
                            //}

                            bodyData.joint[j] = jointData;
                        }
                    }

                    // estimate additional joints
                    CalcBodySpecialJoints(ref bodyData);

                    // set body position & orientation
                    bodyData.kinectPos = bodyData.joint[0].kinectPos;
                    bodyData.position = bodyData.joint[0].position;
                    bodyData.orientation = bodyData.joint[0].orientation;
                }

                fsData._alTrackedBodies[i] = bodyData;
            }

            //// get tracked bodies
            //UpdateTrackedBodies(alTrackedBodies, ref trackedBodiesCount,
            //    sensorToWorld, sensorRotInv, scaleX, scaleY, bIgnoreZCoordinates, this);
            //rawBodyTimestamp = (depthFrameTime != 0 ? depthFrameTime : _bodyStreamProvider.GetBodyDataTimestamp()) + trackedBodiesCount;
            ////Debug.Log("D" + deviceIndex + " RawBodyTimestamp: " + rawBodyTimestamp + ", BodyCount: " + trackedBodiesCount);

            // estimate additional data
            for (int i = 0; i < fsData._trackedBodiesCount; i++)
            {
                KinectInterop.BodyData bodyData = fsData._alTrackedBodies[i];
                if (!bodyData.bIsTracked)
                    continue;

                // filter joint positions
                if (_unicamInt.jointPositionFilter != null)
                {
                    _unicamInt.jointPositionFilter.UpdateFilter(ref bodyData);
                }

                // calculate bone dirs
                KinectInterop.CalcBodyJointDirs(ref bodyData);

                // calculate joint orientations
                _unicamInt.CalcBodyJointOrients(ref bodyData);

                // body orientation
                bodyData.normalRotation = bodyData.joint[0].normalRotation;
                bodyData.mirroredRotation = bodyData.joint[0].mirroredRotation;

                // estimate hand states
                CalcBodyHandStates(ref bodyData);

                fsData._alTrackedBodies[i] = bodyData;
                //Debug.Log("  (T)User ID: " + bodyData.liTrackingID + ", body: " + i + ", bi: " + bodyData.iBodyIndex + ", pos: " + bodyData.joint[0].kinectPos + ", rot: " + bodyData.joint[0].normalRotation.eulerAngles + ", Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }

            // clean up joint-position history
            if (_unicamInt.jointPositionFilter != null)
            {
                _unicamInt.jointPositionFilter.CleanUpUserHistory();
            }

            // save body data on the main thread
            fsData._bodyDataTimestamp = fsData._bodyTrackerTimestamp + fsData._trackedBodiesCount;
            _bodySaveDispatcher.Enqueue(fsData);
            //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsData._fsIndex}, ProcessedBodyData time: {fsData._bodyDataTimestamp}, bCount: {fsData._trackedBodiesCount}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
        }

        // save processed body data to the frame (executed on the main thread)
        private void SaveFrameBodyData(FramesetData fsData)
        {
            FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();

            fsd._trackedBodiesCount = fsData._trackedBodiesCount;
            fsd._alTrackedBodies = fsData._alTrackedBodies;

            // body frame is ready
            fsd._bodyDataTimestamp = fsData._bodyTrackerTimestamp + fsData._trackedBodiesCount;
            fsd._isBodyDataFrameReady = true;
            _lastInfBtFs[fsData._refBtTs] = fsd;
            //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, RawBodyDataTime: {fsd._bodyDataTimestamp}, bCount: {fsd._trackedBodiesCount}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff") + $"\nfsData: {fsData._fsIndex}, fsTime: {fsData._bodyDataTimestamp}");
        }

        // estimates hand states for the given body
        private void CalcBodyHandStates(ref KinectInterop.BodyData bodyData)
        {
            ulong uTimeDelta = bodyData.bodyTimestamp - lastHandStatesTimestamp;
            float fTimeDelta = uTimeDelta * 0.0000001f;
            float fTimeSmooth = 5f * fTimeDelta;

            int h = (int)KinectInterop.JointType.WristLeft;
            int f = (int)KinectInterop.JointType.HandtipLeft;
            float lHandFingerAngle = 0f;

            if (bodyData.joint[h].trackingState != KinectInterop.TrackingState.NotTracked &&
                bodyData.joint[f].trackingState != KinectInterop.TrackingState.NotTracked)
            {
                //lHandFingerAngle = Quaternion.Angle(bodyData.joint[h].normalRotation, bodyData.joint[f].normalRotation);

                //lHandFingerAngle = (leftHandFingerAngle + lHandFingerAngle) / 2f;  // Mathf.Lerp(leftHandFingerAngle, lHandFingerAngle, fTimeSmooth);
                //bodyData.leftHandState = (lHandFingerAngle >= 40f) ? KinectInterop.HandState.Closed : KinectInterop.HandState.Open;  // 50f
                bodyData.leftHandState = KinectInterop.HandState.Open;
            }
            else
            {
                bodyData.leftHandState = KinectInterop.HandState.NotTracked;
            }

            h = (int)KinectInterop.JointType.WristRight;
            f = (int)KinectInterop.JointType.HandtipRight;
            float rHandFingerAngle = 0f;

            if (bodyData.joint[h].trackingState != KinectInterop.TrackingState.NotTracked &&
                bodyData.joint[f].trackingState != KinectInterop.TrackingState.NotTracked)
            {
                //rHandFingerAngle = Quaternion.Angle(bodyData.joint[h].normalRotation, bodyData.joint[f].normalRotation);

                //rHandFingerAngle = (rightHandFingerAngle + rHandFingerAngle) / 2f;  // Mathf.Lerp(rightHandFingerAngle, rHandFingerAngle, fTimeSmooth);
                //bodyData.rightHandState = (rHandFingerAngle >= 40f) ? KinectInterop.HandState.Closed : KinectInterop.HandState.Open;  // 50f
                bodyData.rightHandState = KinectInterop.HandState.Open;
            }
            else
            {
                bodyData.rightHandState = KinectInterop.HandState.NotTracked;
            }

            //Debug.Log($"LHangle: {lHandFingerAngle:F0}, RHangle: {rHandFingerAngle:F0}\nLHstate: {bodyData.leftHandState}, RHstate: {bodyData.rightHandState}, ts: {bodyData.bodyTimestamp}");

            if (fTimeSmooth >= 1f)
            {
                leftHandFingerAngle = lHandFingerAngle;
                rightHandFingerAngle = rHandFingerAngle;
                lastHandStatesTimestamp = bodyData.bodyTimestamp;
            }
        }

        // tries to estimate the pose depth from the current 2d & 3d landmarks
        private float TryEstimatePoseDepth(int poseIndex, KinectInterop.SensorData sensorData, FramesetData fsData)
        {
            PoseLmProviderData plmData = GetProviderData(fsData);
            if (fsData == null || plmData == null || fsData._bodyTrackerTimestamp == 0 || sensorData == null || sensorData.depthCamIntr == null)
                return 0f;

            int lrPairsCount = FL_JOINT_INDEX.Length >> 1;
            float estDepth = 0f;
            int estCount = 0;

            Vector4[] poseLandmarks = plmData._poseLandmarks[poseIndex];
            Vector4[] poseWorldLandmarks = plmData._poseWorldLandmarks[poseIndex];

            //Debug.Log($"  fs: {fsData._fsIndex}, LmScore: {poseWorldLandmarks[BODY_LM_COUNT].x:F3}");
            if (poseWorldLandmarks[BODY_LM_COUNT].x < landmarkScoreThreshold)
                return 0f;
            
            // don't consider poses with too short shoulders
            Vector3 shDir = poseWorldLandmarks[11] - poseWorldLandmarks[12];
            if (shDir.magnitude < MIN_SHOULDER_LEN)
                return 0f;

            for (int j = 0; j < lrPairsCount; j++)
            {
                int li = FL_JOINT_INDEX[j * 2];
                int ri = FL_JOINT_INDEX[j * 2 + 1];

                Vector4 lPos = poseWorldLandmarks[li];
                Vector4 rPos = poseWorldLandmarks[ri];

                //if (lPos.w < jointInferredThreshold || rPos.w < jointInferredThreshold)  // jointTrackedThreshold
                //    continue;

                Vector2 lUV = (Vector2)poseLandmarks[li];
                Vector2 rUV = (Vector2)poseLandmarks[ri];

                KinectInterop.CameraIntrinsics intr = sensorData.depthCamIntr;
                float dx = CalcDepthFromPosUv(lPos.x, rPos.x, lUV.x, rUV.x, lPos.z, rPos.z, intr.fx, intr.width);
                if (dx != 0f)
                {
                    estDepth += dx;
                    estCount++;
                }

                float dy = CalcDepthFromPosUv(lPos.y, rPos.y, lUV.y, rUV.y, lPos.z, rPos.z, intr.fy, intr.height);
                if (dy != 0f)
                {
                    estDepth += dy;
                    estCount++;
                }
            }

            if (estCount > 0)
            {
                estDepth /= estCount;
            }

            //Debug.Log($"  fs: {fsData._fsIndex}, Pose {poseIndex} est depth: {estDepth:F3}, count: {estCount}");
            return estDepth;
        }

        // calculates depth from 3d & 2d positions and focal len
        private float CalcDepthFromPosUv(float pos1, float pos2, float uv1, float uv2, float d1, float d2, float fLen, int imageRes)
        {
            if (Mathf.Abs(uv2 - uv1) > 0.02f && Mathf.Abs(pos2 - pos1) > 0.15f)
            {
                float d = ((uv1 - 0.5f) * d1 - (uv2 - 0.5f) * d2 - (pos1 - pos2) * fLen / imageRes) / (uv1 - uv2);
                return Mathf.Abs(d);
            }

            return 0f;
        }

        //// gets the space position for the specified normalized depth image coordinates.
        //private Vector3 GetSpacePosForNormDepthPixel(KinectInterop.SensorData sensorData, FramesetData fsData,
        //    Vector2 normDepthPos, Vector3 depthScale, bool getDepthOnly = false)
        //{
        //    if(_depthProvider == null)
        //        return Vector3.zero;

        //    int dx = Mathf.Clamp(Mathf.RoundToInt((depthScale.x >= 0f ? normDepthPos.x : (1f - normDepthPos.x)) * _depthImageWidth), 0, _depthImageWidth - 1);
        //    int dy = Mathf.Clamp(Mathf.RoundToInt((depthScale.y >= 0f ? normDepthPos.y : (1f - normDepthPos.y)) * _depthImageHeight), 0, _depthImageHeight - 1);

        //    int di = dx + dy * _depthImageWidth;
        //    ushort depth = fsData != null && fsData._depthFrame != null && di < fsData._depthFrame.Length ? fsData._depthFrame[di] : (ushort)0;

        //    Vector2 imagePos = new Vector2(dx, dy);
        //    Vector3 spacePos = !getDepthOnly ? _depthProvider.UnprojectPoint(sensorData.depthCamIntr, imagePos, depth * 0.001f) : Vector3.zero;

        //    if (spacePos == Vector3.zero)
        //        spacePos = new Vector3(0f, 0f, depth * 0.001f);
        //    //Debug.Log($"fs: {fsData._fsIndex}, Depth: {depth * 0.001f}\nhipsPos: {normDepthPos:F2} - {imagePos:F0} - {spacePos:F2}");

        //    // dump depth frame
        //    //DumpFrameToFile("depthframe.txt", depthFrame, new Vector2Int(sensorData.depthImageWidth, sensorData.depthImageHeight), new Vector2Int(dx, dy));

        //    return spacePos;
        //}

        //// dumps the depth frame to a file
        //private void DumpFrameToFile(string fileName, ushort[] depthFrame, Vector2Int frameSize, Vector2Int poi)
        //{
        //    if (depthFrame == null)  // System.IO.File.Exists(fileName)
        //        return;

        //    System.Text.StringBuilder sbBuf = new System.Text.StringBuilder();

        //    for (int y = 0, i = 0; y < frameSize.y; y++)
        //    {
        //        for (int x = 0; x < frameSize.x; x++, i++)
        //        {
        //            ushort d = (ushort)(depthFrame[i] / 1000);  //  / 1000
        //            //int d = (int)rawDepth[i] / 100;

        //            if (poi != null && poi.x == x && poi.y == y)
        //                sbBuf.Append('X');
        //            else if (d > 0 && d < 4)
        //                sbBuf.AppendFormat("{0:X}", d);
        //            else
        //                sbBuf.Append(' ');
        //        }

        //        sbBuf.AppendLine();
        //    }

        //    System.IO.File.WriteAllText(fileName, sbBuf.ToString());

        //    Debug.Log("Dumped depth image to file: " + fileName);
        //}


        // pose joints to joint-type conversion
        private static readonly int[] PoseJoint2JointType =
        {
            /** 00 */ (int)KinectInterop.JointType.Nose,
            /** 01 */ -1,  // left eye inner
            /** 02 */ (int)KinectInterop.JointType.EyeRight,
            /** 03 */ -1,  // left eye outer
            /** 04 */ -1,  // right eye inner
            /** 05 */ (int)KinectInterop.JointType.EyeLeft,
            /** 06 */ -1,  // right eye outer
            /** 07 */ (int)KinectInterop.JointType.EarRight,
            /** 08 */ (int)KinectInterop.JointType.EarLeft,
            /** 09 */ -1,  // mouth left
            /** 10 */ -1,  // mouth right
            /** 11 */ (int)KinectInterop.JointType.ShoulderRight,
            /** 12 */ (int)KinectInterop.JointType.ShoulderLeft,
            /** 13 */ (int)KinectInterop.JointType.ElbowRight,
            /** 14 */ (int)KinectInterop.JointType.ElbowLeft,
            /** 15 */ (int)KinectInterop.JointType.WristRight,
            /** 16 */ (int)KinectInterop.JointType.WristLeft,
            /** 17 */ -1,  // left pinky
            /** 18 */ -1,  // right pinky
            /** 19 */ (int)KinectInterop.JointType.HandtipRight,
            /** 20 */ (int)KinectInterop.JointType.HandtipLeft,
            /** 21 */ (int)KinectInterop.JointType.ThumbRight,
            /** 22 */ (int)KinectInterop.JointType.ThumbLeft,
            /** 23 */ (int)KinectInterop.JointType.HipRight,
            /** 24 */ (int)KinectInterop.JointType.HipLeft,
            /** 25 */ (int)KinectInterop.JointType.KneeRight,
            /** 26 */ (int)KinectInterop.JointType.KneeLeft,
            /** 27 */ (int)KinectInterop.JointType.AnkleRight,
            /** 28 */ (int)KinectInterop.JointType.AnkleLeft,
            /** 29 */ -1,  // left heel
            /** 30 */ -1,  // right heel
            /** 31 */ (int)KinectInterop.JointType.FootRight,
            /** 32 */ (int)KinectInterop.JointType.FootLeft,
            /** 33 */ (int)KinectInterop.JointType.Pelvis,  // pelvis
            /** 34 */ -1,  // hat
            /** 35 */ -1,  // wrist right
            /** 36 */ -1,  // hand right
            /** 37 */ -1,  // wrist left
            /** 38 */ -1,  // hand left
            /** 39 */ -1   // pose confidence
        };

        // estimates additional joints for the given body
        private void CalcBodySpecialJoints(ref KinectInterop.BodyData bodyData)
        {
            // pelvis
            int l = (int)KinectInterop.JointType.HipLeft;
            int r = (int)KinectInterop.JointType.HipRight;
            int hc = (int)KinectInterop.JointType.Pelvis;

            bodyData.joint[hc].trackingState = bodyData.joint[l].trackingState < bodyData.joint[r].trackingState ?
                bodyData.joint[l].trackingState : bodyData.joint[r].trackingState;

            // shoulder
            l = (int)KinectInterop.JointType.ShoulderLeft;
            r = (int)KinectInterop.JointType.ShoulderRight;

            {
                // spine joints
                KinectInterop.TrackingState ts = bodyData.joint[l].trackingState < bodyData.joint[r].trackingState ?
                    (bodyData.joint[l].trackingState < bodyData.joint[hc].trackingState ? bodyData.joint[l].trackingState : bodyData.joint[hc].trackingState) :
                    (bodyData.joint[r].trackingState < bodyData.joint[hc].trackingState ? bodyData.joint[r].trackingState : bodyData.joint[hc].trackingState);

                Vector3 posLeftK = bodyData.joint[l].kinectPos;
                Vector3 posRightK = bodyData.joint[r].kinectPos;
                Vector3 scPosK = (posLeftK + posRightK) * 0.5f;

                Vector3 posLeft = bodyData.joint[l].position;
                Vector3 posRight = bodyData.joint[r].position;
                Vector3 scPos = (posLeft + posRight) * 0.5f;

                Vector3 hcPosK = bodyData.joint[hc].kinectPos;
                Vector3 hcPos = bodyData.joint[hc].position;

                // spine naval
                int c = (int)KinectInterop.JointType.SpineNaval;
                KinectInterop.JointData jointData = bodyData.joint[c];

                //// check hip center state
                //if (bodyData.joint[hc].trackingState >= KinectInterop.TrackingState.Inferred)
                {
                    jointData.trackingState = ts;
                    jointData.kinectPos = hcPosK + (scPosK - hcPosK) * 0.3f;
                    jointData.position = hcPos + (scPos - hcPos) * 0.3f;

                    bodyData.joint[c] = jointData;

                    // spine chest
                    c = (int)KinectInterop.JointType.SpineChest;
                    jointData = bodyData.joint[c];

                    jointData.trackingState = ts;
                    jointData.kinectPos = hcPosK + (scPosK - hcPosK) * 0.6f;
                    jointData.position = hcPos + (scPos - hcPos) * 0.6f;

                    bodyData.joint[c] = jointData;
                }

                // shoulder tracking state
                ts = bodyData.joint[l].trackingState < bodyData.joint[r].trackingState ?
                    bodyData.joint[l].trackingState : bodyData.joint[r].trackingState;

                // clavicle left
                c = (int)KinectInterop.JointType.ClavicleLeft;
                jointData = bodyData.joint[c];

                jointData.trackingState = ts;
                jointData.kinectPos = posLeftK + (posRightK - posLeftK) * 0.45f;
                //jointData.imagePos = posLeftI + (posRightI - posLeftI) * 0.45f;
                jointData.position = posLeft + (posRight - posLeft) * 0.45f;

                bodyData.joint[c] = jointData;

                // clavicle right
                c = (int)KinectInterop.JointType.ClavicleRight;
                jointData = bodyData.joint[c];

                jointData.trackingState = ts;
                jointData.kinectPos = posLeftK + (posRightK - posLeftK) * 0.55f;
                //jointData.imagePos = posLeftI + (posRightI - posLeftI) * 0.55f;
                jointData.position = posLeft + (posRight - posLeft) * 0.55f;

                bodyData.joint[c] = jointData;

                // neck
                c = (int)KinectInterop.JointType.Neck;
                jointData = bodyData.joint[c];

                jointData.trackingState = ts;
                jointData.kinectPos = scPosK;
                jointData.position = scPos;

                bodyData.joint[c] = jointData;

                // head
                c = (int)KinectInterop.JointType.Head;
                jointData = bodyData.joint[c];

                jointData.trackingState = ts;
                jointData.kinectPos = hcPosK + (scPosK - hcPosK) * 1.25f;
                jointData.position = hcPos + (scPos - hcPos) * 1.25f;

                //int neck = (int)KinectInterop.JointType.Neck;
                //int nose = (int)KinectInterop.JointType.Nose;
                //int earl = (int)KinectInterop.JointType.EarLeft;
                //int earr = (int)KinectInterop.JointType.EarRight;

                //Vector3 enPosK = ((bodyData.joint[earl].kinectPos + bodyData.joint[earr].kinectPos) * 0.5f - bodyData.joint[neck].kinectPos).normalized;
                //Vector3 nnPosK = bodyData.joint[nose].kinectPos - bodyData.joint[neck].kinectPos;
                //Vector3 enPos = ((bodyData.joint[earl].position + bodyData.joint[earr].position) * 0.5f - bodyData.joint[neck].position).normalized;
                //Vector3 nnPos = bodyData.joint[nose].position - bodyData.joint[neck].position;

                //jointData.trackingState = ts;
                //jointData.kinectPos = bodyData.joint[neck].kinectPos + enPosK * Vector3.Dot(enPosK, nnPosK) * 0.5f;
                //jointData.position = bodyData.joint[neck].position + enPos * Vector3.Dot(enPos, nnPos) * 0.5f;

                bodyData.joint[c] = jointData;
            }

            // hand left
            int w = (int)KinectInterop.JointType.WristLeft;
            int h = (int)KinectInterop.JointType.HandLeft;
            int e = (int)KinectInterop.JointType.ElbowLeft;

            //if (bodyData.joint[w].trackingState >= KinectInterop.TrackingState.Inferred)
            {
                KinectInterop.JointData jointData = bodyData.joint[h];
                jointData.trackingState = bodyData.joint[w].trackingState;
                jointData.orientation = bodyData.joint[w].orientation;

                Vector3 posWrist = bodyData.joint[w].kinectPos;
                Vector3 posElbow = bodyData.joint[e].kinectPos;
                jointData.kinectPos = posWrist + (posWrist - posElbow) * 0.2f;

                //Vector2 imgWrist = bodyData.joint[w].imagePos;
                //Vector2 imgElbow = bodyData.joint[e].imagePos;
                //jointData.imagePos = imgWrist + (imgWrist - imgElbow) * 0.2f;

                posWrist = bodyData.joint[w].position;
                posElbow = bodyData.joint[e].position;
                jointData.position = posWrist + (posWrist - posElbow) * 0.2f;

                bodyData.joint[h] = jointData;
            }

            // hand right
            w = (int)KinectInterop.JointType.WristRight;
            h = (int)KinectInterop.JointType.HandRight;
            e = (int)KinectInterop.JointType.ElbowRight;

            //if (bodyData.joint[w].trackingState >= KinectInterop.TrackingState.Inferred)
            {
                KinectInterop.JointData jointData = bodyData.joint[h];
                jointData.trackingState = bodyData.joint[w].trackingState;
                jointData.orientation = bodyData.joint[w].orientation;

                Vector3 posWrist = bodyData.joint[w].kinectPos;
                Vector3 posElbow = bodyData.joint[e].kinectPos;
                jointData.kinectPos = posWrist + (posWrist - posElbow) * 0.2f;

                //Vector2 imgWrist = bodyData.joint[w].imagePos;
                //Vector2 imgElbow = bodyData.joint[e].imagePos;
                //jointData.imagePos = imgWrist + (imgWrist - imgElbow) * 0.2f;

                posWrist = bodyData.joint[w].position;
                posElbow = bodyData.joint[e].position;
                jointData.position = posWrist + (posWrist - posElbow) * 0.2f;

                bodyData.joint[h] = jointData;
            }

            //// ankle left
            //int knee = (int)KinectInterop.JointType.KneeLeft;
            //int ank = (int)KinectInterop.JointType.AnkleLeft;
            //int foot = (int)KinectInterop.JointType.FootLeft;

            ////if (bodyData.joint[knee].trackingState != KinectInterop.TrackingState.NotTracked &&
            ////   bodyData.joint[ank].trackingState != KinectInterop.TrackingState.NotTracked &&
            ////   bodyData.joint[foot].trackingState != KinectInterop.TrackingState.NotTracked)
            //{
            //    Vector3 vAnkDir = bodyData.joint[ank].kinectPos - bodyData.joint[knee].kinectPos;
            //    Vector3 vFootDir = bodyData.joint[foot].kinectPos - bodyData.joint[ank].kinectPos;

            //    Vector3 vFootProj = Vector3.Project(vFootDir, vAnkDir);
            //    bodyData.joint[ank].kinectPos += vFootProj;

            //    vAnkDir = bodyData.joint[ank].position - bodyData.joint[knee].position;
            //    vFootDir = bodyData.joint[foot].position - bodyData.joint[ank].position;

            //    vFootProj = Vector3.Project(vFootDir, vAnkDir);
            //    bodyData.joint[ank].position += vFootProj;
            //}

            //// ankle right
            //knee = (int)KinectInterop.JointType.KneeRight;
            //ank = (int)KinectInterop.JointType.AnkleRight;
            //foot = (int)KinectInterop.JointType.FootRight;

            ////if (bodyData.joint[knee].trackingState != KinectInterop.TrackingState.NotTracked &&
            ////   bodyData.joint[ank].trackingState != KinectInterop.TrackingState.NotTracked &&
            ////   bodyData.joint[foot].trackingState != KinectInterop.TrackingState.NotTracked)
            //{
            //    Vector3 vAnkDir = bodyData.joint[ank].kinectPos - bodyData.joint[knee].kinectPos;
            //    Vector3 vFootDir = bodyData.joint[foot].kinectPos - bodyData.joint[ank].kinectPos;

            //    Vector3 vFootProj = Vector3.Project(vFootDir, vAnkDir);
            //    bodyData.joint[ank].kinectPos += vFootProj;

            //    vAnkDir = bodyData.joint[ank].position - bodyData.joint[knee].position;
            //    vFootDir = bodyData.joint[foot].position - bodyData.joint[ank].position;

            //    vFootProj = Vector3.Project(vFootDir, vAnkDir);
            //    bodyData.joint[ank].position += vFootProj;
            //}
        }

    }
}
