using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using com.rfilkov.kinect;
using com.rfilkov.devices;


namespace com.rfilkov.providers
{
    public class WebcamColorStreamProvider : MonoBehaviour, IColorStreamProvider
    {
        // raw-image to display the debug texture
        public UnityEngine.UI.RawImage debugImage;


        // references
        private UniCamInterface _unicamInt;
        private IDeviceManager _deviceManager;
        private KinectManager _kinectManager;

        // stream provider properties
        private StreamState _streamState = StreamState.NotStarted;
        private string _providerId = null;

        // color image props
        //private Texture _colorImage;
        //private object _colorLock;
        private KinectInterop.SensorData _sensorData;

        // focal length
        private Vector2 _focalLength = Vector2.zero;

        // color texture material
        private Material _flippedImageMaterial;

        // scaled image props
        private bool _isScalingEnabled = false;
        private int _scaledImageWidth = 0;
        private int _scaledImageHeight = 0;


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
                Debug.LogWarning("Device manager is not working. Color stream provider can't start.");
                return null;
            }

            Shader flippedImageShader = Shader.Find("Kinect/ColorTexFlipYShader");
            if (flippedImageShader)
            {
                _flippedImageMaterial = new Material(flippedImageShader);
            }

            _providerId = deviceManager.GetDeviceId() + "_cs";
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
            // scaling props
            _isScalingEnabled = false;

