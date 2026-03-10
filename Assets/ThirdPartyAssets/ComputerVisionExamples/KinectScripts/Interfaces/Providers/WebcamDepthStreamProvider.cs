using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using com.rfilkov.kinect;
using com.rfilkov.devices;

namespace com.rfilkov.providers
{
    public class WebcamDepthStreamProvider : MonoBehaviour, IDepthStreamProvider
    {
        //[Tooltip("Whether to use the lite model or the heavy, more precise model.")]
        //public bool useLiteModel = false;

        [Tooltip("Raw image for debugging purposes.")]
        public UnityEngine.UI.RawImage _debugImage = null;

        [Tooltip("Raw image 2 for debugging purposes.")]
        public UnityEngine.UI.RawImage _debugImage2 = null;


        // depth inference model
        private string _depthModelName;  // set in StartProvider() 
        private int _depthModelSteps = 1;  // 3

        //// mean & std constants
        //private static readonly Vector4 _depthAnyMean = new Vector4(0.485f, 0.456f, 0.406f, 0f);
        //private static readonly Vector4 _depthAnyStd = new Vector4(0.229f, 0.224f, 0.225f, 0f);

        // references
        private UniCamInterface _unicamInt;
        private IDeviceManager _deviceManager;
        private KinectManager _kinectManager;

        // stream provider properties
        private StreamState _streamState = StreamState.NotStarted;
        private string _providerId = null;

        //private ushort[] _depthFrame;
        //private object _depthLock;
        private KinectInterop.SensorData _sensorData;

        // last inference time & framesets
        private ulong _lastInfTime = 0;
        private Queue<ulong> _lastInfTs = new Queue<ulong>();
        private Dictionary<ulong, FramesetData> _lastInfDtFs = new Dictionary<ulong, FramesetData>();

        // focal length
        private Vector2 _focalLength = Vector2.zero;

        // the input texture
        private RenderTexture _sourceTex;

        //private TensorFloat _srcTexTensor;
        //private ComputeBuffer _srcTexBuffer;

        // reference to SM
        private SentisManager _sentisManager = null;
        private RuntimeModel _modelDepth = null;
        private const string _modelName = "dan";
        //private IWorker _worker = null;

        // provider state
        private enum ProviderState : int { NotStarted, EstimateDepth, ProcessDepth, Ready, CopyFrame }
        //private ProviderState _providerState = ProviderState.NotStarted;

        // model output
        private string _outputName;

        // conversion shader params
        private ComputeShader _tex2BufShader;
        private Material _tex2BufMaterial;

        //private ComputeBuffer _depthBufMM;
        private ComputeBuffer _depthBufOut;
        private ulong _depthBufTimestamp = 0;

        // scaled frame props
        private bool _isScalingEnabled = false;
        private int _scaledFrameWidth = 0;
        private int _scaledFrameHeight = 0;

        private ComputeBuffer _scaledDepthBuf = null;

        // scaling shader params
        private ComputeShader _scaleBufShader;
        private Material _scaleBufMaterial;
        //private ComputeBuffer _scaleSrcBuf;
        private int _scaleBufKernel;

        // model params
        private string _inputName;

        private int _inputWidth = 0;
        private int _inputHeight = 0;
        private int _inputChannels = 0;
        //private TextureTransform _inputTrans;
        //private Tensor<float> _inputTensor;

        private int _outputWidth = 0;
        private int _outputHeight = 0;

        private int _imageWidth = 0;
        private int _imageHeight = 0;
        private int _sourceWidth = 0;
        private int _sourceHeight = 0;

        //// depth min/max buffer
        //private float[] _depthMM = new float[2];

        // mask material and texture
        private Material _depthMaskMaterial;
        private RenderTexture _depthMaskTex;


        /// <summary>
        /// Returns the required device manager class name for this stream, or null if no device is required.
        /// </summary>
        /// <returns>Device manager class name, or null</returns>
        public string GetDeviceManagerClass()
        {
            return "com.rfilkov.devices.WebcamDeviceManager";
        }

