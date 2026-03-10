using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using com.rfilkov.kinect;

namespace com.rfilkov.providers
{
    public interface IColorStreamProvider : IStreamProvider
    {
        /// <summary>
        /// Returns the color image scale.
        /// </summary>
        /// <returns>Color image scale</returns>
        Vector3 GetColorImageScale();

        /// <summary>
        /// Returns the color image format.
        /// </summary>
        /// <returns>Color image format</returns>
        TextureFormat GetColorImageFormat();

        /// <summary>
        /// Returns the color image stride, in bytes per pixel.
        /// </summary>
        /// <returns>Color image stride</returns>
        int GetColorImageStride();

        /// <summary>
        /// Returns the current resolution of the color image.
        /// </summary>
        /// <returns>Color image size.</returns>
        Vector2Int GetColorImageSize();

        /// <summary>
        /// Gets or sets color camera focal length.
        /// </summary>
        Vector2 FocalLength { get; set; }

        /// <summary>
        /// Returns the color camera intrinsics.
        /// </summary>
        /// <returns>Color camera intrinsics</returns>
        KinectInterop.CameraIntrinsics GetCameraIntrinsics();

        /// <summary>
        /// Returns the color-to-depth camera extrinsics.
        /// </summary>
        /// <returns>Color-to-depth camera intrinsics</returns>
        KinectInterop.CameraExtrinsics GetCameraExtrinsics();

        /// <summary>
        /// Returns the BT-source image scale (how it was flipped, compared to the original image).
        /// </summary>
        /// <returns>Color image scale</returns>
        Vector3 GetBTSourceImageScale();

        /// <summary>
        /// Returns source image for body tracker.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        /// <returns>Color camera image</returns>
        Texture GetBTSourceImage(FramesetData fsData);

        /// <summary>
        /// Sets scaled image properties.
        /// </summary>
        /// <param name="isEnabled">Whether the scaling is enabled or not</param>
        /// <param name="scaledWidth">Scaled image width</param>
        /// <param name="scaledHeight">Scaled image height</param>
        void SetScaledImageProps(bool isEnabled, int scaledWidth, int scaledHeight);

        /// <summary>
        /// Checks if the image scaling is enabled or not.
        /// </summary>
        /// <returns>true if scaling is enabled, false otherwise</returns>
        bool IsImageScalingEnabled();

    }
}
