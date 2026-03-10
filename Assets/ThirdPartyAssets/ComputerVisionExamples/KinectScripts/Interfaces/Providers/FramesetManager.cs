using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using com.rfilkov.kinect;


namespace com.rfilkov.providers
{
    /// <summary>
    /// Frameset data holder.
    /// </summary>
    public class FramesetData
    {
        public int _fsIndex;
        public bool _isFramesetReady;
        public FramesetData _fsPrev;

        // color
        public bool _isColorDataCopied;
        public Texture _colorImage;
        public ulong _colorImageTimestamp;
        public bool _isColorImageReady;
        public int _colorProviderState;
        public object _colorProviderSettings;

        public Texture _scaledColorImage;
        public ulong _scaledColorImageTimestamp;
        public bool _isScaledColorImageReady;

        // depth
        public bool _isDepthDataCopied;
        public ushort[] _depthFrame;
        public ulong _depthFrameTimestamp;
        public bool _isDepthFrameReady;
        public int _depthProviderState;
        public object _depthProviderSettings;

        public ushort[] _scaledDepthFrame;
        public ulong _scaledDepthFrameTimestamp;
        public bool _isScaledDepthFrameReady;
        //public ComputeBuffer _scaledDepthBuf;

        // ir
        public bool _isIrDataCopied;
        public ushort[] _irFrame;
        public ulong _irFrameTimestamp;
        public bool _isIrFrameReady;
        public int _irProviderState;
        public object _irProviderSettings;

        public ushort[] _scaledIrFrame;
        public ulong _scaledIrFrameTimestamp;
        public bool _isScaledIrFrameReady;

        // body data
        public bool _isBodyDataCopied;
        public Texture _btSourceImage;
        public ulong _btSourceImageTimestamp;
        public bool _isBodyTrackerFrameReady;
        public ulong _bodyTrackerTimestamp;

        public uint _trackedBodiesCount;
        public List<KinectInterop.BodyData> _alTrackedBodies = new List<KinectInterop.BodyData>();

        public ulong _bodyDataTimestamp;
        public bool _isBodyDataFrameReady;
        public int _bodyProviderState;
        public object _bodyProviderSettings;

        // body index
        public byte[] _bodyIndexFrame;
        public ulong _bodyIndexTimestamp;
        public bool _isBodyIndexFrameReady;

        public byte[] _scaledBodyIndexFrame;
        public ulong _scaledBodyIndexFrameTimestamp;
        public bool _isScaledBodyIndexFrameReady;
        //public ComputeBuffer _scaledBodyIndexBuf;

        // object-tracking data
        public bool _isObjDataCopied;
        public ulong _objSourceImageTimestamp;
        public bool _isObjTrackerFrameReady;
        public ulong _objTrackerTimestamp;

        public uint _trackedObjCount;
        public List<KinectInterop.ObjectData> _alTrackedObjects = new List<KinectInterop.ObjectData>();

        public ulong _objDataTimestamp;
        public bool _isObjDataFrameReady;
        public int _objProviderState;
        public object _objProviderSettings;

        // object index
        public byte[] _objIndexFrame;
        public ulong _objIndexTimestamp;
        public bool _isObjIndexFrameReady;

        public byte[] _scaledObjIndexFrame;
        public ulong _scaledObjIndexFrameTimestamp;
        public bool _isScaledObjIndexFrameReady;

        // device data
        public object _rawFrame;
        public ulong _rawFrameTimestamp;
        public bool _isRawFrameReady;

        public object _procFrame;
        public ulong _procFrameTimestamp;
        public bool _isProcFrameReady;

        // reference frame ts
        public ulong _refDtTs;
        public ulong _refBtTs;
        public ulong _refOtTs;


        public override string ToString()
        {
            return $"index: {_fsIndex}, ts: {_rawFrameTimestamp}, ready: {_isFramesetReady}\nrfReady: {_isRawFrameReady}, pfReady: {_isProcFrameReady}\n" +
                $"cReady: {_isColorImageReady}, dReady: {_isDepthFrameReady}, bdReady: {_isBodyDataFrameReady}, biReady: {_isBodyIndexFrameReady}, odReady: {_isObjDataFrameReady}, oiReady: {_isObjIndexFrameReady}";
        }