        /// <summary>
        /// Returns the required device index for this stream, or -1 if no device is required
        /// </summary>
        /// <param name="unicamInt">UniCam interface</param>
        /// <param name="devices">List of available devices</param>
        /// <returns>Device index, or -1</returns>
        public int GetDeviceIndex(UniCamInterface unicamInt, List<KinectInterop.SensorDeviceInfo> devices)
        {
            return unicamInt.deviceIndex;
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

            if (deviceManager == null || deviceManager.GetDeviceState() != DeviceState.Working)
            {
                Debug.LogWarning("Device manager is not working. Depth stream provider can't start.");
                return null;
            }

            // instantiate SM
            _sentisManager = SentisManager.Instance;
            if(_sentisManager == null)
            {
                _sentisManager = gameObject.AddComponent<SentisManager>();
            }
            else
            {
                _sentisManager.StartManager();
            }

            // set depth model name
            //if (!Application.isMobilePlatform && !useLiteModel)
            {
                // standard model
                _depthModelName = "dan_1.1";
                _depthModelSteps = 5;
            }
            //else
            //{
            //    // lite model
            //    _depthModelName = "dzn_1.1";
            //    _depthModelSteps = 3;
            //}

            //Debug.Log($"Depth-model: {_depthModelName}, steps: {_depthModelSteps}");

            // load the model
            _modelDepth = _sentisManager.AddModel(_modelName, _depthModelName, _depthModelSteps);
            if(_modelDepth == null)
            {
                // model not found
                return null;
            }

            //var output = modelDepth.outputs[0];
            //modelDepth.layers.Add(new Unity.Sentis.Layers.ReduceMin("min0", new[] { output }, false));
            //modelDepth.layers.Add(new Unity.Sentis.Layers.ReduceMax("max0", new[] { output }, false));
            //modelDepth.outputs = new List<string>() { output, "min0", "max0" };

            _inputName = _modelDepth.inputNames[0];
            var inShape = _modelDepth.inputShapes[_inputName];

            _inputWidth = (int)inShape[3];
            _inputHeight = (int)inShape[2];
            _inputChannels = (int)inShape[1];

            //// set input transform
            //_inputTrans = new TextureTransform().SetDimensions(_inputWidth, _inputHeight, _inputChannels).SetTensorLayout(TensorLayout.NCHW);
            //_inputTensor = new Tensor<float>(new TensorShape(1, _inputChannels, _inputHeight, _inputWidth));

            _outputName = _modelDepth.outputNames[0];  // 0  // levit - 1
            _outputWidth = _inputWidth;
            _outputHeight = _inputHeight;
            //Debug.Log($"DM {_inputName} - W: {_inputWidth}, H: {_inputHeight}, C: {_inputChannels}, {_outputName} - W: {_outputWidth}, H: {_outputHeight}");

            // create worker
            //var workerType = WorkerFactory.GetBestTypeForDevice(Unity.Sentis.DeviceType.GPU);
            //_worker = WorkerFactory.CreateWorker(workerType, model, false);

            if(KinectInterop.IsSupportsComputeShaders())
            {
                _tex2BufShader = Resources.Load("DepthTexToBufferShader") as ComputeShader;
                _scaleBufShader = Resources.Load("DepthImgScaleShader") as ComputeShader;
                _scaleBufKernel = _scaleBufShader != null ? _scaleBufShader.FindKernel("ScaleDepthImage") : -1;
            }

            // create the input texture
            _sourceTex = KinectInterop.CreateRenderTexture(_sourceTex, _inputWidth, _inputHeight, RenderTextureFormat.ARGB32);

            //_srcTexTensor = TensorFloat.AllocZeros(new TensorShape(1, _inputChannels, _inputHeight, _inputWidth));
            //ComputeTensorData srcTensorData = ComputeTensorData.Pin(_srcTexTensor);
            //_srcTexBuffer = srcTensorData.buffer;

            // last inference time & framesets
            _lastInfTime = 0;
            _lastInfTs.Clear();
            _lastInfDtFs.Clear();

            // set provider state
            //_providerState = ProviderState.EstimateDepth;

            _providerId = deviceManager.GetDeviceId() + "_ds";
            _streamState = StreamState.Working;
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
            //// remove model from SM
            //_sentisManager?.RemoveModel(_modelName);

            //_inputTensor?.Dispose();
            //_inputTensor = null;

            // texture & buffers
            KinectInterop.Destroy(_sourceTex);
            _sourceTex = null;

            //_srcTexTensor?.Dispose();
            //_srcTexTensor = null;
            //_srcTexBuffer = null;

            //_depthBufMM?.Release();
            //_depthBufMM = null;
            _depthBufOut?.Release();
            _depthBufOut = null;

            // scaling props
            _isScalingEnabled = false;

            //_scaleSrcBuf?.Release();
            //_scaleSrcBuf = null;

            _scaledDepthBuf?.Release();
            _scaledDepthBuf = null;

            if(_depthMaskTex != null)
            {
                KinectInterop.Destroy(_depthMaskTex);
                _depthMaskTex = null;
                _depthMaskMaterial = null;
            }

            KinectInterop.Destroy(_tex2BufMaterial);
            KinectInterop.Destroy(_scaleBufMaterial);

            // last inference time & framesets
            _lastInfTs.Clear();
            _lastInfDtFs.Clear();

            _modelDepth = null;
            _streamState = StreamState.Stopped;
        }

        /// <summary>
        /// Releases stream resources allocated in the frameset.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        public void ReleaseFrameData(FramesetData fsData)
        {
            if (fsData == null || fsData._isDepthDataCopied)
                return;

            fsData._depthFrame = null;
            fsData._scaledDepthFrame = null;
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
            // does nothing
        }

