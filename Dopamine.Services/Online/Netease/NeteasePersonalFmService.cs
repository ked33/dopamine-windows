using Dopamine.Core.Base;
using Dopamine.Core.Logging;
using Dopamine.Services.Entities;
using Dopamine.Services.Playback;
using Prism.Ioc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    public sealed class NeteasePersonalFmService : INeteasePersonalFmService
    {
        private const int RefillThreshold = 1;

        private readonly IContainerProvider container;
        private readonly INeteaseMusicService musicService;
        private readonly INeteaseSessionService sessionService;
        private readonly IPlaybackService playbackService;
        private readonly SemaphoreSlim operationGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim refillGate = new SemaphoreSlim(1, 1);

        private CancellationTokenSource sessionCancellationTokenSource;
        private int sessionGeneration;
        private bool isActive;
        private bool isBusy;
        private NeteaseError error;

        public NeteasePersonalFmService(
            IContainerProvider container,
            INeteaseMusicService musicService,
            INeteaseSessionService sessionService,
            IPlaybackService playbackService)
        {
            this.container = container;
            this.musicService = musicService;
            this.sessionService = sessionService;
            this.playbackService = playbackService;

            this.playbackService.PlayingTrackChanged += (_, __) => this.HandlePlaybackStateChanged();
            this.playbackService.QueueChanged += (_, __) => this.HandlePlaybackStateChanged();
            this.sessionService.SessionChanged += (_, __) =>
            {
                if (this.sessionService.State != NeteaseSessionState.SignedIn)
                {
                    this.Exit();
                }
            };
        }

        public bool IsActive => this.isActive &&
            this.playbackService.QueueContext == PlaybackQueueContext.NeteasePersonalFm;

        public bool IsBusy => this.isBusy;

        public int BufferedTrackCount => this.IsActive ? this.GetRemainingTrackCount() : 0;

        public TrackViewModel CurrentTrack => this.IsActive ? this.playbackService.CurrentTrack : null;

        public NeteaseError Error => this.error;

        public event EventHandler StateChanged = delegate { };

        public async Task<NeteaseResult<bool>> StartAsync(CancellationToken cancellationToken)
        {
            if (this.IsActive)
            {
                this.RaiseStateChanged();
                return NeteaseResult<bool>.Success(true);
            }

            if (!await this.operationGate.WaitAsync(0))
            {
                return NeteaseResult<bool>.Failure(new NeteaseError(
                    NeteaseErrorCode.Unknown,
                    "Language_Netease_Operation_In_Progress"));
            }

            try
            {
                this.BeginNewSession();
                int generation = this.sessionGeneration;
                using (var startCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    this.sessionCancellationTokenSource.Token,
                    cancellationToken))
                {
                    CancellationToken token = startCancellationTokenSource.Token;
                this.SetBusy(true);
                this.SetError(null);

                NeteaseResult<IReadOnlyList<NeteasePersonalFmItem>> result =
                    await this.musicService.GetPersonalFmAsync(token);

                if (!result.IsSuccess)
                {
                    this.SetError(result.Error);
                    return NeteaseResult<bool>.Failure(result.Error);
                }

                List<TrackViewModel> tracks = this.MapTracks(result.Value, null);

                if (tracks.Count == 0)
                {
                    var emptyError = new NeteaseError(
                        NeteaseErrorCode.EmptyResponse,
                        "Language_Netease_Personal_Fm_Empty");
                    this.SetError(emptyError);
                    return NeteaseResult<bool>.Failure(emptyError);
                }

                if (generation != this.sessionGeneration || token.IsCancellationRequested)
                {
                    return NeteaseResult<bool>.Failure(new NeteaseError(
                        NeteaseErrorCode.Cancelled,
                        "Language_Netease_Cancelled"));
                }

                this.isActive = true;
                bool started = await this.playbackService.PlayTransientQueueAsync(
                    tracks,
                    tracks[0],
                    PlaybackQueueContext.NeteasePersonalFm);

                if (!started)
                {
                    this.isActive = false;
                    var playbackError = new NeteaseError(
                        NeteaseErrorCode.Unknown,
                        "Language_Netease_Service_Unavailable");
                    this.SetError(playbackError);
                    return NeteaseResult<bool>.Failure(playbackError);
                }

                this.RaiseStateChanged();
                return NeteaseResult<bool>.Success(true);
                }
            }
            catch (OperationCanceledException)
            {
                return NeteaseResult<bool>.Failure(new NeteaseError(
                    NeteaseErrorCode.Cancelled,
                    "Language_Netease_Cancelled"));
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not start Netease personal FM. ErrorType={0}", ex.GetType().Name);
                var startError = new NeteaseError(
                    NeteaseErrorCode.Unknown,
                    "Language_Netease_Service_Unavailable");
                this.SetError(startError);
                return NeteaseResult<bool>.Failure(startError);
            }
            finally
            {
                this.SetBusy(false);
                this.operationGate.Release();
            }
        }

        public async Task<NeteaseResult<bool>> SkipAsync(CancellationToken cancellationToken)
        {
            if (!this.IsActive)
            {
                return NeteaseResult<bool>.Failure(new NeteaseError(
                    NeteaseErrorCode.EmptyResponse,
                    "Language_Netease_Personal_Fm_Not_Active"));
            }

            if (this.IsBusy)
            {
                return NeteaseResult<bool>.Failure(new NeteaseError(
                    NeteaseErrorCode.Unknown,
                    "Language_Netease_Operation_In_Progress"));
            }

            this.SetBusy(true);

            try
            {
                return await this.SkipCoreAsync(cancellationToken);
            }
            finally
            {
                this.SetBusy(false);
            }
        }

        public async Task<NeteaseResult<bool>> DislikeCurrentAsync(CancellationToken cancellationToken)
        {
            string songId = this.CurrentTrack?.SourceInfo?.RemoteId;

            if (!this.IsActive || string.IsNullOrWhiteSpace(songId))
            {
                return NeteaseResult<bool>.Failure(new NeteaseError(
                    NeteaseErrorCode.EmptyResponse,
                    "Language_Netease_Personal_Fm_Not_Active"));
            }

            if (this.IsBusy)
            {
                return NeteaseResult<bool>.Failure(new NeteaseError(
                    NeteaseErrorCode.Unknown,
                    "Language_Netease_Operation_In_Progress"));
            }

            this.SetBusy(true);

            try
            {
                NeteaseResult<bool> result = await this.musicService.DislikePersonalFmSongAsync(
                    songId,
                    cancellationToken);

                if (!result.IsSuccess)
                {
                    this.SetError(result.Error);
                    return result;
                }

                return await this.SkipCoreAsync(cancellationToken);
            }
            finally
            {
                this.SetBusy(false);
            }
        }

        public void Exit()
        {
            this.isActive = false;
            this.SetBusy(false);
            this.CancelSession();
            this.RaiseStateChanged();
        }

        private void BeginNewSession()
        {
            this.CancelSession();
            Interlocked.Increment(ref this.sessionGeneration);
            this.sessionCancellationTokenSource = new CancellationTokenSource();
        }

        private void CancelSession()
        {
            CancellationTokenSource source = Interlocked.Exchange(
                ref this.sessionCancellationTokenSource,
                null);
            source?.Cancel();
            source?.Dispose();
            Interlocked.Increment(ref this.sessionGeneration);
        }

        private async void HandlePlaybackStateChanged()
        {
            if (this.isActive && this.playbackService.QueueContext != PlaybackQueueContext.NeteasePersonalFm)
            {
                this.Exit();
                return;
            }

            this.RaiseStateChanged();

            if (this.IsActive && this.GetRemainingTrackCount() <= RefillThreshold)
            {
                await this.EnsureMoreAsync(CancellationToken.None);
            }
        }

        private async Task EnsureMoreAsync(CancellationToken cancellationToken)
        {
            if (!this.IsActive || !await this.refillGate.WaitAsync(0))
            {
                return;
            }

            try
            {
                int generation = this.sessionGeneration;
                CancellationToken sessionToken = this.sessionCancellationTokenSource?.Token ?? cancellationToken;
                using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                    sessionToken,
                    cancellationToken))
                {
                    NeteaseResult<IReadOnlyList<NeteasePersonalFmItem>> result =
                        await this.musicService.GetPersonalFmAsync(linkedSource.Token);

                    if (!result.IsSuccess)
                    {
                        if (result.Error?.Code != NeteaseErrorCode.Cancelled)
                        {
                            this.SetError(result.Error);
                        }

                        return;
                    }

                    if (!this.IsActive || generation != this.sessionGeneration)
                    {
                        return;
                    }

                    var existingIds = new HashSet<string>(
                        this.playbackService.Queue
                            .Select(x => x?.SourceInfo?.RemoteId)
                            .Where(x => !string.IsNullOrWhiteSpace(x)),
                        StringComparer.Ordinal);
                    List<TrackViewModel> tracks = this.MapTracks(result.Value, existingIds);

                    if (tracks.Count > 0)
                    {
                        bool shouldResume = this.GetRemainingTrackCount() == 0 &&
                            this.playbackService.IsStopped;
                        await this.playbackService.AddToQueueAsync(tracks);

                        if (shouldResume && this.IsActive)
                        {
                            await this.playbackService.PlayNextAsync();
                        }

                        this.SetError(null);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not refill Netease personal FM. ErrorType={0}", ex.GetType().Name);
                this.SetError(new NeteaseError(
                    NeteaseErrorCode.Unknown,
                    "Language_Netease_Service_Unavailable"));
            }
            finally
            {
                this.refillGate.Release();
                this.RaiseStateChanged();
            }
        }

        private async Task<NeteaseResult<bool>> SkipCoreAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (this.GetRemainingTrackCount() <= 0)
                {
                    await this.EnsureMoreAsync(cancellationToken);
                }

                if (this.GetRemainingTrackCount() <= 0)
                {
                    var emptyError = new NeteaseError(
                        NeteaseErrorCode.EmptyResponse,
                        "Language_Netease_Personal_Fm_Empty");
                    this.SetError(emptyError);
                    return NeteaseResult<bool>.Failure(emptyError);
                }

                await this.playbackService.PlayNextAsync();
                this.SetError(null);
                this.RaiseStateChanged();
                return NeteaseResult<bool>.Success(true);
            }
            catch (OperationCanceledException)
            {
                return NeteaseResult<bool>.Failure(new NeteaseError(
                    NeteaseErrorCode.Cancelled,
                    "Language_Netease_Cancelled"));
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not skip a Netease personal FM track. ErrorType={0}", ex.GetType().Name);
                var skipError = new NeteaseError(
                    NeteaseErrorCode.Unknown,
                    "Language_Netease_Service_Unavailable");
                this.SetError(skipError);
                return NeteaseResult<bool>.Failure(skipError);
            }
        }

        private List<TrackViewModel> MapTracks(
            IReadOnlyList<NeteasePersonalFmItem> items,
            ISet<string> existingIds)
        {
            var tracks = new List<TrackViewModel>();

            foreach (NeteasePersonalFmItem item in items ?? Array.Empty<NeteasePersonalFmItem>())
            {
                string id = item?.Song?.Id;

                if (string.IsNullOrWhiteSpace(id) || (existingIds != null && !existingIds.Add(id)))
                {
                    continue;
                }

                TrackViewModel track = NeteaseTrackFactory.Create(this.container, item.Song);

                if (track != null)
                {
                    tracks.Add(track);
                }
            }

            return tracks;
        }

        private int GetRemainingTrackCount()
        {
            IList<TrackViewModel> queue = this.playbackService.Queue;

            if (queue == null || queue.Count == 0)
            {
                return 0;
            }

            int currentIndex = queue.IndexOf(this.playbackService.CurrentTrack);
            return currentIndex < 0 ? queue.Count : Math.Max(0, queue.Count - currentIndex - 1);
        }

        private void SetBusy(bool value)
        {
            if (this.isBusy != value)
            {
                this.isBusy = value;
                this.RaiseStateChanged();
            }
        }

        private void SetError(NeteaseError value)
        {
            if (!object.ReferenceEquals(this.error, value))
            {
                this.error = value;
                this.RaiseStateChanged();
            }
        }

        private void RaiseStateChanged()
        {
            this.StateChanged(this, EventArgs.Empty);
        }
    }
}