        /// <summary>
        /// Clears frame-ready flags for all streams.
        /// </summary>
        public void ClearReadyFlags()
        {
            _isFramesetReady = false;

            _isColorImageReady = false;
            _isScaledColorImageReady = false;
            _colorProviderState = 0;

            _isDepthFrameReady = false;
            _isScaledDepthFrameReady = false;
            _depthProviderState = 0;

            _isIrFrameReady = false;
            _isScaledIrFrameReady = false;
            _irProviderState = 0;

            _isBodyTrackerFrameReady = false;
            _isBodyDataFrameReady = false;
            _isBodyIndexFrameReady = false;
            _isScaledBodyIndexFrameReady = false;
            _bodyProviderState = 0;

            _isObjTrackerFrameReady = false;
            _isObjDataFrameReady = false;
            _isObjIndexFrameReady = false;
            _isScaledObjIndexFrameReady = false;
            _objProviderState = 0;

            _isRawFrameReady = false;
            _isProcFrameReady = false;
            _rawFrameTimestamp = 0;
            _procFrameTimestamp = 0;

            _isColorDataCopied = false;
            _isDepthDataCopied = false;
            _isIrDataCopied = false;
            _isBodyDataCopied = false;
            _isObjDataCopied = false;

            _refDtTs = 0;
            _refBtTs = 0;
            _refOtTs = 0;

            //Debug.Log($"    fs: {_fsIndex} Cleared fs ready flags");
        }

        /// <summary>
        /// Checks if any of the frames in the frameset are ready.
        /// </summary>
        /// <returns>true if any of the frames is ready, false otherwise</returns>
        public bool IsAnyFrameReady()
        {
            return _isColorImageReady || _isScaledColorImageReady || _isDepthFrameReady || _isScaledDepthFrameReady || _isIrFrameReady || _isScaledIrFrameReady ||
                _isBodyDataFrameReady || _isBodyIndexFrameReady || _isScaledBodyIndexFrameReady || _isObjDataFrameReady || _isObjIndexFrameReady || _isScaledObjIndexFrameReady;
        }

        /// <summary>
        /// Copy color image data from the previous frame.
        /// </summary>
        public void CopyPrevColorData()
        {
            if (_fsPrev == null || _fsPrev._fsIndex == _fsIndex || _fsPrev._isColorDataCopied)
                return;

            if (!_fsPrev._isColorImageReady)
            {
                _colorImage = _fsPrev._colorImage; _fsPrev._colorImage = null;
                _colorImageTimestamp = _fsPrev._colorImageTimestamp;
                _isColorImageReady = _fsPrev._isColorImageReady;

                _colorProviderState = _fsPrev._colorProviderState;
                _colorProviderSettings = _fsPrev._colorProviderSettings; _fsPrev._colorProviderSettings = null;
            }

            //if (_fsPrev._isColorImageReady)
            else
            {
                // clear after ready
                _isColorImageReady = false;
                _colorProviderState = 0;
            }

            if (!_fsPrev._isScaledColorImageReady)
            {
                _scaledColorImage = _fsPrev._scaledColorImage; _fsPrev._scaledColorImage = null;
                _scaledColorImageTimestamp = _fsPrev._scaledColorImageTimestamp;
                _isScaledColorImageReady = _fsPrev._isScaledColorImageReady;
            }

            //if (_fsPrev._isScaledColorImageReady)
            else
                _isScaledColorImageReady = false;

            _fsPrev._isColorDataCopied = true;
            //Debug.Log($"    fs: {_fsIndex} - copied color frame {_fsPrev._fsIndex}, state: {_colorProviderState}\ncReady: {_isColorImageReady}, scReady: {_isScaledColorImageReady}");
        }

