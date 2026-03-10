using System.Collections;
using System.Collections.Generic;
using System.Linq;


namespace com.rfilkov.trackers
{
    public class ByteTracker
    {
        /// <summary>
        /// Threshold for the first association.
        /// </summary>
        public float trackHighThresh = 0.5f;

        /// <summary>
        /// Threshold for the second association.
        /// </summary>
        public float trackLowThresh = 0.2f;  // 0.1f

        /// <summary>
        /// Threshold for new track initialization, if the detection does not match any tracks.
        /// </summary>
        public float newTrackThresh = 0.6f;

        /// <summary>
        /// Threshold for matching tracks.
        /// </summary>
        public float matchThresh = 0.8f;


        /// <summary>
        /// List of currently tracked tracks.
        /// </summary>
        public List<STrack> TrackedTracks => _trackedTracks;

        /// <summary>
        /// List of currently lost tracks.
        /// </summary>
        public List<STrack> LostTracks => _lostTracks;

        /// <summary>
        /// List of removed tracks.
        /// </summary>
        public List<STrack> RemovedTracks => _removedTracks;

        /// <summary>
        /// List of all (tracked and lost) tracks;
        /// </summary>
        public List<STrack> AllTracks => _allTracks;


        // tracks of different types
        private List<STrack> _trackedTracks = new List<STrack>(10);
        private Dictionary<ulong, STrack> _trackedIdToTrack = new Dictionary<ulong, STrack>();
        private List<STrack> _allTracks = new List<STrack>(10);

        private List<STrack> _lostTracks = new List<STrack>(10);
        private List<STrack> _removedTracks = new List<STrack>(10);
        private HashSet<ulong> _removedTrackIds = new HashSet<ulong>();

        // maximum number of removed tracks in the list
        private const int MAX_REMOVED_TRACKS = 20;

        // maximum time a track to remain lost
        private const ulong MAX_TIME_LOST = 5000000;  // 0.5 second(s)

        // local variables
        //private ulong _frameId = 0;
        private ulong _trackIdCount = 0;
        private bool _isFirstUpdate = false;


        public ByteTracker()
        {
            _isFirstUpdate = true;
        }

