using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    // The pane's share of the window height, set by dragging the dividers.
    public double Size { get; set; } = 1;

    // True: shares the previous pane's row (its column when side by side), with Share as its part of it.
    public bool SplitWithPrevious { get; set; }
    public double Share { get; set; } = 1;

    // Backup alerts when the unread count rises with no notification. Off for mail by default,
    // where the app's own notification settings decide what matters.
    public bool CountAlerts { get; set; } = true;

    // Null: the shared default profile. Otherwise a separate WebView2 profile with its own sign-ins.
    public string? Profile { get; set; }

    // Null: the default sound. "none", a sound from Windows' Media folder, or a .wav file path.
    public string? Sound { get; set; }

    public PaneConfig Copy() => new() { Name = Name, Url = Url, Color = Color, AlertOn = AlertOn, CountAlerts = CountAlerts };
}

// The apps offered by each pane's "Switch to" and "Add app below" menus.
public static class AppCatalog
{
    public static IReadOnlyList<PaneConfig> Apps { get; } = new PaneConfig[]
    {
        new() { Name = "Slack", Url = "https://app.slack.com/client", Color = "#E01E5A" },
        new() { Name = "WhatsApp", Url = "https://web.whatsapp.com", Color = "#25D366" },
        new() { Name = "Teams", Url = "https://teams.microsoft.com", Color = "#7B83EB" },
        new() { Name = "Claude", Url = "https://claude.ai", Color = "#D97757" },
        new() { Name = "ChatGPT", Url = "https://chatgpt.com", Color = "#10A37F" },
        new() { Name = "Discord", Url = "https://discord.com/app", Color = "#5865F2" },
        new() { Name = "Outlook", Url = "https://outlook.office.com/mail/", Color = "#0F78D4", CountAlerts = false },
        new() { Name = "Telegram", Url = "https://web.telegram.org/a/", Color = "#2AABEE" },
        new() { Name = "Messenger", Url = "https://www.messenger.com", Color = "#0084FF" },
        new() { Name = "Google Chat", Url = "https://chat.google.com", Color = "#00AC47" },
        new() { Name = "Gmail", Url = "https://mail.google.com", Color = "#EA4335", CountAlerts = false },
    };

    public static PaneConfig Get(string name) => Apps.First(a => a.Name == name).Copy();
}

// A saved set of apps and how they're arranged. The hub shows one layout at a time.
public sealed class LayoutConfig
{
    public string Name { get; set; } = "";

    // "stacked" (top to bottom), "side-by-side" or "grid".
    public string Arrangement { get; set; } = "stacked";
    public List<PaneConfig> Panes { get; set; } = new();
}

// config.json can be edited by hand while Message Hub is closed. The app rewrites it whenever
// something is changed from a pane's menu.
public sealed class HubConfig
{
    public List<LayoutConfig> Layouts { get; set; } = new();
    public string ActiveLayout { get; set; } = "";
    public List<string> VipKeywords { get; set; } = new();
    public bool Sound { get; set; } = true;
    public bool FlashTaskbar { get; set; } = true;
    public bool ExpandOnVip { get; set; } = false;
    public bool CloseToTray { get; set; } = true;

    // Versions before 0.4 kept a single set of panes here. Read once, then moved into Layouts.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PaneConfig>? Panes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Layout { get; set; }

    [JsonIgnore]
    public LayoutConfig Current => Layouts.FirstOrDefault(l => l.Name == ActiveLayout) ?? Layouts[0];

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
            if (config == null) return Defaults();

            // Hand edits can leave nulls the defaults would otherwise cover.
            config.Layouts ??= new();
            config.VipKeywords ??= new();
            config.ActiveLayout ??= "";
            config.Layouts.RemoveAll(l => l == null || l.Panes == null || l.Name == null);
            foreach (var layout in config.Layouts)
            {
                layout.Arrangement ??= "stacked";
                layout.Panes.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Url));
                foreach (var pane in layout.Panes)
                {
                    pane.Name ??= pane.Url;
                    pane.Color ??= "#888888";
                    pane.AlertOn ??= "all";
                }
            }

            if (config is { Panes.Count: > 0 } && config.Layouts.Count == 0)
            {
                config.Layouts.Add(new LayoutConfig { Name = "Main", Arrangement = config.Layout ?? "stacked", Panes = config.Panes });
                config.Layouts.Add(DiscordAndChatGpt());
                config.ActiveLayout = "Main";
            }
            config.Panes = null;
            config.Layout = null;
            config.Layouts.RemoveAll(l => l.Panes.Count == 0);
            if (config.Layouts.Count > 0) return config;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"config.json couldn't be read, so the defaults are in use.\n\n{ex.Message}", "Message Hub");
        }

        return Defaults();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    static HubConfig Defaults() => new()
    {
        ActiveLayout = "Main",
        Layouts =
        {
            new LayoutConfig
            {
                Name = "Main",
                Panes =
                {
                    AppCatalog.Get("Slack"),
                    AppCatalog.Get("WhatsApp"),
                    AppCatalog.Get("Teams"),
                    AppCatalog.Get("Claude"),
                },
            },
            DiscordAndChatGpt(),
        },
    };

    static LayoutConfig DiscordAndChatGpt() => new()
    {
        Name = "Discord + ChatGPT",
        Arrangement = "side-by-side",
        Panes = { AppCatalog.Get("Discord"), AppCatalog.Get("ChatGPT") },
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

    // Do not disturb: alerts stay quiet until this time. DateTime.MaxValue means until turned off.
    public DateTime? SnoozedUntil { get; set; }

    public bool TrayHintShown { get; set; }
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
