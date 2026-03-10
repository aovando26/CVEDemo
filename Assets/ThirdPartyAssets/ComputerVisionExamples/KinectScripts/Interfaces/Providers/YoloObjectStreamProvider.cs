using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.Rendering;
using com.rfilkov.kinect;
using com.rfilkov.devices;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;


namespace com.rfilkov.providers
{
    // Object detection structure
    [StructLayout(LayoutKind.Sequential)]
    public struct ObjectDetection
    {
        public Vector2 center;
        public Vector2 size;
        public int objType;
        public float score;

        public float m0, m1, m2, m3, m4, m5, m6, m7;
        public float m8, m9, m10, m11, m12, m13, m14, m15;
        public float m16, m17, m18, m19, m20, m21, m22, m23;
        public float m24, m25, m26, m27, m28, m29, m30, m31;

        public const int SIZE = 38 * sizeof(float);  // 6 * sizeof(float);  // 

        public override string ToString()
        {
            return $"{(KinectInterop.ObjectType)objType} - score: {score:F3}\ncenter: {center:F2}; size: {size:F2}\nm0: {m0:F3}, m1: {m1:F3}, m2: {m2:F3}\nm28: {m28:F3}, m29: {m29:F3}, m30: {m30:F3}, m31: {m31:F3}";
        }
    }


    /// <summary>
    /// Per-frame settings of YoloObj-provider. 
    /// </summary>
    public class YoloObjProviderData
    {
        //public uint[] _objCounts;
        public ObjectDetection[] _objDetections;
        public uint _objCount;
    }

    /// <summary>
    /// Yolo object stream provider.
    /// </summary>
    public class YoloObjectStreamProvider : MonoBehaviour, IObjectStreamProvider
    {
        [Tooltip("Detection score threshold")]
        [Range(0, 1.0f)]
        public float scoreThreshold = 0.5f;

        [Tooltip("NMS IoU threshold")]
        [Range(0, 1.0f)]
        public float iouThreshold = 0.3f;

        [Tooltip("Maximum number of tracked objects")]
        [Range(0, 100)]
        public int maxTrackedObjects = 20;

        [Tooltip("Specific object types to track. Tracks all, if none specified.")]
        public KinectInterop.ObjectType[] trackObjectTypes = new KinectInterop.ObjectType[0];

        [Tooltip("Raw image for debugging purposes.")]
        public UnityEngine.UI.RawImage _debugImage = null;

        [Tooltip("Raw image 2 for debugging purposes.")]
        public UnityEngine.UI.RawImage _debugImage2 = null;


        // contains the allowed object types, if any
        private HashSet<int> _allowedObjTypes = new HashSet<int>();

        // references
        private UniCamInterface _unicamInt;
        private IDeviceManager _deviceManager;
        private KinectManager _kinectManager;
        //private UnityEngine.UI.RawImage _debugImage;

        // stream provider properties
        private string _providerId = null;
        private StreamState _streamState = StreamState.NotStarted;

        // set of object types to track
        private HashSet<KinectInterop.ObjectType> _trackObjectTypes = new HashSet<KinectInterop.ObjectType>();

        // original image
        //private RenderTexture _sourceTex = null;
        private Vector3 _srcImageScale = Vector3.one;

        // scaled frame props
        private bool _isScalingEnabled = false;
        private int _scaledFrameWidth = 0;
        private int _scaledFrameHeight = 0;

        private ComputeBuffer _scaledObjIndexBuf = null;

        // scaling shader params
        private ComputeShader _scaleBufShader;
        private Material _scaleBufMaterial;
        //private ComputeBuffer _scaleSrcBuf;
        private int _scaleBufKernel;

        private string _inputNameDetect;
        private int _inputWidthDetect = 0;
        private int _inputHeightDetect = 0;
        private int _inputChannelsDetect = 0;

        // reference to SM
        private SentisManager _sentisManager = null;
        private RuntimeModel _objModelDetect = null;

        private const string _objModelName = "odet";

        // provider state
        private enum ProviderState : int { NotStarted, DetectObjects, ProcessDetection, Ready, CopyFrame }
        //private ProviderState _providerState = ProviderState.NotStarted;

        // constants
        private const int MAX_DETECTIONS = 100;
        private const int MASK_IMAGE_SIZE = 160;

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
        //private ComputeBuffer _detTexBuffer;

        private uint[] _rawDetCount = new uint[1];
        private uint[] _nmsObjCount = new uint[1];

        private ComputeBuffer _detCountBuffer;
        private ComputeBuffer _detOutputBuffer;
        private ComputeBuffer _detDisTypeBuffer;

        private ComputeBuffer _nmsCountBuffer;
        private ComputeBuffer _nmsOutputBuffer;
        private ComputeBuffer _nmsMaskBuffer;

        // object-index image buffer
        private ComputeBuffer _objIndexBuffer;

        // mask material and texture
        private Material _objSegmMaterial;
        private RenderTexture _objSegmTex;

        // depth image properties
        private IColorStreamProvider _colorProvider = null;
        //private IIRStreamProvider _irProvider = null;
        private IDepthStreamProvider _depthProvider = null;
        private int _depthImageWidth = 0;
        private int _depthImageHeight = 0;

        // sensor data
        private KinectInterop.SensorData _sensorData = null;

        // last inference time & framesets
        private ulong _lastInfTime = 0;
        private Queue<ulong> _lastInfTs = new Queue<ulong>();
        private Dictionary<ulong, FramesetData> _lastInfOtFs = new Dictionary<ulong, FramesetData>();
        private Dictionary<ulong, FramesetData> _lastInfOiFs = new Dictionary<ulong, FramesetData>();

        // detection textures
        private RenderTexture _detectionTex;
        //private Material _detectionMat;

        // object tracking manager
        private YoloObjectTracker _objectTracker = null;
        private trackers.ByteTracker _byteTracker = null;

        // action dispatchers
        private const int MAX_QUEUED_FRAMES = FramesetManager.FRAMESETS_COUNT;
        private Queue<FramesetData> _objProcessDispatcher = new Queue<FramesetData>(MAX_QUEUED_FRAMES);
        private Queue<FramesetData> _objSaveDispatcher = new Queue<FramesetData>(MAX_QUEUED_FRAMES);


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

            // tracked object types
            _trackObjectTypes.Clear();
            foreach (var objType in trackObjectTypes)
            {
                if (!_trackObjectTypes.Contains(objType))
                    _trackObjectTypes.Add(objType);
            }

            // get color image provider
            _colorProvider = unicamInt._colorStreamProvider;
            if (_colorProvider == null)
                throw new Exception("Source stream is not available. Please enable the color stream.");

            // get depth image size
            _depthProvider = unicamInt._depthStreamProvider;
            if (_depthProvider == null)
                throw new Exception("Depth stream provider is not available.");

            var depthImageSize = _depthProvider.GetDepthImageSize();
            _depthImageWidth = depthImageSize.x;
            _depthImageHeight = depthImageSize.y;