        /// <summary>
        /// Updates tracking lists.
        /// </summary>
        /// <param name="detObjects">Detected objects</param>
        /// <returns>Tracked IDs to tracks</returns>
        public Dictionary<ulong, STrack> Update(providers.FramesetData fsData, IList<STrack> detObjects, ulong frameTs)
        {
            #region Step 1: Get detections 
            //_frameId++;

            // Create new Tracks using the result of object detection
            List<STrack> detHighTracks = new List<STrack>();
            List<STrack> detLowTracks = new List<STrack>();

            foreach (STrack track in detObjects)
            {
                if (track.Score >= trackHighThresh)
                {
                    detHighTracks.Add(track);
                }
                else if (track.Score >= trackLowThresh)
                {
                    detLowTracks.Add(track);
                }
            }

            // Create lists of existing STrack
            List<STrack> activeTracks = new List<STrack>();
            List<STrack> nonActiveTracks = new List<STrack>();
            List<STrack> lostActiveTracks = new List<STrack>();

            // determine all tracked tracks
            //List<STrack> trackPool = new List<STrack>();
            //HashSet<ulong> trackPoolIds = new HashSet<ulong>();

            foreach (STrack track in _trackedTracks)
            {
                if (!track.IsActivated)
                {
                    nonActiveTracks.Add(track);
                }
                else
                {
                    activeTracks.Add(track);

                    //trackPool.Add(track);
                    //trackPoolIds.Add(track.TrackId);
                }

                // predict current pose
                track.Predict();
            }

            // add lost tracks to track-pool
            foreach (STrack track in _lostTracks)
            {
                //if(!trackPoolIds.Contains(track.TrackId))
                //{
                //    trackPool.Add(track);
                //    trackPoolIds.Add(track.TrackId);
                //}

                lostActiveTracks.Add(track);

                // predict current pose
                track.Predict();
            }

            //// predict current pose
            //foreach (STrack track in trackPool)
            //{
            //    track.Predict();
            //}
            #endregion

            #region Step 2: First association, with high score detection boxes
            List<STrack> activatedTracks = new List<STrack>();
            List<STrack> refoundTracks = new List<STrack>();

            List<STrack> unconfirmedDetections = new List<STrack>();
            List<STrack> unconfirmedTracks = new List<STrack>();
            {
                // active tracks
                float[,] dists = CalcIouDistance(activeTracks, detHighTracks);
                LinearAssignment("s2-active", dists, activeTracks.Count, detHighTracks.Count, matchThresh,
                    out var matchesIdx,
                    out var unmatchTrackIdx,
                    out var unmatchDetectionIdx);

                foreach (var (ti, di) in matchesIdx)
                {
                    STrack track = activeTracks[ti];
                    STrack det = detHighTracks[di];
                    //UnityEngine.Debug.Log($"    s2-matched active track {track} to\n        det: {det}\ndist: {dists[ti, di]:F2}, thresh: {matchThresh:F2}\n{MatOps.ToString(dists)}");

                    //if(track._objType == det._objType)
                    {
                        if (track.State == TrackState.Tracked)
                        {
                            track.Update(det, frameTs);
                            activatedTracks.Add(track);
                        }
                        else
                        {
                            track.ReActivate(det, frameTs);
                            refoundTracks.Add(track);
                        }

                        track.CopyTo(det);
                    }
                    //else
                    //{
                    //    // obj types don't match
                    //    unmatchTrackIdx.Add(ti);
                    //    unmatchDetectionIdx.Add(di);
                    //}
                }

                // unconfirmed detections
                List<STrack> detHighTracks2 = new List<STrack>();
                foreach (var di in unmatchDetectionIdx)
                {
                    STrack det = detHighTracks[di];
                    detHighTracks2.Add(det);
                }

                // unconfirmed tracks
                foreach (var ti in unmatchTrackIdx)
                {
                    STrack track = activeTracks[ti];

                    if (track.State == TrackState.Tracked)
                    {
                        unconfirmedTracks.Add(track);
                    }
                }

                activeTracks.Clear();
                detHighTracks.Clear();

                // lost tracks
                dists = CalcIouDistance(lostActiveTracks, detHighTracks2);
                LinearAssignment("s2-lost", dists, lostActiveTracks.Count, detHighTracks2.Count, matchThresh,
                    out matchesIdx,
                    out unmatchTrackIdx,
                    out unmatchDetectionIdx);

                foreach (var (ti, di) in matchesIdx)
                {
                    STrack track = lostActiveTracks[ti];
                    STrack det = detHighTracks2[di];
                    //UnityEngine.Debug.Log($"    s2-matched lost track {track} to\n        det: {det}\ndist: {dists[ti, di]:F2}, thresh: {matchThresh:F2}\n{MatOps.ToString(dists)}");

                    //if(track._objType == det._objType)
                    {
                        if (track.State == TrackState.Tracked)
                        {
                            track.Update(det, frameTs);
                            activatedTracks.Add(track);
                        }
                        else
                        {
                            track.ReActivate(det, frameTs);
                            refoundTracks.Add(track);
                        }

                        track.CopyTo(det);
                    }
                }

                // unconfirmed detections
                foreach (var di in unmatchDetectionIdx)
                {
                    STrack det = detHighTracks2[di];
                    unconfirmedDetections.Add(det);
                }

                // unconfirmed tracks
                foreach (var ti in unmatchTrackIdx)
                {
                    STrack track = lostActiveTracks[ti];

                    if (track.State == TrackState.Tracked)
                    {
                        unconfirmedTracks.Add(track);
                    }
                }

                lostActiveTracks.Clear();
                detHighTracks2.Clear();
            }
            #endregion

            #region Step 3: Second association, with unconfirmed tracks to the low score detections
            List<STrack> lostTracks = new List<STrack>();
            {
                float[,] dists = CalcIouDistance(unconfirmedTracks, detLowTracks);
                LinearAssignment("s3-unconfirmed", dists, unconfirmedTracks.Count, detLowTracks.Count, 0.5f,
                                    out var matchesIdx,
                                    out var unmatchTrackIdx,
                                    out var unmatchDetectionIdx);

                foreach (var (ti, di) in matchesIdx)
                {
                    STrack track = unconfirmedTracks[ti];
                    STrack det = detLowTracks[di];
                    //UnityEngine.Debug.Log($"    s3-matched track {track} to\n        det: {det}\ndist: {dists[ti, di]:F2}, thresh: {0.5f:F2}\n{MatOps.ToString(dists)}");

                    //if (track._objType == det._objType)
                    {
                        if (track.State == TrackState.Tracked)
                        {
                            track.Update(det, frameTs);
                            activatedTracks.Add(track);
                        }
                        else
                        {
                            track.ReActivate(det, frameTs);
                            refoundTracks.Add(track);
                        }

                        track.CopyTo(det);
                    }
                    //else
                    //{
                    //    // obj types don't match
                    //    unmatchTrackIdx.Add(ti);
                    //    unmatchDetectionIdx.Add(di);
                    //}
                }

                // lost tracks
                foreach (var ti in unmatchTrackIdx)
                {
                    STrack track = unconfirmedTracks[ti];

                    if (track.State != TrackState.Lost)
                    {
                        track.MarkAsLost();
                        lostTracks.Add(track);
                    }
                }

                detLowTracks.Clear();
                unconfirmedTracks.Clear();
            }
            #endregion

            #region Step 4: Init new tracks 
            List<STrack> removedTracks = new List<STrack>();
            {
                // Deal with unconfirmed tracks, usually tracks with only one beginning frame
                float[,] dists = CalcIouDistance(nonActiveTracks, unconfirmedDetections);
                LinearAssignment("s3-inactive", dists, nonActiveTracks.Count, unconfirmedDetections.Count, 0.7f,
                                    out var matchesIdx,
                                    out var unmatchUnconfirmedIdx,
                                    out var unmatchDetectionIdx);

                foreach (var (ti, di) in matchesIdx)
                {
                    STrack track = nonActiveTracks[ti];
                    STrack det = unconfirmedDetections[di];
                    //UnityEngine.Debug.Log($"    s4-matched track {track} to\n        det: {det}\ndist: {dists[ti, di]:F2}, thresh: {0.7f:F2}\n{MatOps.ToString(dists)}");

                    //if (track._objType == det._objType)
                    {
                        track.Update(det, frameTs);
                        activatedTracks.Add(track);

                        track.CopyTo(det);
                    }
                    //else
                    //{
                    //    // obj types don't match
                    //    unmatchUnconfirmedIdx.Add(ti);
                    //    unmatchDetectionIdx.Add(di);
                    //}
                }

                // lose unconfirmed tracks
                foreach (var ti in unmatchUnconfirmedIdx)
                {
                    STrack track = nonActiveTracks[ti];
                    //UnityEngine.Debug.Log($"    s4-lost non-active track {track}");

                    //track.MarkAsRemoved();
                    //removedTracks.Add(track);

                    track.MarkAsLost();
                    lostTracks.Add(track);
                }

                // new tracks
                foreach (var di in unmatchDetectionIdx)
                {
                    STrack track = unconfirmedDetections[di];
                    if (track.Score < newTrackThresh)
                        continue;

                    _trackIdCount++;
                    track.Activate(frameTs, _trackIdCount, _isFirstUpdate);
                    activatedTracks.Add(track);
                    //UnityEngine.Debug.Log($"    s4-created new track {track}");
                }

                nonActiveTracks.Clear();
                unconfirmedDetections.Clear();
            }
            #endregion

            #region Step 5: Update state
            foreach (STrack track in _lostTracks)
            {
                if ((frameTs - track.FrameTs) > MAX_TIME_LOST)
                {
                    track.MarkAsRemoved();
                    removedTracks.Add(track);
                    //UnityEngine.Debug.Log($"    s5-remove expired track {track}");
                }
            }

            // determine all tracked tracks
            List<STrack> allTrackedTracks = new List<STrack>();
            HashSet<ulong> allTrackedIds = new HashSet<ulong>();
            //HashSet<ulong> onlyTrackedIds = new HashSet<ulong>();

            // add tracked tracks
            foreach (STrack track in _trackedTracks)
            {
                if (track.State == TrackState.Tracked)
                {
                    allTrackedTracks.Add(track);
                    allTrackedIds.Add(track.TrackId);
                    //onlyTrackedIds.Add(track.TrackId);
                }
            }

            // add activated tracks
            foreach (STrack track in activatedTracks)
            {
                if(!allTrackedIds.Contains(track.TrackId))
                {
                    allTrackedTracks.Add(track);
                    allTrackedIds.Add(track.TrackId);
                }
            }

            // add refound tracks
            foreach (STrack track in refoundTracks)
            {
                if (!allTrackedIds.Contains(track.TrackId))
                {
                    allTrackedTracks.Add(track);
                    allTrackedIds.Add(track.TrackId);
                }
            }

            activatedTracks.Clear();
            refoundTracks.Clear();

            // determine all lost tracks
            List<STrack> allLostTracks = new List<STrack>();
            HashSet<ulong> allLostIds = new HashSet<ulong>();

            // add lost tracks
            foreach (STrack track in _lostTracks)
            {
                allLostTracks.Add(track);
                allLostIds.Add(track.TrackId);
            }

            // remove tracked tracks
            foreach (STrack track in allTrackedTracks)
            {
                if(allLostIds.Contains(track.TrackId))
                {
                    allLostTracks.Remove(track);
                    allLostIds.Remove(track.TrackId);
                }
            }

            // add lost tracks
            foreach (STrack track in lostTracks)
            {
                if (!allLostIds.Contains(track.TrackId))
                {
                    allLostTracks.Add(track);
                    allLostIds.Add(track.TrackId);
                }
            }

            // remove removed tracks
            foreach (STrack track in _removedTracks)
            {
                if (allLostIds.Contains(track.TrackId))
                {
                    allLostTracks.Remove(track);
                    allLostIds.Remove(track.TrackId);
                }
            }

            // exclude overlayed tracked and lost tracks
            RemoveTrackedLostDuplicates(allTrackedTracks, allLostTracks);

            // remove too close tracks
            RemoveCloseTracks(_trackedTracks, _trackedIdToTrack, _trackedTracks, _trackedIdToTrack, isRemove1: true, isRemove2: true);
            //RemoveCloseTracks(_trackedTracks, _trackedIdToTrack, _lostTracks, null, isRemove1: false, isRemove2: true);
            //RemoveCloseTracks(_lostTracks, null, _lostTracks, null, isRemove1: true, isRemove2: true);

            // add newly removed tracks
            foreach (STrack track in removedTracks)
            {
                if (!_removedTrackIds.Contains(track.TrackId))
                {
                    _removedTracks.Add(track);
                    _removedTrackIds.Add(track.TrackId);
                }
            }

            // clip remove stracks to MAX_REMOVED_TRACKS maximum
            while (_removedTracks.Count > MAX_REMOVED_TRACKS)
            {
                STrack track = _removedTracks[0];

                _removedTrackIds.Remove(track.TrackId);
                _removedTracks.RemoveAt(0);
            }

            // cleanup
            allTrackedTracks.Clear();
            allTrackedIds.Clear();
            allLostTracks.Clear();
            allLostIds.Clear();

            lostTracks.Clear();
            removedTracks.Clear();

            #endregion

            //// display tracks
            //System.Text.StringBuilder sbBuf = new System.Text.StringBuilder();
            //sbBuf.Append($"fs: {fsData._fsIndex} - Detected({detObjects.Count}), Tracked({_trackedTracks.Count}), ts: {frameTs}\nLost({_lostTracks.Count}), Removed({_removedTracks.Count}):").AppendLine();

            //foreach (STrack track in detObjects)
            //    sbBuf.Append("    d-").Append(track).AppendLine();
            //foreach (STrack track in _trackedTracks)
            //    sbBuf.Append("    t-").Append(track).AppendLine();
            //foreach (STrack track in _lostTracks)
            //    sbBuf.Append("    l-").Append(track).AppendLine();
            //foreach (STrack track in _removedTracks)
            //    sbBuf.Append("    r-").Append(track).AppendLine();
            //UnityEngine.Debug.Log(sbBuf);

            // add tracked and lost tracks to all-tracks
            _allTracks.Clear();
            _allTracks.AddRange(_trackedTracks);
            _allTracks.AddRange(_lostTracks);

            _isFirstUpdate = false;

            return _trackedIdToTrack;  // _trackedTracks;  // .Where(track => track.IsActivated).ToArray();
        }

