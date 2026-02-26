using UnityEngine;
using System.Collections;
using com.rfilkov.kinect;


namespace com.rfilkov.components
{
    public class HandColorOverlayer : MonoBehaviour
    {
        [Tooltip("Camera used to estimate the overlay positions of 3D-objects over the background. By default it is the main camera.")]
        public Camera foregroundCamera;

        [Tooltip("Index of the player, tracked by this component. 0 means the 1st player, 1 - the 2nd one, 2 - the 3rd one, etc.")]
        public int playerIndex = 0;

        [Tooltip("Game object used to overlay the left hand.")]
        public Transform leftHandOverlay;

        [Tooltip("Game object used to overlay the right hand.")]
        public Transform rightHandOverlay;

        //public float smoothFactor = 10f;

        // reference to KinectManager
        private KinectManager kinectManager;


        void Update()
        {
            if (foregroundCamera == null)
            {
                // by default use the main camera
                foregroundCamera = Camera.main;
            }

            if (kinectManager == null)
            {
                kinectManager = KinectManager.Instance;
            }

            if (kinectManager && kinectManager.IsInitialized() && foregroundCamera)
            {
                // get the background rectangle (use the portrait background, if available)
                Rect backgroundRect = foregroundCamera.pixelRect;
                PortraitBackground portraitBack = PortraitBackground.Instance;

                if (portraitBack && portraitBack.enabled)
                {
                    backgroundRect = portraitBack.GetBackgroundRect();
                }

                // overlay the joints
                if (kinectManager.IsUserDetected(playerIndex))
                {
                    ulong userId = kinectManager.GetUserIdByIndex(playerIndex);

                    OverlayJoint(userId, (int)KinectInterop.JointType.HandLeft, leftHandOverlay, backgroundRect);
                    OverlayJoint(userId, (int)KinectInterop.JointType.HandRight, rightHandOverlay, backgroundRect);
                }

            }
        }


        // overlays the given object over the given user joint
        private void OverlayJoint(ulong userId, int jointIndex, Transform overlayObj, Rect imageRect)
        {
            int sensorIndex = kinectManager.GetPrimaryBodySensorIndex();
            Vector3 screenPos = kinectManager.GetJointPosColorOverlay(userId, jointIndex, sensorIndex, imageRect);

            if (overlayObj && foregroundCamera && screenPos != Vector3.zero)
            {
                float zDistance = overlayObj.position.z - foregroundCamera.transform.position.z;
                Vector3 posJoint = foregroundCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, zDistance));

                overlayObj.position = posJoint;
            }
        }

    }
}
