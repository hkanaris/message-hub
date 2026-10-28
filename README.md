# message-hub

Slack, WhatsApp, Teams and Claude stacked in one window, with alerts you can't miss.

## Get it

Download `MessageHub.exe` from the latest release (or from the latest **Build** run under Actions) and run it. Nothing to install: it uses the WebView2 runtime that ships with Windows 11. Sign in to each app once; logins are kept.

## Using it

- **New message:** the pane's header pulses in the app's colour, shows the message, the taskbar button flashes and a sound plays.
- **Click a header** to jump to that message and clear the alert.
- **Double-click a header** (or the expand button) to give that pane the whole window. The others shrink to their headers but keep alerting.
- **Ctrl+1–9** expands a pane, **Ctrl+0** restores, **Ctrl+,** opens settings.
- **Ctrl+mouse wheel** zooms a pane; the zoom is remembered.
- **Right-click a header** for reload, reset zoom, open in browser, settings and restart.
- Links open in your normal browser.

Close the desktop Slack, WhatsApp and Teams apps, or you'll get every notification twice. Keep desktop Teams for calls and screen sharing.

## Settings

`%APPDATA%\MessageHub\config.json`, created on first run. Edit it, then restart from the right-click menu.

```json
{
  "panes": [
    { "name": "Slack", "url": "https://app.slack.com/client", "color": "#E01E5A", "alertOn": "all" },
    { "name": "WhatsApp", "url": "https://web.whatsapp.com", "color": "#25D366", "alertOn": "vip" },
    { "name": "Teams", "url": "https://teams.microsoft.com", "color": "#7B83EB", "alertOn": "all" },
    { "name": "Claude", "url": "https://claude.ai", "color": "#D97757", "alertOn": "all" }
  ],
  "vipKeywords": ["Stuart", "Directors", "incident-management"],
  "sound": true,
  "flashTaskbar": true,
  "expandOnVip": false
}
```

- `alertOn`: `all` alerts on every notification, `vip` only when the sender or text matches `vipKeywords`, `none` never (the header still shows the latest message).
- `vipKeywords`: names, chats or channels that matter. Matching is on the notification's title and text, ignoring case.
- `expandOnVip`: a VIP message expands its pane automatically.
- Add a pane (ChatGPT, Discord, 4trak…) by adding an entry; `"enabled": false` hides one.

Which messages trigger a notification is still each app's own setting (Slack's notification preferences, muted WhatsApp chats and so on).

## Build

Pushes build `MessageHub.exe` with GitHub Actions. Tag `v*` to publish a release. Locally: `dotnet publish src/MessageHub -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` (.NET 8 SDK).