        // Remove duplicate tracked/lost tracks with non-maximum IoU distance.
        private void RemoveTrackedLostDuplicates(List<STrack> allTrackedTracks, List<STrack> allLostTracks)
        {
            _trackedIdToTrack.Clear();
            _trackedTracks.Clear();
            _lostTracks.Clear();

            _removedTracks.Clear();
            _removedTrackIds.Clear();

            //if(allLostTracks.Count > 0)
            //{
            //    UnityEngine.Debug.Log($"AllLostTracks({allLostTracks.Count}):");
            //    foreach (var track in allLostTracks)
            //    {
            //        UnityEngine.Debug.Log($"  {track}");
            //    }
            //}

            List<(int, int)> pairs;
            float[,] ious = CalcIouDistance(allTrackedTracks, allLostTracks);

            if (ious == null)
            {
                pairs = new List<(int, int)>();
            }
            else
            {
                int rows = ious.GetLength(0);
                int cols = ious.GetLength(1);
                pairs = new List<(int, int)>();  // (rows * cols / 2);

                for (var i = 0; i < rows; i++)
                    for (var j = 0; j < cols; j++)
                        if (ious[i, j] < 0.15f)
                            pairs.Add((i, j));
            }

            bool[] dupTracked = new bool[allTrackedTracks.Count];
            bool[] dupLost = new bool[allLostTracks.Count];

            foreach (var (aIdx, bIdx) in pairs)
            {
                ulong timeTracked = allTrackedTracks[aIdx].FrameTs - allTrackedTracks[aIdx].StartFrameTs;
                ulong timeLost = allLostTracks[bIdx].FrameTs - allLostTracks[bIdx].StartFrameTs;

                if (timeTracked > timeLost)
                    dupLost[bIdx] = true;
                else
                    dupTracked[aIdx] = true;

                //UnityEngine.Debug.Log($"  DupPair - trackedId: {allTrackedTracks[aIdx].TrackId}, lostId: {allLostTracks[bIdx].TrackId}, timeTracked: {timeTracked}, timeLost: {timeLost}, dupT: {dupTracked[aIdx]}, dupL: {dupLost[bIdx]}\n{allTrackedTracks[aIdx]}\n{allLostTracks[bIdx]}");
            }

            pairs.Clear();

            for (int ai = 0; ai < allTrackedTracks.Count; ai++)
            {
                STrack track = allTrackedTracks[ai];

                if (!dupTracked[ai])
                {
                    _trackedTracks.Add(track);
                    _trackedIdToTrack[track.TrackId] = track;
                }
                else
                {
                    track.MarkAsLost();
                    _lostTracks.Add(track);
                    //UnityEngine.Debug.Log($"    dup-lost dup-tracked track {track}");
                }
            }

            for (int bi = 0; bi < allLostTracks.Count; bi++)
            {
                STrack track = allLostTracks[bi];

                if (!dupLost[bi])
                {
                    _lostTracks.Add(track);
                }
                else if (!_removedTrackIds.Contains(allLostTracks[bi].TrackId))
                {
                    track.MarkAsRemoved();
                    _removedTracks.Add(track);
                    _removedTrackIds.Add(track.TrackId);
                    //UnityEngine.Debug.Log($"    dup-removed dup-lost track {track}");
                }
            }

            //if (_lostTracks.Count > 0)
            //{
            //    UnityEngine.Debug.Log($"LostTracks({_lostTracks.Count}):");
            //    foreach (var track in _lostTracks)
            //    {
            //        UnityEngine.Debug.Log($"  {track}");
            //    }
            //}

            //if (_removedTracks.Count > 0)
            //{
            //    UnityEngine.Debug.Log($"RemovedTracks({_removedTracks.Count}):");
            //    foreach (var track in _removedTracks)
            //    {
            //        UnityEngine.Debug.Log($"  {track}");
            //    }
            //}
        }

