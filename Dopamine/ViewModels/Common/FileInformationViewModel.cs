using Digimezzo.Foundation.Core.Logging;
using Dopamine.Core.Logging;
using Digimezzo.Foundation.Core.Utils;
using Dopamine.Core.Utils;
using Dopamine.Data.Metadata;
using Dopamine.Services.Metadata;
using Dopamine.Services.Entities;
using Dopamine.Services.Online.Netease;
using Dopamine.Services.Playback;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.ViewModels.Common
{
    public class FileInformationViewModel : BindableBase
    {
        private IMetadataService metaDataService;

        // Song
        private string songTitle;
        private string songArtists;
        private string songAlbum;
        private string songYear;
        private string songGenres;
        private string songTrackNumber;

        // File
        private string fileName;
        private string fileFolder;
        private string filePath;
        private string fileSize;
        private string fileLastModified;

        // Audio
        private string audioDuration;
        private string audioType;
        private string audioSampleRate;
        private string audioBitrate;
        private string audioSize;

        // Online source
        private bool isOnline;
        private string onlineSource;
        private readonly TrackViewModel onlineTrack;
        private readonly INeteaseMusicService neteaseMusicService;
        private readonly IList<IOnlineAudioFallbackProvider> audioFallbackProviders;
        private CancellationTokenSource audioInformationCancellationTokenSource;
        private bool hasLoadedOnlineAudioInformation;

        public bool IsOnline
        {
            get { return this.isOnline; }
            private set
            {
                if (SetProperty<bool>(ref this.isOnline, value))
                {
                    RaisePropertyChanged(nameof(this.IsLocalFile));
                }
            }
        }

        public bool IsLocalFile => !this.IsOnline;

        public string OnlineSource
        {
            get { return this.onlineSource; }
            private set { SetProperty<string>(ref this.onlineSource, value); }
        }

        public string SongTitle
        {
            get { return this.songTitle; }
            set { SetProperty<string>(ref this.songTitle, value); }
        }

        public string SongArtists
        {
            get { return this.songArtists; }
            set { SetProperty<string>(ref this.songArtists, value); }
        }

        public string SongAlbum
        {
            get { return this.songAlbum; }
            set { SetProperty<string>(ref this.songAlbum, value); }
        }

        public string SongYear
        {
            get { return this.songYear; }
            set { SetProperty<string>(ref this.songYear, value); }
        }

        public string SongGenres
        {
            get { return this.songGenres; }
            set { SetProperty<string>(ref this.songGenres, value); }
        }

        public string SongTrackNumber
        {
            get { return this.songTrackNumber; }
            set { SetProperty<string>(ref this.songTrackNumber, value); }
        }

        public string FileName
        {
            get { return this.fileName; }
            set { SetProperty<string>(ref this.fileName, value); }
        }

        public string FileFolder
        {
            get { return this.fileFolder; }
            set { SetProperty<string>(ref this.fileFolder, value); }
        }

        public string FilePath
        {
            get { return this.filePath; }
            set { SetProperty<string>(ref this.filePath, value); }
        }

        public string FileSize
        {
            get { return this.fileSize; }
            set { SetProperty<string>(ref this.fileSize, value); }
        }

        public string FileLastModified
        {
            get { return this.fileLastModified; }
            set { SetProperty<string>(ref this.fileLastModified, value); }
        }

        public string AudioDuration
        {
            get { return this.audioDuration; }
            set { SetProperty<string>(ref this.audioDuration, value); }
        }


        public string AudioType
        {
            get { return this.audioType; }
            set { SetProperty<string>(ref this.audioType, value); }
        }

        public string AudioSampleRate
        {
            get { return this.audioSampleRate; }
            set { SetProperty<string>(ref this.audioSampleRate, value); }
        }

        public string AudioBitrate
        {
            get { return this.audioBitrate; }
            set { SetProperty<string>(ref this.audioBitrate, value); }
        }

        public string AudioSize
        {
            get { return this.audioSize; }
            set { SetProperty<string>(ref this.audioSize, value); }
        }
        
        public FileInformationViewModel(IMetadataService metaDataService, string path)
        {
            this.metaDataService = metaDataService;

            this.GetFileMetadata(path);
            this.GetFileInformation(path);
        }

        public FileInformationViewModel(
            TrackViewModel track,
            INeteaseMusicService neteaseMusicService,
            IEnumerable<IOnlineAudioFallbackProvider> audioFallbackProviders)
        {
            if (track == null || track.SourceInfo == null || track.IsLocalFile)
            {
                return;
            }

            this.onlineTrack = track;
            this.neteaseMusicService = neteaseMusicService;
            this.audioFallbackProviders = (audioFallbackProviders ?? Enumerable.Empty<IOnlineAudioFallbackProvider>())
                .OrderBy(x => x.Order)
                .ToList();
            this.IsOnline = true;
            this.SongTitle = track.TrackTitle;
            this.SongArtists = track.ArtistName;
            this.SongAlbum = track.AlbumTitle;
            this.AudioDuration = track.Track.Duration.HasValue
                ? FormatUtils.FormatTime(TimeSpan.FromMilliseconds(track.Track.Duration.Value))
                : string.Empty;
            this.OnlineSource = ResourceUtils.GetString("Language_Netease_Music");
            this.SetAudioInformationLoading();
        }

        public async Task LoadOnlineAudioInformationAsync()
        {
            if (!this.IsOnline || this.hasLoadedOnlineAudioInformation ||
                this.onlineTrack?.SourceInfo == null || this.neteaseMusicService == null ||
                string.IsNullOrWhiteSpace(this.onlineTrack.SourceInfo.RemoteId))
            {
                return;
            }

            this.CancelOnlineAudioInformation();
            var cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            this.audioInformationCancellationTokenSource = cancellationTokenSource;
            this.SetAudioInformationLoading();

            try
            {
                NeteaseAudioResolution official = await this.neteaseMusicService.ResolveOfficialAudioAsync(
                    this.onlineTrack.SourceInfo.RemoteId,
                    false,
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (official != null && official.IsSuccess)
                {
                    this.ApplyAudioInformation(
                        ResourceUtils.GetString("Language_Netease_Music"),
                        official.Type,
                        official.BitRate,
                        official.Size);
                    this.hasLoadedOnlineAudioInformation = true;
                    return;
                }

                foreach (IOnlineAudioFallbackProvider provider in this.audioFallbackProviders)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!provider.CanHandle(official?.Error))
                    {
                        continue;
                    }

                    try
                    {
                        OnlineAudioFallbackResult fallback = await provider.TryResolveAsync(
                            new OnlineAudioFallbackRequest
                            {
                                Track = this.onlineTrack,
                                OfficialFailure = official.Error,
                                ForceRefresh = false
                            },
                            cancellationToken);

                        cancellationToken.ThrowIfCancellationRequested();

                        if (fallback != null && fallback.IsSuccess)
                        {
                            string source = string.IsNullOrWhiteSpace(fallback.ProviderId)
                                ? provider.Id
                                : fallback.ProviderId;
                            this.ApplyAudioInformation(
                                string.Format("UnblockNeteaseMusic ({0})", source),
                                fallback.MediaType,
                                fallback.Bitrate,
                                fallback.Size);
                            this.hasLoadedOnlineAudioInformation = true;
                            return;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warning(
                            "Could not resolve online audio information from fallback provider. Provider={0}, ErrorType={1}",
                            provider.Id,
                            ex.GetType().Name);
                    }
                }

                this.SetAudioInformationUnavailable();
                this.hasLoadedOnlineAudioInformation = true;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    "Could not load online audio information. ErrorType={0}",
                    ex.GetType().Name);
                this.SetAudioInformationUnavailable();
                this.hasLoadedOnlineAudioInformation = true;
            }
            finally
            {
                if (object.ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref this.audioInformationCancellationTokenSource,
                        null,
                        cancellationTokenSource),
                    cancellationTokenSource))
                {
                    cancellationTokenSource.Dispose();
                }
            }
        }

        public void CancelOnlineAudioInformation()
        {
            CancellationTokenSource source = Interlocked.Exchange(
                ref this.audioInformationCancellationTokenSource,
                null);
            source?.Cancel();
            source?.Dispose();
        }

        private void SetAudioInformationLoading()
        {
            string loading = ResourceUtils.GetString("Language_Netease_Loading_Audio_Information");
            this.AudioType = loading;
            this.AudioBitrate = loading;
            this.AudioSize = loading;
        }

        private void SetAudioInformationUnavailable()
        {
            string unavailable = ResourceUtils.GetString("Language_Not_Available");
            this.AudioType = unavailable;
            this.AudioBitrate = unavailable;
            this.AudioSize = unavailable;
        }

        private void ApplyAudioInformation(string source, string type, long bitRate, long size)
        {
            string unavailable = ResourceUtils.GetString("Language_Not_Available");
            this.OnlineSource = string.IsNullOrWhiteSpace(source) ? unavailable : source;
            this.AudioType = FormatAudioType(type, unavailable);
            this.AudioBitrate = FormatBitRate(bitRate, unavailable);
            this.AudioSize = size > 0 ? FormatUtils.FormatFileSize(size) : unavailable;
        }

        private static string FormatAudioType(string type, string unavailable)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return unavailable;
            }

            int separatorIndex = type.LastIndexOf('/');
            string displayType = separatorIndex >= 0 && separatorIndex < type.Length - 1
                ? type.Substring(separatorIndex + 1)
                : type;
            return displayType.ToUpperInvariant();
        }

        private static string FormatBitRate(long bitRate, string unavailable)
        {
            if (bitRate <= 0)
            {
                return unavailable;
            }

            double kiloBitsPerSecond = bitRate >= 1000 ? bitRate / 1000.0 : bitRate;
            return string.Format(CultureInfo.InvariantCulture, "{0:0.#} kbps", kiloBitsPerSecond);
        }
    
        private void GetFileMetadata(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                var fm = new FileMetadata(path);

                this.SongTitle = fm.Title.Value;
                this.SongAlbum = fm.Album.Value;
                this.SongArtists = string.Join(", ", fm.Artists.Values);
                this.SongGenres = string.Join(", ", fm.Genres.Values);
                this.SongYear = fm.Year.Value.ToString();
                this.SongTrackNumber = fm.TrackNumber.Value.ToString();
                this.AudioDuration = FormatUtils.FormatTime(fm.Duration);
                this.AudioType = fm.Type;
                this.AudioSampleRate = string.Format("{0} {1}", fm.SampleRate.ToString(), "Hz");
                this.AudioBitrate = string.Format("{0} {1}", fm.BitRate.ToString(), "kbps");
            }
            catch (Exception ex)
            {
                AppLog.Error("Error while getting file Metadata. Exception: {0}", ex.Message);
            }
        }

        private void GetFileInformation(string path)
        {
            try
            {
                this.FileName = FileUtils.FileName(path);
                this.FileFolder = FileUtils.DirectoryName(path);
                this.FilePath = path;
                this.FileSize = FormatUtils.FormatFileSize(FileUtils.SizeInBytes(path));
                this.FileLastModified = FileUtils.DateModified(path).ToString("D", new CultureInfo(ResourceUtils.GetString("Language_ISO639-1")));
            }
            catch (Exception ex)
            {
                AppLog.Error("Error while getting file Information. Exception: {0}", ex.Message);
            }
        }
    }
}
