namespace Dopamine.Services.Entities
{
    public sealed class TrackSourceInfo
    {
        public TrackSourceKind Kind { get; set; }

        public string ProviderId { get; set; }

        public string RemoteId { get; set; }

        public string ArtworkUrl { get; set; }

        public TrackSourceInfo DeepCopy()
        {
            return new TrackSourceInfo
            {
                Kind = this.Kind,
                ProviderId = this.ProviderId,
                RemoteId = this.RemoteId,
                ArtworkUrl = this.ArtworkUrl
            };
        }

        public static TrackSourceInfo LocalFile()
        {
            return new TrackSourceInfo { Kind = TrackSourceKind.LocalFile };
        }
    }
}