            _streamState = StreamState.Stopped;
        }

        /// <summary>
        /// Releases stream resources allocated in the frameset.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        public void ReleaseFrameData(FramesetData fsData)
        {
            if (fsData == null || fsData._isColorDataCopied)
                return;

            if(fsData._colorImage != null)
            {
                ((RenderTexture)fsData._colorImage).Release();
                fsData._colorImage = null;
            }

            if(fsData._scaledColorImage != null)
            {
                ((RenderTexture)fsData._scaledColorImage).Release();
                fsData._scaledColorImage = null;
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
            // does nothing
        }

        /// <summary>
        /// Updates the stream data in the main thread.
        /// </summary>
        /// <param name="sensorData">Sensor data</param>
        /// <param name="fsData">Frameset data</param>
        public void UpdateStreamData(KinectInterop.SensorData sensorData, FramesetData fsData)
        {
            if(!fsData._isColorImageReady && fsData._isRawFrameReady)
            {
                Texture deviceImage = (Texture)fsData._rawFrame;
                if (fsData._colorImage == null || fsData._colorImage.width != deviceImage.width || fsData._colorImage.height != deviceImage.height)
                {
                    fsData._colorImage = KinectInterop.CreateRenderTexture((RenderTexture)fsData._colorImage, deviceImage.width, deviceImage.height, RenderTextureFormat.ARGB32);

                    if (debugImage != null)
                    {
                        debugImage.texture = fsData._colorImage;

                        float debugH = debugImage.rectTransform.sizeDelta.y;
                        debugImage.rectTransform.sizeDelta = new Vector2(debugH * debugImage.texture.width / debugImage.texture.height, debugH);
                    }
                }

                // flip the image
                Graphics.Blit(deviceImage, (RenderTexture)fsData._colorImage, _flippedImageMaterial);
                fsData._colorImageTimestamp = fsData._rawFrameTimestamp;
                fsData._isColorImageReady = true;

                //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsData._fsIndex}, RawColorTime: {fsData._colorImageTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
            }

            if (fsData._isColorImageReady && _isScalingEnabled && !fsData._isScaledColorImageReady && 
                fsData._scaledColorImageTimestamp != fsData._colorImageTimestamp)
            {
                // scale color image
                fsData._scaledColorImageTimestamp = fsData._colorImageTimestamp;
                ScaleColorImage(fsData);
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
            return fsData._isColorImageReady;  // ||
                // (_kinectManager != null && _kinectManager.getColorFrames == KinectManager.ColorTextureType.None);
        }

        /// <summary>
        /// Returns the color image scale.
        /// </summary>
        /// <returns>Color image scale</returns>
        public Vector3 GetColorImageScale()
        {
            return new Vector3(1f, -1f, 1f);
        }

        /// <summary>
        /// Returns the color image format.
        /// </summary>
        /// <returns>Color image format</returns>
        public TextureFormat GetColorImageFormat()
        {
            return TextureFormat.RGB24;
        }

        /// <summary>
        /// Returns the color image stride, in bytes per pixel.
        /// </summary>
        /// <returns>Color image stride</returns>
        public int GetColorImageStride()
        {
            return 3;
        }

        /// <summary>
        /// Returns the current resolution of the color image.
        /// </summary>
        /// <returns>Color image size.</returns>
        public Vector2Int GetColorImageSize()
        {
            Vector2Int imageSize = _deviceManager != null ? (Vector2Int)_deviceManager.GetDeviceProperty(DeviceProp.Resolution) : Vector2Int.zero;
            return imageSize;
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
        /// Returns the color camera intrinsics.
        /// </summary>
        /// <returns>Color camera intrinsics</returns>
        public KinectInterop.CameraIntrinsics GetCameraIntrinsics()
        {
            if (_focalLength == Vector2.zero)
                return null;

            var camIntr = new KinectInterop.CameraIntrinsics();

            var imageSize = GetColorImageSize();
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
        /// Returns the color-to-depth camera extrinsics.
        /// </summary>
        /// <returns>Color-to-depth camera intrinsics</returns>
        public KinectInterop.CameraExtrinsics GetCameraExtrinsics()
        {
            var extr = new KinectInterop.CameraExtrinsics();

            extr.rotation = new float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };  // zero rotation
            extr.translation = new float[3];  // zero translation

            return extr;
        }

        /// <summary>
        /// Returns the BT-source image scale (how it was flipped, compared to the original image).
        /// </summary>
        /// <returns>Color image scale</returns>
        public Vector3 GetBTSourceImageScale()
        {
            return new Vector3(1f, 1f, 1f);
        }

        /// <summary>
        /// Returns source image for body tracker.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        /// <returns>Color camera image</returns>
        public Texture GetBTSourceImage(FramesetData fsData)
        {
            return (Texture)fsData._rawFrame;
        }

        /// <summary>
        /// Sets scaled image properties.
        /// </summary>
        /// <param name="isEnabled">Whether the scaling is enabled or not</param>
        /// <param name="scaledWidth">Scaled image width</param>
        /// <param name="scaledHeight">Scaled image height</param>
        public void SetScaledImageProps(bool isEnabled, int scaledWidth, int scaledHeight)
        {
            _isScalingEnabled = isEnabled;
            _scaledImageWidth = scaledWidth;
            _scaledImageHeight = scaledHeight;
        }

        /// <summary>
        /// Checks if the image scaling is enabled or not.
        /// </summary>
        /// <returns>true if scaling is enabled, false otherwise</returns>
        public bool IsImageScalingEnabled()
        {
            return _isScalingEnabled;
        }

        // scales current color image, according to requirements
        private void ScaleColorImage(FramesetData fsData)
        {
            if (fsData._colorImage == null)
                return;

            // scale color texture
            if (fsData._scaledColorImage == null || fsData._scaledColorImage.width != _scaledImageWidth || fsData._scaledColorImage.height != _scaledImageHeight)
            {
                fsData._scaledColorImage = KinectInterop.CreateRenderTexture((RenderTexture)fsData._scaledColorImage, _scaledImageWidth, _scaledImageHeight, RenderTextureFormat.ARGB32);
            }

            Graphics.Blit(fsData._colorImage, (RenderTexture)fsData._scaledColorImage);

            //if (debugImage != null && debugImage.texture == null)
            //{
            //    debugImage.texture = fsData._scaledColorImage;
            //}

            fsData._scaledColorImageTimestamp = fsData._colorImageTimestamp;
            fsData._isScaledColorImageReady = true;
            //Debug.Log($"D{_unicamInt.deviceIndex} fs: {fsData._fsIndex}, ScaledColorTime: {fsData._scaledColorImageTimestamp}, Now: " + DateTime.Now.ToString("HH:mm:ss.fff"));
        }

    }
}