        // Remove close tracks
        private void RemoveCloseTracks(List<STrack> tracks1, Dictionary<ulong, STrack> track1Id, List<STrack> tracks2, Dictionary<ulong, STrack> track2Id, bool isRemove1, bool isRemove2)
        {
            List<(int, int)> pairs;
            float[,] cDist = CalcCenterDistance(tracks1, tracks2);

            if (cDist == null)
            {
                //pairs = new List<(int, int)>();
                return;
            }
            else
            {
                int rows = cDist.GetLength(0);
                int cols = cDist.GetLength(1);
                pairs = new List<(int, int)>();  // (rows * cols / 2);

                for (var i = 0; i < rows; i++)
                    for (var j = 0; j < cols; j++)
                        if (i != j && cDist[i, j] < 0.15f)
                            pairs.Add((i, j));
            }

            bool[] dupTracked1 = new bool[tracks1.Count];
            bool[] dupTracked2 = new bool[tracks2.Count];

            foreach (var (aIdx, bIdx) in pairs)
            {
                ulong timeTracked1 = tracks1[aIdx].FrameTs - tracks1[aIdx].StartFrameTs;
                ulong timeTracked2 = tracks2[bIdx].FrameTs - tracks2[bIdx].StartFrameTs;

                if (timeTracked1 > timeTracked2)
                    dupTracked2[bIdx] = true;
                else
                    dupTracked1[aIdx] = true;

                //UnityEngine.Debug.Log($"  ClosePair - tracked1Id: {tracks1[aIdx].TrackId}, tracked2Id: {tracks2[bIdx].TrackId}, timeTracked1: {timeTracked1}, timeTracked2: {timeTracked2}, dup1: {dupTracked1[aIdx]}, dup2: {dupTracked2[bIdx]}\n{tracks1[aIdx]}\n{tracks2[bIdx]}");
            }

            pairs.Clear();

            if(isRemove1 || isRemove2 && tracks1 == tracks2)
            {
                for (int i = 0; i < tracks1.Count; i++)
                {
                    if (dupTracked1[i])
                    {
                        STrack track = tracks1[i];

                        if (track.State == TrackState.Tracked)
                        {
                            //UnityEngine.Debug.Log($"    close-lost too-close track12: {track}");
                            track.MarkAsLost();
                            _lostTracks.Add(track);
                        }
                        else
                        {
                            //UnityEngine.Debug.Log($"    close-removed too-close track12: {track}");
                            track.MarkAsRemoved();
                            _removedTracks.Add(track);
                            _removedTrackIds.Add(track.TrackId);
                        }
                    }
                }

                for (int i = tracks1.Count - 1; i >= 0; i--)
                {
                    if (dupTracked1[i])
                    {
                        ulong trackId = tracks1[i].TrackId;

                        tracks1.RemoveAt(i);
                        if (track1Id != null)
                            track1Id.Remove(trackId);
                    }
                }
            }
            else
            {
                if (isRemove1)
                {
                    for (int i = 0; i < tracks1.Count; i++)
                    {
                        if (dupTracked1[i])
                        {
                            STrack track = tracks1[i];

                            if (track.State == TrackState.Tracked)
                            {
                                //UnityEngine.Debug.Log($"    close-lost too-close track1: {track}");
                                track.MarkAsLost();
                                _lostTracks.Add(track);
                            }
                            else
                            {
                                //UnityEngine.Debug.Log($"    close-removed too-close track1: {track}");
                                track.MarkAsRemoved();
                                _removedTracks.Add(track);
                                _removedTrackIds.Add(track.TrackId);
                            }
                        }
                    }

                    for (int i = tracks1.Count - 1; i >= 0; i--)
                    {
                        if (dupTracked1[i])
                        {
                            ulong trackId = tracks1[i].TrackId;

                            tracks1.RemoveAt(i);
                            if (track1Id != null)
                                track1Id.Remove(trackId);
                        }
                    }
                }

                if (isRemove2)
                {
                    for (int i = 0; i < tracks2.Count; i++)
                    {
                        if (dupTracked2[i])
                        {
                            STrack track = tracks2[i];

                            if (track.State == TrackState.Tracked)
                            {
                                //UnityEngine.Debug.Log($"    close-lost too-close track2: {track}");
                                track.MarkAsLost();
                                _lostTracks.Add(track);
                            }
                            else
                            {
                                //UnityEngine.Debug.Log($"    close-removed too-close track2: {track}");
                                track.MarkAsRemoved();
                                _removedTracks.Add(track);
                                _removedTrackIds.Add(track.TrackId);
                            }
                        }
                    }

                    for (int i = tracks2.Count - 1; i >= 0; i--)
                    {
                        if (dupTracked2[i])
                        {
                            ulong trackId = tracks2[i].TrackId;

                            tracks2.RemoveAt(i);
                            if (track2Id != null)
                                track2Id.Remove(trackId);
                        }
                    }
                }
            }

        }

