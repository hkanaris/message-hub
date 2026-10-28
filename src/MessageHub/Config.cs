using System.IO;
using System.Text.Json;
using System.Windows;

namespace MessageHub;

public sealed class PaneConfig
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Color { get; set; } = "#888888";

    // "all": every notification alerts loudly. "vip": only ones matching vipKeywords.
    // "none": never loud; the header still shows the latest message.
    public string AlertOn { get; set; } = "all";
    public bool Enabled { get; set; } = true;
}

// config.json belongs to the user: the app writes it once with defaults and never again.
public sealed class HubConfig
{
    public List<PaneConfig> Panes { get; set; } = new();
    public List<string> VipKeywords { get; set; } = new();
    public bool Sound { get; set; } = true;
    public bool FlashTaskbar { get; set; } = true;
    public bool ExpandOnVip { get; set; } = false;

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MessageHub");

    public static string FilePath { get; } = Path.Combine(Folder, "config.json");

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static HubConfig Load()
    {
        if (!File.Exists(FilePath))
        {
            var defaults = Defaults();
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(defaults, Json));
            return defaults;
        }

        try
        {
            var config = JsonSerializer.Deserialize<HubConfig>(File.ReadAllText(FilePath), Json);
            if (config is { Panes.Count: > 0 }) return config;
        }
        catch (JsonException ex)
        {
            MessageBox.Show($"config.json couldn't be read, so the defaults are in use.\n\n{ex.Message}", "Message Hub");
        }

        return Defaults();
    }

    static HubConfig Defaults() => new()
    {
        Panes =
        {
            new() { Name = "Slack", Url = "https://app.slack.com/client", Color = "#E01E5A" },
            new() { Name = "WhatsApp", Url = "https://web.whatsapp.com", Color = "#25D366" },
            new() { Name = "Teams", Url = "https://teams.microsoft.com", Color = "#7B83EB" },
            new() { Name = "Claude", Url = "https://claude.ai", Color = "#D97757" },
        },
    };
}

// state.json is the app's own: window position and each pane's zoom.
public sealed class HubState
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool Maximized { get; set; }
    public Dictionary<string, double> Zoom { get; set; } = new();

    static string FilePath { get; } = Path.Combine(HubConfig.Folder, "state.json");

    public static HubState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<HubState>(File.ReadAllText(FilePath), HubConfig.Json) ?? new();
        }
        catch (JsonException)
        {
        }

        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(HubConfig.Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, HubConfig.Json));
    }
}
