using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.rfilkov.providers
{
    public interface IIRStreamProvider : IStreamProvider
    {
        /// <summary>
        /// Returns the IR image scale.
        /// </summary>
        /// <returns>IR image scale</returns>
        Vector3 GetIRImageScale();

        /// <summary>
        /// Returns the minimum IR value.
        /// </summary>
        /// <returns>Min IR value</returns>
        float GetMinIRValue();

        /// <summary>
        /// Returns the maximum IR value.
        /// </summary>
        /// <returns>Max IR value</returns>
        float GetMaxIRValue();

        /// <summary>
        /// Returns the current resolution of the IR image.
        /// </summary>
        /// <returns>IR image size</returns>
        Vector2Int GetIRImageSize();

        /// <summary>
        /// Returns the BT-source image scale (how it was flipped, compared to the original image).
        /// </summary>
        /// <returns>BT image scale</returns>
        Vector3 GetBTSourceImageScale();

        /// <summary>
        /// Returns source image for body tracker.
        /// </summary>
        /// <returns>BT source image</returns>
        Texture GetBTSourceImage(FramesetData fsData);

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