        /// <summary>
        /// Copy depth frame data from the previous frame.
        /// </summary>
        public void CopyPrevDepthData()
        {
            if (_fsPrev == null || _fsPrev._fsIndex == _fsIndex || _fsPrev._isDepthDataCopied)
                return;

            if (!_fsPrev._isDepthFrameReady)
            {
                _depthFrame = _fsPrev._depthFrame; _fsPrev._depthFrame = null;
                _depthFrameTimestamp = _fsPrev._depthFrameTimestamp;
                _isDepthFrameReady = _fsPrev._isDepthFrameReady;

                _depthProviderState = _fsPrev._depthProviderState;
                _depthProviderSettings = _fsPrev._depthProviderSettings; _fsPrev._depthProviderSettings = null;
            }

            //if (_fsPrev._isDepthFrameReady)
            else
            {
                // clear after ready
                _isDepthFrameReady = false;
                _depthProviderState = 0;
            }

            if (!_fsPrev._isScaledDepthFrameReady)
            {
                _scaledDepthFrame = _fsPrev._scaledDepthFrame; _fsPrev._scaledDepthFrame = null;
                _scaledDepthFrameTimestamp = _fsPrev._scaledDepthFrameTimestamp;
                _isScaledDepthFrameReady = _fsPrev._isScaledDepthFrameReady;
            }

            //if (_fsPrev._isScaledDepthFrameReady)
            else
                _isScaledDepthFrameReady = false;

            _refDtTs = _fsPrev._refDtTs;

            _fsPrev._isDepthDataCopied = true;
            //Debug.Log($"    fs: {_fsIndex} - copied depth frame {_fsPrev._fsIndex}, state: {_depthProviderState}\ndReady: {_isDepthFrameReady}, sdReady: {_isScaledDepthFrameReady}");
        }

        /// <summary>
        /// Copy IR frame data from the previous frame.
        /// </summary>
        public void CopyPrevIrData()
        {
            if (_fsPrev == null || _fsPrev._fsIndex == _fsIndex || _fsPrev._isIrDataCopied)
                return;

            if (!_fsPrev._isIrFrameReady)
            {
                _irFrame = _fsPrev._irFrame; _fsPrev._irFrame = null;
                _irFrameTimestamp = _fsPrev._irFrameTimestamp;
                _isIrFrameReady = _fsPrev._isIrFrameReady;

                _irProviderState = _fsPrev._irProviderState;
                _irProviderSettings = _fsPrev._irProviderSettings; _fsPrev._irProviderSettings = null;
            }

            //if (_fsPrev._isIrFrameReady)
            else
            {
                // clear after ready
                _isIrFrameReady = false;
                _irProviderState = 0;
            }

            if (!_fsPrev._isScaledIrFrameReady)
            {
                _scaledIrFrame = _fsPrev._scaledIrFrame; _fsPrev._scaledIrFrame = null;
                _scaledIrFrameTimestamp = _fsPrev._scaledIrFrameTimestamp;
                _isScaledIrFrameReady = _fsPrev._isScaledIrFrameReady;
            }

            //if (_fsPrev._isScaledIrFrameReady)
            else
                _isScaledIrFrameReady = false;

            _fsPrev._isIrDataCopied = true;
            //Debug.Log($"    fs: {_fsIndex} - copied ir frame {_fsPrev._fsIndex}, state: {_irProviderState}\nirReady: {_isIrFrameReady}, sirReady: {_isScaledIrFrameReady}");
        }