        // solves linear assignment problem based on the provided cost matrix and cost threshold
        private void LinearAssignment(string lapName, float[,] costMatrix, int costRows, int costCols, float thresh, out IList<(int, int)> matches, out IList<int> riUnmatched, out IList<int> ciUnmatched)
        {
            matches = new List<(int, int)>();

            if (costMatrix is null)
            {
                riUnmatched = Enumerable.Range(0, costRows).ToArray();
                ciUnmatched = Enumerable.Range(0, costCols).ToArray();
                //UnityEngine.Debug.Log($"{lapName} - r: {costRows}, c: {costCols} - costMatrix is empty.");
                return;
            }

            LAPJV.Solve(lapName, costMatrix, thresh, out int[] rowSol, out int[] colSol);

            riUnmatched = new List<int>();
            ciUnmatched = new List<int>();

            for (int i = 0; i < costRows /**rowsol.Length*/; i++)
            {
                if (rowSol[i] >= 0)
                {
                    //int[] riSol = new int[] { i, rowSol[i] };
                    matches.Add((i, rowSol[i]));
                }
                else
                {
                    riUnmatched.Add(i);
                }
            }

            for (var i = 0; i < costCols /**colSol.Length*/; i++)
            {
                if (colSol[i] < 0)
                {
                    ciUnmatched.Add(i);
                }
            }

        }

