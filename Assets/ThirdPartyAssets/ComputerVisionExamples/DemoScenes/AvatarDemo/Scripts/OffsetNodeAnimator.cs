using UnityEngine;
using System.Collections;


namespace com.rfilkov.components
{
    public class OffsetNodeAnimator : MonoBehaviour
    {
        [Tooltip("Speed at which the offset node moves through the scene, in m/s.")]
        public float moveSpeed = 1f;

        [Tooltip("Speed at which the offset node rotates, in deg/s.")]
        public float rotateSpeed = 30f;

        [Tooltip("Distance in meters from the starting position, where the offset node stops and rotates.")]
        public float moveDistance = 3f;


        private Vector3 startPos = Vector3.zero;
        private Quaternion startRot = Quaternion.identity;


        void Start ()
        {
            StartCoroutine(AnimationRoutine());
        }


        // animates the offset node by moving it to the specified distance and then rotating it at 90 degrees
        private IEnumerator AnimationRoutine()
        {
            startPos = transform.position;
            startRot = transform.rotation;

            while (true)
            {
                while((transform.position - startPos).magnitude < moveDistance)
                {
                    transform.Translate(Vector3.forward * moveSpeed * Time.deltaTime);
                    //Debug.Log($"Translated: {(transform.position - startPos):F3}");
                    yield return null;
                }

                startPos = transform.position;
                yield return new WaitForSeconds(0.5f);

                while (Quaternion.Angle(startRot, transform.rotation) < 90f)
                {
                    transform.Rotate(0f, rotateSpeed * Time.deltaTime, 0f);
                    //Debug.Log($"Rotated: {Quaternion.Angle(startRot, transform.rotation):F1}");
                    yield return null;
                }

                startRot = transform.rotation;
                yield return new WaitForSeconds(0.5f);
            }
        }

    }
}
