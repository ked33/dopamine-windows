using System;
using System.Collections.Generic;

namespace Dopamine.Services.Online.Netease
{
    public sealed class NeteaseResult<T>
    {
        public bool IsSuccess { get; private set; }

        public T Value { get; private set; }

        public NeteaseError Error { get; private set; }

        public static NeteaseResult<T> Success(T value)
        {
            return new NeteaseResult<T> { IsSuccess = true, Value = value };
        }

        public static NeteaseResult<T> Failure(NeteaseError error)
        {
            return new NeteaseResult<T> { IsSuccess = false, Error = error };
        }
    }

    public sealed class NeteaseAccountProfile
    {
        public string UserId { get; set; }

        public string Nickname { get; set; }

        public int VipType { get; set; }
    }

    public sealed class NeteaseQrKey
    {
        public string Unikey { get; set; }

        public string ChainId { get; set; }

        public string QrContent { get; set; }
    }

    public sealed class NeteaseQrCheck
    {
        public int Code { get; set; }
    }

    public enum NeteaseQrState
    {
        WaitingForScan = 0,
        WaitingForConfirm = 1,
        Authorized = 2,
        Expired = 3,
        Cancelled = 4,
        Error = 5
    }

    public sealed class NeteaseQrSession
    {
        public string Unikey { get; set; }

        public string ChainId { get; set; }

        public string YdDeviceToken { get; set; }

        public string QrContent { get; set; }

        public long LoginGeneration { get; set; }
    }

    public sealed class NeteaseQrPollResult
    {
        public NeteaseQrState State { get; set; }

        public NeteaseAccountProfile Account { get; set; }

        public NeteaseError Error { get; set; }
    }

    public sealed class NeteaseLoginResult
    {
        public bool IsSuccess { get; set; }

        public NeteaseAccountProfile Account { get; set; }

        public NeteaseError Error { get; set; }
    }

    public sealed class NeteaseRecommendedSong
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public IReadOnlyList<string> Artists { get; set; }

        public string AlbumId { get; set; }

        public string AlbumName { get; set; }

        public long DurationMilliseconds { get; set; }

        public string ArtworkUrl { get; set; }

        public bool IsKnownUnavailable { get; set; }
    }

    public sealed class NeteaseLikedLibrary
    {
        public string PlaylistId { get; set; }

        public IReadOnlyList<string> SongIds { get; set; }
    }

    public sealed class NeteaseIntelligenceRecommendation
    {
        public NeteaseRecommendedSong Song { get; set; }

        public bool IsRecommended { get; set; }
    }

    public sealed class NeteasePersonalFmItem
    {
        public NeteaseRecommendedSong Song { get; set; }

        public string RecommendedReason { get; set; }
    }

    public sealed class NeteaseRecommendationSnapshot
    {
        public int Version { get; set; }

        public string AccountUserId { get; set; }

        public DateTime RecommendationDate { get; set; }

        public List<NeteaseRecommendedSong> Songs { get; set; }
    }

    public sealed class NeteaseRecommendationMutation
    {
        public string RemovedSongId { get; set; }

        public NeteaseRecommendedSong Replacement { get; set; }

        public IReadOnlyList<NeteaseRecommendedSong> UpdatedRecommendations { get; set; }

        public bool PersistenceSucceeded { get; set; }

        public bool RequiresRefresh { get; set; }
    }

    public sealed class NeteaseRecommendationLoadResult
    {
        public bool Exists { get; set; }

        public bool IsSuccess { get; set; }

        public NeteaseRecommendationSnapshot Snapshot { get; set; }

        public NeteaseError Error { get; set; }
    }

    public sealed class NeteaseAudioResolution
    {
        public bool IsSuccess { get; set; }

        public string SongId { get; set; }

        public string Url { get; set; }

        public string Type { get; set; }

        public string QualityLevel { get; set; }

        public long BitRate { get; set; }

        public long Size { get; set; }

        public NeteaseError Error { get; set; }
    }

    public sealed class NeteaseLyricResult
    {
        public bool IsSuccess { get; set; }

        public string Lyric { get; set; }

        public string TranslationLyric { get; set; }

        public string RomajiLyric { get; set; }

        public string KaraokeLyric { get; set; }

        public string KaraokeTranslationLyric { get; set; }

        public NeteaseError Error { get; set; }
    }

    public enum NeteaseSessionState
    {
        SignedOut = 0,
        Restoring = 1,
        SigningIn = 2,
        SignedIn = 3,
        OfflineUnknown = 4,
        Expired = 5,
        Error = 6
    }

    public sealed class NeteaseSessionSnapshot
    {
        public int Version { get; set; }

        public Dictionary<string, string> Cookies { get; set; }

        public NeteaseAccountProfile Account { get; set; }
    }

    public sealed class NeteaseSessionLoadResult
    {
        public bool Exists { get; set; }

        public bool IsSuccess { get; set; }

        public NeteaseSessionSnapshot Snapshot { get; set; }

        public NeteaseError Error { get; set; }
    }
}
