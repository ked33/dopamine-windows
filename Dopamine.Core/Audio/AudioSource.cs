namespace Dopamine.Core.Audio
{
    public enum AudioSourceKind
    {
        LocalFile = 0,
        RemoteUri = 1
    }

    public sealed class AudioSource
    {
        public AudioSourceKind Kind { get; private set; }

        public string Location { get; private set; }

        private AudioSource(AudioSourceKind kind, string location)
        {
            this.Kind = kind;
            this.Location = location;
        }

        public static AudioSource FromLocalFile(string path)
        {
            return new AudioSource(AudioSourceKind.LocalFile, path);
        }

        public static AudioSource FromRemoteUri(string uri)
        {
            return new AudioSource(AudioSourceKind.RemoteUri, uri);
        }
    }
}
