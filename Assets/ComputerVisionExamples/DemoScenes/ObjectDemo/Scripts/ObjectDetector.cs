using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using com.rfilkov.kinect;


namespace com.rfilkov.components
{
    public class ObjectDetector : MonoBehaviour
    {
        [Tooltip("Depth sensor index - 0 is the 1st one, 1 - the 2nd one, etc.")]
        public int sensorIndex = 0;

        [Tooltip("Camera used to estimate the overlay positions of 3D-objects over the background. By default it is the main camera.")]
        public Camera foregroundCamera;

        [Tooltip("Blob prefab, used to represent the blob in the 3D space.")]
        public GameObject objectPrefab;

        [Tooltip("The blobs root object.")]
        public GameObject objectsRoot;

        [Tooltip("UI-Text to display info messages.")]
        public Text infoText;


        // reference to KM
        private KinectManager kinectManager = null;

        // background rectangle
        private Rect backgroundRect = Rect.zero;

        // list of cubes
        private List<ObjectCenter> objCenters = new List<ObjectCenter>();

        // object-center offset
        private readonly Vector3 objCenterOffset = new Vector3(0f, 0.05f, 0f);


        // contains data about the object center on screen
        private class ObjectCenter
        {
            public GameObject gameObject;
            public Transform transform;
            public TextMesh titleText;

            public ObjectCenter(GameObject go)
            {
                gameObject = go;
                transform = go.transform;
            }
        }


        void Start()
        {
            if (objectsRoot == null)
            {
                objectsRoot = gameObject;  // new GameObject("ObjectsRoot");
            }

            if (foregroundCamera == null)
            {
                // by default use the main camera
                foregroundCamera = Camera.main;
            }
        }

        void Update()
        {
            if(kinectManager == null)
            {
                kinectManager = KinectManager.Instance;
            }

            if (kinectManager == null || !kinectManager.IsInitialized())
                return;

            if (foregroundCamera)
            {
                // get the background rectangle (use the portrait background, if available)
                backgroundRect = foregroundCamera.pixelRect;
                PortraitBackground portraitBack = PortraitBackground.Instance;

                if (portraitBack && portraitBack.enabled)
                {
                    backgroundRect = portraitBack.GetBackgroundRect();
                }
            }

            if (objectPrefab)
            {
                // instantiates representative blob objects for each blog
                InstantiateObjCenters();
            }

            if (infoText)
            {
                int numObjects = kinectManager.GetObjectCount();
                string sMessage = numObjects + " objects detected:\n";

                for (int o = 0; o < numObjects; o++)
                {
                    var objData = kinectManager.GetObjectData(o);

                    //if (objData != null)
                    //{
                    //    var objectManager = kinectManager.objectManager;
                    //    bool isObjId = objectManager.dictObjectIdToIndex.ContainsKey(objData.trackingID);
                    //    int objIndex = isObjId ? objectManager.dictObjectIdToIndex[objData.trackingID] : -1;
                    //    var objData0 = kinectManager.GetObjectData(objData.trackingID);

                    //    //Debug.Log($"objIndex: {o}, objId: {objData.trackingID}, valid: {isObjId}, index: {objIndex}\ntype: {objData.objType}, id: {objData.trackingID}, oi: {objData0.objIndex}, tracked: {objData.isTracked}");
                    //}

                    if (objData != null && objData.isTracked)
                    {
                        sMessage += $"Object {objData.trackingID} {objData.objType} at {objData.position:F2}\n";  //  - {objData.kinectPos:F2} - {objData.center2d}
                    }
                }

                //Debug.Log(sMessage);
                infoText.text = sMessage;
            }
        }


        // instantiates object centers for each object
        private void InstantiateObjCenters()
        {
            int numObjects = kinectManager.GetObjectCount();

            for (int o = 0; o < numObjects; o++)
            {
                while (o >= objCenters.Count)
                {
                    GameObject objInstance = Instantiate(objectPrefab, new Vector3(0, 0, -10), Quaternion.identity);
                    ObjectCenter objCenter = new ObjectCenter(objInstance);

                    objCenters.Add(objCenter);
                    objCenter.transform.parent = objectsRoot.transform;

                    TextMesh objTitle = objCenters[o].gameObject.GetComponentInChildren<TextMesh>();
                    objCenter.titleText = objTitle;
                }

                var objData = kinectManager.GetObjectData(o);
                ulong objId = objData.trackingID;

                Vector3 objSensorPos = kinectManager.GetObjectKinectPosition(objId, applySpaceScale: false);
                Vector3 objCenterPos = kinectManager.GetPosColorOverlay(objSensorPos, sensorIndex, foregroundCamera, backgroundRect);

                objCenters[o].transform.position = objCenterPos + objCenterOffset;
                objCenters[o].gameObject.name = "Object" + objId;

                if(objCenters[o].titleText != null)
                {
                    var objType = kinectManager.GetObjectType(objId);
                    objCenters[o].titleText.text = $"{objId}/{objType}";
                }
            }

            // remove the extra centers
            for (int o = objCenters.Count - 1; o >= numObjects; o--)
            {
                Destroy(objCenters[o].gameObject);
                objCenters.RemoveAt(o);
            }
        }

    }
}
