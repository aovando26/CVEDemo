using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraLeftRightMover : MonoBehaviour
{
    [Tooltip("Time in seconds, from start till end")]
    public float timeToEnd = 10f;

    [Tooltip("Whether to go back after reaching the target")]
    public bool goBack = true;


    private Vector3 startPos, targetPos;
    private Quaternion startRot, targetRot;
    private float startTime, currentTime;


    void Start()
    {
        startPos = transform.position;

        startRot = transform.rotation;
        Vector3 startRotEuler = startRot.eulerAngles;

        targetPos = new Vector3(-startPos.x, startPos.y, startPos.z);
        targetRot = Quaternion.Euler(startRotEuler.x, -startRotEuler.y, startRotEuler.z);

        startTime = Time.time;
    }

    void Update()
    {
        currentTime = Time.time - startTime;
        float timePercent = Mathf.Clamp01(currentTime / timeToEnd);

        transform.position = Vector3.Lerp(startPos, targetPos, timePercent);
        transform.rotation = Quaternion.Lerp(startRot, targetRot, timePercent);

        if(timePercent == 1f && goBack)
        {
            Vector3 tempPos = startPos; startPos = targetPos; targetPos = tempPos;
            Quaternion tempRot = startRot; startRot = targetRot; targetRot = tempRot;

            startTime = Time.time;
        }
    }

}
