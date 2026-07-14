using Dopamine.Core.Audio;
using Dopamine.Services.Entities;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Playback
{
    public sealed class PlaybackSourceResolver : IPlaybackSourceResolver
    {
        private readonly NeteasePlaybackSourceResolver neteaseResolver;

        public PlaybackSourceResolver(NeteasePlaybackSourceResolver neteaseResolver)
        {
            this.neteaseResolver = neteaseResolver;
        }

        public Task<PlaybackSourceResolution> ResolveAsync(
            TrackViewModel track,
            PlaybackSourceRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (track == null)
            {
                return Task.FromResult(Failure(PlaybackFailureReason.Unknown, string.Empty));
            }

            if (track.IsLocalFile)
            {
                if (!File.Exists(track.Path))
                {
                    return Task.FromResult(Failure(PlaybackFailureReason.FileNotFound, string.Empty));
                }

                return Task.FromResult(new PlaybackSourceResolution
                {
                    IsSuccess = true,
                    AudioSource = AudioSource.FromLocalFile(track.Path)
                });
            }

            if (track.SourceInfo != null && track.SourceInfo.Kind == TrackSourceKind.Netease)
            {
                return this.neteaseResolver.ResolveAsync(track, request, cancellationToken);
            }

            return Task.FromResult(Failure(PlaybackFailureReason.ApiChanged, "Language_Netease_Service_Unavailable"));
        }

        private static PlaybackSourceResolution Failure(PlaybackFailureReason reason, string messageKey)
        {
            return new PlaybackSourceResolution
            {
                FailureReason = reason,
                MessageKey = messageKey,
                IsSuccess = false
            };
        }
    }
}
