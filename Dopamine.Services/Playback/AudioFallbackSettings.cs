using Digimezzo.Foundation.Core.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Dopamine.Services.Playback
{
    public sealed class AudioFallbackSource
    {
        public string Id { get; set; }
        public bool Enabled { get; set; }
    }

    public sealed class AudioFallbackSourceDefinition
    {
        public AudioFallbackSourceDefinition(string id, string name)
        {
            this.Id = id;
            this.Name = name;
        }

        public string Id { get; }
        public string Name { get; }
        public string Source => this.Id.Substring(this.Id.IndexOf(':') + 1);
        public string ProviderId => this.Id.StartsWith("gd:", StringComparison.Ordinal)
            ? "gdstudio" : "unblockneteasemusic";
    }

    public static class AudioFallbackCatalog
    {
        public static IReadOnlyList<AudioFallbackSourceDefinition> Sources { get; } = Array.AsReadOnly(new[]
        {
            new AudioFallbackSourceDefinition("gd:netease", "GD · Netease"),
            new AudioFallbackSourceDefinition("gd:joox", "GD · JOOX"),
            new AudioFallbackSourceDefinition("gd:bilibili", "GD · Bilibili"),
            new AudioFallbackSourceDefinition("gd:tencent", "GD · QQ Music"),
            new AudioFallbackSourceDefinition("gd:kuwo", "GD · Kuwo"),
            new AudioFallbackSourceDefinition("gd:tidal", "GD · TIDAL"),
            new AudioFallbackSourceDefinition("gd:qobuz", "GD · Qobuz"),
            new AudioFallbackSourceDefinition("gd:apple", "GD · Apple Music"),
            new AudioFallbackSourceDefinition("gd:ytmusic", "GD · YouTube Music"),
            new AudioFallbackSourceDefinition("gd:spotify", "GD · Spotify"),
            new AudioFallbackSourceDefinition("unblock:kugou", "Unblock · Kugou"),
            new AudioFallbackSourceDefinition("unblock:bodian", "Unblock · Bodian"),
            new AudioFallbackSourceDefinition("unblock:kuwo", "Unblock · Kuwo")
        });

        public static AudioFallbackSourceDefinition Find(string id) => Sources.FirstOrDefault(x => x.Id == id);
    }

    public sealed class AudioFallbackConfiguration
    {
        public int Version { get; set; } = 1;
        public bool Enabled { get; set; }
        public int GdQuality { get; set; } = 320;
        public bool UnblockEnableFlac { get; set; }
        public List<AudioFallbackSource> Sources { get; set; } = new List<AudioFallbackSource>();
        public bool HasUnblockSources => this.Enabled && this.Sources.Any(
            x => x.Enabled && x.Id.StartsWith("unblock:", StringComparison.Ordinal));

        public static AudioFallbackConfiguration CreateDefault(bool enabled = false,
            IEnumerable<string> legacySources = null, bool enableFlac = false)
        {
            var legacy = new HashSet<string>(legacySources ?? Enumerable.Empty<string>());
            // GD Netease is always the first selected fallback on migration/new installs.
            return new AudioFallbackConfiguration
            {
                Enabled = enabled,
                UnblockEnableFlac = enableFlac,
                Sources = AudioFallbackCatalog.Sources.Select(x => new AudioFallbackSource
                {
                    Id = x.Id,
                    Enabled = x.Id == "gd:netease" || (x.ProviderId == "unblockneteasemusic" && legacy.Contains(x.Source))
                }).ToList()
            };
        }

        public AudioFallbackConfiguration Normalize()
        {
            var result = new AudioFallbackConfiguration
            {
                Enabled = this.Enabled,
                GdQuality = new[] { 128, 192, 320, 740, 999 }.Contains(this.GdQuality) ? this.GdQuality : 320,
                UnblockEnableFlac = this.UnblockEnableFlac
            };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AudioFallbackSource row in this.Sources ?? new List<AudioFallbackSource>())
            {
                if (row != null && AudioFallbackCatalog.Find(row.Id) != null && seen.Add(row.Id))
                    result.Sources.Add(new AudioFallbackSource { Id = row.Id, Enabled = row.Enabled });
            }
            // New catalog entries are appended disabled; never overwrite an intentional order or empty selection.
            foreach (var item in AudioFallbackCatalog.Sources.Where(x => seen.Add(x.Id)))
                result.Sources.Add(new AudioFallbackSource { Id = item.Id });
            return result;
        }
    }

    public interface IAudioFallbackSettings
    {
        AudioFallbackConfiguration Current { get; }
        string Error { get; }
        event EventHandler Changed;
        bool TrySave(AudioFallbackConfiguration configuration);
    }

    public sealed class AudioFallbackSettings : IAudioFallbackSettings
    {
        private readonly object gate = new object();
        private readonly string path;
        private AudioFallbackConfiguration current;
        private bool invalidFile;

        public AudioFallbackSettings() : this(Path.Combine(SettingsClient.ApplicationFolder(), "AudioFallback.json"),
            AudioFallbackConfiguration.CreateDefault(UnblockNeteaseMusicSettings.IsEnabled,
                UnblockNeteaseMusicSettings.Sources, UnblockNeteaseMusicSettings.EnableFlac)) { }

        public AudioFallbackSettings(string path, AudioFallbackConfiguration defaults)
        {
            this.path = path;
            this.current = defaults.Normalize();
            try
            {
                if (System.IO.File.Exists(path))
                {
                    var loaded = JsonSerializer.Deserialize<AudioFallbackConfiguration>(System.IO.File.ReadAllText(path, Encoding.UTF8));
                    if (loaded == null || loaded.Version != 1 || loaded.Sources == null)
                        throw new InvalidDataException();
                    this.current = loaded.Normalize();
                }
                else
                {
                    this.TrySave(this.current);
                }
            }
            catch (Exception ex)
            {
                this.invalidFile = true;
                this.current.Enabled = false;
                this.Error = ex.GetType().Name;
            }
        }

        // Return defensive copies so callers cannot change live settings without a successful save.
        public AudioFallbackConfiguration Current { get { lock (this.gate) return this.current.Normalize(); } }
        public string Error { get; private set; }
        public event EventHandler Changed = delegate { };

        public bool TrySave(AudioFallbackConfiguration configuration)
        {
            lock (this.gate)
            {
                if (this.invalidFile) return false; // Preserve unreadable/newer files for recovery.
                string temporary = this.path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    var normalized = configuration.Normalize();
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(this.path)));
                    byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized,
                        new JsonSerializerOptions { WriteIndented = true, IgnoreReadOnlyProperties = true }));
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    if (System.IO.File.Exists(this.path)) System.IO.File.Replace(temporary, this.path, null);
                    else System.IO.File.Move(temporary, this.path);
                    this.current = normalized;
                    this.Error = null;
                }
                catch (Exception ex)
                {
                    this.Error = ex.GetType().Name;
                    return false;
                }
                finally
                {
                    try { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); } catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            this.Changed(this, EventArgs.Empty);
            return true;
        }
    }
}