            // get source image scale
            _srcImageScale = _colorProvider != null ? _colorProvider.GetBTSourceImageScale() : Vector3.one;

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
            string odetModelName = "oy11nf_1.3";
            _objModelDetect = _sentisManager.AddModel(_objModelName, odetModelName);
            if (_objModelDetect == null)
            {
                // model not found
                return null;
            }

            _inputNameDetect = _objModelDetect.inputNames[0];
            var inShapeDetect = _objModelDetect.inputShapes[_inputNameDetect];

            _inputWidthDetect = (int)inShapeDetect[3];
            _inputHeightDetect = (int)inShapeDetect[2];
            _inputChannelsDetect = (int)inShapeDetect[1];
            //Debug.Log($"ObjDet model loaded - iW: {_inputWidthDetect}, iH: {_inputHeightDetect}, iC: {_inputChannelsDetect}, name: {_inputNameDetect}, dt: {DateTime.Now.ToString("HH:mm:ss.fff")}");

            // init detection texture
            _detectionTex = KinectInterop.CreateRenderTexture(_detectionTex, _inputWidthDetect, _inputHeightDetect, RenderTextureFormat.ARGB32);

            // init shaders
            //Shader lboxTexShader = Shader.Find("Kinect/LetterboxTexShader");
            //_detectionMat = new Material(lboxTexShader);

            // create detTex-to-tensor shader, tensor & buffer
            _texToTensorShader = Resources.Load("TexToTensorShader") as ComputeShader;
            //_detTexTrans = new TextureTransform().SetDimensions(_inputWidthDetect, _inputHeightDetect, _inputChannelsDetect).SetTensorLayout(TensorLayout.NCHW);
            //_detTexTensor = new Tensor<float>(new TensorShape(1, _inputChannelsDetect, _inputHeightDetect, _inputWidthDetect));

            // allowed object types
            _allowedObjTypes.Clear();
            int[] disabledObjTypes = new int[(int)KinectInterop.ObjectType.Count];
            if(trackObjectTypes.Length > 0)
            {
                Array.Fill<int>(disabledObjTypes, -1);
            }

            foreach (var objType in trackObjectTypes)
            {
                disabledObjTypes[(int)objType] = 0;
                if (!_allowedObjTypes.Contains((int)objType))
                    _allowedObjTypes.Add((int)objType);
            }

            if (KinectInterop.IsSupportsComputeShaders())
            {
                // processing shader
                _processShader = Resources.Load("YoloObjectCompute") as ComputeShader;
                //_processShader.SetInt("_currentRegCount", 0);

                // scale shader
                _scaleBufShader = Resources.Load("BodyIndexScaleShader") as ComputeShader;
                _scaleBufKernel = _scaleBufShader != null ? _scaleBufShader.FindKernel("ScaleBodyIndexImage") : -1;

                // create detect-output buffers
                _detCountBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
                _detOutputBuffer = new ComputeBuffer(MAX_DETECTIONS, ObjectDetection.SIZE, ComputeBufferType.Append);

                _detDisTypeBuffer = new ComputeBuffer((int)KinectInterop.ObjectType.Count, sizeof(int));
                _detDisTypeBuffer.SetData(disabledObjTypes);

                _nmsCountBuffer = new ComputeBuffer(1, sizeof(uint));
                _nmsOutputBuffer = new ComputeBuffer(maxTrackedObjects, ObjectDetection.SIZE);
                _nmsMaskBuffer = new ComputeBuffer(maxTrackedObjects * MASK_IMAGE_SIZE * MASK_IMAGE_SIZE, sizeof(float));
            }

            // create object tracker
            _objectTracker = new YoloObjectTracker(scoreThreshold);
            _byteTracker = new trackers.ByteTracker();
            _byteTracker.trackHighThresh = scoreThreshold;
            _byteTracker.trackLowThresh = scoreThreshold * 0.4f;
            _byteTracker.newTrackThresh = scoreThreshold;

            // last inference time & framesets
            _lastInfTime = 0;
            _lastInfTs.Clear();
            _lastInfOtFs.Clear();
            _lastInfOiFs.Clear();

            _providerId = deviceManager != null ? deviceManager.GetDeviceId() + "_os" : "yolo_os";
            _streamState = StreamState.Working;

            if (_debugImage2 != null)
            {
                Shader objSegmShader = Shader.Find("Kinect/ObjectSegmShader");
                _objSegmMaterial = new Material(objSegmShader);
                _objSegmTex = KinectInterop.CreateRenderTexture(_objSegmTex, MASK_IMAGE_SIZE, MASK_IMAGE_SIZE);

                _debugImage2.texture = _objSegmTex;  // _detectionTex;
            }

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
            //// remove models from SM
            //_sentisManager.RemoveModel(_objModelName);

            // dispose det-tex-tensor
            //_detTexTensor?.Dispose();
            //_detTexTensor = null;
            //_detTexBuffer = null;

            _detectionTex?.Release();
            _detectionTex = null;

            if (KinectInterop.IsSupportsComputeShaders())
            {
                // compute buffers
                _detCountBuffer?.Dispose();
                _detCountBuffer = null;

                _detOutputBuffer?.Dispose();
                _detOutputBuffer = null;

                _detDisTypeBuffer?.Dispose();
                _detDisTypeBuffer = null;

                _nmsCountBuffer?.Dispose();
                _nmsCountBuffer = null;

                _nmsOutputBuffer?.Dispose();
                _nmsOutputBuffer = null;

                _nmsMaskBuffer?.Dispose();
                _nmsMaskBuffer = null;
            }

            // scaling props
            _isScalingEnabled = false;

            //_scaleSrcBuf?.Release();
            //_scaleSrcBuf = null;

            _scaledObjIndexBuf?.Release();
            _scaledObjIndexBuf = null;

            _objSegmTex?.Release();
            _objSegmTex = null;
            _objSegmMaterial = null;

            _objIndexBuffer?.Dispose();
            _objIndexBuffer = null;

            KinectInterop.Destroy(_processIndexMaterial);
            KinectInterop.Destroy(_clearIndexMaterial);
            KinectInterop.Destroy(_scaleBufMaterial);

            _objectTracker.StopObjectTracking();
            //_userMgr = null;

            // last inference time & framesets
            _lastInfTs.Clear();
            _lastInfOtFs.Clear();
            _lastInfOiFs.Clear();

            _objModelDetect = null;
            _streamState = StreamState.Stopped;
        }

        // creates provider settings, as needed
        private YoloObjProviderData GetProviderData(FramesetData fsData)
        {
            if (fsData == null || _streamState != StreamState.Working)
                return null;
            if (fsData._objProviderSettings != null)
                return (YoloObjProviderData)fsData._objProviderSettings;

            // create and initialize plm data
            YoloObjProviderData objDetData = new YoloObjProviderData();

            //objDetData._objCounts = new uint[1];
            objDetData._objDetections = new ObjectDetection[maxTrackedObjects];
            objDetData._objCount = 0;

            fsData._objProviderSettings = objDetData;

            return objDetData;
        }

