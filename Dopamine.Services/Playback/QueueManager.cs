using Digimezzo.Foundation.Core.Logging;
using Dopamine.Core.Base;
using Dopamine.Core.Extensions;
using Dopamine.Data.Entities;
using Dopamine.Data.Metadata;
using Dopamine.Data.Repositories;
using Dopamine.Services.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dopamine.Services.Playback
{
    internal class QueueManager
    {
        private static readonly object randomLock = new object();
        private static readonly Random random = new Random();
        private ITrackRepository trackRepository;
        private TrackViewModel currentTrack;
        private object queueLock = new object();
        private List<TrackViewModel> queue = new List<TrackViewModel>(); // Queued tracks in original order
        private List<int> playbackOrder = new List<int>(); // Playback order of queued tracks (Contains the indexes of list the queued tracks)

        public QueueManager(ITrackRepository trackRepository)
        {
            this.trackRepository = trackRepository;
        }

        public IList<TrackViewModel> Queue
        {
            get { return this.queue; }
        }

        private List<int> GetQueueIndices()
        {
            if (this.queue != null)
            {
                return Enumerable.Range(0, this.queue.Count).ToList();
            }

            return new List<int>();
        }

        private static int GetRandomNumber(int minValue, int maxValue)
        {
            lock (randomLock)
            {
                return random.Next(minValue, maxValue);
            }
        }

        private static void EnsureQueueID(TrackViewModel track)
        {
            if (track != null && string.IsNullOrWhiteSpace(track.QueueID))
            {
                track.QueueID = Guid.NewGuid().ToString();
            }
        }

        private List<int> GetRandomizedQueueIndices(int firstQueueIndexToAvoid)
        {
            List<int> randomizedQueueIndices = this.GetQueueIndices().Randomize();

            if (firstQueueIndexToAvoid >= 0 && randomizedQueueIndices.Count > 1 && randomizedQueueIndices.First().Equals(firstQueueIndexToAvoid))
            {
                randomizedQueueIndices[0] = randomizedQueueIndices[1];
                randomizedQueueIndices[1] = firstQueueIndexToAvoid;
            }

            return randomizedQueueIndices;
        }

        private void InsertShuffledIndicesAfterCurrentTrack(List<int> queueIndices)
        {
            if (queueIndices == null || queueIndices.Count == 0)
            {
                return;
            }

            if (this.currentTrack == null || !this.queue.Contains(this.currentTrack) || this.playbackOrder.Count == 0)
            {
                this.playbackOrder.AddRange(queueIndices.Randomize());
                return;
            }

            int currentTrackIndex = this.FindPlaybackOrderIndex(this.currentTrack);

            if (currentTrackIndex < 0)
            {
                this.playbackOrder.AddRange(queueIndices.Randomize());
                return;
            }

            List<int> playedAndCurrentIndices = this.playbackOrder.Take(currentTrackIndex + 1).ToList();
            List<int> notYetPlayedIndices = this.playbackOrder.Skip(currentTrackIndex + 1).ToList();

            foreach (int queueIndex in queueIndices.Randomize())
            {
                int insertIndex = GetRandomNumber(0, notYetPlayedIndices.Count + 1);
                notYetPlayedIndices.Insert(insertIndex, queueIndex);
            }

            playedAndCurrentIndices.AddRange(notYetPlayedIndices);
            this.playbackOrder = playedAndCurrentIndices;
        }

        private int FindQueueIndex(TrackViewModel track)
        {
            if (this.queue != null)
            {
                return this.queue.IndexOf(track);
            }

            return 0;
        }

        private int FindPlaybackOrderIndex(TrackViewModel track)
        {
            if (this.queue != null && this.playbackOrder != null)
            {
                int queueIndex = this.queue.IndexOf(track);
                return this.playbackOrder.IndexOf(queueIndex);
            }

            return 0;
        }

        public async Task ShuffleAsync()
        {
            await Task.Run(() =>
            {
                lock (this.queueLock)
                {
                    if (this.queue.Count > 0)
                    {
                        if (this.currentTrack == null || !this.queue.Contains(this.currentTrack))
                        {
                            // We're not playing a track from the queue: just shuffle.
                            this.playbackOrder = this.GetRandomizedQueueIndices(-1);
                        }
                        else
                        {
                            // We're playing a track from the queue: shuffle, but make sure the playing track comes first.
                            int currentTrackIndex = this.FindQueueIndex(this.currentTrack);
                            this.playbackOrder = new List<int>();
                            this.playbackOrder.Add(currentTrackIndex);
                            List<int> tempPlaybackOrder = this.GetQueueIndices();
                            tempPlaybackOrder.Remove(currentTrackIndex);
                            this.playbackOrder.AddRange(tempPlaybackOrder.Randomize());
                        }
                    }
                }
            });
        }

        public async Task UnShuffleAsync()
        {
            await Task.Run(() =>
            {
                lock (this.queueLock)
                {
                    if (this.queue.Count > 0)
                    {
                        this.playbackOrder = this.GetQueueIndices();
                    }
                }
            });
        }

        public IDictionary<string, long> GetShuffleOrderByQueueID()
        {
            var shuffleOrderByQueueID = new Dictionary<string, long>();

            lock (this.queueLock)
            {
                for (int shuffleOrderID = 0; shuffleOrderID < this.playbackOrder.Count; shuffleOrderID++)
                {
                    int queueIndex = this.playbackOrder[shuffleOrderID];

                    if (queueIndex >= 0 && queueIndex < this.queue.Count)
                    {
                        TrackViewModel track = this.queue[queueIndex];
                        EnsureQueueID(track);
                        shuffleOrderByQueueID[track.QueueID] = shuffleOrderID;
                    }
                }
            }

            return shuffleOrderByQueueID;
        }

        public TrackViewModel CurrentTrack()
        {
            try
            {
                if (this.currentTrack != null)
                {
                    return this.currentTrack;
                }
                else
                {
                    return this.FirstTrack();
                }
            }
            catch (Exception ex)
            {
                LogClient.Error("Could not get current track. Exception: {0}", ex.Message);
            }

            return null;
        }

        public TrackViewModel FirstTrack()
        {
            TrackViewModel firstTrack = null;

            try
            {
                if (this.playbackOrder != null && this.playbackOrder.Count > 0)
                {
                    firstTrack = this.queue[this.playbackOrder.First()];
                }
            }
            catch (Exception ex)
            {
                LogClient.Error("Could not get first track. Exception: {0}", ex.Message);
            }

            return firstTrack;
        }

        public async Task<TrackViewModel> PreviousTrackAsync(LoopMode loopMode)
        {
            TrackViewModel previousTrack = null;

            await Task.Run(() =>
            {
                try
                {
                    lock (this.queueLock)
                    {
                        if (this.playbackOrder != null && this.playbackOrder.Count > 0)
                        {
                            int currentTrackIndex = this.FindPlaybackOrderIndex(this.currentTrack);

                            if (loopMode == LoopMode.One)
                            {
                                // Return the current track
                                previousTrack = this.currentTrack;
                            }
                            else
                            {
                                if (currentTrackIndex > 0)
                                {
                                    // If we didn't reach the start of the queue, return the previous track.
                                    previousTrack = this.queue[this.playbackOrder[currentTrackIndex - 1]];
                                }
                                else if (loopMode == LoopMode.All)
                                {
                                    // When LoopMode.All is enabled, when we reach the start of the queue, return the last track.
                                    previousTrack = this.queue[this.playbackOrder.Last()];
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogClient.Error("Could not get previous track. Exception: {0}", ex.Message);
                }
            });

            return previousTrack;
        }

        public async Task<TrackViewModel> NextTrackAsync(LoopMode loopMode, bool returnToStart, bool startNewShuffleRound)
        {
            TrackViewModel nextTrack = null;

            await Task.Run(() =>
            {
                try
                {
                    lock (this.queueLock)
                    {
                        if (this.playbackOrder != null && this.playbackOrder.Count > 0)
                        {
                            int currentTrackIndex = this.FindPlaybackOrderIndex(this.currentTrack);

                            if (loopMode.Equals(LoopMode.One))
                            {
                                // Return the current track
                                nextTrack = this.queue[this.playbackOrder[currentTrackIndex]];
                            }
                            else
                            {
                                if (currentTrackIndex < this.playbackOrder.Count - 1)
                                {
                                    // If we didn't reach the end of the queue, return the next track.
                                    int increment = 1;

                                    nextTrack = this.queue[this.playbackOrder[currentTrackIndex + increment]];

                                    // HACK: voids getting stuck on the same track when the playlist contains the same track multiple times
                                    while (this.currentTrack.Path.Equals(nextTrack.Path))
                                    {
                                        increment++;
                                        nextTrack = this.queue[this.playbackOrder[currentTrackIndex + increment]];
                                    }
                                }
                                else if (loopMode.Equals(LoopMode.All) | returnToStart)
                                {
                                    if (startNewShuffleRound)
                                    {
                                        this.playbackOrder = this.GetRandomizedQueueIndices(this.FindQueueIndex(this.currentTrack));
                                    }

                                    // When LoopMode.All is enabled, when we reach the end of the queue, return the first track.
                                    nextTrack = this.queue[this.playbackOrder.First()];
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogClient.Error("Could not get next track. Exception: {0}", ex.Message);
                }
            });

            return nextTrack;
        }

        public async Task<EnqueueResult> EnqueueAsync(IList<TrackViewModel> tracks, bool shuffle)
        {
            var result = new EnqueueResult { IsSuccess = true };
            var addedQueueIndices = new List<int>();

            try
            {
                await Task.Run(() =>
                {
                    lock (this.queueLock)
                    {
                        foreach (TrackViewModel track in tracks)
                        {
                            TrackViewModel queuedTrack = track.DeepCopy();
                            EnsureQueueID(queuedTrack);

                            this.queue.Add(queuedTrack);
                            addedQueueIndices.Add(this.queue.Count - 1);
                        }

                        result.EnqueuedTracks = tracks;
                    }
                });

                if (shuffle)
                {
                    await Task.Run(() =>
                    {
                        lock (this.queueLock)
                        {
                            if (this.playbackOrder.Count == 0 || this.currentTrack == null || !this.queue.Contains(this.currentTrack))
                            {
                                this.playbackOrder = this.GetRandomizedQueueIndices(-1);
                            }
                            else
                            {
                                this.InsertShuffledIndicesAfterCurrentTrack(addedQueueIndices);
                            }
                        }
                    });
                }
                else
                {
                    await this.UnShuffleAsync();
                }
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                LogClient.Error("Error while enqueuing tracks. Exception: {0}", ex.Message);
            }

            return result;
        }

        public async Task<EnqueueResult> EnqueueRestoredAsync(IList<TrackViewModel> tracks, IList<long> shuffleOrderIDs, bool shuffle)
        {
            var result = new EnqueueResult { IsSuccess = true };

            try
            {
                await Task.Run(() =>
                {
                    lock (this.queueLock)
                    {
                        foreach (TrackViewModel track in tracks)
                        {
                            TrackViewModel queuedTrack = track.DeepCopy(true);
                            EnsureQueueID(queuedTrack);
                            this.queue.Add(queuedTrack);
                        }

                        result.EnqueuedTracks = tracks;

                        if (shuffle && shuffleOrderIDs != null && shuffleOrderIDs.Count == this.queue.Count)
                        {
                            this.playbackOrder = Enumerable.Range(0, this.queue.Count).OrderBy(i => shuffleOrderIDs[i]).ThenBy(i => i).ToList();
                        }
                        else if (shuffle)
                        {
                            this.playbackOrder = this.GetRandomizedQueueIndices(-1);
                        }
                        else
                        {
                            this.playbackOrder = this.GetQueueIndices();
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                LogClient.Error("Error while restoring queued tracks. Exception: {0}", ex.Message);
            }

            return result;
        }

        public async Task<EnqueueResult> EnqueueNextAsync(IList<TrackViewModel> tracks)
        {
            var result = new EnqueueResult { IsSuccess = true };

            try
            {
                await Task.Run(() =>
                {
                    lock (this.queueLock)
                    {
                        int queueIndex = 0;
                        int playbackOrderIndex = 0;
                        int playbackOrderCount = this.playbackOrder.Count;

                        if (this.currentTrack != null)
                        {
                            queueIndex = this.FindQueueIndex(this.currentTrack);
                            playbackOrderIndex = this.FindPlaybackOrderIndex(this.currentTrack);
                        }

                        var tracksToAdd = new List<TrackViewModel>();

                        foreach (TrackViewModel track in tracks)
                        {
                            TrackViewModel queuedTrack = track.DeepCopy();
                            EnsureQueueID(queuedTrack);
                            tracksToAdd.Add(queuedTrack);
                        }

                        this.queue.InsertRange(queueIndex + 1, tracksToAdd);

                        for (int i = 0; i < this.playbackOrder.Count; i++)
                        {
                            if (this.playbackOrder[i] > queueIndex)
                            {
                                this.playbackOrder[i] += tracksToAdd.Count;
                            }
                        }

                        this.playbackOrder.InsertRange(playbackOrderIndex + 1, Enumerable.Range(queueIndex + 1, tracksToAdd.Count));

                        result.EnqueuedTracks = tracks;
                    }
                });
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                LogClient.Error("Error while enqueuing tracks next. Exception: {0}", ex.Message);
            }


            return result;
        }

        public async Task<bool> ClearQueueAsync()
        {
            bool isSuccess = true;

            await Task.Run(() =>
            {
                try
                {
                    lock (this.queueLock)
                    {
                        this.currentTrack = null;
                        this.queue.Clear();
                        this.playbackOrder.Clear();
                    }
                }
                catch (Exception ex)
                {
                    isSuccess = false;
                    LogClient.Error("Error while clearing queue. Exception: {0}", ex.Message);
                }
            });

            return isSuccess;
        }

        public async Task<DequeueResult> DequeueAsync(IList<TrackViewModel> tracks)
        {
            bool isSuccess = true;
            bool isPlayingTrackDequeued = false;
            IList<TrackViewModel> dequeuedTracks = new List<TrackViewModel>();
            TrackViewModel nextAvailableTrack = null;

            await Task.Run(() =>
            {
                lock (this.queueLock)
                {
                    try
                    {
                        // First, get the tracks to dequeue and which are in the queue (normally it's all of them. But we're just making sure.)
                        IList<TrackViewModel> tracksToDequeue = this.queue.Where(x => tracks.Contains(x)).ToList();
                        List<int> queueIndicesToRemove = tracksToDequeue.Select(x => this.queue.IndexOf(x)).Where(x => x >= 0).Distinct().OrderByDescending(x => x).ToList();

                        if (tracksToDequeue.Contains(this.currentTrack))
                        {
                            int currentPlaybackOrderIndex = this.FindPlaybackOrderIndex(this.currentTrack);
                            int nextPlaybackOrderIndex = currentPlaybackOrderIndex + 1;
                            List<int> queueIndicesToRemoveAscending = queueIndicesToRemove.OrderBy(x => x).ToList();

                            while (nextPlaybackOrderIndex < this.playbackOrder.Count && queueIndicesToRemoveAscending.Contains(this.playbackOrder[nextPlaybackOrderIndex]))
                            {
                                nextPlaybackOrderIndex++;
                            }

                            if (nextPlaybackOrderIndex < this.playbackOrder.Count)
                            {
                                nextAvailableTrack = this.queue[this.playbackOrder[nextPlaybackOrderIndex]];
                            }
                        }

                        foreach (int queueIndexToRemove in queueIndicesToRemove)
                        {
                            TrackViewModel trackToDequeue = null;

                            try
                            {
                                trackToDequeue = this.queue[queueIndexToRemove];

                                this.playbackOrder.RemoveAll(x => x == queueIndexToRemove);

                                for (int i = 0; i < this.playbackOrder.Count; i++)
                                {
                                    if (this.playbackOrder[i] > queueIndexToRemove)
                                    {
                                        this.playbackOrder[i] -= 1;
                                    }
                                }

                                if (trackToDequeue.Equals(this.currentTrack))
                                {
                                    isPlayingTrackDequeued = true;
                                    this.currentTrack = null;
                                }

                                dequeuedTracks.Add(trackToDequeue);
                                this.queue.RemoveAt(queueIndexToRemove);
                            }
                            catch (Exception ex)
                            {
                                LogClient.Error($"Error while removing track with path='{trackToDequeue?.Path}' from the queue. Exception: {ex.Message}");
                                throw;
                            }

                        }
                    }
                    catch (Exception ex)
                    {
                        LogClient.Error($"Error while removing tracks from the queue. Queue will be cleared. Exception: {ex.Message}");
                        isSuccess = false;
                    }
                }
            });

            if (!isSuccess)
            {
                LogClient.Warning($"Removing tracks from queue failed. Clearing queue.");
                await this.ClearQueueAsync();
                dequeuedTracks = new List<TrackViewModel>(tracks);
            }

            var dequeueResult = new DequeueResult
            {
                IsSuccess = isSuccess,
                DequeuedTracks = dequeuedTracks,
                NextAvailableTrack = nextAvailableTrack,
                IsPlayingTrackDequeued = isPlayingTrackDequeued
            };

            return dequeueResult;
        }

        public void SetCurrentTrack(TrackViewModel track)
        {
            if (track == null)
            {
                this.currentTrack = null;
                return;
            }

            EnsureQueueID(track);

            this.currentTrack = this.queue.Where(x => !string.IsNullOrWhiteSpace(x.QueueID) && x.QueueID.Equals(track.QueueID)).FirstOrDefault();

            if (this.currentTrack == null)
            {
                this.currentTrack = this.queue.Where(x=> x.SafePath.Equals(track.SafePath)).FirstOrDefault();
            }
        }

        public async Task<bool> UpdateQueueOrderAsync(IList<TrackViewModel> tracks, bool isShuffled)
        {
            if (tracks == null || tracks.Count == 0)
            {
                return false;
            }

            bool isSuccess = true;

            try
            {
                await Task.Run(() =>
                {
                    lock (this.queueLock)
                    {
                        List<string> currentPlaybackQueueIDs = this.playbackOrder
                            .Where(i => i >= 0 && i < this.queue.Count)
                            .Select(i => this.queue[i].QueueID)
                            .ToList();

                        this.queue.Clear();
                        this.queue.AddRange(tracks);

                        foreach (TrackViewModel track in this.queue)
                        {
                            EnsureQueueID(track);
                        }

                        if (isShuffled)
                        {
                            var updatedPlaybackOrder = new List<int>();

                            foreach (string queueID in currentPlaybackQueueIDs)
                            {
                                int queueIndex = this.queue.FindIndex(x => !string.IsNullOrWhiteSpace(x.QueueID) && x.QueueID.Equals(queueID));

                                if (queueIndex >= 0 && !updatedPlaybackOrder.Contains(queueIndex))
                                {
                                    updatedPlaybackOrder.Add(queueIndex);
                                }
                            }

                            foreach (int queueIndex in this.GetQueueIndices())
                            {
                                if (!updatedPlaybackOrder.Contains(queueIndex))
                                {
                                    updatedPlaybackOrder.Add(queueIndex);
                                }
                            }

                            this.playbackOrder = updatedPlaybackOrder;
                        }
                        else
                        {
                            this.playbackOrder = this.GetQueueIndices();
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                isSuccess = false;
                LogClient.Error("Could update queue order. Exception: {0}", ex.Message);
            }

            return isSuccess;
        }

        public async Task<UpdateQueueMetadataResult> UpdateMetadataAsync(IList<FileMetadata> fileMetadatas)
        {
            var result = new UpdateQueueMetadataResult();

            IList<Track> tracks = await this.trackRepository.GetTracksAsync(fileMetadatas.Select(x => x.Path).ToList());

            await Task.Run(() =>
            {
                lock (this.queueLock)
                {
                    if (this.Queue != null)
                    {
                        // Queue
                        result.IsQueueChanged = true;

                        foreach (TrackViewModel trackViewModel in this.queue)
                        {
                            Track newTrack = tracks.Where(x => x.SafePath.Equals(trackViewModel.SafePath)).FirstOrDefault();

                            trackViewModel.UpdateTrack(newTrack);

                            // Playing track
                            if (trackViewModel.SafePath.Equals(this.currentTrack.SafePath))
                            {
                                result.IsPlayingTrackChanged = true;
                                this.currentTrack.UpdateTrack(newTrack);
                            }
                        }
                    }
                }
            });

            return result;
        }

        public async Task UpdateQueueLanguageAsync()
        {
            await Task.Run(() =>
            {
                lock (this.queueLock)
                {
                    if (this.Queue != null)
                    {
                        foreach (TrackViewModel trackViewModel in this.queue)
                        {
                            trackViewModel.Refresh();
                        } 
                    }

                    if (this.currentTrack != null)
                    {
                        this.currentTrack.Refresh();
                    }
                }
            });
        }
    }
}
