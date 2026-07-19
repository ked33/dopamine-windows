using Dopamine.Core.Settings;
using Dopamine.Services.Entities;
using Dopamine.Services.Playlist;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Dopamine.Services.Playback
{
    /// <summary>
    /// Persists the shuffle preference of each individual playlist as a JSON map
    /// in the "Playback|PlaylistShuffleMap" setting. Only playlists with shuffle
    /// enabled are stored: a missing entry means "play in list order".
    /// </summary>
    public static class PlaylistShuffleMemory
    {
        private const string SettingsNamespace = "Playback";
        private const string SettingName = "PlaylistShuffleMap";

        private static readonly object syncRoot = new object();

        public static string CreateContextId(PlaylistType type, string playlistName)
        {
            if (string.IsNullOrWhiteSpace(playlistName))
            {
                return null;
            }

            // Lower-case normalization matches PlaylistViewModel.Equals (OrdinalIgnoreCase)
            // and the case-insensitive playlist file names on Windows.
            return $"{type}:{playlistName.Trim().ToLowerInvariant()}";
        }

        public static string CreateContextId(PlaylistViewModel playlist)
        {
            if (playlist == null)
            {
                return null;
            }

            return CreateContextId(playlist.Type, playlist.Name);
        }

        public static bool Get(string contextId)
        {
            if (string.IsNullOrEmpty(contextId))
            {
                return false;
            }

            lock (syncRoot)
            {
                bool shuffle;

                return ReadMap().TryGetValue(contextId, out shuffle) && shuffle;
            }
        }

        public static void Set(string contextId, bool shuffle)
        {
            if (string.IsNullOrEmpty(contextId))
            {
                return;
            }

            lock (syncRoot)
            {
                Dictionary<string, bool> map = ReadMap();

                if (shuffle)
                {
                    bool currentShuffle;

                    if (map.TryGetValue(contextId, out currentShuffle) && currentShuffle)
                    {
                        return;
                    }

                    map[contextId] = true;
                }
                else if (!map.Remove(contextId))
                {
                    return;
                }

                WriteMap(map);
            }
        }

        public static void Rename(string oldContextId, string newContextId)
        {
            if (string.IsNullOrEmpty(oldContextId) || string.IsNullOrEmpty(newContextId) ||
                string.Equals(oldContextId, newContextId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lock (syncRoot)
            {
                Dictionary<string, bool> map = ReadMap();
                bool shuffle;

                if (!map.TryGetValue(oldContextId, out shuffle))
                {
                    return;
                }

                map.Remove(oldContextId);
                map[newContextId] = shuffle;
                WriteMap(map);
            }
        }

        public static void Remove(string contextId)
        {
            if (string.IsNullOrEmpty(contextId))
            {
                return;
            }

            lock (syncRoot)
            {
                Dictionary<string, bool> map = ReadMap();

                if (map.Remove(contextId))
                {
                    WriteMap(map);
                }
            }
        }

        private static Dictionary<string, bool> ReadMap()
        {
            string json = SettingDefaults.GetOrAdd<string>(SettingsNamespace, SettingName, string.Empty);

            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    Dictionary<string, bool> map = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);

                    if (map != null)
                    {
                        return new Dictionary<string, bool>(map, StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch (Exception)
                {
                    // Corrupt JSON: start over with an empty map. It gets rewritten on the next Set.
                }
            }

            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        private static void WriteMap(Dictionary<string, bool> map)
        {
            string json = map.Count > 0 ? JsonConvert.SerializeObject(map) : string.Empty;
            SettingDefaults.SetSafe<string>(SettingsNamespace, SettingName, json);
        }
    }
}
