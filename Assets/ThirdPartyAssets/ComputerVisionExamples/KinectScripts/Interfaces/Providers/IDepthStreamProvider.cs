using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using com.rfilkov.kinect;

namespace com.rfilkov.providers
{
    public interface IDepthStreamProvider : IStreamProvider
    {
        /// <summary>
        /// Returns the sensor space scale.
        /// </summary>
        /// <returns>Sensor space scale</returns>
        Vector3 GetSensorSpaceScale();

        /// <summary>
        /// Returns the depth image scale.
        /// </summary>
        /// <returns>Depth image scale</returns>
        Vector3 GetDepthImageScale();

        /// <summary>
        /// Returns the current resolution of the depth image.
        /// </summary>
        /// <returns>Depth image size</returns>
        Vector2Int GetDepthImageSize();

        /// <summary>
        /// Gets or sets depth camera focal length.
        /// </summary>
        Vector2 FocalLength { get; set; }

        /// <summary>
        /// Returns the depth camera intrinsics.
        /// </summary>
        /// <returns>Depth camera intrinsics</returns>
        KinectInterop.CameraIntrinsics GetCameraIntrinsics();

        /// <summary>
        /// Returns the depth-to-color camera extrinsics.
        /// </summary>
        /// <returns>Depth-to-color camera intrinsics</returns>
        KinectInterop.CameraExtrinsics GetCameraExtrinsics();

        /// <summary>
        /// Returns the depth confidence percentage [0-1]. 0 means depth values are not reliable at all, 1 means depth values are 100% reliable.
        /// </summary>
        /// <returns>Depth confidence [0-1].</returns>
        float GetDepthConfidence();

        /// <summary>
        /// Returns the current depth buffer as compute-buffer or native-array.
        /// </summary>
        /// <param name="fsData">Frameset data</param>
        /// <param name="isPermanent">Whether the result is permanent, or should be destroyed by the caller.</param>
        /// <returns>Current depth buffer</returns>
        object GetDepthBuffer(FramesetData fsData, out bool isPermanent);

        /// <summary>
        /// Sets scaled frame properties.
        /// </summary>
        /// <param name="isEnabled">Whether the scaling is enabled or not</param>
        /// <param name="scaledWidth">Scaled image width</param>
        /// <param name="scaledHeight">Scaled image height</param>
        void SetScaledFrameProps(bool isEnabled, int scaledWidth, int scaledHeight);

        /// <summary>
        /// Checks if the frame scaling is enabled or not.
        /// </summary>
        /// <returns>true if scaling is enabled, false otherwise</returns>
        bool IsFrameScalingEnabled();

        /// <summary>
        /// Unprojects a point from an image to camera space.
        /// </summary>
        /// <param name="intr">Camera intrinsics</param>
        /// <param name="pixel">Image position</param>
        /// <param name="depth">Depth, in meters</param>
        /// <returns>3D position of the point, in meters</returns>
        Vector3 UnprojectPoint(KinectInterop.CameraIntrinsics intr, Vector2 pixel, float depth);

        /// <summary>
        /// Projects a point from camera space to image.
        /// </summary>
        /// <param name="intr">Camera intrinsics</param>
        /// <param name="point">3D position of the point, in meters</param>
        /// <returns>2D position of the point, on image</returns>
        Vector2 ProjectPoint(KinectInterop.CameraIntrinsics intr, Vector3 point);

        /// <summary>
        /// Transforms a point from one camera space to another.
        /// </summary>
        /// <param name="extr">Camera extrinsics</param>
        /// <param name="point">3D position of the point, in meters</param>
        /// <returns>3D position of the point, in meters</returns>
        Vector3 TransformPoint(KinectInterop.CameraExtrinsics extr, Vector3 point);

    }
}
