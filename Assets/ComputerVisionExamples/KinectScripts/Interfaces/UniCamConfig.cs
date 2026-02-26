using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Universal camera configuration.
/// </summary>
[CreateAssetMenu(fileName = "UniCamConfig", menuName = "Config / UniCam Configuration", order = 1)]
public class UniCamConfig : ScriptableObject
{
    /// <summary>
    /// Full class path to the color-stream provider, if any.
    /// </summary>
    //[TextArea]
    public string colorStreamProvider;

    /// <summary>
    /// Full class path to the depth-stream provider, if any.
    /// </summary>
    //[TextArea]
    public string depthStreamProvider;

    /// <summary>
    /// Full class path to the IR-stream provider, if any.
    /// </summary>
    //[TextArea]
    public string IRStreamProvider;

    /// <summary>
    /// Full class path to the body-stream provider, if any.
    /// </summary>
    //[TextArea]
    public string bodyStreamProvider;

    /// <summary>
    /// Full class path to the object-stream provider, if any.
    /// </summary>
    //[TextArea]
    public string objectStreamProvider;

    /// <summary>
    /// Full class path to the pose-stream provider, if any.
    /// </summary>
    //[TextArea]
    public string poseStreamProvider;

    /// <summary>
    /// Full class path to the camera-intrinsics stream provider, if any.
    /// </summary>
    //[TextArea]
    public string camIntrStreamProvider;

}