        /// <summary>
        /// Updates the stream data in the main thread.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="fsData">Frameset data</param>
        public void UpdateStreamData(KinectInterop.SensorData sensorData, FramesetData fsData)
        {
            // get the correct frame
            fsData = _unicamInt.isSyncDepthAndColor ? fsData : _unicamInt.GetLastOpenFrameset();  //?._fsPrev;

            if (!fsData._isDepthFrameReady)
            {
                // process source image
                ProcessSourceImage(fsData);
            }

            if (fsData._isDepthFrameReady && _isScalingEnabled && !fsData._isScaledDepthFrameReady &&
                fsData._scaledDepthFrameTimestamp != fsData._depthFrameTimestamp)
            {
                // scale the depth frame
                fsData._scaledDepthFrameTimestamp = fsData._depthFrameTimestamp;
                ScaleDepthFrame(fsData);
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
            if(!_unicamInt.isSyncDepthAndColor || fsData._depthProviderState >= (int)ProviderState.ProcessDepth ||
                (_kinectManager != null && _kinectManager.getDepthFrames == KinectManager.DepthTextureType.None))
            {
                return true;
            }

            return false;
        }

        // processes the source image to estimate depth
        private void ProcessSourceImage(FramesetData fsData)
        {
            if (_sentisManager == null || _modelDepth == null)
                return;

            if (fsData._depthProviderState == 0)
            {
                fsData._depthProviderState = (int)ProviderState.EstimateDepth;
            }

            int fsIndex = _unicamInt.isSyncDepthAndColor ? fsData._fsIndex : -1;  // fsData._fsIndex;  // 
            //Debug.Log($"  DE: fs: {fsData._fsIndex} - {(ProviderState)fsData._depthProviderState}/{fsData._depthProviderState}, fsIndex: {fsIndex}\n  dBufTs: {_depthBufTimestamp}, copied: {fsData._isDepthDataCopied}, ready: {fsData._isDepthFrameReady}, fsTs: {fsData._depthFrameTimestamp}");

            switch ((ProviderState)fsData._depthProviderState)
            {
                case ProviderState.EstimateDepth:
                    if (!_sentisManager.IsModelStarted(fsIndex, _modelName))
                    {
                        // check for inference time
                        //Debug.Log($"fs: {fsData._fsIndex} EstDepth - colorTs: {fsData._rawFrameTimestamp}, lastTs: {_lastInfTime}\ndTs: {fsData._rawFrameTimestamp - _lastInfTime}" + (_lastInfDtFs.ContainsKey(_lastInfTime) ? $";    last fs: {_lastInfDtFs[_lastInfTime]._fsIndex}, ts: {_lastInfDtFs[_lastInfTime]._rawFrameTimestamp}" : ""));
                        if (_lastInfTime != 0 && fsData._rawFrameTimestamp > _lastInfTime && (fsData._rawFrameTimestamp - _lastInfTime) < KinectInterop.MIN_TIME_BETWEEN_INF &&
                            _lastInfDtFs.ContainsKey(_lastInfTime) && _lastInfDtFs[_lastInfTime]._rawFrameTimestamp == _lastInfTime)
                        {
                            // continue w/ copy-frame wait
                            SetCopyLastInfFrame(fsData);
                        }

                        // estimate depth
                        else if (EstimateDepthFromSource(fsData))
                        {
                            fsData._depthProviderState = (int)ProviderState.ProcessDepth;
                        }
                    }
                    //else if (_sentisManager.GetModelFsIndex(_modelName) == fsData._fsIndex)
                    //{
                    //    fsData._depthProviderState = (int)ProviderState.ProcessDepth;
                    //}
                    break;

                case ProviderState.ProcessDepth:
                    if (_sentisManager.IsModelReady(fsIndex, _modelName))
                    {
                        // copy model output to buffer
                        ProcessDepthOuput(fsData);

                        _sentisManager.ClearModelReady(fsIndex, _modelName);
                        fsData._depthProviderState = (int)ProviderState.Ready;  // (int)ProviderState.EstimateDepth;

                        //// in this special case, go to the next model
                        //if (_unicamInt.isSyncDepthAndColor && !_unicamInt.isSyncBodyAndDepth &&
                        //    _sentisManager.GetCurrentModelName() == _modelName)
                        //{
                        //    _sentisManager.GotoNextModel();
                        //    //Debug.LogWarning("SpecialCase: Go to the next model.");
                        //}
                    }
                    break;

                case ProviderState.CopyFrame:
                    // copy data from the last finished frame
                    CopyLastReadyFrameData(fsData);
                    break;
            }
        }

        // copies data from the last finished frame
        private void CopyLastReadyFrameData(FramesetData fsData)
        {
            FramesetData fsd = _unicamInt.isSyncDepthAndColor ? fsData : _unicamInt.GetLastOpenFrameset();

            // depth data
            FramesetData lastDtFs = _lastInfDtFs.ContainsKey(fsData._refDtTs) && _lastInfDtFs[fsData._refDtTs]._rawFrameTimestamp == fsData._refDtTs ? _lastInfDtFs[fsData._refDtTs] : null;
            if (lastDtFs == null && _unicamInt.isSyncDepthAndColor)
                lastDtFs = fsd._fsPrev;

            if (!fsd._isDepthFrameReady && (lastDtFs == null || lastDtFs._isDepthFrameReady))
            {
                if(lastDtFs != null)
                {
                    if (fsd._depthFrame == null || fsd._depthFrame.Length != lastDtFs._depthFrame.Length)
                    {
                        fsd._depthFrame = new ushort[lastDtFs._depthFrame.Length];
                    }

                    KinectInterop.CopyBytes(lastDtFs._depthFrame, sizeof(ushort), fsd._depthFrame, sizeof(ushort));
                }

                fsd._depthFrameTimestamp = fsd._rawFrameTimestamp;
                fsd._isDepthFrameReady = true;
                //Debug.Log($"    D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, RawCpDepthTime: {fsd._depthFrameTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));

                if (_isScalingEnabled && !fsd._isScaledDepthFrameReady &&
                    fsd._scaledDepthFrameTimestamp != fsd._depthFrameTimestamp)
                {
                    // set data for depth scaling
                    int bufferLength2 = (_imageWidth * _imageHeight) >> 1;

                    if (_depthBufOut == null || _depthBufOut.count != bufferLength2)
                    {
                        _depthBufOut?.Dispose();
                        _depthBufOut = KinectInterop.CreateComputeBuffer(_depthBufOut, bufferLength2, sizeof(uint));
                    }

                    KinectInterop.SetComputeBufferData(_depthBufOut, fsd._depthFrame, fsd._depthFrame.Length, sizeof(ushort));
                    //Debug.Log($"    D{_unicamInt.deviceIndex} fs: {fsd._fsIndex} SetCpDepthData for scaling, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                }
            }

            // set provider state to ready, if all data is copied
            if (fsd._isDepthFrameReady &&
                (!_isScalingEnabled || fsd._isScaledDepthFrameReady))
            {
                fsData._depthProviderState = (int)ProviderState.Ready;
                //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, CopyDepth - Provider is ready, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }
        }

        // sets provider state to copy from last inf-frame
        private void SetCopyLastInfFrame(FramesetData fsData)
        {
            fsData._refDtTs = _lastInfTime;
            fsData._depthProviderState = (int)ProviderState.CopyFrame;
            //Debug.Log($"fs: {fsData._fsIndex}, Depth ts: {fsData._rawFrameTimestamp} will copy frame from ts: {_lastInfTime}\ndTs: {fsData._rawFrameTimestamp - _lastInfTime}" + (_lastInfDtFs.ContainsKey(_lastInfTime) ? $";    last fs: {_lastInfDtFs[_lastInfTime]._fsIndex}, ts: {_lastInfDtFs[_lastInfTime]._rawFrameTimestamp}" : ""));
        }

        // sets this inf-frame as last inf-frame
        private void SetThisAsLastInfFrame(FramesetData fsData)
        {
            //Debug.Log($"fs: {fsData._fsIndex}, Depth ts: {fsData._rawFrameTimestamp} will do inference. lastTs: {_lastInfTime}\ndTs: {fsData._rawFrameTimestamp - _lastInfTime}");

            if (_lastInfTs.Count >= KinectInterop.MAX_LASTINF_QUEUE_LENGTH)
            {
                ulong firstTs = _lastInfTs.Dequeue();

                if (_lastInfDtFs.ContainsKey(firstTs))
                    _lastInfDtFs.Remove(firstTs);
            }

            _lastInfTime = fsData._rawFrameTimestamp;
            _lastInfTs.Enqueue(_lastInfTime);
            fsData._refDtTs = _lastInfTime;

            _lastInfDtFs[_lastInfTime] = fsData;
        }


        // estimates depth from the source image
        private bool EstimateDepthFromSource(FramesetData fsData)
        {
            if (!fsData._isDepthFrameReady && fsData._isRawFrameReady && fsData._rawFrameTimestamp != 0)
            {
                // get source image
                Texture image = (Texture)fsData._rawFrame;
                //if (image == null)
                //    return false;

                if (_sourceWidth != image.width || _sourceHeight != image.height)
                {
                    UpdateDepthFrameSize(image.width, image.height);
                }

                // create or recreate depth frame & depth buf
                int depthFrameLen = _imageWidth * _imageHeight;
                if (fsData._depthFrame == null || fsData._depthFrame.Length != depthFrameLen)
                {
                    fsData._depthFrame = new ushort[depthFrameLen];
                }

                if (KinectInterop.IsSupportsComputeShaders())
                {
                    int bufferLength2 = depthFrameLen >> 1;
                    if (_depthBufOut == null || _depthBufOut.count != bufferLength2)
                    {
                        _depthBufOut = KinectInterop.CreateComputeBuffer(_depthBufOut, bufferLength2, sizeof(uint));
                    }
                }

                if (_kinectManager != null && _kinectManager.getDepthFrames == KinectManager.DepthTextureType.None)
                {
                    // skip depth estimation
                    FramesetData fsd = _unicamInt.isSyncDepthAndColor ? fsData : _unicamInt.GetLastOpenFrameset();

                    fsd._depthFrameTimestamp = fsData._rawFrameTimestamp;
                    fsd._isDepthFrameReady = true;

                    // scale the depth frame, if needed
                    if (_isScalingEnabled && !fsd._isScaledDepthFrameReady &&
                        fsd._scaledDepthFrameTimestamp != fsd._depthFrameTimestamp)
                    {
                        fsd._scaledDepthFrameTimestamp = fsd._depthFrameTimestamp;
                        ScaleDepthFrame(fsd);
                    }

                    _depthBufTimestamp = (ulong)DateTime.UtcNow.Ticks;
                    fsData._depthProviderState = (int)ProviderState.Ready;

                    return false;
                }

                // set last inference frame
                SetThisAsLastInfFrame(fsData);

                // get the texture to process
                //Graphics.Blit(image, _sourceTex);

                CommandBuffer cmd = new CommandBuffer { name = "StoreSrcTexCb" };
                cmd.Blit(image, _sourceTex);

                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();
                //Debug.Log($"fs: {fsData._fsIndex}, Blit(_sourceTex:depth), frame: {kinect.UniCamInterface._frameIndex}");

                if (_debugImage != null && _debugImage.texture == null)
                {
                    _debugImage.texture = _sourceTex;
                }

                //// estimate output depth
                //_tex2BufShader.SetTexture(0, _DepthTex, _sourceTex);
                //_tex2BufShader.SetBuffer(0, _DepthTensorBuf, _srcTexBuffer);

                //_tex2BufShader.SetInt(_DepthTexWidth, _inputWidth);
                //_tex2BufShader.SetInt(_DepthTexHeight, _inputHeight);
                //_tex2BufShader.SetInt(_IsLinearColorSpace, QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);
                ////_tex2BufShader.SetVector(_DepthAnyMean, _depthAnyMean);
                ////_tex2BufShader.SetVector(_DepthAnyStd, _depthAnyStd);

                //_tex2BufShader.Dispatch(0, _inputWidth / 8, _inputHeight / 8, 1);
                ////Debug.Log($"fs: {fsData._fsIndex} - Tex2Tensor shader invoked");

                // start model inference
                //TensorFloat t = TextureConverter.ToTensor(_sourceTex, _inputWidth, _inputHeight, 3);
                //Tensor<float> srcTexTensor = TextureConverter.ToTensor(_sourceTex, _inputTrans);
                //TextureConverter.ToTensor(_sourceTex, _inputTensor, _inputTrans);

                int fsIndex = _unicamInt.isSyncDepthAndColor ? fsData._fsIndex : -1;  // fsData._fsIndex;  // 
                bool dmModelStarted = _sentisManager.StartInference(fsIndex, _modelName, _inputName, _sourceTex);  // t);  //

                //if (dmModelStarted)
                //    Debug.LogWarning($"fs: {fsData._fsIndex}, Model dm inference scheduled");
                //Debug.Log($"fs: {fsData._fsIndex}, StartInferenceDm:{dmModelStarted}, frame: {kinect.UniCamInterface._frameIndex}");

                return dmModelStarted;
            }

            return false;
        }

        // copies the output tensor to depth frame
        private void ProcessDepthOuput(FramesetData fsData)
        {
            int bufferLength = _imageWidth * _imageHeight;
            if (fsData._depthFrame == null || fsData._depthFrame.Length != bufferLength)
            {
                fsData._depthFrame = new ushort[bufferLength];
            }

            // skip inference this frame
            _sentisManager.SkipModelInfThisFrame();

            if (KinectInterop.IsSupportsComputeShaders())
            {
                // compute shaders
                int bufferLength2 = bufferLength >> 1;

                if (_depthBufOut == null || _depthBufOut.count != bufferLength2)
                {
                    _depthBufOut?.Dispose();
                    _depthBufOut = KinectInterop.CreateComputeBuffer(_depthBufOut, bufferLength2, sizeof(uint));
                }

                // get output buffer
                int fsIndex = _unicamInt.isSyncDepthAndColor ? fsData._fsIndex : -1;
                ComputeBuffer depthBufRaw = _sentisManager.OutputAsComputeBuffer(fsIndex, _modelName, _outputName);

                if (_debugImage2 != null)
                {
                    if (_depthMaskTex == null || _depthMaskTex.width != _outputWidth || _depthMaskTex.height != _outputHeight)
                    {
                        _depthMaskTex = KinectInterop.CreateRenderTexture(_depthMaskTex, _outputWidth, _outputHeight, RenderTextureFormat.ARGB32);
                    }

                    if (_depthMaskMaterial == null)
                    {
                        // create mask material
                        Shader depthMaskShader = Shader.Find("Custom/DepthMaskShader");

                        if (depthMaskShader)
                        {
                            _depthMaskMaterial = new Material(depthMaskShader);
                            _depthMaskTex = KinectInterop.CreateRenderTexture(_depthMaskTex, _outputWidth, _outputHeight, RenderTextureFormat.ARGB32);
                        }
                    }

                    _depthMaskMaterial.SetBuffer(_DepthBufRaw, depthBufRaw);
                    //_depthMaskMaterial.SetBuffer(_DepthBufMM, _depthBufMM);

                    _depthMaskMaterial.SetInt(_DepthTexWidth, _outputWidth);
                    _depthMaskMaterial.SetInt(_DepthTexHeight, _outputHeight);

                    Graphics.Blit(null, _depthMaskTex, _depthMaskMaterial);

                    if (_debugImage2.texture != _depthMaskTex)  // _sourceTex  // _depthMaskTex
                    {
                        _debugImage2.texture = _depthMaskTex;  // _sourceTex  // _depthMaskTex

                        Vector2 sizeImage = _debugImage2.rectTransform.sizeDelta;
                        _debugImage2.rectTransform.sizeDelta = new Vector2(sizeImage.y * _debugImage2.texture.width / _debugImage2.texture.height, sizeImage.y);
                    }
                }

                // estimate output depth
                CommandBuffer cmd = new CommandBuffer { name = "DepthToBufCb" };
                cmd.SetComputeBufferParam(_tex2BufShader, 2, _DepthBufRaw, depthBufRaw);
                cmd.SetComputeBufferParam(_tex2BufShader, 2, _DepthBufOut, _depthBufOut);
                cmd.SetComputeIntParam(_tex2BufShader, _DepthTexWidth, _outputWidth);
                cmd.SetComputeIntParam(_tex2BufShader, _DepthTexHeight, _outputHeight);
                cmd.SetComputeIntParam(_tex2BufShader, _DepthImgWidth, _imageWidth);
                cmd.SetComputeIntParam(_tex2BufShader, _DepthImgHeight, _imageHeight);

                cmd.DispatchCompute(_tex2BufShader, 2, _depthBufOut.count >> 4, 1, 1);
                _depthBufTimestamp = (ulong)DateTime.UtcNow.Ticks;
                //Debug.Log($"fs: {fsData._fsIndex} - DepthOut2Buffer shader started, Now: " + DateTime.Now.ToString("HH:mm:ss.fff") +
                //    $"\noW: {_outputWidth}, oH: {_outputHeight}, iW: {_imageWidth}, iH: {_imageHeight}, bufLen: {_depthBufOut.count}");
                //Debug.Log($"fs: {fsData._fsIndex} - DepthBufToOut, AsyncGPUReadback(_depthBufOut), frame: {kinect.UniCamInterface._frameIndex}");

                cmd.RequestAsyncReadback(_depthBufOut, request =>
                {
                    FramesetData fsd = _unicamInt.isSyncDepthAndColor ? fsData : _unicamInt.GetLastOpenFrameset();

                    if (fsd != null && fsd._depthFrame != null)
                    {
                        if (request.width == (fsd._depthFrame.Length * sizeof(ushort)))
                            request.GetData<ushort>().CopyTo(fsd._depthFrame);
                        fsd._depthFrameTimestamp = fsData._rawFrameTimestamp;
                        fsd._isDepthFrameReady = true;

                        _lastInfDtFs[fsData._refDtTs] = fsd;
                        //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, RawDepthTimeCS: {fsd._depthFrameTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                        //Debug.Log($" sdEnabled: {_isScalingEnabled}, sdReady: {fsd._isScaledDepthFrameReady}, sdTime: {fsd._scaledDepthFrameTimestamp}");

                        // scale the depth frame, if needed
                        if (_isScalingEnabled && !fsd._isScaledDepthFrameReady &&
                            fsd._scaledDepthFrameTimestamp != fsd._depthFrameTimestamp)
                        {
                            fsd._scaledDepthFrameTimestamp = fsd._depthFrameTimestamp;
                            ScaleDepthFrame(fsd);
                        }
                    }
                });

                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();
            }

        }

        private static readonly int _DepthBufRaw = Shader.PropertyToID("_DepthBufRaw");
        private static readonly int _DepthBufOut = Shader.PropertyToID("_DepthBufOut");

        private static readonly int _DepthTexWidth = Shader.PropertyToID("_DepthTexWidth");
        private static readonly int _DepthTexHeight = Shader.PropertyToID("_DepthTexHeight");
        private static readonly int _DepthImgWidth = Shader.PropertyToID("_DepthImgWidth");
        private static readonly int _DepthImgHeight = Shader.PropertyToID("_DepthImgHeight");


        /// <summary>
        /// Returns the sensor space scale.
        /// </summary>
        /// <returns>Sensor space scale</returns>
        public Vector3 GetSensorSpaceScale()
        {
            return new Vector3(1f, -1f, 1f);
        }

        /// <summary>
        /// Returns the depth image scale.
        /// </summary>
        /// <returns>Depth image scale</returns>
        public Vector3 GetDepthImageScale()
        {
            return new Vector3(1f, -1f, 1f);
        }

        /// <summary>
        /// Returns the current resolution of the depth image.
        /// </summary>
        /// <returns>Depth image size</returns>
        public Vector2Int GetDepthImageSize()
        {
            Vector2Int imageSize = _deviceManager != null ? (Vector2Int)_deviceManager.GetDeviceProperty(DeviceProp.Resolution) : Vector2Int.zero;

            if (_imageWidth == 0 || _imageHeight == 0 || _sourceWidth != imageSize.x || _sourceHeight != imageSize.y)
            {
                UpdateDepthFrameSize(imageSize.x, imageSize.y);
            }

            return new Vector2Int(_imageWidth, _imageHeight);
        }

        // updates current depth frame size
        private void UpdateDepthFrameSize(int srcImageW, int srcImageH)
        {
            _sourceWidth = srcImageW;
            _sourceHeight = srcImageH;
            int minSize = Mathf.Min(_outputWidth, _outputHeight);

            if (minSize > 0)
            {
                _imageWidth = Mathf.RoundToInt(minSize * Mathf.Max((float)srcImageW / srcImageH, 1f)) & 0xFFF0;
                _imageHeight = Mathf.RoundToInt(minSize * Mathf.Max((float)srcImageH / srcImageW, 1f)) & 0xFFF0;  // 0xFFFC;  // 
            }
            else
            {
                _imageWidth = srcImageW & 0xFFF0;
                _imageHeight = srcImageH & 0xFFF0;  // 0xFFFC;  // 
            }

            //Debug.Log($"Updated depth-frame-size to: {_imageWidth}x{_imageHeight}\ncolor-image-size is: {srcImageW}x{srcImageH}");
        }

        /// <summary>
        /// Gets or sets color camera focal length.
        /// </summary>
        public Vector2 FocalLength
        {
            get => _focalLength;
            set => _focalLength = value;
        }

        /// <summary>
        /// Returns the depth camera intrinsics.
        /// </summary>
        /// <returns>Depth camera intrinsics</returns>
        public KinectInterop.CameraIntrinsics GetCameraIntrinsics()
        {
            if (_focalLength == Vector2.zero)
                return null;

            var camIntr = new KinectInterop.CameraIntrinsics();

            var imageSize = GetDepthImageSize();
            camIntr.cameraType = 0;
            camIntr.width = imageSize.x;
            camIntr.height = imageSize.y;

            if (camIntr.width == 0 || camIntr.height == 0)
                return null;

            camIntr.ppx = camIntr.width >> 1;
            camIntr.ppy = camIntr.height >> 1;

            //float fLen = Mathf.Max(camIntr.width, camIntr.height);
            camIntr.fx = _focalLength.x;
            camIntr.fy = _focalLength.y;

            camIntr.distCoeffs = new float[6];
            camIntr.distType = KinectInterop.DistortionType.Linear;  //.BrownConrady;

            camIntr.hFOV = 2f * Mathf.Atan2((float)camIntr.width * 0.5f, camIntr.fx) * Mathf.Rad2Deg;
            camIntr.vFOV = 2f * Mathf.Atan2((float)camIntr.height * 0.5f, camIntr.fy) * Mathf.Rad2Deg;

            return camIntr;
        }

        /// <summary>
        /// Returns the depth-to-color camera extrinsics.
        /// </summary>
        /// <returns>Depth-to-color camera intrinsics</returns>
        public KinectInterop.CameraExtrinsics GetCameraExtrinsics()
        {
            var extr = new KinectInterop.CameraExtrinsics();

            extr.rotation = new float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };  // zero rotation
            extr.translation = new float[3];  // zero translation

            return extr;
        }

        /// <summary>
        /// Returns the depth confidence percentage [0-1]. 0 means depth values are not reliable at all, 1 means depth values are 100% reliable.
        /// </summary>
        /// <returns>Depth confidence [0-1].</returns>
        public float GetDepthConfidence()
        {
            return 0f;  // 0% reliable
        }

        /// <summary>
        /// Returns the current depth buffer as compute-buffer or native-array.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        /// <param name="isPermanent">Whether the result is permanent, or should be destroyed by the caller.</param>
        /// <returns>Current depth buffer</returns>
        public object GetDepthBuffer(FramesetData fsData, out bool isPermanent)
        {
            isPermanent = false;
            if (_depthBufTimestamp == 0)
                return null;
            if (_unicamInt.isSyncDepthAndColor && _unicamInt.isSyncBodyAndDepth && fsData._depthProviderState != (int)ProviderState.Ready)
                return null;

            if (KinectInterop.IsSupportsComputeShaders())
            {
                isPermanent = true;
                return _depthBufOut;
            }

            return null;
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

        // scales current depth frame, according to requirements
        private void ScaleDepthFrame(FramesetData fsData)
        {
            int destFrameLen = _scaledFrameWidth * _scaledFrameHeight;
            if (fsData._scaledDepthFrame == null || fsData._scaledDepthFrame.Length != destFrameLen)
                fsData._scaledDepthFrame = new ushort[destFrameLen];

            if (_kinectManager != null && _kinectManager.getDepthFrames == KinectManager.DepthTextureType.None)
            {
                // skip scaled depth estimation
                FramesetData fsd = _unicamInt.isSyncDepthAndColor ? fsData : _unicamInt.GetLastOpenFrameset();

                fsd._scaledDepthFrameTimestamp = fsData._depthFrameTimestamp;
                fsd._isScaledDepthFrameReady = true;

                return;
            }

            if (KinectInterop.IsSupportsComputeShaders())
            {
                // compute shaders
                //int srcBufLength = _imageWidth * _imageHeight >> 1;

                //if (_scaleSrcBuf == null || _scaleSrcBuf.count != srcBufLength)
                //    _scaleSrcBuf = KinectInterop.CreateComputeBuffer(_scaleSrcBuf, srcBufLength, sizeof(uint));

                //if (fsData._depthFrame != null)
                //{
                //    KinectInterop.SetComputeBufferData(_scaleSrcBuf, fsData._depthFrame, fsData._depthFrame.Length, sizeof(ushort));
                //}

                int bufferLength2 = (_imageWidth * _imageHeight) >> 1;
                if (_depthBufOut == null || _depthBufOut.count != bufferLength2)
                {
                    _depthBufOut?.Dispose();
                    _depthBufOut = KinectInterop.CreateComputeBuffer(_depthBufOut, bufferLength2, sizeof(uint));
                }

                int destBufLen = destFrameLen >> 1;
                if (_scaledDepthBuf == null || _scaledDepthBuf.count != destBufLen)
                    _scaledDepthBuf = KinectInterop.CreateComputeBuffer(_scaledDepthBuf, destBufLen, sizeof(uint));

                CommandBuffer cmd = new CommandBuffer { name = "ScaleDepthCb" };
                cmd.SetComputeBufferParam(_scaleBufShader, _scaleBufKernel, _DepthBuf, _depthBufOut);  // _scaleSrcBuf
                cmd.SetComputeBufferParam(_scaleBufShader, _scaleBufKernel, _TargetBuf, _scaledDepthBuf);
                cmd.SetComputeIntParam(_scaleBufShader, _DepthImgWidth, _imageWidth);
                cmd.SetComputeIntParam(_scaleBufShader, _DepthImgHeight, _imageHeight);
                cmd.SetComputeIntParam(_scaleBufShader, _TargetImgWidth, _scaledFrameWidth);
                cmd.SetComputeIntParam(_scaleBufShader, _TargetImgHeight, _scaledFrameHeight);
                cmd.DispatchCompute(_scaleBufShader, _scaleBufKernel, _scaledFrameWidth / 8, _scaledFrameHeight / 8, 1);

                //Debug.Log($"fs: {fsData._fsIndex} - ScaleDepthFrame C-shader started" +
                //    $"\niW: {_imageWidth}, iH: {_imageHeight}, scW: {_scaledFrameWidth}, scH: {_scaledFrameHeight}");

                cmd.RequestAsyncReadback(_scaledDepthBuf, request =>
                {
                    FramesetData fsd = _unicamInt.isSyncDepthAndColor ? fsData : _unicamInt.GetLastOpenFrameset();

                    if (fsd != null && fsd._scaledDepthFrame != null)
                    {
                        if (request.width == (fsd._scaledDepthFrame.Length * sizeof(ushort)))
                            request.GetData<ushort>().CopyTo(fsd._scaledDepthFrame);
                        fsd._scaledDepthFrameTimestamp = fsData._depthFrameTimestamp;
                        fsd._isScaledDepthFrameReady = true;
                        //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, ScaledDepthTimeCS: {fsd._scaledDepthFrameTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
                    }
                });

                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();
            }

        }

        private static readonly int _DepthBuf = Shader.PropertyToID("_DepthBuf");
        private static readonly int _TargetBuf = Shader.PropertyToID("_TargetBuf");

        private static readonly int _TargetImgWidth = Shader.PropertyToID("_TargetImgWidth");
        private static readonly int _TargetImgHeight = Shader.PropertyToID("_TargetImgHeight");

        /// <summary>
        /// Unprojects a point from an image to camera space.
        /// </summary>
        /// <param name="intr">Camera intrinsics</param>
        /// <param name="pixel">Image position</param>
        /// <param name="depth">Depth, in meters</param>
        /// <returns>3D position of the point, in meters</returns>
        public Vector3 UnprojectPoint(KinectInterop.CameraIntrinsics intr, Vector2 pixel, float depth)
        {
            if (depth <= 0f || intr == null)
                return Vector3.zero;

            float x = (pixel.x - intr.ppx) / intr.fx * depth;
            float y = (pixel.y - intr.ppy) / intr.fy * depth;
            //float y = ((intr.height - pixel.y) - intr.ppy) / intr.fy * depth;

            return new Vector3(x, y, depth);
        }

        /// <summary>
        /// Projects a point from camera space to image.
        /// </summary>
        /// <param name="intr">Camera intrinsics</param>
        /// <param name="point">3D position of the point, in meters</param>
        /// <returns>2D position of the point, on image</returns>
        public Vector2 ProjectPoint(KinectInterop.CameraIntrinsics intr, Vector3 point)
        {
            if (point == Vector3.zero || intr == null)
                return Vector2.zero;

            float x = point.x / point.z;
            float y = point.y / point.z;

            float px = x * intr.fx + intr.ppx;
            float py = y * intr.fy + intr.ppy;
            //float py = intr.height - (y * intr.fy + intr.ppy);

            return new Vector2(px, py);
        }

        /// <summary>
        /// Transforms a point from one camera space to another.
        /// </summary>
        /// <param name="extr">Camera extrinsics</param>
        /// <param name="point">3D position of the point, in meters</param>
        /// <returns>3D position of the point, in meters</returns>
        public Vector3 TransformPoint(KinectInterop.CameraExtrinsics extr, Vector3 point)
        {
            float toPointX = extr.rotation[0] * point.x + extr.rotation[1] * point.y + extr.rotation[2] * point.z + extr.translation[0];
            float toPointY = extr.rotation[3] * point.x + extr.rotation[4] * point.y + extr.rotation[5] * point.z + extr.translation[1];
            float toPointZ = extr.rotation[6] * point.x + extr.rotation[7] * point.y + extr.rotation[8] * point.z + extr.translation[2];
            Vector3 toPoint = new Vector3(toPointX, toPointY, toPointZ);

            return toPoint;
        }

    }
}
