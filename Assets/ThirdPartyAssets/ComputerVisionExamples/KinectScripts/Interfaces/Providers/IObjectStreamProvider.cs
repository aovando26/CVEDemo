using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using com.rfilkov.kinect;

namespace com.rfilkov.providers
{
    public interface IObjectStreamProvider : IStreamProvider
    {
        /// <summary>
        /// Returns the object-index image scale.
        /// </summary>
        /// <returns>Object-index image scale</returns>
        Vector3 GetObjectIndexImageScale();

        /// <summary>
        /// Returns the object-index image size.
        /// </summary>
        /// <returns>Object-index image size (width, height)</returns>
        Vector2Int GetObjectIndexImageSize();

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

    }
}
