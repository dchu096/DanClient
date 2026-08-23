# Discord Rich Presence Setup

DanClient can publish Rich Presence through Discord IPC while the launcher is open and after a game starts.

Discord requires an application client ID:

1. Create an application in the Discord Developer Portal.
2. Copy the application ID.
3. Set it as a user environment variable:

```powershell
setx DANCLIENT_DISCORD_APP_ID "your-discord-application-id"
```

Restart Rider, the terminal, or the installed DanClient app after changing the environment variable.

If this value is not set, DanClient still runs normally; the Rich Presence toggle simply has nothing to connect to.