        /// <summary>
        /// Releases stream resources allocated in the frameset.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        public void ReleaseFrameData(FramesetData fsData)
        {
            if (fsData == null || fsData._isObjDataCopied)
                return;

            fsData._alTrackedObjects.Clear();
            fsData._objIndexFrame = null;
            fsData._scaledObjIndexFrame = null;

            if(fsData._objProviderSettings != null)
            {
                YoloObjProviderData plmData = (YoloObjProviderData)fsData._objProviderSettings;

                //plmData._objCounts = null;
                plmData._objDetections = null;
                plmData._objCount = 0;

                fsData._objProviderSettings = null;
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
            int queueLen = _objProcessDispatcher.Count;
            for(int i = 0; i < queueLen; i++)
            {
                FramesetData fsd = _objProcessDispatcher.Dequeue();
                ProcessObjectFrame(sensorData, fsd);
            }
        }

        /// <summary>
        /// Updates the stream data in the main thread.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="fsData">Frameset data</param>
        public void UpdateStreamData(KinectInterop.SensorData sensorData, FramesetData fsData)
        {
            //while (_objSaveDispatcher.TryDequeue(out FramesetData actionFs))
            int queueLen = _objSaveDispatcher.Count;
            for (int i = 0; i < queueLen; i++)
            {
                FramesetData fsd = _objSaveDispatcher.Dequeue();
                SaveFrameObjectData(fsd);
            }

            // get the correct frame
            fsData = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();  //?._fsPrev;
            bool isObjIndexRequested = (_unicamInt.frameSourceFlags & KinectInterop.FrameSource.TypeObjectIndex) != 0;

            if (!fsData._isObjDataFrameReady || (isObjIndexRequested && !fsData._isObjIndexFrameReady))
            {
                ProcessSourceImage(fsData);
            }

            if (fsData._isObjIndexFrameReady && _isScalingEnabled && !fsData._isScaledObjIndexFrameReady &&
                fsData._scaledObjIndexFrameTimestamp != fsData._objIndexTimestamp)
            {
                fsData._scaledObjIndexFrameTimestamp = fsData._objIndexTimestamp;
                ScaleObjIndexFrame(fsData);
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
            if (!_unicamInt.isSyncBodyAndDepth || fsData._objProviderState >= (int)ProviderState.ProcessDetection ||
                (_kinectManager != null && _kinectManager.getObjectFrames == KinectManager.ObjectTrackingType.None))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the object-index image scale.
        /// </summary>
        /// <returns>Object-index image scale</returns>
        public Vector3 GetObjectIndexImageScale()
        {
            return new Vector3(_srcImageScale.x, -1f, 1f);  // depthScale.x
        }

        /// <summary>
        /// Returns the object-index image size.
        /// </summary>
        /// <returns>Object-index image size (width, height)</returns>
        public Vector2Int GetObjectIndexImageSize()
        {
            return new Vector2Int(_depthImageWidth, _depthImageHeight);
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

        // scales current object segmentation frame, according to requirements
        private void ScaleObjIndexFrame(FramesetData fsData)
        {
            int destFrameLen = _scaledFrameWidth * _scaledFrameHeight;
            if (fsData._scaledObjIndexFrame == null || fsData._scaledObjIndexFrame.Length != destFrameLen)
            {
                fsData._scaledObjIndexFrame = new byte[destFrameLen];
            }

            if (KinectInterop.IsSupportsComputeShaders())
            {
                // compute shaders
                //int srcBufLength = _depthImageWidth * _depthImageHeight >> 2;
                //if (_scaleSrcBuf == null || _scaleSrcBuf.count != srcBufLength)
                //    _scaleSrcBuf = KinectInterop.CreateComputeBuffer(_scaleSrcBuf, srcBufLength, sizeof(uint));

                //if (fsData._objIndexFrame != null)
                //{
                //    KinectInterop.SetComputeBufferData(_scaleSrcBuf, fsData._objIndexFrame, fsData._objIndexFrame.Length, sizeof(byte));
                //}

                int oiImageBufLen = (_depthImageWidth * _depthImageHeight) >> 2;
                if (_objIndexBuffer == null || _objIndexBuffer.count != oiImageBufLen)
                {
                    _objIndexBuffer?.Dispose();
                    _objIndexBuffer = new ComputeBuffer(oiImageBufLen, sizeof(uint));
                }

                int destBufLen = destFrameLen >> 2;
                if (_scaledObjIndexBuf == null || _scaledObjIndexBuf.count != destBufLen)
                    _scaledObjIndexBuf = KinectInterop.CreateComputeBuffer(_scaledObjIndexBuf, destBufLen, sizeof(uint));

                CommandBuffer cmd = new CommandBuffer { name = "ScaleObjIndexCb" };
                cmd.SetComputeBufferParam(_scaleBufShader, _scaleBufKernel, "_BodyIndexBuf", _objIndexBuffer);  // _scaleSrcBuf
                cmd.SetComputeBufferParam(_scaleBufShader, _scaleBufKernel, "_TargetBuf", _scaledObjIndexBuf);
                cmd.SetComputeIntParam(_scaleBufShader, "_BodyIndexImgWidth", _depthImageWidth);
                cmd.SetComputeIntParam(_scaleBufShader, "_BodyIndexImgHeight", _depthImageHeight);
                cmd.SetComputeIntParam(_scaleBufShader, "_TargetImgWidth", _scaledFrameWidth);
                cmd.SetComputeIntParam(_scaleBufShader, "_TargetImgHeight", _scaledFrameHeight);
                cmd.DispatchCompute(_scaleBufShader, _scaleBufKernel, _scaledFrameWidth / 8, _scaledFrameHeight / 8, 1);
                //Debug.Log($"fs: {fsData._fsIndex} - Started obj-seg scaling for ts: {fsData._scaledObjIndexFrameTimestamp}. Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));

                cmd.RequestAsyncReadback(_scaledObjIndexBuf, request =>
                {
                    FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();

                    if (fsd != null && fsd._scaledObjIndexFrame != null)
                    {
                        if (request.width == fsd._scaledObjIndexFrame.Length)
                            request.GetData<byte>().CopyTo(fsd._scaledObjIndexFrame);
                        fsd._scaledObjIndexFrameTimestamp = fsData._objIndexTimestamp;
                        fsd._isScaledObjIndexFrameReady = true;
                        //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsd._fsIndex} ScaledObjIndexTime: {fsd._scaledObjIndexFrameTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                    }
                });

                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();
            }

        }


        // processes the source image to detect bodies
        private void ProcessSourceImage(FramesetData fsData)
        {
            if (_sentisManager == null || _objModelDetect == null)
                return;

            if (fsData._objProviderState == 0)
            {
                //SetInitialProviderState(fsData);
                fsData._objProviderState = (int)ProviderState.DetectObjects;
            }

            YoloObjProviderData odetData = GetProviderData(fsData);
            int fsIndex = _unicamInt.isSyncBodyAndDepth ? fsData._fsIndex : -1;  // fsData._fsIndex;  // 
            //Debug.Log($"  OT: fs: {fsData._fsIndex} - {(ProviderState)fsData._objProviderState}/{fsData._objProviderState} - oCount: {odetData._objCount}\nmStarted: {_sentisManager.IsModelStarted(fsIndex, _objModelName)}, mReady: {_sentisManager.IsModelReady(fsIndex, _objModelName)}");

            switch ((ProviderState)fsData._objProviderState)
            {
                case ProviderState.DetectObjects:
                    // check for inference time
                    //Debug.Log($"fs: {fsData._fsIndex} DetectObjects - colorTs: {fsData._rawFrameTimestamp}, lastTs: {_lastInfTime}\ndTs: {fsData._rawFrameTimestamp - _lastInfTime}" + (_lastInfOtFs.ContainsKey(_lastInfTime) ? $";    last fs: {_lastInfOtFs[_lastInfTime]._fsIndex}, ts: {_lastInfOtFs[_lastInfTime]._rawFrameTimestamp}" : ""));
                    if (_lastInfTime != 0 && fsData._rawFrameTimestamp > _lastInfTime && (fsData._rawFrameTimestamp - _lastInfTime) < KinectInterop.MIN_TIME_BETWEEN_INF &&
                        _lastInfOtFs.ContainsKey(_lastInfTime) && _lastInfOtFs[_lastInfTime]._rawFrameTimestamp == _lastInfTime)
                    {
                        // continue w/ copy-frame wait
                        SetCopyLastInfFrame(fsData);
                    }

                    // detect bodies
                    else if (!_sentisManager.IsModelStarted(fsIndex, _objModelName))
                    {
                        if(DetectObjectsInImage(fsData))
                        {
                            fsData._objProviderState = (int)ProviderState.ProcessDetection;
                        }
                    }
                    break;

                case ProviderState.ProcessDetection:
                    // process detected bodies
                    if (_sentisManager.IsModelReady(fsIndex, _objModelName) && odetData != null)
                    {
                        if(ProcessDetectedObjects(fsData, odetData))
                        {
                            _sentisManager.ClearModelReady(fsIndex, _objModelName);
                            fsData._objProviderState = (int)ProviderState.Ready;
                        }
                    }
                    break;

                case ProviderState.CopyFrame:
                    // copy data from the last finished frame
                    CopyLastReadyFrameData(fsData, odetData);
                    break;
            }
        }

        // copies data from the last finished frame
        private void CopyLastReadyFrameData(FramesetData fsData, YoloObjProviderData odetData)
        {
            FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();

            // object data
            FramesetData lastOtFs = _lastInfOtFs.ContainsKey(fsData._refOtTs) && _lastInfOtFs[fsData._refOtTs]._rawFrameTimestamp == fsData._refOtTs ? _lastInfOtFs[fsData._refOtTs] : null;
            if (lastOtFs == null && _unicamInt.isSyncBodyAndDepth)
                lastOtFs = fsd._fsPrev;

            if (!fsd._isObjDataFrameReady && (lastOtFs == null || lastOtFs._isObjDataFrameReady))
            {
                if (lastOtFs != null)
                {
                    uint objCount = fsd._trackedObjCount = lastOtFs._trackedObjCount;

                    // create the needed slots
                    while (fsd._alTrackedObjects.Count < objCount)
                    {
                        fsd._alTrackedObjects.Add(new KinectInterop.ObjectData());
                    }

                    for (int i = 0; i < objCount; i++)
                    {
                        fsd._alTrackedObjects[i] = lastOtFs._alTrackedObjects[i];
                    }
                }

                // object frame is ready
                fsd._objDataTimestamp = fsd._rawFrameTimestamp + fsData._trackedObjCount;
                fsd._isObjDataFrameReady = true;
                //Debug.Log($"    D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, RawCpObjDataTime: {fsd._objDataTimestamp}, oCount: {fsd._trackedObjCount}, fsObjects: {fsd._alTrackedObjects.Count}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff") + $"\nfsData: {fsData._fsIndex}, fsTime: {fsData._objDataTimestamp}");
            }

            // object-index frame
            FramesetData lastOiFs = _lastInfOiFs.ContainsKey(fsData._refOtTs) && _lastInfOiFs[fsData._refOtTs]._rawFrameTimestamp == fsData._refOtTs ? _lastInfOiFs[fsData._refOtTs] : null;
            if (lastOiFs == null && _unicamInt.isSyncBodyAndDepth)
                lastOiFs = fsd._fsPrev;

            bool isObjIndexRequested = (_unicamInt.frameSourceFlags & KinectInterop.FrameSource.TypeObjectIndex) != 0;
            if (isObjIndexRequested && !fsd._isObjIndexFrameReady && (lastOiFs == null || lastOiFs._isObjIndexFrameReady))
            {
                if (lastOiFs != null)
                {
                    if (fsd._objIndexFrame == null || fsd._objIndexFrame.Length != lastOiFs._objIndexFrame.Length)
                    {
                        fsd._objIndexFrame = new byte[lastOiFs._objIndexFrame.Length];
                    }

                    KinectInterop.CopyBytes(lastOiFs._objIndexFrame, sizeof(byte), fsd._objIndexFrame, sizeof(byte));
                }

                fsd._objIndexTimestamp = fsd._rawFrameTimestamp;
                fsd._isObjIndexFrameReady = true;
                //Debug.Log($"    D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, RawCpObjIndexTime: {fsd._objIndexTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));

                if (_isScalingEnabled && !fsd._isScaledObjIndexFrameReady &&
                    fsd._scaledObjIndexFrameTimestamp != fsd._objIndexTimestamp)
                {
                    // set data for object scaling
                    int oiImageBufLen = (_depthImageWidth * _depthImageHeight) >> 2;
                    if (_objIndexBuffer == null || _objIndexBuffer.count != oiImageBufLen)
                    {
                        _objIndexBuffer?.Dispose();
                        _objIndexBuffer = new ComputeBuffer(oiImageBufLen, sizeof(uint));
                    }

                    KinectInterop.SetComputeBufferData(_objIndexBuffer, fsd._objIndexFrame, fsd._objIndexFrame.Length, sizeof(byte));
                    //Debug.Log($"    D{_unicamInt.deviceIndex} fs: {fsd._fsIndex} SetCpObjIndexData for scaling, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                }
            }

            // set provider state to ready, if all data is copied
            if (fsd._isObjDataFrameReady &&
                (!isObjIndexRequested || fsd._isObjIndexFrameReady))
            {
                fsData._objProviderState = (int)ProviderState.Ready;
                //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, CopyObjDet - Provider is ready, Now: " + DateTime.Now.ToString("HH:mm:ss.fff") + $"\nfsData: {fsData._fsIndex}, fsTime: {fsData._rawFrameTimestamp}");
            }
        }

        // sets provider state to copy from last inf-frame
        private void SetCopyLastInfFrame(FramesetData fsData)
        {
            fsData._refOtTs = _lastInfTime;
            fsData._objProviderState = (int)ProviderState.CopyFrame;
            //Debug.Log($"fs: {fsData._fsIndex}, ObjDet ts: {fsData._rawFrameTimestamp} will copy frame from ts: {_lastInfTime}\ndTs: {fsData._rawFrameTimestamp - _lastInfTime}" + (_lastInfOtFs.ContainsKey(_lastInfTime) ? $";    last fs: {_lastInfOtFs[_lastInfTime]._fsIndex}, ts: {_lastInfOtFs[_lastInfTime]._rawFrameTimestamp}" : ""));
        }

        // sets this inf-frame as last inf-frame
        private void SetThisAsLastInfFrame(FramesetData fsData)
        {
            //Debug.Log($"fs: {fsData._fsIndex}, ObjDet ts: {fsData._rawFrameTimestamp} will do inference. lastTs: {_lastInfTime}\ndTs: {fsData._rawFrameTimestamp - _lastInfTime}");

            if (_lastInfTs.Count >= KinectInterop.MAX_LASTINF_QUEUE_LENGTH)
            {
                ulong firstTs = _lastInfTs.Dequeue();

                if (_lastInfOtFs.ContainsKey(firstTs))
                    _lastInfOtFs.Remove(firstTs);
                if (_lastInfOiFs.ContainsKey(firstTs))
                    _lastInfOiFs.Remove(firstTs);
            }

            _lastInfTime = fsData._rawFrameTimestamp;
            _lastInfTs.Enqueue(_lastInfTime);
            fsData._refOtTs = _lastInfTime;

            _lastInfOtFs[_lastInfTime] = fsData;
            _lastInfOiFs[_lastInfTime] = fsData;
        }


        // initializes object detection from the source image
        private Texture InitObjectDetection(FramesetData fsData)
        {
            // check depth image size
            Vector2Int depthImageSize = _depthProvider.GetDepthImageSize();

            if (_depthImageWidth != depthImageSize.x || _depthImageHeight != depthImageSize.y)
            {
                _depthImageWidth = depthImageSize.x;
                _depthImageHeight = depthImageSize.y;
            }

            if ((_unicamInt.frameSourceFlags & KinectInterop.FrameSource.TypeObjectIndex) != 0)
            {
                int objIndexFrameSize = depthImageSize.x * depthImageSize.y;

                if (fsData._objIndexFrame == null || fsData._objIndexFrame.Length != objIndexFrameSize)
                {
                    fsData._objIndexFrame = new byte[objIndexFrameSize];
                }
            }

            // get source image time
            Texture srcImage = null;
            ulong imageTime = _colorProvider != null ? fsData._colorImageTimestamp : 0L;

            //if (imageTime != 0 && fsData._objSourceImageTimestamp != imageTime)
            {
                // get source image
                srcImage = _colorProvider != null ? _colorProvider.GetBTSourceImage(fsData) : null;
                if (srcImage == null || imageTime == 0 || imageTime != fsData._rawFrameTimestamp)
                    return null;

                // Letterboxing scale factor
                _lboxScale = new Vector2(Mathf.Max((float)srcImage.height / srcImage.width, 1f), Mathf.Max((float)srcImage.width / srcImage.height, 1f));
                _lboxPad = new Vector2((1f - 1f / _lboxScale.x) * 0.5f, (1f - 1f / _lboxScale.y) * 0.5f);
                //Debug.Log($"LetterboxScale: {_lboxScale:F3}, pad: {_lboxPad:F3}, srcW: {srcImage.width}, srcH: {srcImage.height}");

                fsData._objSourceImageTimestamp = imageTime;
            }

            //if (_debugImage != null && _debugImage.texture == null)
            //{
            //    _debugImage.texture = srcImage;

            //    float debugH = _debugImage.rectTransform.sizeDelta.y;
            //    _debugImage.rectTransform.sizeDelta = new Vector2(debugH * fsData._btSourceImage.width / fsData._btSourceImage.height, debugH);
            //}

            return srcImage;
        }

        // detects bodies in the source image
        private bool DetectObjectsInImage(FramesetData fsData)
        {
            // init body detection
            Texture srcImage = InitObjectDetection(fsData);
            if (srcImage == null) 
                return false;

            if (_inputWidthDetect <= 0 || _inputHeightDetect <= 0)
            {
                _inputWidthDetect = (_depthImageWidth + 0x1F) & 0xFFE0;
                _inputHeightDetect = (_depthImageHeight + 0x1F) & 0xFFE0;

                // re-init detection texture
                _detectionTex = KinectInterop.CreateRenderTexture(_detectionTex, _inputWidthDetect, _inputHeightDetect, RenderTextureFormat.ARGB32);
                //Debug.Log($"ObjDet model iW: {_inputWidthDetect}, iH: {_inputHeightDetect}, name: {_inputNameDetect}");
            }

            if (_inputWidthDetect <= 0 || _inputHeightDetect <= 0)
                return false;

            // set last inference frame
            SetThisAsLastInfFrame(fsData);

            // make it letterbox image
            //_detectionMat.SetInt("_isLinearColorSpace", QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);
            //_detectionMat.SetInt("_letterboxWidth", _inputWidthDetect);
            //_detectionMat.SetInt("_letterboxHeight", _inputHeightDetect);
            //_detectionMat.SetVector("_lboxScale", _lboxScale);
            //Graphics.Blit(srcImage, _detectionTex, _detectionMat);

            CommandBuffer cmd = new CommandBuffer { name = "ImageToLetterboxCb" };
            cmd.SetComputeIntParam(_texToTensorShader, "_isLinearColorSpace", QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);
            cmd.SetComputeIntParam(_texToTensorShader, "_letterboxWidth", _inputWidthDetect);
            cmd.SetComputeIntParam(_texToTensorShader, "_letterboxHeight", _inputHeightDetect);
            cmd.SetComputeVectorParam(_texToTensorShader, "_lboxScale", _lboxScale);
            cmd.SetComputeTextureParam(_texToTensorShader, 3, "_sourceTex", srcImage);
            cmd.SetComputeTextureParam(_texToTensorShader, 3, "_lboxTex", _detectionTex);
            cmd.DispatchCompute(_texToTensorShader, 3, _detectionTex.width / 8, _detectionTex.height / 8, 1);

            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();

            if (_debugImage != null && _debugImage.texture == null)
            {
                _debugImage.texture = _detectionTex;

                float debugH = _debugImage.rectTransform.sizeDelta.y;
                _debugImage.rectTransform.sizeDelta = new Vector2(debugH * _detectionTex.width / _detectionTex.height, debugH);
            }

            // start model inference
            int fsIndex = _unicamInt.isSyncBodyAndDepth ? fsData._fsIndex : -1;  // fsData._fsIndex;  // 
            bool detModelStarted = _sentisManager.StartInference(fsIndex, _objModelName, _inputNameDetect, _detectionTex);

            //if (detModelStarted)
            //    Debug.LogWarning($"fs: {fsData._fsIndex}, Model odet inference scheduled.");

            return detModelStarted;
        }

        // processes detected objects in image
        private bool ProcessDetectedObjects(FramesetData fsData, YoloObjProviderData odetData)
        {
            if (_unicamInt.isSyncDepthAndColor && _unicamInt.isSyncBodyAndDepth && !fsData._isDepthFrameReady)
                return false;

            // skip inference this frame
            _sentisManager.SkipModelInfThisFrame();

            // get neural network model output
            if (KinectInterop.IsSupportsComputeShaders())
            {
                // compute shaders
                int fsIndex = _unicamInt.isSyncBodyAndDepth ? fsData._fsIndex : -1;
                long[] detShape = _sentisManager.GetOutputShape(_objModelName, "output_0");
                ComputeBuffer objDataBuf = _sentisManager.OutputAsComputeBuffer(fsIndex, _objModelName, "output_0");

                // get raw object detections
                CommandBuffer cmd = new CommandBuffer { name = "ProcessObjDetectionsCb" };
                cmd.SetBufferCounterValue(_detOutputBuffer, 0);
                cmd.SetComputeFloatParam(_processShader, "_scoreThreshold", scoreThreshold);
                cmd.SetComputeVectorParam(_processShader, "_lboxScale", _lboxScale);
                cmd.SetComputeBufferParam(_processShader, 0, "_detInput", objDataBuf);
                cmd.SetComputeBufferParam(_processShader, 0, "_detOutput", _detOutputBuffer);
                cmd.SetComputeBufferParam(_processShader, 0, "_detDisabledTypeBuf", _detDisTypeBuffer);
                cmd.DispatchCompute(_processShader, 0, (int)detShape[0] / 50, 1, 1);  // div 16
                cmd.CopyCounterValue(_detOutputBuffer, _detCountBuffer, 0);

                //cmd.RequestAsyncReadback(_detCountBuffer, request =>
                //{
                //    request.GetData<uint>().CopyTo(_rawDetCount);
                //    Debug.Log($"fs: {fsData._fsIndex}, RawDetCount: {_rawDetCount[0]}");
                //});
                //cmd.RequestAsyncReadback(_detOutputBuffer, request =>
                //{
                //    ObjectDetection[] rawObjDetections = new ObjectDetection[MAX_DETECTIONS];
                //    request.GetData<ObjectDetection>().CopyTo(rawObjDetections);

                //    for (int i = 0; i < _rawDetCount[0]; i++)
                //    {
                //        var objDet = rawObjDetections[i];
                //        Debug.Log($"  fs: {fsData._fsIndex}, RawObjDet{i + 1}/{_rawDetCount[0]} {objDet}");
                //    }
                //});

                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();

                // Get nms object detecitons
                CommandBuffer cmd2 = new CommandBuffer { name = "DetectObjBatchedNmsCb" };
                cmd2.SetComputeFloatParam(_processShader, "_iouThreshold", iouThreshold);
                cmd2.SetComputeIntParam(_processShader, "_maxObjCount", maxTrackedObjects);
                cmd2.SetComputeBufferParam(_processShader, 1, "_nmsInputBuf", _detOutputBuffer);
                cmd2.SetComputeBufferParam(_processShader, 1, "_nmsCountBuf", _detCountBuffer);
                cmd2.SetComputeBufferParam(_processShader, 1, "_objdet", _nmsOutputBuffer);
                cmd2.SetComputeBufferParam(_processShader, 1, "_objCountBuf", _nmsCountBuffer);
                cmd2.DispatchCompute(_processShader, 1, 1, 1, 1);

                cmd2.RequestAsyncReadback(_nmsCountBuffer, request =>
                {
                    request.GetData<uint>().CopyTo(_nmsObjCount);
                    odetData._objCount = _nmsObjCount[0];
                    //Debug.Log($"fs: {fsData._fsIndex}, NmsObjCount: {odetData._objCount}");
                });

                cmd2.RequestAsyncReadback(_nmsOutputBuffer, request =>
                {
                    FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();  //?._fsPrev;
                    odetData = GetProviderData(fsd);

                    if (fsd != null && odetData != null)
                    {
                        if (odetData._objDetections == null || odetData._objDetections.Length != _nmsOutputBuffer.count)
                        {
                            odetData._objDetections = new ObjectDetection[_nmsOutputBuffer.count];
                        }

                        request.GetData<ObjectDetection>().CopyTo(odetData._objDetections);

                        //for (int i = 0; i < odetData._objCount; i++)
                        //{
                        //    var objDet = odetData._objDetections[i];
                        //    Debug.Log($"fs: {fsd._fsIndex}, NmsObjDet{i + 1}/{odetData._objCount} {objDet}");
                        //}

                        //UpdateFrameObjectDetections(fsd, odetData, finishFrameOnly: true);
                        fsd._objTrackerTimestamp = fsd._objSourceImageTimestamp;
                        fsd._isObjTrackerFrameReady = true;
                        _lastInfOtFs[fsData._refOtTs] = fsd;

                        _objProcessDispatcher.Enqueue(fsd);
                        //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, RawObjTrackerTime: {fsd._objTrackerTimestamp}, oCount: {odetData._objCount}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                    }
                });

                Graphics.ExecuteCommandBuffer(cmd2);
                cmd2.Release();
            }

            // get object segmentation data, if needed
            if (fsData._objIndexFrame != null)
            {
                //TensorShape masksShape = _sentisManager.GetOutputShape(_objModelName, "output_2");
                ProcessObjectSegmentation(fsData, odetData);
            }

            return true;
        }

        // processes object segmentation data for the frame
        private void ProcessObjectSegmentation(FramesetData fsData, YoloObjProviderData odetData)
        {
            //int objCount = _sentisManager.GetOutputShape(_objModelName, "output_0")[0];

            int fsIndex = _unicamInt.isSyncBodyAndDepth ? fsData._fsIndex : -1;
            if (KinectInterop.IsSupportsComputeShaders())
            {
                int oiImageBufLen = (_depthImageWidth * _depthImageHeight) >> 2;

                // compute shaders
                if (_objIndexBuffer == null || _objIndexBuffer.count != oiImageBufLen)
                {
                    _objIndexBuffer?.Dispose();
                    _objIndexBuffer = new ComputeBuffer(oiImageBufLen, sizeof(uint));
                }

                //// clear object-index buffer
                //_processShader.SetBuffer(3, "_objIndexBuf", _objIndexBuffer);
                //_processShader.Dispatch(3, oiImageBufLen / 64, 1, 1);

                // masks buffer
                ComputeBuffer masksBuffer = _sentisManager.OutputAsComputeBuffer(fsIndex, _objModelName, "output_1");
                if (masksBuffer != null)
                {
                    //if (odetData._objCount > 0)
                    {
                        //ComputeBuffer objDataBuf = _sentisManager.TensorToComputeBuffer(_objModelName, "output_0");

                        // process masks
                        CommandBuffer cmd = new CommandBuffer { name = "ProcessObjMasksCb" };
                        cmd.SetComputeBufferParam(_processShader, 2, "_objdet", _nmsOutputBuffer);
                        cmd.SetComputeBufferParam(_processShader, 2, "_objCountBuf", _nmsCountBuffer);
                        cmd.SetComputeBufferParam(_processShader, 2, "_maskInput", masksBuffer);
                        cmd.SetComputeBufferParam(_processShader, 2, "_masksBuf", _nmsMaskBuffer);
                        cmd.SetComputeIntParam(_processShader, "_maskImgWidth", MASK_IMAGE_SIZE);
                        cmd.SetComputeIntParam(_processShader, "_maskImgHeight", MASK_IMAGE_SIZE);
                        cmd.DispatchCompute(_processShader, 2, MASK_IMAGE_SIZE / 8, MASK_IMAGE_SIZE / 8, 1);

                        //cmd.RequestAsyncReadback(_nmsMaskBuffer, request =>
                        //{
                        //    float[] nmsMaskBuf = new float[maxTrackedObjects * MASK_IMAGE_SIZE * MASK_IMAGE_SIZE];
                        //    request.GetData<float>().CopyTo(nmsMaskBuf);

                        //    for (int i = 0; i < odetData._objCount; i++)
                        //    {
                        //        int i0 = i * MASK_IMAGE_SIZE * MASK_IMAGE_SIZE;
                        //        int i1 = i0 + MASK_IMAGE_SIZE * MASK_IMAGE_SIZE - 1;
                        //        Debug.Log($"fs: {fsData._fsIndex}, Obj{i + 1}/{odetData._objCount} - m0: {nmsMaskBuf[i0]:F3}, m1: {nmsMaskBuf[i0 + 1]:F3}, m2: {nmsMaskBuf[i0 + 2]:F3}\nm-3: {nmsMaskBuf[i1 - 3]:F3}, m-2: {nmsMaskBuf[i1 - 2]:F3}, m-1: {nmsMaskBuf[i1 - 1]:F3}, m0: {nmsMaskBuf[i1]:F3}");
                        //    }
                        //});

                        Graphics.ExecuteCommandBuffer(cmd);
                        cmd.Release();

                        // masks to obj-index buffer
                        Vector2 invScale = new Vector2(1f / _lboxScale.x, 1f / _lboxScale.y);
                        Vector3 depthScale = _depthProvider != null ? _depthProvider.GetDepthImageScale() : Vector3.one;

                        CommandBuffer cmd2 = new CommandBuffer { name = "MasksToObjIndexCb" };
                        cmd2.SetComputeBufferParam(_processShader,4, "_objdet", _nmsOutputBuffer);
                        cmd2.SetComputeBufferParam(_processShader, 4, "_objCountBuf", _nmsCountBuffer);
                        cmd2.SetComputeBufferParam(_processShader, 4, "_masksBuf", _nmsMaskBuffer);
                        cmd2.SetComputeBufferParam(_processShader, 4, "_objIndexBuf", _objIndexBuffer);
                        cmd2.SetComputeVectorParam(_processShader, "_invScale", invScale);
                        cmd2.SetComputeFloatParam(_processShader, "_depthScaleX", depthScale.x);
                        cmd2.SetComputeIntParam(_processShader, "_maskImgWidth", MASK_IMAGE_SIZE);
                        cmd2.SetComputeIntParam(_processShader, "_maskImgHeight", MASK_IMAGE_SIZE);
                        cmd2.SetComputeIntParam(_processShader, "_objIndexImgWidth", _depthImageWidth);
                        cmd2.SetComputeIntParam(_processShader, "_objIndexImgHeight", _depthImageHeight);
                        cmd2.DispatchCompute(_processShader, 4, _depthImageWidth / 8, _depthImageHeight / 8, 1);
                        //Debug.Log($"fs: {fsData._fsIndex}, oiImgWidth: {_depthImageWidth}, oiImgHeight: {_depthImageHeight}, oiBufLen: {_objIndexBuffer.count}\nmasksShape: {MASK_SIZE}, invScale: {invScale:F3}. dScale: {depthScale:F0}");

                        //cmd2.RequestAsyncReadback(objDataBuf, objCount * ObjectDetection.SIZE, 0, request =>
                        //{
                        //    ObjectDetection[] objDetections = new ObjectDetection[objCount];
                        //    request.GetData<ObjectDetection>().CopyTo(objDetections);

                        //    for (int i = 0; i < objCount; i++)
                        //    {
                        //        ObjectDetection obj = objDetections[i];
                        //        Debug.Log($"fs: {fsData._fsIndex}, Obj{i + 1}/{objCount} - {obj}\nrectBox - min: {(obj.center - obj.size * 0.5f):F2}, max: {(obj.center + obj.size * 0.5f):F2}");
                        //    }
                        //});

                        Graphics.ExecuteCommandBuffer(cmd2);
                        cmd2.Release();
                    }

                    if (_objSegmMaterial != null && _objSegmTex != null)
                    {
                        // segmentation mask
                        _objSegmMaterial.SetInt("_MaskTexWidth", MASK_IMAGE_SIZE);
                        _objSegmMaterial.SetInt("_MaskTexHeight", MASK_IMAGE_SIZE);
                        //_objSegmMaterial.SetInt("_MaskTexCount", (int)odetData._objCount);
                        _objSegmMaterial.SetBuffer("_ObjCountBuf", _nmsCountBuffer);
                        _objSegmMaterial.SetBuffer("_MaskBuf", _nmsMaskBuffer);

                        Graphics.Blit(null, _objSegmTex, _objSegmMaterial);
                    }
                }

                //Debug.Log($"fs: {fsData._fsIndex} - OdetIndex2Buffer started\nNow: " + DateTime.Now.ToString("HH:mm:ss.fff"));

                // read-back the object index buffer
                AsyncGPUReadback.Request(_objIndexBuffer, request => {
                    FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();

                    if (fsd != null && fsd._objIndexFrame != null)
                    {
                        if (request.width == fsd._objIndexFrame.Length)
                            request.GetData<byte>().CopyTo(fsd._objIndexFrame);

                        fsd._objIndexTimestamp = fsData._objSourceImageTimestamp;
                        fsd._isObjIndexFrameReady = true;
                        _lastInfOiFs[fsData._refOtTs] = fsd;
                        //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, RawObjIndexTime: {fsd._objIndexTimestamp}\nNow: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                        //Debug.Log($" soiEnabled: {_isScalingEnabled}, soiReady: {fsd._isScaledObjIndexFrameReady}, soiTime: {fsd._scaledObjIndexFrameTimestamp}");

                        // scale object-index image, if needed
                        if (_isScalingEnabled && !fsd._isScaledObjIndexFrameReady &&
                            fsd._scaledObjIndexFrameTimestamp != fsd._objIndexTimestamp)
                        {
                            fsd._scaledObjIndexFrameTimestamp = fsd._objIndexTimestamp;
                            ScaleObjIndexFrame(fsd);
                        }
                    }
                });
            }

        }


        // processes the current object frame
        private void ProcessObjectFrame(KinectInterop.SensorData sensorData, FramesetData fsData)
        {
            // get sensor-to-world matrix
            Matrix4x4 sensorToWorld = _unicamInt.GetSensorToWorldMatrix();
            //Quaternion sensorRotInv = _unicamInt.GetSensorRotationNotZFlipped(true);
            //float scaleX = sensorData.sensorSpaceScale.x;
            float scaleY = sensorData.sensorSpaceScale.y;

            Vector3 depthScale = _depthProvider != null ? _depthProvider.GetDepthImageScale() : Vector3.one;
            //float depthConfidence = _depthProvider != null ? _depthProvider.GetDepthConfidence() : 0f;

            YoloObjProviderData odetData = (YoloObjProviderData)fsData._objProviderSettings;  // GetProviderData(fsData);
            //Debug.Log($"fs: {fsData._fsIndex}, ProcessObjectFrame - oCount: {odetData._objCount}\notReady: {fsData._isObjTrackerFrameReady}, otTime: {fsData._objTrackerTimestamp}");

            if (sensorData.depthCamIntr != null && odetData != null)
            {
                // update byte tracker
                trackers.STrack[] detObjects = new trackers.STrack[odetData._objCount];
                for(int i = 0; i < odetData._objCount; i++)
                {
                    ObjectDetection objDetection = odetData._objDetections[i];

                    trackers.RectBox objRect = new trackers.RectBox(objDetection.center.x, objDetection.center.y, objDetection.size.x, objDetection.size.y);
                    detObjects[i] = new trackers.STrack(objRect, objDetection.score);
                    detObjects[i]._objType = objDetection.objType;
                }

                // update byte tracker
                var idToTrack = _byteTracker.Update(fsData, detObjects, fsData._objTrackerTimestamp);

                // update data of all objects
                for (int i = 0; i < odetData._objCount; i++)
                {
                    Vector3 vObjSpacePos = GetSpacePosForNormDepthPixel(sensorData, fsData, i, odetData._objDetections[i].center, depthScale, getDepthOnly: true);
                    float objDepth = vObjSpacePos.z; // estDepth > 0f ? depthConfidence * provDepth + (1f - depthConfidence) * estDepth : provDepth;
                    //Debug.Log($"fs: {fsData._fsIndex}, Obj{i} - spacePos: {vObjSpacePos:F2}");

                    // update object tracking data
                    _objectTracker.UpdateObjectData(i, detObjects[i], ref odetData._objDetections[i], objDepth, (long)fsData._objTrackerTimestamp, idToTrack);
                }

                // remove expired objects
                //_objectTracker.RemoveExpiredObjects(removedIds);
            }

            // get tracked objects
            //List<YoloObjectTracker.ObjTrackingData> trackedObjs = _objectTracker.GetTrackedObjs((long)fsData._objTrackerTimestamp);
            List<trackers.STrack> trackedObjs = _byteTracker.AllTracks;  // _byteTracker.TrackedTracks; // 
            fsData._trackedObjCount = (uint)trackedObjs.Count;

            // create the needed slots
            while (fsData._alTrackedObjects.Count < fsData._trackedObjCount)
            {
                fsData._alTrackedObjects.Add(new KinectInterop.ObjectData());
            }

            for (int i = 0; i < fsData._trackedObjCount; i++)
            {
                YoloObjectTracker.ObjTrackingData trackedObj = (YoloObjectTracker.ObjTrackingData)trackedObjs[i]._dataObj;  // trackedObjs[i];
                KinectInterop.ObjectData objData = fsData._alTrackedObjects[i];

                if(trackedObj != null)
                {
                    objData.trackingID = trackedObj.objId;
                    objData.objIndex = trackedObj.objIndex;
                    objData.objType = trackedObj.objType;

                    // set object position
                    objData.center2d = trackedObj.objImagePos;
                    objData.size2d = trackedObj.objImageSize;

                    objData.score = trackedObj.objScore;
                    objData.isTracked = trackedObjs[i].IsActivated;  // && trackedObjs[i].State == trackers.TrackState.Tracked;  // true;
                    objData.lastTimestamp = fsData._objTrackerTimestamp;

                    // unproject image position
                    float dx = (depthScale.x >= 0f ? trackedObj.objImagePos.x : (1f - trackedObj.objImagePos.x)) * _depthImageWidth;
                    float dy = (depthScale.y >= 0f ? trackedObj.objImagePos.y : (1f - trackedObj.objImagePos.y)) * _depthImageHeight;

                    Vector3 objSpacePos = _depthProvider.UnprojectPoint(sensorData.depthCamIntr, new Vector2(dx, dy), trackedObj.objDepth);
                    objData.position = sensorToWorld.MultiplyPoint3x4(objSpacePos);

                    objSpacePos.y *= scaleY;
                    objData.kinectPos = objSpacePos;
                }
                else
                {
                    // consider as not tracked
                    objData.isTracked = false;
                }

                fsData._alTrackedObjects[i] = objData;
                //Debug.Log($"Obj {i}/{fsData._trackedObjCount} {trackedObj.objType} - id: {trackedObj.objId}, oi: {trackedObj.objIndex}, score: {trackedObj.objScore:F3}\ntracked: {objData.isTracked}, depthScale: {depthScale:F0}, scaleY: {scaleY}");
            }

            // save object data on the main thread
            fsData._objDataTimestamp = fsData._objTrackerTimestamp + fsData._trackedObjCount;
            _objSaveDispatcher.Enqueue(fsData);
            //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsData._fsIndex}, ProcessedObjData time: {fsData._objDataTimestamp}, oCount: {fsData._trackedObjCount}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
        }

        // save processed body data to the frame (executed on the main thread)
        private void SaveFrameObjectData(FramesetData fsData)
        {
            FramesetData fsd = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();

            fsd._trackedObjCount = fsData._trackedObjCount;
            fsd._alTrackedObjects = fsData._alTrackedObjects;

            // object frame is ready
            fsd._objDataTimestamp = fsData._objTrackerTimestamp + fsData._trackedObjCount;
            fsd._isObjDataFrameReady = true;
            _lastInfOtFs[fsData._refOtTs] = fsd;
            //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, RawObjDataTime: {fsd._objDataTimestamp}, oCount: {fsd._trackedObjCount}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff") + $"\nfsData: {fsData._fsIndex}, fsTime: {fsData._bodyDataTimestamp}");
        }

        // gets the space position for the specified normalized depth image coordinates.
        private Vector3 GetSpacePosForNormDepthPixel(KinectInterop.SensorData sensorData, FramesetData fsData, int oi,
            Vector2 normDepthPos, Vector3 depthScale, bool getDepthOnly = false)
        {
            if(_depthProvider == null)
                return Vector3.zero;

            int dx = Mathf.Clamp(Mathf.RoundToInt((depthScale.x >= 0f ? normDepthPos.x : (1f - normDepthPos.x)) * _depthImageWidth), 0, _depthImageWidth - 1);
            int dy = Mathf.Clamp(Mathf.RoundToInt(/**(depthScale.y >= 0f ?*/ normDepthPos.y /**: (1f - normDepthPos.y))*/ * _depthImageHeight), 0, _depthImageHeight - 1);

            int di = dx + dy * _depthImageWidth;
            ushort depth = fsData != null && fsData._depthFrame != null && di < fsData._depthFrame.Length ? fsData._depthFrame[di] : (ushort)0;

            Vector2 imagePos = new Vector2(dx, dy);
            Vector3 spacePos = !getDepthOnly ? _depthProvider.UnprojectPoint(sensorData.depthCamIntr, imagePos, depth * 0.001f) : Vector3.zero;

            if (spacePos == Vector3.zero)
                spacePos = new Vector3(0f, 0f, depth * 0.001f);
            //Debug.Log($"fs: {fsData._fsIndex}, oi: {oi}, depth: {depth * 0.001f}\npos: {normDepthPos:F2} - {imagePos:F0} - {spacePos:F2}");

            return spacePos;
        }


    }
}
