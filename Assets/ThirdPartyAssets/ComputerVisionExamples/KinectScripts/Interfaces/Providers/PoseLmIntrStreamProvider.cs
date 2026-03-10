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
    /// <summary>
    /// PoseLM cam-intr stream provider.
    /// </summary>
    public class PoseLmIntrStreamProvider : MonoBehaviour, ICamIntrStreamProvider
    {
        [Tooltip("Landmark score threshold")]
        [Range(0, 1.0f)]
        public float landmarkScoreThreshold = 0.5f;  // 0.6f;  // 

        [Tooltip("Joint tracked threshold")]
        [Range(0, 1.0f)]
        //[HideInInspector]
        internal float jointTrackedThreshold = 0.5f;  // 0.6f;  // 

        [Tooltip("Raw image for debugging purposes.")]
        public UnityEngine.UI.RawImage _debugImage = null;

        [Tooltip("Raw image 2 for debugging purposes.")]
        public UnityEngine.UI.RawImage _debugImage2 = null;


        // constants
        public const int MAX_BODIES = 1;
        public const int BODY_LM_COUNT = 39;
        public const int SEGMENTATION_SIZE = 256;


        // references
        private UniCamInterface _unicamInt;
        private IDeviceManager _deviceManager;
        private KinectManager _kinectManager;
        //private UnityEngine.UI.RawImage _debugImage;

        // stream provider properties
        private string _providerId = null;
        private StreamState _streamState = StreamState.NotStarted;

        // model input
        private string _inputNameLandmark;
        private int _inputWidthLandmark = 0;
        private int _inputHeightLandmark = 0;
        private int _inputChannelsLandmark = 0;

        // reference to SM
        private SentisManager _sentisManager = null;
        private RuntimeModel _modelLandmark = null;

        private const string _lmodelName = "ilm";

        // provider state
        private enum ProviderState : int { NotStarted, LandmarkBody, ProcessLandmarks, Ready }
        //private ProviderState _providerState = ProviderState.NotStarted;

        // source texture
        private RenderTexture _btSourceImage;
        private ulong _btSourceTimestamp = 0;
        private int _btSrcFrameIndex = -1;
        private FramesetData _btSrcFrame = null;

        // letterbox scale and pad
        private Vector2 _lboxScale = Vector2.one;
        private Vector2 _lboxPad = Vector2.zero;

        // shader for data processing
        private ComputeShader _texToTensorShader;
        private ComputeShader _processShader;

        // region buffers
        private ComputeBuffer _poseRegionBuffer;
        private ComputeBuffer _regionCountBuffer;

        private uint _poseCount = 0;
        private PoseRegion[] _poseRegions;

        private Vector4[][] _poseLandmarks;
        private Vector4[][] _poseWorldLandmarks;
        private Vector4[][] _poseKinectLandmarks;

        // landmark buffers
        private ComputeBuffer _rawLandmarks2dBuf;
        private ComputeBuffer _rawLandmarks3dBuf;

        private ComputeBuffer _landmarks2dBuf;
        private ComputeBuffer _landmarks3dBuf;
        private ComputeBuffer _landmarksKinBuf;

        // body-index image buffer
        public ComputeBuffer _biImageBuffer;
        // mask material and texture
        private Material _segmMaskMaterial;
        private RenderTexture _segmMaskTex;

        // depth image properties
        private IColorStreamProvider _colorProvider = null;
        private IDepthStreamProvider _depthProvider = null;
        private int _depthImageWidth = 0;
        private int _depthImageHeight = 0;

        private KinectInterop.SensorData _sensorData = null;

        // detection textures
        private RenderTexture _landmarkTex;
        //private Material _landmarkMat;
        //private Tensor<float> _lmTexTensor;
        //private TextureTransform _lmTexTrans;

        // ready flags & timestamps
        private bool _isBodyTrackerFrameReady;
        private ulong _bodyTrackerTimestamp;
        private ProviderState _bodyProviderState;


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
            if (_colorProvider == null)
                throw new Exception("Source stream is not available. Please enable the color stream.");

            // get depth image size
            _depthProvider = unicamInt._depthStreamProvider;
            if (_depthProvider == null)
                throw new Exception("Depth stream provider is not available.");

            var depthImageSize = _depthProvider.GetDepthImageSize();
            _depthImageWidth = depthImageSize.x;
            _depthImageHeight = depthImageSize.y;

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

            // load lm model
            string blmModelName = "blm_1.1";
            _modelLandmark = _sentisManager.AddModel(_lmodelName, blmModelName);
            if (_modelLandmark == null)
            {
                // model not found
                return null;
            }

            _inputNameLandmark = _modelLandmark.inputNames[0];
            var inShapeLandmark = _modelLandmark.inputShapes[_inputNameLandmark];

            _inputWidthLandmark = (int)inShapeLandmark[2];
            _inputHeightLandmark = (int)inShapeLandmark[1];
            _inputChannelsLandmark = (int)inShapeLandmark[3];
            //Debug.Log($"BLm model loaded - iW: {_inputWidthLandmark}, iH: {_inputHeightLandmark}, iC: {_inputChannelsLandmark}, name: {_inputNameLandmark}, dt: {DateTime.Now.ToString("HH:mm:ss.fff")}");

            // init textures
            _landmarkTex = KinectInterop.CreateRenderTexture(_landmarkTex, _inputWidthLandmark, _inputHeightLandmark, RenderTextureFormat.ARGB32);

            // init shaders
            //Shader lmarkTexShader = Shader.Find(KinectInterop.IsSupportsComputeShaders() ? "Kinect/CropTexShader" : "Kinect/CropTexMatShader");
            //_landmarkMat = new Material(lmarkTexShader);
            _texToTensorShader = Resources.Load("TexToTensorShader") as ComputeShader;

            //// transforms
            //_lmTexTrans = new TextureTransform().SetDimensions(_inputWidthLandmark, _inputHeightLandmark, _inputChannelsLandmark).SetTensorLayout(TensorLayout.NHWC);
            //_lmTexTensor = new Tensor<float>(new TensorShape(1, _inputHeightLandmark, _inputWidthLandmark, _inputChannelsLandmark));

            if (KinectInterop.IsSupportsComputeShaders())
            {
                // processing shader
                _processShader = Resources.Load("PoseLmCompute") as ComputeShader;
            }

            // init pose-region buffer
            _poseCount = 0;
            _poseRegions = new PoseRegion[MAX_BODIES];

            for (int i = 0; i < MAX_BODIES; i++)
            {
                _poseRegions[i] = new PoseRegion()
                {
                    box = new Vector4(0.5f, 0.5f, 1f, 0f),
                    dBox = Vector4.zero,

                    size = new Vector4(0.5f, 0.5f, 1f, 1f),
                    par = Vector4.zero,  // new Vector4(0f, i == 0 ? 1 : 0, 1, 0),

                    cropMatrix = Matrix4x4.identity,
                    invMatrix = Matrix4x4.identity
                };
            }

            _poseLandmarks = new Vector4[MAX_BODIES][];
            _poseWorldLandmarks = new Vector4[MAX_BODIES][];
            _poseKinectLandmarks = new Vector4[MAX_BODIES][];

            for (int i = 0; i < MAX_BODIES; i++)
            {
                _poseLandmarks[i] = new Vector4[BODY_LM_COUNT + 1];
                _poseWorldLandmarks[i] = new Vector4[BODY_LM_COUNT + 1];
                _poseKinectLandmarks[i] = new Vector4[BODY_LM_COUNT + 1];
            }

            if (KinectInterop.IsSupportsComputeShaders())
            {
                // compute shaders and buffers
                _rawLandmarks2dBuf = new ComputeBuffer(BODY_LM_COUNT + 1, sizeof(float) * 4);  // new ComputeBuffer[MAX_BODIES];
                _rawLandmarks3dBuf = new ComputeBuffer(BODY_LM_COUNT + 1, sizeof(float) * 4);  // new ComputeBuffer[MAX_BODIES];

                _landmarks2dBuf = new ComputeBuffer(BODY_LM_COUNT + 1, sizeof(float) * 4);  // new ComputeBuffer[MAX_BODIES];
                _landmarks3dBuf = new ComputeBuffer(BODY_LM_COUNT + 1, sizeof(float) * 4);  // new ComputeBuffer[MAX_BODIES];
                _landmarksKinBuf = new ComputeBuffer(BODY_LM_COUNT + 1, sizeof(float) * 4);  // new ComputeBuffer[MAX_BODIES];
                //_smoothLandmarks3dBuf = new ComputeBuffer[MAX_BODIES];

                _landmarks2dBuf.SetData(_poseLandmarks[0]);
                _landmarks3dBuf.SetData(_poseWorldLandmarks[0]);
                _landmarksKinBuf.SetData(_poseKinectLandmarks[0]);

                _poseRegionBuffer = new ComputeBuffer(MAX_BODIES, PoseRegion.SIZE);  // 24
                _regionCountBuffer = new ComputeBuffer(1, sizeof(uint));

                // init pose-region buffer
                _poseRegionBuffer.SetData(_poseRegions);

                // init region-count buffer
                uint[] _regionCount = new uint[1];
                _regionCount[0] = 1;  // count
                _regionCountBuffer.SetData(_regionCount);
            }

            _providerId = deviceManager != null ? deviceManager.GetDeviceId() + "_cis" : "poselm_cis";
            _streamState = StreamState.Working;

            if (_debugImage2 != null)
            {
                Shader segmMashShader = Shader.Find("Kinect/SegmMaskShader");
                _segmMaskMaterial = new Material(segmMashShader);
                _segmMaskTex = KinectInterop.CreateRenderTexture(_segmMaskTex, SEGMENTATION_SIZE, SEGMENTATION_SIZE);

                _debugImage2.texture = _segmMaskTex;  // _detectionTex;  // _landmarkTex;  // _segmMaskTex;  // _sourceTex;  // 
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
            //_sentisManager.DisableModel(_lmodelName);

            _btSourceImage?.Release();
            _btSourceImage = null;

            //_lmTexTensor?.Dispose();
            //_lmTexTensor = null;

            _landmarkTex?.Release();
            _landmarkTex = null;

            _poseLandmarks = null;
            _poseWorldLandmarks = null;
            _poseKinectLandmarks = null;

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

                _poseRegionBuffer?.Dispose();
                _poseRegionBuffer = null;

                _regionCountBuffer?.Dispose();
                _regionCountBuffer = null;
            }

            _segmMaskTex?.Release();
            _segmMaskTex = null;
            _segmMaskMaterial = null;

            _modelLandmark = null;
            _streamState = StreamState.Stopped;
        }

        /// <summary>
        /// Releases stream resources allocated in the frameset.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        public void ReleaseFrameData(FramesetData fsData)
        {
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
            // do nothing
        }

        /// <summary>
        /// Updates the stream data in the main thread.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="fsData">Frameset data</param>
        public void UpdateStreamData(KinectInterop.SensorData sensorData, FramesetData fsData)
        {
            // get the correct frame
            fsData = _unicamInt.isSyncBodyAndDepth ? fsData : _unicamInt.GetLastOpenFrameset();  //?._fsPrev;

            if (!fsData._isBodyTrackerFrameReady)  // || (fsData._bodyIndexFrame != null && !fsData._isBodyIndexFrameReady))
            {
                ProcessSourceImage(fsData);
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
            return true;
        }

        // processes the source image to detect bodies
        private void ProcessSourceImage(FramesetData fsData)
        {
            if (_sentisManager == null || _modelLandmark == null)
                return;

            if (_bodyProviderState == 0)
            {
                _bodyProviderState = ProviderState.LandmarkBody;
                _btSrcFrameIndex = _unicamInt.isSyncBodyAndDepth ? fsData._fsIndex : -1;
            }

            //int fsIndex = _unicamInt.isSyncBodyAndDepth ? fsData._fsIndex : -1;  // fsData._fsIndex;  // 
            //Debug.Log($"  CI: fs: {fsData._fsIndex} - {_bodyProviderState} - frame: {_btSrcFrameIndex}, pCount: {_poseCount}");

            switch (_bodyProviderState)
            {
                case ProviderState.LandmarkBody:
                    if (!_sentisManager.IsModelStarted(_btSrcFrameIndex, _lmodelName))
                    {
                        if(DetectBodyLandmarks(fsData, 0))
                        {
                            _bodyProviderState = ProviderState.ProcessLandmarks;
                        }
                    }
                    break;

                case ProviderState.ProcessLandmarks:
                    // process detected landmarks
                    if (_sentisManager.IsModelReady(_btSrcFrameIndex, _lmodelName))
                    {
                        if(ProcessDetectedLandmarks(_btSrcFrame, 0))
                        {
                            _sentisManager.ClearModelReady(_btSrcFrameIndex, _lmodelName);

                            if (_bodyProviderState == ProviderState.ProcessLandmarks)
                            {
                                _bodyProviderState = ProviderState.Ready;
                            }
                        }
                    }
                    break;
            }
        }

        // initializes body detection from the source image
        private bool InitBodyDetection(FramesetData fsData)
        {
            // check depth image size
            Vector2Int depthImageSize = _depthProvider.GetDepthImageSize();

            if (_depthImageWidth != depthImageSize.x || _depthImageHeight != depthImageSize.y)
            {
                _depthImageWidth = depthImageSize.x;
                _depthImageHeight = depthImageSize.y;
            }

            // source frame index
            _btSrcFrameIndex = _unicamInt.isSyncBodyAndDepth ? fsData._fsIndex : -1;
            _btSrcFrame = fsData;

            // get source image time
            Texture srcImage = null;
            ulong imageTime = _colorProvider != null ? _btSrcFrame._colorImageTimestamp : 0L;

            //if (imageTime != 0 && _btSourceTimestamp != imageTime)
            {
                // get source image
                srcImage = _colorProvider != null ? _colorProvider.GetBTSourceImage(_btSrcFrame) : null;
                if (srcImage == null || imageTime == 0 || imageTime != fsData._rawFrameTimestamp)
                    return false;

                // letterboxing scale factor
                _lboxScale = new Vector2(Mathf.Max((float)srcImage.height / srcImage.width, 1f), Mathf.Max((float)srcImage.width / srcImage.height, 1f));
                _lboxPad = new Vector2((1f - 1f / _lboxScale.x) * 0.5f, (1f - 1f / _lboxScale.y) * 0.5f);
                //Debug.Log($"LboxScale: {_lboxScale:F3}, pad: {_lboxPad:F3}, srcW: {srcImage.width}, srcH: {srcImage.height}");

                if (_btSourceImage == null || _btSourceImage.width != srcImage.width || _btSourceImage.height != srcImage.height)
                {
                    _btSourceImage = KinectInterop.CreateRenderTexture((RenderTexture)_btSourceImage, srcImage.width, srcImage.height, RenderTextureFormat.ARGB32);
                }

                //Graphics.Blit(srcImage, _btSourceImage);

                CommandBuffer cmd = new CommandBuffer { name = "StoreSrcImageCb" };
                cmd.Blit(srcImage, _btSourceImage);

                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();
                //Debug.Log($"fs: {fsData._fsIndex}, Blit(_btSourceImage), frame: {kinect.UniCamInterface._frameIndex}");

                _btSourceTimestamp = imageTime;
            }

            // check region scaling
            int maxWH = Mathf.Max(srcImage.width, srcImage.height);
            float sizeX = (float)srcImage.width / maxWH;
            float sizeY = (float)srcImage.height / maxWH;

            if(_poseRegions[0].size.x != sizeX || _poseRegions[0].size.y != sizeY)
            {
                PoseRegion poseRegion = _poseRegions[0];

                float minSize = Mathf.Min(sizeX, sizeY);
                poseRegion.size = new Vector4(sizeX, sizeY, minSize, minSize);
                poseRegion.box.z = minSize;

                Vector3 cropT = new Vector3(poseRegion.box.x - poseRegion.box.z * 0.5f, poseRegion.box.y - poseRegion.box.z * 0.5f, 0f);
                Vector3 cropS = new Vector3(poseRegion.box.z, poseRegion.box.z, 1f);
                poseRegion.cropMatrix.SetTRS(cropT, Quaternion.identity, cropS);

                Vector3 invT = -cropT / poseRegion.box.z;
                Vector3 invS = new Vector3(1f / poseRegion.box.z, 1f / poseRegion.box.z, 1f);
                poseRegion.invMatrix.SetTRS(invT, Quaternion.identity, invS);

                _poseRegions[0] = poseRegion;
                if (KinectInterop.IsSupportsComputeShaders())
                {
                    _poseRegionBuffer.SetData(_poseRegions);
                }

                //Debug.Log($"UpdPoseReg{0}: {poseRegion}\nsize: {poseRegion.size:F2}, par: {poseRegion.par:F3}\n" +  // );  //
                //    $"CropMat T: {poseRegion.cropMatrix.GetColumn(3):F2}, R: {poseRegion.cropMatrix.rotation.eulerAngles:F0}, S: {poseRegion.cropMatrix.lossyScale:F2}\n" +
                //    $"InvMat T:{poseRegion.invMatrix.GetColumn(3):F2}, R: {poseRegion.invMatrix.rotation.eulerAngles:F0}, S: {poseRegion.invMatrix.lossyScale:F2}");
            }

            //if (_debugImage != null && _debugImage.texture == null)
            //{
            //    _debugImage.texture = fsData._btSourceImage;

            //    float debugH = _debugImage.rectTransform.sizeDelta.y;
            //    _debugImage.rectTransform.sizeDelta = new Vector2(debugH * fsData._btSourceImage.width / fsData._btSourceImage.height, debugH);
            //}

            return true;
        }

        // detects body landmarks in source image
        private bool DetectBodyLandmarks(FramesetData fsData, uint bi)
        {
            // init body detection
            if (!InitBodyDetection(fsData))
                return false;

            // crop the region from the letterbox image
            //_landmarkMat.SetInt("_poseIndex", (int)bi);
            //_landmarkMat.SetVector("_lboxScale", _lboxScale);
            //_landmarkMat.SetInt("_cropTexWidth", _inputWidthLandmark);
            //_landmarkMat.SetInt("_cropTexHeight", _inputHeightLandmark);
            //_landmarkMat.SetInt("_isLinearColorSpace", QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);

            //if (KinectInterop.IsSupportsComputeShaders())
            //{
            //    _landmarkMat.SetBuffer("_cropRegion", _poseRegionBuffer);  // _poseRegionBuffer);
            //}

            //Graphics.Blit(_btSourceImage, _landmarkTex, _landmarkMat);

            CommandBuffer cmd = new CommandBuffer { name = "_BodyImageToTexCb" };
            cmd.SetComputeIntParam(_texToTensorShader, "_poseIndex", (int)bi);
            cmd.SetComputeIntParam(_texToTensorShader, "_cropTexWidth", _inputWidthLandmark);
            cmd.SetComputeIntParam(_texToTensorShader, "_cropTexHeight", _inputHeightLandmark);
            cmd.SetComputeVectorParam(_texToTensorShader, "_lboxScale", _lboxScale);
            cmd.SetComputeIntParam(_texToTensorShader, "_isLinearColorSpace", QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);
            cmd.SetComputeTextureParam(_texToTensorShader, 4, "_sourceTex", _btSourceImage);
            cmd.SetComputeBufferParam(_texToTensorShader, 4, "_cropRegion", _poseRegionBuffer);
            cmd.SetComputeTextureParam(_texToTensorShader, 4, "_cropTexture", _landmarkTex);
            cmd.DispatchCompute(_texToTensorShader, 4, _landmarkTex.width / 8, _landmarkTex.height / 8, 1);

            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();

            if (_debugImage != null)  // && _debugImage.texture == null)
            {
                _debugImage.texture = _landmarkTex;

                float debugH = _debugImage.rectTransform.sizeDelta.y;
                _debugImage.rectTransform.sizeDelta = new Vector2(debugH * _landmarkTex.width / _landmarkTex.height, debugH);
            }

            // start model inference
            //int fsIndex = _unicamInt.isSyncBodyAndDepth ? fsData._fsIndex : -1;  // fsData._fsIndex;  // 
            bool lmModelStarted = _sentisManager.StartInference(_btSrcFrameIndex, _lmodelName, _inputNameLandmark, _landmarkTex);

            //if (lmModelStarted)
            //    Debug.LogWarning($"fs: {fsData._fsIndex}, body {bi} cropped and model {_lmodelName} inference scheduled.");

            return lmModelStarted;
        }

        // processes detected body landmarks
        private bool ProcessDetectedLandmarks(FramesetData fsData, uint bi)
        {
            if (KinectInterop.IsSupportsComputeShaders())
            {
                // compute shaders
                ComputeBuffer depthMapBuf = (ComputeBuffer)_depthProvider.GetDepthBuffer(_btSrcFrame, out bool isDepthPermanent);
                Vector2Int depthImageSize = _depthProvider.GetDepthImageSize();
                KinectInterop.CameraIntrinsics camIntr = _sensorData.depthCamIntr;

                if (depthMapBuf == null || !_btSrcFrame._isDepthFrameReady)
                {
                    //Debug.Log($"DepthMapBuf not available, fs: {_btSrcFrame._fsIndex}\nbuf: {depthMapBuf}, avl: {_btSrcFrame._isDepthFrameReady}");
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

                //var heatmapBuffer = _sentisManager.OutputAsComputeBuffer(fsIndex, _lmodelName, "Identity_3");
                //var heatmapShape = _sentisManager.PeekOutputShape(fsIndex, _lmodelName, "Identity_3");

                _processShader.SetInt("_landmarkCount", BODY_LM_COUNT);
                _processShader.SetInt("_poseIndex", (int)bi);
                _processShader.SetVector("_lboxScale", _lboxScale);

                _processShader.SetBuffer(5, "_poseFlag", poseFlagBuffer);
                _processShader.SetBuffer(5, "_landmarkInput", landmarkBuffer);
                _processShader.SetBuffer(5, "_landmarkWorldInput", worldLandmarkBuffer);

                _processShader.SetBuffer(5, "_depthMapBuf", depthMapBuf);
                _processShader.SetBuffer(5, "_poseRegions", _poseRegionBuffer);  // _poseRegionBuffer);

                _processShader.SetVector("_intrPpt", camIntr != null ? new Vector2(camIntr.ppx, camIntr.ppy) : Vector2.zero);
                _processShader.SetVector("_intrFlen", camIntr != null ? new Vector2(camIntr.fx, camIntr.fy) : Vector2.zero);
                _processShader.SetInt("_depthMapWidth", depthImageSize.x);  // _sensorData.depthImageWidth);
                _processShader.SetInt("_depthMapHeight", depthImageSize.y);  // _sensorData.depthImageHeight);

                //_processShader.SetBuffer(5, "_Heatmap", heatmapBuffer);
                //_processShader.SetInt("_HmHeight", heatmapShape[1]);
                //_processShader.SetInt("_HmWidth", heatmapShape[2]);
                //_processShader.SetInt("_KernelSize", 9);
                //_processShader.SetFloat("_MinConfidence", 0.5f);

                //_processShader.SetBuffer(5, "_OutLandmark", _rawLandmarks2dBuf);
                //_processShader.SetBuffer(5, "_OutLandmarkWorld", _rawLandmarks3dBuf);
                _processShader.SetBuffer(5, "_landmarkOutput", _landmarks2dBuf);
                _processShader.SetBuffer(5, "_landmarkWorldOutput", _landmarks3dBuf);
                _processShader.SetBuffer(5, "_landmarkSensorOutput", _landmarksKinBuf);

                _processShader.SetInt("_segmInputWidth", SEGMENTATION_SIZE);
                _processShader.SetInt("_segmInputHeight", SEGMENTATION_SIZE);
                _processShader.SetBuffer(5, "_segmInputBuffer", lmSegmBuffer);

                _processShader.Dispatch(5, 1, 1, 1);

                if (!isDepthPermanent)
                {
                    depthMapBuf.Dispose();
                }

                if (_segmMaskMaterial != null && _segmMaskTex != null)
                {
                    // segmentation mask
                    _segmMaskMaterial.SetInt("_SegmTexWidth", SEGMENTATION_SIZE);
                    _segmMaskMaterial.SetInt("_SegmTexHeight", SEGMENTATION_SIZE);
                    _segmMaskMaterial.SetBuffer("_SegmBuf", lmSegmBuffer);

                    _segmMaskMaterial.SetInt("_LmCount", BODY_LM_COUNT);
                    _segmMaskMaterial.SetBuffer("_LmBuffer", landmarkBuffer);  // _lmLandmarkBuffers[bi]

                    Graphics.Blit(null, _segmMaskTex, _segmMaskMaterial);
                }

                // start over
                _bodyProviderState = ProviderState.NotStarted;
                //Debug.Log($"fs: {fsData._fsIndex} - BlmOut2Buffer started, bi: {bi}\nNow: " + DateTime.Now.ToString("HH:mm:ss.fff"));

                // save results
                AsyncGPUReadback.Request(_landmarks2dBuf, request =>
                {
                    if (_poseLandmarks != null && _poseLandmarks[bi] != null)
                    {
                        request.GetData<Vector4>().CopyTo(_poseLandmarks[bi]);
                        //Debug.Log($"  fs: {fsData._fsIndex}, PoseLm2D-{bi} rect: {_poseLandmarks[bi][BODY_LM_COUNT]:F2}, ts: {_btSourceTimestamp}\n{string.Join("\n  ", _poseLandmarks[bi].Select((item, index) => index.ToString() + "-" + item.ToString()))}");
                    }
                });
                //AsyncGPUReadback.Request(_landmarksKinBuf, request =>
                //{
                //    if (_poseKinectLandmarks != null && _poseKinectLandmarks[bi] != null)
                //    {
                //        request.GetData<Vector4>().CopyTo(_poseKinectLandmarks[bi]);
                //        Debug.Log($"  fs: {fsData._fsIndex}, KinectLm3D-{bi} ts: {_btSourceTimestamp}\n{string.Join("\n  ", _poseKinectLandmarks[bi].Select((item, index) => index.ToString() + "-" + item.ToString()))}");
                //    }
                //});
                AsyncGPUReadback.Request(_landmarks3dBuf, request =>
                {
                    if (_poseWorldLandmarks != null && _poseWorldLandmarks[bi] != null)
                    {
                        request.GetData<Vector4>().CopyTo(_poseWorldLandmarks[bi]);
                        //Debug.Log($"  fs: {fsData._fsIndex}, PoseLm3D-{bi} score: {_poseWorldLandmarks[bi][BODY_LM_COUNT].x:F3}, ts: {_btSourceTimestamp}\n{string.Join("\n  ", _poseWorldLandmarks[bi].Select((item, index) => index.ToString() + "-" + item.ToString()))}");

                        //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsData._fsIndex}, BlmOut2Buffer finished, bi {bi}:\nTime: {_btSourceTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                        _poseCount = (uint)(_poseWorldLandmarks[bi][BODY_LM_COUNT].x >= landmarkScoreThreshold ? 1 : 0);

                        _bodyTrackerTimestamp = _btSourceTimestamp;
                        _isBodyTrackerFrameReady = true;
                        //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsData._fsIndex}, RawBodyTrackerTime: {_bodyTrackerTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                    }
                });

            }

            return true;
        }


        // joint for fl-estimation (shoulders & hips)
        private static readonly int[] FL_JOINT_INDEX = { 12, 23, 24, 11, 24, 27, 28, 23, 8, 13, 14, 7 };  // ls, rh, lh, rs; lh, ra, la, rh, lear, relb, lelb, rear
        private const float MAX_PROJ_RMSE = 0.05f;
        private const float MIN_SHOULDER_LEN = 0.15f;

        /// <summary>
        /// Tries to check the camera intrinsics on the detected raw body data.
        /// </summary>
        /// <param name="cameraRes">Camera resolution (color or depth)</param>
        /// <param name="sensorData">Sensor data</param>
        /// <returns>true if the camera intrinsics seem right, false otherwise</returns>
        public bool TryCheckCamIntr(DepthSensorBase.PointCloudResolution cameraRes, KinectInterop.SensorData sensorData, FramesetData fsData)
        {
            if (!_isBodyTrackerFrameReady)
                return false;

            if (cameraRes == DepthSensorBase.PointCloudResolution.DepthCameraResolution && sensorData.depthCamIntr == null ||
                cameraRes == DepthSensorBase.PointCloudResolution.ColorCameraResolution && sensorData.colorCamIntr == null)
            {
                return false;
            }

            int flJointsCount = FL_JOINT_INDEX.Length;
            float projError = 0f;
            int projCount = 0;

            for (int i = 0; i < _poseCount; i++)
            {
                Vector4[] poseLandmarks = _poseLandmarks[i];
                Vector4[] poseWorldLandmarks = _poseWorldLandmarks[i];

                if (poseWorldLandmarks[BODY_LM_COUNT].x < landmarkScoreThreshold)
                    continue;

                // get hip center position
                Vector3 depthScale = _depthProvider != null ? _depthProvider.GetDepthImageScale() : Vector3.one;
                Vector2 vHipImagePos = poseLandmarks[33];  // (poseLandmarks[24] + poseLandmarks[23]) * 0.5f;
                Vector3 vHipSpacePos = GetSpacePosForNormDepthPixel(sensorData, fsData, vHipImagePos, depthScale);
                Vector3 vHipSensorPos = new Vector3(vHipSpacePos.x * depthScale.x, vHipSpacePos.y, vHipSpacePos.z);

                for (int j = 0; j < flJointsCount; j++)
                {
                    int ji = FL_JOINT_INDEX[j];

                    Vector4 jointW = poseWorldLandmarks[ji];
                    if (jointW.w < jointTrackedThreshold)  // jointInferredThreshold
                        continue;

                    Vector3 jointPos = vHipSensorPos + (Vector3)jointW;
                    Vector2 jointUV = poseLandmarks[ji];

                    KinectInterop.CameraIntrinsics intr = cameraRes == DepthSensorBase.PointCloudResolution.ColorCameraResolution ?
                        sensorData.colorCamIntr : sensorData.depthCamIntr;

                    Vector2 projUV = ProjectPoint(intr, jointPos);
                    projError += Vector2.SqrMagnitude(projUV - jointUV);
                    projCount++;
                }
            }

            if (projCount > 0)
            {
                projError /= projCount;
                projError = Mathf.Sqrt(projError);
            }

            //Debug.Log($"fs: {fsData._fsIndex}, Check {cameraRes} intr\nrmse: {projError:F3}, max: {MAX_PROJ_RMSE}, count: {projCount}, poses: {plmData._poseCount}");
            return (projError < MAX_PROJ_RMSE);
        }

        // projects a space point on plane using the camera intrinsics
        private Vector2 ProjectPoint(KinectInterop.CameraIntrinsics intr, Vector3 point)
        {
            float px = point.x / point.z * intr.fx + intr.ppx;
            float py = point.y / point.z * intr.fy + intr.ppy;

            return new Vector2(px / intr.width, py / intr.height);
        }

        // used by focal-length estimator
        private const int FL_EST_FRAMES = 20;  // number of frames for focal length estimation
        private Dictionary<int, int> _flFrameCount = new();
        private Dictionary<int, ulong> _flLastTimestamp = new();
        private Dictionary<int, Vector2> _flFocalLen = new();
        private Dictionary<int, int> _flCount = new();

        /// <summary>
        /// Tries to estimate focal length out of the detected raw body data.
        /// </summary>
        /// <param name="cameraRes">Camera resolution (color or depth)</param>
        /// <param name="sensorData">Sensor data</param>
        /// <returns>Focal length, or Vector2.zero</returns>
        public Vector2 TryEstimateFocalLen(DepthSensorBase.PointCloudResolution cameraRes, KinectInterop.SensorData sensorData, FramesetData fsData, out float percentReady)
        {
            int camResKey = cameraRes == DepthSensorBase.PointCloudResolution.ColorCameraResolution ? sensorData.colorImageWidth + sensorData.colorImageHeight * 10000 :
                sensorData.depthImageWidth + sensorData.depthImageHeight * 10000;

            if (!_flFrameCount.ContainsKey(camResKey))
            {
                // init dictionaries
                _flFrameCount[camResKey] = 0;
                _flLastTimestamp[camResKey] = 0;
                _flFocalLen[camResKey] = Vector2.zero;
                _flCount[camResKey] = 0;
            }

            percentReady = (float)_flFrameCount[camResKey] / FL_EST_FRAMES;
            if (!_isBodyTrackerFrameReady)
                return Vector2.zero;
            if (_bodyTrackerTimestamp == 0 || _flLastTimestamp[camResKey] == _bodyTrackerTimestamp)
                return Vector2.zero;

            _flLastTimestamp[camResKey] = _bodyTrackerTimestamp;
            if (_flFrameCount[camResKey] >= FL_EST_FRAMES)
            {
                percentReady = 1f;  // immediately ready
                return (_flCount[camResKey] > 0 ? _flFocalLen[camResKey] / _flCount[camResKey] : Vector2.zero);
            }

            int lrPairsCount = FL_JOINT_INDEX.Length >> 1;
            int validEstCount = 0;
            int fsi = fsData._fsIndex;

            Vector2Int imageSize = cameraRes == DepthSensorBase.PointCloudResolution.ColorCameraResolution ?
                new Vector2Int(sensorData.colorImageWidth, sensorData.colorImageHeight) :
                new Vector2Int(sensorData.depthImageWidth, sensorData.depthImageHeight);

            for (int i = 0; i < _poseCount; i++)
            {
                Vector4[] poseLandmarks = _poseLandmarks[i];
                Vector4[] poseWorldLandmarks = _poseWorldLandmarks[i];

                if (poseWorldLandmarks[BODY_LM_COUNT].x < landmarkScoreThreshold)
                    continue;

                // don't consider poses with too short shoulders
                Vector3 shDir = poseWorldLandmarks[11] - poseWorldLandmarks[12];
                if (shDir.magnitude < MIN_SHOULDER_LEN)
                    continue;

                // get hip center position
                Vector3 depthScale = _depthProvider != null ? _depthProvider.GetDepthImageScale() : Vector3.one;
                Vector2 vHipImagePos = poseLandmarks[33];  // (poseLandmarks[24] + poseLandmarks[23]) * 0.5f;
                Vector3 vHipSpacePos = GetSpacePosForNormDepthPixel(sensorData, fsData, vHipImagePos, depthScale);
                Vector3 vHipSensorPos = new Vector3(vHipSpacePos.x * depthScale.x, vHipSpacePos.y, vHipSpacePos.z);
                //Debug.Log($"fs: {fsi}, dScale: {depthScale:F2}, hipUV: {vHipImagePos:F2}\nspacePos: {vHipSpacePos:F2}, sensorPos: {vHipSensorPos:F2}");

                for (int j = 0; j < lrPairsCount; j++)
                {
                    int li = FL_JOINT_INDEX[j * 2];
                    int ri = FL_JOINT_INDEX[j * 2 + 1];
                    //Debug.Log($"  bi: {i}, li: {li}, ri: {ri}, hipPos: {vHipSensorPos}");

                    Vector4 lJointW = poseWorldLandmarks[li];
                    Vector4 rJointW = poseWorldLandmarks[ri];

                    //if (lJointW.w < 0.01f || rJointW.w < 0.01f)
                    if (lJointW.w < jointTrackedThreshold || rJointW.w < jointTrackedThreshold)  // jointInferredThreshold
                        continue;

                    Vector3 lPos = vHipSensorPos + (Vector3)lJointW;
                    Vector3 rPos = vHipSensorPos + (Vector3)rJointW;
                    Vector2 lUV = poseLandmarks[li];
                    Vector2 rUV = poseLandmarks[ri];

                    Vector2 fLen = CalcFocalLen(fsi, li, ri, lPos, rPos, lUV, rUV, imageSize.x, imageSize.y);
                    if (fLen != Vector2.zero)
                    {
                        _flFocalLen[camResKey] += fLen;
                        _flCount[camResKey]++;
                        validEstCount++;
                    }
                }
            }

            if (validEstCount > 0)
            {
                _flFrameCount[camResKey]++;
            }

            percentReady = (float)_flFrameCount[camResKey] / FL_EST_FRAMES;

            Vector2 focalLen = _flFrameCount[camResKey] >= FL_EST_FRAMES && _flCount[camResKey] > 0 ? _flFocalLen[camResKey] / _flCount[camResKey] : Vector2.zero;
            //Debug.Log($"fs: {fsi}, EstFL {cameraRes} {_flFrameCount[camResKey]}/{FL_EST_FRAMES} - fLen: {focalLen:F2}\ncamRes: {camResKey}, count: {_flCount[camResKey]}, poses: {_poseCount}, tempFL: {(_flFocalLen[camResKey] / _flCount[camResKey]):F2}, ori: {Screen.orientation}");

            return focalLen;
        }

        // calculates focal lengths on x & y, given a couple of 3d points and their respective projections
        private Vector2 CalcFocalLen(int fsi, int i1, int i2, Vector3 pos1, Vector3 pos2, Vector2 uv1, Vector2 uv2, int imageW, int imageH)
        {
            //if (uv1.x >= 0f && uv1.x < 1f && uv1.y >= 0f && uv1.y < 1f && uv2.x >= 0f && uv2.x < 1f && uv2.y >= 0f && uv2.y < 1f)
            if(Mathf.Abs(uv2.x - uv1.x) > 0.02f && Mathf.Abs(uv2.y - uv1.y) > 0.02f &&
                 Mathf.Abs(pos2.x - pos1.x) > 0.15f && Mathf.Abs(pos2.y - pos1.y) > 0.15f)
            {
                float fx = Mathf.Abs(((uv2.x - 0.5f) * pos2.z - (uv1.x - 0.5f) * pos1.z) * imageW / (pos2.x - pos1.x));
                float fy = Mathf.Abs(((uv2.y - 0.5f) * pos2.z - (uv1.y - 0.5f) * pos1.z) * imageH / (pos2.y - pos1.y));

                float diff = fx / fy;
                //Debug.Log($"  fs: {fsi}, i1: {i1}, i2: {i2}, fx: {fx:F3}, fy: {fy:F3}, diff: {diff:F2}, w: {imageW}, h: {imageH}\nuv1: {uv1:F2}, uv2: {uv2:F2}, pos1: {pos1:F2}, pos2: {pos2:F2}");

                if(diff >= 0.5f && diff <= 2f)
                    return new Vector2(fx, fy);
            }

            return Vector2.zero;
        }

        // gets the space position for the specified normalized depth image coordinates.
        private Vector3 GetSpacePosForNormDepthPixel(KinectInterop.SensorData sensorData, FramesetData fsData,
            Vector2 normDepthPos, Vector3 depthScale, bool getDepthOnly = false)
        {
            if(_depthProvider == null)
                return Vector3.zero;

            int dx = Mathf.Clamp(Mathf.RoundToInt((depthScale.x >= 0f ? normDepthPos.x : (1f - normDepthPos.x)) * _depthImageWidth), 0, _depthImageWidth - 1);
            int dy = Mathf.Clamp(Mathf.RoundToInt((depthScale.y >= 0f ? normDepthPos.y : (1f - normDepthPos.y)) * _depthImageHeight), 0, _depthImageHeight - 1);

            int di = dx + dy * _depthImageWidth;
            ushort depth = fsData != null && fsData._depthFrame != null? fsData._depthFrame[di] : (ushort)0;

            Vector2 imagePos = new Vector2(dx, dy);
            Vector3 spacePos = !getDepthOnly && sensorData.depthCamIntr != null ? _depthProvider.UnprojectPoint(sensorData.depthCamIntr, imagePos, depth * 0.001f) : Vector3.zero;
            //Debug.Log($"fs: {fsData._fsIndex}, Depth: {depth * 0.001f}, hipsPos: {normDepthPos} - {imagePos} - {spacePos}");

            if (spacePos == Vector3.zero)
                spacePos = new Vector3(0f, 0f, depth * 0.001f);

            return spacePos;
        }

    }
}