        /// <summary>
        /// Copy body frame data from the previous frame.
        /// </summary>
        public void CopyPrevBodyData()
        {
            if (_fsPrev == null || _fsPrev._fsIndex == _fsIndex || _fsPrev._isBodyDataCopied)
                return;

            // body tracker
            if (!_fsPrev._isBodyTrackerFrameReady)
            {
                _btSourceImage = _fsPrev._btSourceImage; _fsPrev._btSourceImage = null;
                _btSourceImageTimestamp = _fsPrev._btSourceImageTimestamp;
                _isBodyTrackerFrameReady = _fsPrev._isBodyTrackerFrameReady;
                _bodyTrackerTimestamp = _fsPrev._bodyTrackerTimestamp;
            }

            //if (_fsPrev._isBodyTrackerFrameReady)  // clear after ready
            else
                _isBodyTrackerFrameReady = false;

            // body data
            if(!_isBodyDataFrameReady)
            {
                _trackedBodiesCount = _fsPrev._trackedBodiesCount;
                _alTrackedBodies = _fsPrev._alTrackedBodies;

                 _bodyDataTimestamp = _fsPrev._bodyDataTimestamp;
                _isBodyDataFrameReady = _fsPrev._isBodyDataFrameReady;

                _bodyProviderState = _fsPrev._bodyProviderState;
                _bodyProviderSettings = _fsPrev._bodyProviderSettings; _fsPrev._bodyProviderSettings = null;
            }

            if (_fsPrev._isBodyDataFrameReady)
            {
                // clear after ready
                _isBodyDataFrameReady = false;
                if(_bodyProviderState == 6)  // only if it's ready (to not override a different state)
                {
                    _bodyProviderState = 0;
                }
            }

            // body index
            if (!_fsPrev._isBodyIndexFrameReady)
            {
                _bodyIndexFrame = _fsPrev._bodyIndexFrame; _fsPrev._bodyIndexFrame = null;
                _bodyIndexTimestamp = _fsPrev._bodyIndexTimestamp;
                _isBodyIndexFrameReady = _fsPrev._isBodyIndexFrameReady;
            }

            //if (_fsPrev._isBodyIndexFrameReady)  // clear after ready
            else
                _isBodyIndexFrameReady = false;

            // scaled body index
            if (!_fsPrev._isScaledBodyIndexFrameReady)
            {
                _scaledBodyIndexFrame = _fsPrev._scaledBodyIndexFrame; _fsPrev._scaledBodyIndexFrame = null; 
                _scaledBodyIndexFrameTimestamp = _fsPrev._scaledBodyIndexFrameTimestamp;
                _isScaledBodyIndexFrameReady = _fsPrev._isScaledBodyIndexFrameReady;
            }

            //if (_fsPrev._isScaledBodyIndexFrameReady)  // clear after ready
            else
                _isScaledBodyIndexFrameReady = false;

            _refBtTs = _fsPrev._refBtTs;

            _fsPrev._isBodyDataCopied = true;
            //Debug.Log($"    fs: {_fsIndex} - copied body frame {_fsPrev._fsIndex}, state: {_bodyProviderState}, btReady: {_isBodyTrackerFrameReady}\nbdReady: {_isBodyDataFrameReady}, biReady: {_isBodyIndexFrameReady}, sbiReady: {_isScaledBodyIndexFrameReady}");
        }

        /// <summary>
        /// Copy object-frame data from the previous frame.
        /// </summary>
        public void CopyPrevObjData()
        {
            if (_fsPrev == null || _fsPrev._fsIndex == _fsIndex || _fsPrev._isObjDataCopied)
                return;

            // object tracker
            if (!_fsPrev._isObjTrackerFrameReady)
            {
                _objSourceImageTimestamp = _fsPrev._objSourceImageTimestamp;
                _isObjTrackerFrameReady = _fsPrev._isObjTrackerFrameReady;
                _objTrackerTimestamp = _fsPrev._objTrackerTimestamp;
            }

            //if (_fsPrev._isObjTrackerFrameReady)  // clear after ready
            else
                _isObjTrackerFrameReady = false;

            // object data
            if (!_isObjDataFrameReady)
            {
                _trackedObjCount = _fsPrev._trackedObjCount;
                _alTrackedObjects = _fsPrev._alTrackedObjects;

                _objDataTimestamp = _fsPrev._objDataTimestamp;
                _isObjDataFrameReady = _fsPrev._isObjDataFrameReady;

                _objProviderState = _fsPrev._objProviderState;
                _objProviderSettings = _fsPrev._objProviderSettings; _fsPrev._objProviderSettings = null;
                //Debug.Log($"    fs: {_fsIndex} CopyPrevObjData - _objProviderState: {_objProviderState}\nready: {_isObjDataFrameReady}, prevReady: {_fsPrev._isObjDataFrameReady}, prevState: {_fsPrev._objProviderState}, fsPrev: {_fsPrev._fsIndex}");
            }

            if (_fsPrev._isObjDataFrameReady)
            {
                // clear after ready
                _isObjDataFrameReady = false;
                if (_objProviderState == 3)  // only if it's ready (to not override a different state)
                {
                    _objProviderState = 0;
                    //Debug.Log($"    fs: {_fsIndex} PrevObjFrameReady - _objProviderState: {_objProviderState}\nfsPrev: {_fsPrev._fsIndex}, prevReady: {_fsPrev._isObjDataFrameReady}, prevState: {_fsPrev._objProviderState}");
                }
            }

            // object index
            if (!_fsPrev._isObjIndexFrameReady)
            {
                _objIndexFrame = _fsPrev._objIndexFrame; _fsPrev._objIndexFrame = null;
                _objIndexTimestamp = _fsPrev._objIndexTimestamp;
                _isObjIndexFrameReady = _fsPrev._isObjIndexFrameReady;
            }

            //if (_fsPrev._isObjIndexFrameReady)  // clear after ready
            else
                _isObjIndexFrameReady = false;

            // scaled object index
            if (!_fsPrev._isScaledObjIndexFrameReady)
            {
                _scaledObjIndexFrame = _fsPrev._scaledObjIndexFrame; _fsPrev._scaledObjIndexFrame = null;
                _scaledObjIndexFrameTimestamp = _fsPrev._scaledObjIndexFrameTimestamp;
                _isScaledObjIndexFrameReady = _fsPrev._isScaledObjIndexFrameReady;
            }

            //if (_fsPrev._isScaledObjIndexFrameReady)  // clear after ready
            else
                _isScaledObjIndexFrameReady = false;

            _refOtTs = _fsPrev._refOtTs;

            _fsPrev._isObjDataCopied = true;
            //Debug.Log($"    fs: {_fsIndex} - copied obj frame {_fsPrev._fsIndex}, state: {_objProviderState}, objReady: {_isObjTrackerFrameReady}\nodReady: {_isObjDataFrameReady}, oiReady: {_isObjIndexFrameReady}, soiReady: {_isScaledObjIndexFrameReady}");
        }

    }


