using System.IO;
using System.Media;

namespace MessageHub;

// Alert sounds: Windows' default, none, one of the notification sounds in C:\Windows\Media, or any .wav.
static class Sounds
{
    public const string None = "none";

    static readonly string MediaFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    static readonly string[] Wanted =
    {
        "Windows Notify Messaging", "Windows Notify Email", "Windows Notify Calendar", "Windows Notify System Generic",
        "Windows Notify", "Windows Proximity Notification", "Windows Message Nudge", "Windows Ding",
        "chimes", "chord", "ding", "notify", "tada",
    };

    public static IReadOnlyList<string> Choices { get; } =
        Wanted.Where(name => File.Exists(Path.Combine(MediaFolder, name + ".wav"))).ToList();

    public static bool IsFile(string? sound) => sound != null && sound.Contains('\\');

    public static void Play(string? sound)
    {
        if (sound == None) return;

        var path = string.IsNullOrEmpty(sound) ? null
            : IsFile(sound) ? sound
            : Path.Combine(MediaFolder, sound + ".wav");

        try
        {
            if (path != null && File.Exists(path))
            {
                new SoundPlayer(path).Play();
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
        }

        SystemSounds.Asterisk.Play();
    }
}
