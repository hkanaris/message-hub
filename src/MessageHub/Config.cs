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

    // The pane's share of the window height, set by dragging the dividers.
    public double Size { get; set; } = 1;

    public PaneConfig Copy() => new() { Name = Name, Url = Url, Color = Color, AlertOn = AlertOn };
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
        new() { Name = "Outlook", Url = "https://outlook.office.com/mail/", Color = "#0F78D4" },
        new() { Name = "Telegram", Url = "https://web.telegram.org/a/", Color = "#2AABEE" },
        new() { Name = "Messenger", Url = "https://www.messenger.com", Color = "#0084FF" },
        new() { Name = "Google Chat", Url = "https://chat.google.com", Color = "#00AC47" },
        new() { Name = "Gmail", Url = "https://mail.google.com", Color = "#EA4335" },
    };

    public static PaneConfig Get(string name) => Apps.First(a => a.Name == name).Copy();
}

// config.json can be edited by hand. The app rewrites it when panes are changed from a pane's menu.
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

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    static HubConfig Defaults() => new()
    {
        Panes =
        {
            AppCatalog.Get("Slack"),
            AppCatalog.Get("WhatsApp"),
            AppCatalog.Get("Teams"),
            AppCatalog.Get("Claude"),
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