    /// <summary>
    /// Frameset manager - holds and manages the framesets for write and read.
    /// </summary>
    public class FramesetManager
    {
        // number of framesets in the list
        public const int FRAMESETS_COUNT = 10;

        // maximum processing framesets count (at the same time)
        private const int MAX_PROCESSING_FS = 3;

        // FramesetManager singleton instance
        private static FramesetManager _instance = null;

        // frameset data
        private FramesetData[] _framesets = new FramesetData[FRAMESETS_COUNT];

        // frameset indices
        private int _readIndex = 0;
        private int _writeIndex = 0;
        private int _prevWriteIndex = -1;


        /// <summary>
        /// Singleton instance of the FramesetManager.
        /// </summary>
        public static FramesetManager Instance
        {
            get
            {
                if(_instance == null)
                {
                    _instance = new FramesetManager();
                }

                return _instance;
            }
        }

        /// <summary>
        /// Resets read and write indices.
        /// </summary>
        public void ResetFramesetIndices()
        {
            _readIndex = 0;
            _writeIndex = 0;
            _prevWriteIndex = -1;
        }

        /// <summary>
        /// Gets read index.
        /// </summary>
        /// <returns>Read index</returns>
        public int GetReadIndex()
        {
            return _readIndex;
        }

        /// <summary>
        /// Gets write index.
        /// </summary>
        /// <returns>Write index</returns>
        public int GetWriteIndex()
        {
            return _writeIndex;
        }

        /// <summary>
        /// Gets next read index.
        /// </summary>
        /// <returns>Next read index</returns>
        public int GetNextReadIndex()
        {
            return (_readIndex + 1) % FRAMESETS_COUNT;
        }

