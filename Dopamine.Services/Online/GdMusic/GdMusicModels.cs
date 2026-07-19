using System.Collections.Generic;

namespace Dopamine.Services.Online.GdMusic
{
    public sealed class GdMusicSearchResult
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public IReadOnlyList<string> Artists { get; set; }

        public string AlbumName { get; set; }

        public string PictureId { get; set; }

        public string Source { get; set; }
    }

    public sealed class GdMusicTrackUrl
    {
        public string Url { get; set; }

        public int BitRate { get; set; }

        public long SizeKilobytes { get; set; }
    }
}
