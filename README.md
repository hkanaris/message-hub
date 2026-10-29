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
- **Swap apps:** click a header's **⋯** button (or right-click the header). **Switch to** replaces the pane with another app, **Add app below** adds a new pane, and there's **Move up**, **Move down** and **Remove**. Built in: Slack, WhatsApp, Teams, Claude, ChatGPT, Discord, Outlook, Telegram, Messenger, Google Chat and Gmail. **Custom…** takes any web address. Changes apply at once and are saved.
- The same menu has reload, reset zoom, open in browser, settings and restart.
- Links open in your normal browser.

Close the desktop Slack, WhatsApp and Teams apps, or you'll get every notification twice. Keep desktop Teams for calls and screen sharing.

## Settings

`%APPDATA%\MessageHub\config.json`, created on first run. Edit it, then restart from the header menu. Changing apps from the menu rewrites this file.

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
- `"enabled": false` hides a pane without removing it.

Which messages trigger a notification is still each app's own setting (Slack's notification preferences, muted WhatsApp chats and so on).

## Build

Pushes build `MessageHub.exe` with GitHub Actions. Tag `v*` to publish a release. Locally: `dotnet publish src/MessageHub -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` (.NET 8 SDK).