        /// <summary>
        /// Increments read index.
        /// </summary>
        /// <returns>true on success, false otherwise</returns>
        public bool IncReadIndex()
        {
            if (_readIndex != _writeIndex)
            {
                _readIndex = (_readIndex + 1) % FRAMESETS_COUNT;
                //Debug.Log($"  readIndex set to {_readIndex}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets next write index.
        /// </summary>
        /// <returns>Next write index</returns>
        public int GetNextWriteIndex()
        {
            return (_writeIndex + 1) % FRAMESETS_COUNT;
        }

        /// <summary>
        /// Increments write index.
        /// </summary>
        /// <returns>true on success, false otherwise</returns>
        public bool IncWriteIndex()
        {
            int newIndex = (_writeIndex + 1) % FRAMESETS_COUNT;
            if (newIndex != _readIndex)
            {
                int inProcCount = GetProcessingWriteSlotsCount();
                if(inProcCount < MAX_PROCESSING_FS)
                {
                    _prevWriteIndex = _writeIndex;
                    _writeIndex = newIndex;

                    //Debug.Log($"  writeIndex set to {_writeIndex}, prevIndex: {_prevWriteIndex}, processing: {inProcCount + 1}/{MAX_PROCESSING_FS}");
                    return true;
                }
            }

            return false;
        }

        // returns the count of write slots currently in processing
        private int GetProcessingWriteSlotsCount()
        {
            int openFsCount = OpenFramesetsCount;
            int inProcCount = 0;

            for (int i = 0; i < openFsCount; i++)
            {
                FramesetData fsData = GetOpenFramesetAt(i);
                if (fsData != null && !fsData._isFramesetReady)
                    inProcCount++;
            }

            return inProcCount;
        }


        /// <summary>
        /// Returns the current read frameset.
        /// </summary>
        /// <returns>Current read frameset</returns>
        public FramesetData GetCurrentFrameset()
        {
            return _readIndex != _writeIndex ? _framesets [_readIndex] : null;
        }

        /// <summary>
        /// Returns the current write frameset.
        /// </summary>
        /// <returns>Current write frameset</returns>
        public FramesetData GetLastOpenFrameset()
        {
            return _framesets[_writeIndex];
        }

        /// <summary>
        /// Returns the previous write frameset.
        /// </summary>
        /// <returns>Previous write frameset</returns>
        public FramesetData GetPrevOpenFrameset()
        {
            return _prevWriteIndex >= 0 ? _framesets[_prevWriteIndex] : null;
        }

        /// <summary>
        /// Returns the index of first open frame that is not ready, or -1 if not found
        /// </summary>
        /// <returns>The index of first open frame, or -1 if not found</returns>
        public int GetFirstOpenFrameIndex()
        {
            int openFsCount = OpenFramesetsCount;

            for (int i = 0; i < openFsCount; i++)
            {
                FramesetData fsData = GetOpenFramesetAt(i);
                if (fsData != null && !fsData._isFramesetReady)
                    return fsData._fsIndex;
            }

            return -1;
        }


        /// <summary>
        /// Returns the number of write framesets.
        /// </summary>
        public int OpenFramesetsCount
        {
            get
            {
                int writeIndex = _writeIndex < _readIndex ? _writeIndex + FRAMESETS_COUNT : _writeIndex;
                return (writeIndex - _readIndex + 1);
            }
        }

        /// <summary>
        /// Returns the write frameset at the given index.
        /// </summary>
        /// <param name="index">Open frameset index</param>
        /// <returns>Frameset data, or null if not found</returns>
        public FramesetData GetOpenFramesetAt(int index)
        {
            int fsIndex = (_readIndex + index) % FRAMESETS_COUNT;
            int writeIndex = _writeIndex < _readIndex ? _writeIndex + FRAMESETS_COUNT : _writeIndex;
            int writeCount = writeIndex - _readIndex + 1;

            if (index >= 0 && index <= writeCount)
            {
                return _framesets[fsIndex];
            }

            return null;
        }

        /// <summary>
        /// Returns the frameset at the given index.
        /// </summary>
        /// <param name="index">Frameset index</param>
        /// <returns>Frameset data, or null if not found</returns>
        public FramesetData GetFramesetAt(int index)
        {
            if (index >= 0 && index < FRAMESETS_COUNT)
            {
                return _framesets[index];
            }

            return null;
        }

        /// <summary>
        /// Creates the frameset manager.
        /// </summary>
        public FramesetManager()
        {
            //// set instance or destroy the extra manager
            //if (Instance != null)
            //{
            //    Destroy(this);
            //    return;
            //}

            //_instance = this;

            for(int i = 0; i < FRAMESETS_COUNT; i++)
            {
                _framesets[i] = new FramesetData();
                _framesets[i]._fsIndex = i;
            }

            // to avoid prev = null
            _framesets[0]._fsPrev = _framesets[0];
        }

    }
}
