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
    public class DepthcamStreamProvider : MonoBehaviour, IDepthStreamProvider
    {
        //[Tooltip("Raw image for debugging purposes.")]
        //public UnityEngine.UI.RawImage _debugImage = null;

        //[Tooltip("Raw image 2 for debugging purposes.")]
        //public UnityEngine.UI.RawImage _debugImage2 = null;


        // references
        private UniCamInterface _unicamInt;
        private DepthcamDeviceManager _deviceManager;
        private KinectManager _kinectManager;

        // stream provider properties
        private StreamState _streamState = StreamState.NotStarted;
        private string _providerId = null;

        //private ushort[] _depthFrame;
        //private object _depthLock;
        private KinectInterop.SensorData _sensorData;

        // focal length
        private Vector2 _focalLength = Vector2.zero;

        // provider state
        private enum ProviderState : int { NotStarted, EstimateDepth, ProcessDepth, Ready }
        //private ProviderState _providerState = ProviderState.NotStarted;

        // conversion shader params
        private ComputeShader _tex2BufShader;
        private Material _tex2BufMaterial;

        //private ComputeBuffer _depthBufMM;
        private ComputeBuffer _depthBufOut;
        //private Tensor<int> _depthOutTensor;
        //private TextureTensorData _depthTensorData;
        private ulong _depthBufTimestamp = 0;

        // scaled frame props
        private bool _isScalingEnabled = false;
        private int _scaledFrameWidth = 0;
        private int _scaledFrameHeight = 0;

        private ComputeBuffer _scaledDepthBuf = null;
        //private Tensor<int> _scaledDepthTensor;
        //private TextureTensorData _scaledTensorData;

        // scaling shader params
        private ComputeShader _scaleBufShader;
        private Material _scaleBufMaterial;
        //private ComputeBuffer _scaleSrcBuf;
        private int _scaleBufKernel;

        private int _imageWidth = 0;
        private int _imageHeight = 0;


        /// <summary>
        /// Returns the required device manager class name for this stream, or null if no device is required.
        /// </summary>
        /// <returns>Device manager class name, or null</returns>
        public string GetDeviceManagerClass()
        {
            return "com.rfilkov.devices.DepthcamDeviceManager";
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
            _deviceManager = (DepthcamDeviceManager)deviceManager;
            _kinectManager = kinectManager;
            _sensorData = sensorData;
            _streamState = StreamState.NotStarted;

            if (deviceManager == null || deviceManager.GetDeviceState() != DeviceState.Working)
            {
                Debug.LogWarning("Device manager is not working. Depth stream provider can't start.");
                return null;
            }

            if(KinectInterop.IsSupportsComputeShaders())
            {
                _tex2BufShader = Resources.Load("DepthTexToBufferShader") as ComputeShader;
                _scaleBufShader = Resources.Load("DepthImgScaleShader") as ComputeShader;
                _scaleBufKernel = _scaleBufShader != null ? _scaleBufShader.FindKernel("ScaleDepthImage") : -1;
            }

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
            _depthBufOut?.Release();
            _depthBufOut = null;

            //_depthOutTensor?.Dispose();
            //_depthOutTensor = null;
            //_depthTensorData = null;

            // scaling props
            _isScalingEnabled = false;

            _scaledDepthBuf?.Release();
            _scaledDepthBuf = null;

            //_scaledDepthTensor?.Dispose();
            //_scaledDepthTensor = null;
            //_scaledTensorData = null;

            KinectInterop.Destroy(_tex2BufMaterial);
            KinectInterop.Destroy(_scaleBufMaterial);

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
            if (fsData._depthProviderState == 0)
            {
                fsData._depthProviderState = (int)ProviderState.EstimateDepth;
            }

            //int fsIndex = _unicamInt.isSyncDepthAndColor ? fsData._fsIndex : -1;  // fsData._fsIndex;  // 
            //Debug.Log($"  DE: fs: {fsData._fsIndex} - {(ProviderState)fsData._depthProviderState}/{fsData._depthProviderState}, fsIndex: {fsIndex}\ndBufTs: {_depthBufTimestamp}");

            switch ((ProviderState)fsData._depthProviderState)
            {
                case ProviderState.EstimateDepth:
                    if (EstimateDepth(fsData))
                    {
                        fsData._depthProviderState = (int)ProviderState.Ready;
                    }
                    break;
            }
        }

        // estimates depth from the depthcam image
        private bool EstimateDepth(FramesetData fsData)
        {
            if (!fsData._isDepthFrameReady && fsData._depthFrameTimestamp != fsData._rawFrameTimestamp &&
                _deviceManager != null && _deviceManager.GetDepthBufTimestamp(fsData) != 0)
            {
                fsData._depthProviderState = (int)ProviderState.ProcessDepth;

                // get depth image size
                Vector2Int imageSize = _deviceManager.GetImageSize();
                if (_imageWidth != imageSize.x || _imageHeight != imageSize.y)
                {
                    _imageWidth = imageSize.x;
                    _imageHeight = imageSize.y;
                }

                // create or recreate depth frame
                int depthFrameLen = _imageWidth * _imageHeight;
                if (fsData._depthFrame == null || fsData._depthFrame.Length != depthFrameLen)
                {
                    fsData._depthFrame = new ushort[depthFrameLen];
                }

                int bufferLength2 = depthFrameLen >> 1;
                if (_depthBufOut == null || _depthBufOut.count != bufferLength2)
                {
                    _depthBufOut = KinectInterop.CreateComputeBuffer(_depthBufOut, bufferLength2, sizeof(uint));
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

                if (KinectInterop.IsSupportsComputeShaders())
                {
                    // estimate output depth
                    ComputeBuffer depthBufRaw = _deviceManager.GetDepthBuffer();
                    //ulong depthBufTs = _deviceManager.GetDepthBufTimestamp(fsData);

                    _tex2BufShader.SetBuffer(2, _DepthBufRaw, depthBufRaw);
                    _tex2BufShader.SetBuffer(2, _DepthBufOut, _depthBufOut);

                    _tex2BufShader.SetInt(_DepthTexWidth, _imageWidth);
                    _tex2BufShader.SetInt(_DepthTexHeight, _imageHeight);
                    _tex2BufShader.SetInt(_DepthImgWidth, _imageWidth);
                    _tex2BufShader.SetInt(_DepthImgHeight, _imageHeight);

                    _tex2BufShader.Dispatch(2, _depthBufOut.count >> 4, 1, 1);
                    _depthBufTimestamp = (ulong)DateTime.UtcNow.Ticks;
                    fsData._depthFrameTimestamp = fsData._rawFrameTimestamp;
                    //Debug.Log($"fs: {fsData._fsIndex} - DepthRawToOutBuf started, ts: {fsData._rawFrameTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));

                    AsyncGPUReadback.Request(_depthBufOut, request =>
                    {
                        FramesetData fsd = _unicamInt.isSyncDepthAndColor ? fsData : _unicamInt.GetLastOpenFrameset();

                        if (fsd != null && fsd._depthFrame != null)
                        {
                            if (request.width == (fsd._depthFrame.Length * sizeof(ushort)))
                                request.GetData<ushort>().CopyTo(fsd._depthFrame);

                            fsd._depthFrameTimestamp = fsd._rawFrameTimestamp;  // depthBufTs;
                            fsd._isDepthFrameReady = true;

                            //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsd._fsIndex}, RawDepthTimeCS: {fsd._depthFrameTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff") +
                            //    $"\nsdEnabled: {_isScalingEnabled}, sdReady: {fsd._isScaledDepthFrameReady}, sdTime: {fsd._scaledDepthFrameTimestamp}");

                            // scale the depth frame, if needed
                            if (_isScalingEnabled && !fsd._isScaledDepthFrameReady &&
                                fsd._scaledDepthFrameTimestamp != fsd._depthFrameTimestamp)
                            {
                                fsd._scaledDepthFrameTimestamp = fsd._depthFrameTimestamp;
                                ScaleDepthFrame(fsd);
                            }
                        }
                    });
                }

                return true;
            }

            return false;
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
            return _deviceManager != null ? _deviceManager.GetImageSize() : Vector2Int.zero;
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
                int destBufLen = destFrameLen >> 1;
                if (_scaledDepthBuf == null || _scaledDepthBuf.count != destBufLen)
                    _scaledDepthBuf = KinectInterop.CreateComputeBuffer(_scaledDepthBuf, destBufLen, sizeof(uint));

                _scaleBufShader.SetBuffer(_scaleBufKernel, _DepthBuf, _depthBufOut);  // _scaleSrcBuf
                _scaleBufShader.SetBuffer(_scaleBufKernel, _TargetBuf, _scaledDepthBuf);

                _scaleBufShader.SetInt(_DepthImgWidth, _imageWidth);
                _scaleBufShader.SetInt(_DepthImgHeight, _imageHeight);
                _scaleBufShader.SetInt(_TargetImgWidth, _scaledFrameWidth);
                _scaleBufShader.SetInt(_TargetImgHeight, _scaledFrameHeight);

                _scaleBufShader.Dispatch(_scaleBufKernel, _scaledFrameWidth / 8, _scaledFrameHeight / 8, 1);
                //Debug.Log($"fs: {fsData._fsIndex} - ScaleDepthFrame C-shader started");

                AsyncGPUReadback.Request(_scaledDepthBuf, request =>
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