        // Calculates matrix of IOU distances (0-1) between two sets of tracks.
        private static float[,] CalcIouDistance(List<STrack> aTracks, List<STrack> bTracks)
        {
            if (aTracks.Count == 0 || bTracks.Count == 0)
                return null;

            var iouDist = new float[aTracks.Count, bTracks.Count];

            for (var ai = 0; ai < aTracks.Count; ai++)
            {
                for (var bi = 0; bi < bTracks.Count; bi++)
                {
                    iouDist[ai, bi] = 1f - aTracks[ai].RectBox.CalcIoU(bTracks[bi].RectBox);
                }
            }

            return iouDist;
        }

        // Calculates matrix of center distances between two sets of tracks.
        private static float[,] CalcCenterDistance(List<STrack> aTracks, List<STrack> bTracks)
        {
            if (aTracks.Count == 0 || bTracks.Count == 0)
                return null;

            var centerDist = new float[aTracks.Count, bTracks.Count];

            for (var ai = 0; ai < aTracks.Count; ai++)
            {
                for (var bi = 0; bi < bTracks.Count; bi++)
                {
                    centerDist[ai, bi] = aTracks[ai].RectBox.CalcCenterDist(bTracks[bi].RectBox);
                }
            }

            return centerDist;
        }

    }
}