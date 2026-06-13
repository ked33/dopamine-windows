using Digimezzo.Foundation.Core.Logging;
using Dopamine.Data;
using Dopamine.Data.Entities;
using SQLite;
using System;
using System.Collections.Generic;

namespace Dopamine.Services.Indexing
{
    internal class IndexerCache
    {
        private Dictionary<string, long> cachedTrackIdsBySafePath;

        private ISQLiteConnectionFactory factory;

        public IndexerCache(ISQLiteConnectionFactory factory)
        {
            this.factory = factory;
        }

        public bool HasCachedTrack(ref Track track)
        {
            bool hasCachedTrack = false;
            long similarTrackId = 0;

            if (this.cachedTrackIdsBySafePath == null || string.IsNullOrEmpty(track.SafePath))
            {
                return false;
            }

            try
            {
                if (this.cachedTrackIdsBySafePath.TryGetValue(track.SafePath, out similarTrackId))
                {
                    hasCachedTrack = true;
                    track.TrackID = similarTrackId;
                }
            }
            catch (Exception ex)
            {
                LogClient.Error("There was a problem checking if Track with path '{0}' exists in the cache. Exception: {1}", track.Path, ex.Message);
            }

            return hasCachedTrack;
        }

        public void AddTrack(Track track)
        {
            if (!string.IsNullOrEmpty(track.SafePath))
            {
                if (this.cachedTrackIdsBySafePath == null)
                {
                    this.cachedTrackIdsBySafePath = new Dictionary<string, long>();
                }

                this.cachedTrackIdsBySafePath[track.SafePath] = track.TrackID;
            }
        }

        public void Initialize()
        {
            // Track lookup only needs SafePath -> TrackID. Avoid keeping full Track entities in memory while indexing.
            using (SQLiteConnection conn = this.factory.GetConnection())
            {
                this.cachedTrackIdsBySafePath = new Dictionary<string, long>();

                foreach (Track track in conn.Query<Track>("SELECT TrackID, SafePath FROM Track;"))
                {
                    this.AddTrack(track);
                }
            }
        }

        public void Clear()
        {
            this.cachedTrackIdsBySafePath?.Clear();
            this.cachedTrackIdsBySafePath = null;
        }
    }
}
