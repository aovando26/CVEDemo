using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.rfilkov.trackers
{

    public enum TrackState : byte
    {
        New = 0,
        Tracked = 1,
        Lost = 2,
        Removed = 3,
    }


    public class STrack
    {
        private readonly KalmanFilter _filter = new KalmanFilter();
        //private float[,] _f1x8Mean = new float[1, 8];
        //private float[,] _f8x8Covariance = new float[8, 8];
        private Vector4 _posMean, _sizeMean;
        private Matrix4x4 _posCovariance, _sizeCovariance;

        public int _objType = 0;
        public RectBox _rectBox;
        public TrackState _state = TrackState.New;
        public bool _isActivated = false;
        public float _score;

        public ulong _trackId = 0;
        public ulong _frameTs = 0;
        public ulong _startFrameTs = 0;
        // public int _trackletLen = 0;

        public object _dataObj;


        public STrack(RectBox rectBox, float score)
        {
            _rectBox = rectBox;
            _score = score;
        }

        /// <summary>
        /// 
        /// </summary>
        public RectBox RectBox => _rectBox;

        /// <summary>
        /// 
        /// </summary>
        public TrackState State => _state;

        /// <summary>
        /// 
        /// </summary>
        public bool IsActivated => _isActivated;

        /// <summary>
        /// 
        /// </summary>
        public float Score => _score;

        /// <summary>
        /// 
        /// </summary>
        public ulong TrackId => _trackId;

        /// <summary>
        /// 
        /// </summary>
        public ulong FrameTs => _frameTs;

        /// <summary>
        /// 
        /// </summary>
        public ulong StartFrameTs => _startFrameTs;

        ///// <summary>
        ///// 
        ///// </summary>
        //public int TrackletLength => _trackletLen;


        public override string ToString()
        {
            return $"id: {_trackId}, active: {_isActivated}, state: {_state}, score: {_score:F3}, rect: {_rectBox}, ts: {_frameTs}, start: {_startFrameTs}, pos: {(Vector2)_posMean:F2}, size: {(Vector2)_sizeMean:F2}";
        }


        private void UpdateRect()
        {
            //_rectBox.width = _f1x8Mean[0, 2] * _f1x8Mean[0, 3];
            //_rectBox.height = _f1x8Mean[0, 3];
            //_rectBox.xMin = _f1x8Mean[0, 0] - _rectBox.width * 0.5f;
            //_rectBox.yMin = _f1x8Mean[0, 1] - _rectBox.height * 0.5f;
            _rectBox.UpdateRect(_posMean.x, _posMean.y, _sizeMean.x * _sizeMean.y, _sizeMean.y);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="frameTs"></param>
        /// <param name="trackId"></param>
        public void Activate(ulong frameTs, ulong trackId, bool isFirstUpdate)
        {
            _filter.Initiate(ref _posMean, ref _sizeMean, ref _posCovariance, ref _sizeCovariance, _rectBox.ToXYAH());

            UpdateRect();
            //Debug.Log($"    Activate.UpdateRect({this})");

            _state = TrackState.Tracked;
            if (isFirstUpdate)
            {
                _isActivated = true;
            }

            _trackId = trackId;
            _frameTs = frameTs;
            _startFrameTs = frameTs;
            //_trackletLen = 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="newTrack"></param>
        /// <param name="frameTs"></param>
        /// <param name="newTrackId"></param>
        public void ReActivate(STrack newTrack, ulong frameTs)
        {
            _filter.Update(ref _posMean, ref _sizeMean, ref _posCovariance, ref _sizeCovariance, newTrack.RectBox.ToXYAH());

            UpdateRect();
            //Debug.Log($"    ReActivate.UpdateRect({this})");

            _state = TrackState.Tracked;
            _isActivated = true;
            _score = newTrack.Score;

            _frameTs = frameTs;
            //_trackletLen = 0;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Predict()
        {
            if (_state != TrackState.Tracked)
            {
                _sizeMean.y = 0f;
            }

            _filter.Predict(ref _posMean, ref _sizeMean, ref _posCovariance, ref _sizeCovariance);
        }

        /// <summary>
        /// Updates the track rect, score and frameId.
        /// </summary>
        /// <param name="newTrack"></param>
        /// <param name="frameTs"></param>
        public void Update(STrack newTrack, ulong frameTs)
        {
            _filter.Update(ref _posMean, ref _sizeMean, ref _posCovariance, ref _sizeCovariance, newTrack.RectBox.ToXYAH());

            UpdateRect();
            //Debug.Log($"    Update.UpdateRect({this})");

            _state = TrackState.Tracked;
            _isActivated = true;
            _score = newTrack.Score;
            _frameTs = frameTs;
            //_trackletLen++;
        }

        /// <summary>
        /// Copy current track parameters to other track.
        /// </summary>
        /// <param name="track">Other track</param>
        public void CopyTo(STrack track)
        {
            track._objType = _objType;
            track._rectBox = _rectBox;

            track._state = _state;
            track._isActivated = _isActivated;
            track._score = _score;

            track._trackId = _trackId;
            track._frameTs = _frameTs;
            track._startFrameTs = _startFrameTs;

            track._dataObj = _dataObj;
        }

        /// <summary>
        /// Marks track as lost.
        /// </summary>
        public void MarkAsLost() => _state = TrackState.Lost;

        /// <summary>
        /// Marks track as removed.
        /// </summary>
        public void MarkAsRemoved() => _state = TrackState.Removed;

    }
}