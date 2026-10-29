# message-hub

Slack, WhatsApp, Teams and Claude stacked in one window, with alerts you can't miss.

## Get it

Download `MessageHub.exe` from [the latest release](../../releases/latest), unblock it (right-click, **Properties**, tick **Unblock**) and run it. Nothing to install: it uses the WebView2 runtime that ships with Windows 11. Sign in to each app once; logins are kept.

## Using it

- **New message:** the pane's header pulses in the app's colour, shows the message, the taskbar button flashes and a sound plays.
- **Click a header** to jump to that message and clear the alert.
- **Layout:** in the **⋯** menu, **Layout** switches between **Stacked**, **Side by side** and **Grid**, and sets how many apps to show (2–6).
- **Resize:** drag the divider between two panes (stacked and side by side). Sizes are remembered.
- **Double-click a header** (or the expand button) to give that pane the whole window. The others shrink to their headers but keep alerting.
- **Ctrl+1–9** expands a pane, **Ctrl+0** restores.
- **Ctrl+mouse wheel** zooms a pane; the zoom is remembered.
- **Swap apps:** click a header's **⋯** button (or right-click the header). **Switch to** replaces the pane with another app, **Add app below** adds a new pane, and there's **Move up**, **Move down** and **Remove**. Built in: Slack, WhatsApp, Teams, Claude, ChatGPT, Discord, Outlook, Telegram, Messenger, Google Chat and Gmail. **Custom…** takes any web address. Changes apply at once and are saved.
- **Alerts for (app):** All messages, VIP only, or Off.
- **Do not disturb:** 30 minutes, 1 hour or until you turn it off. The taskbar shows a grey "z" while it's on.
- **Settings:** edit the VIP list, turn sound, taskbar flashing and expand-on-VIP on or off, check for updates.
- **Updates:** Message Hub checks for a new release shortly after starting and every 6 hours, and offers to update itself.
- The same menu has reload, reset zoom and open in browser.
- Links open in your normal browser.

Close the desktop Slack, WhatsApp and Teams apps, or you'll get every notification twice. Keep desktop Teams for calls and screen sharing.

## Settings

Everything below can be set from the **⋯** menu. It's stored in `%APPDATA%\MessageHub\config.json`, which you can also edit by hand while Message Hub is closed.

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
  "expandOnVip": false,
  "layout": "stacked"
}
```

- `alertOn`: `all` alerts on every notification, `vip` only when the sender or text matches `vipKeywords`, `none` never (the header still shows the latest message).
- `vipKeywords`: names, chats or channels that matter. Matching is on the notification's title and text, ignoring case.
- `expandOnVip`: a VIP message expands its pane automatically.
- `"enabled": false` hides a pane without removing it.

Which messages trigger a notification is still each app's own setting (Slack's notification preferences, muted WhatsApp chats and so on).

## Build

Every push builds `MessageHub.exe` with GitHub Actions. A push to `main` also publishes it as release `v<Version>`, taken from `MessageHub.csproj`, so bump `<Version>` with each change. Locally: `dotnet publish src/MessageHub -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` (.NET 8 SDK).
