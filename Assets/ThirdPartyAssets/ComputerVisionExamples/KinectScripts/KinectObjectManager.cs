using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace com.rfilkov.kinect
{
    /// <summary>
    /// Kinect object manager is the component that tracks the objects in front of the sensor.
    /// </summary>
    public class KinectObjectManager : MonoBehaviour
    {

        [System.Serializable]
        public class KinectObjectEvent : UnityEvent<ulong, int> { }


        /// <summary>
        /// Fired when new object gets detected.
        /// </summary>
        [Tooltip("This event is fired, when new object gets detected.")]
        //public event System.Action<ulong, int> OnUserAdded;
        public KinectObjectEvent OnObjectAdded = new KinectObjectEvent();

        /// <summary>
        /// Fired when object gets removed.
        /// </summary>
        [Tooltip("This event is fired, when object gets removed.")]
        //public event System.Action<ulong, int> OnUserRemoved;
        public KinectObjectEvent OnObjectRemoved = new KinectObjectEvent();


        // List of all objects
        internal List<ulong> alObjectIds = new List<ulong>();
        internal Dictionary<ulong, int> dictObjectIdToIndex = new Dictionary<ulong, int>();
        internal ulong[] aObjectIndexIds = new ulong[KinectInterop.Constants.MaxObjectCount];
        internal Dictionary<ulong, float> dictObjectIdToTime = new Dictionary<ulong, float>();

        // reference to KM
        private KinectManager kinectManager = null;


        protected virtual void Start()
        {
            kinectManager = KinectManager.Instance;
        }


        // Returns empty object slot for the given objId
        protected virtual int GetEmptySlot()
        {
            int oidIndex = -1;

            // look for the 1st available slot
            for (int i = 0; i < aObjectIndexIds.Length; i++)
            {
                if (aObjectIndexIds[i] == 0)
                {
                    oidIndex = i;
                    break;
                }
            }

            return oidIndex;
        }


        // releases the object slot.
        protected virtual void FreeObjectSlot(int oidIndex)
        {
            aObjectIndexIds[oidIndex] = 0;
        }


        // Adds objId to the list of objects
        public virtual int AddObject(ulong objId, int objIndex)
        {
            if (!alObjectIds.Contains(objId))
            {
                int oidIndex = GetEmptySlot();

                if (oidIndex >= 0)
                {
                    aObjectIndexIds[oidIndex] = objId;
                }
                else
                {
                    // no empty object-index slot
                    return -1;
                }

                dictObjectIdToIndex[objId] = objIndex;
                dictObjectIdToTime[objId] = Time.time;
                alObjectIds.Add(objId);

                return oidIndex;
            }

            return -1;
        }


        // fires the OnObjectAdded-event
        internal void FireOnObjectAdded(ulong objId, int objIndex)
        {
            OnObjectAdded?.Invoke(objId, objIndex);
        }


        // Remove a lost objId
        public virtual int RemoveObject(ulong objId)
        {
            int uidIndex = System.Array.IndexOf(aObjectIndexIds, objId);

            // remove object-id from the global users lists
            dictObjectIdToIndex.Remove(objId);
            dictObjectIdToTime.Remove(objId);
            alObjectIds.Remove(objId);

            if (uidIndex >= 0)
            {
                FreeObjectSlot(uidIndex);
            }

            return uidIndex;
        }


        // fires the OnObjectRemoved-event
        internal void FireOnObjectRemoved(ulong objId, int objIndex)
        {
            OnObjectRemoved?.Invoke(objId, objIndex);
        }


        // prints the currently tracked object IDs and indices
        public void PrintManagedObjects(uint trackedObjectsCount, KinectInterop.ObjectData[] alTrackedObjects)
        {
            System.Text.StringBuilder sbBuf = new System.Text.StringBuilder();

            for (int i = 0; i < KinectInterop.Constants.MaxObjectCount; i++)
            {
                if (aObjectIndexIds[i] != 0)
                {
                    ulong objId = aObjectIndexIds[i];
                    int index = dictObjectIdToIndex.ContainsKey(objId) ? dictObjectIdToIndex[objId] : -1;

                    if (index >= 0 && index < trackedObjectsCount)
                    {
                        var objData = alTrackedObjects[index];
                        sbBuf.AppendLine($"objIndex: {i} - id: {objId}, index: {index}/{trackedObjectsCount}\ntype: {objData.objType}, id: {objData.trackingID}, oi: {objData.objIndex}, tracked: {objData.isTracked}");
                    }
                    else
                    {
                        sbBuf.AppendLine($"{i} - id: {objId}, index: {index}/{trackedObjectsCount}");
                    }
                }
            }

            Debug.Log(sbBuf.ToString());
        }

    }
}

