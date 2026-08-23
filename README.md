# DanClient

A Windows Minecraft launcher built with Avalonia — Microsoft sign-in, Fabric, Modrinth mods, profiles, and Discord Rich Presence in one place.

<p align="center">
  <img src="Launcher.UI/Assets/danclient-logo.png" alt="DanClient" width="160" />
</p>

## Features

- **Microsoft account sign-in** via device-code flow (Minecraft Java)
- **Version install & launch** with managed Java runtimes
- **Fabric** installer support
- **Modrinth** browsing and mod install into your instance
- **Profiles** with per-profile instances under `%AppData%\DanClient`
- **Discord Rich Presence** (optional — needs an application ID)
- **Custom Avalonia installer** (`DanClientSetup.exe`) that embeds the launcher payload

## Requirements

- Windows x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build from source

## Quick start (dev)

```powershell
dotnet restore DanClient.sln
dotnet run --project Launcher.UI\Launcher.UI.csproj
```

Release publish of the launcher only:

```powershell
dotnet publish Launcher.UI\Launcher.UI.csproj -c Release -r win-x64 --self-contained
```

## Build the installer

`dotnet build` alone does **not** produce the setup exe. Use the installer script:

```powershell
.\Installer.UI\build-installer.ps1
```

Outputs:

| Path | What |
|------|------|
| `dist\DanClientSetup.exe` | Single-file Avalonia setup (launcher embedded) |
| `Installer\bin\Release\DanClientSetup.exe` | Same copy |
| `Launcher.UI\bin\Release\net10.0\win-x64\publish\` | Launcher publish folder |

Optional: `-Configuration Release` (default) and `-Version 0.1.5`.

## Solution layout

| Project | Role |
|---------|------|
| `Launcher.Core` | Auth, install, Fabric, Modrinth, Java, Discord, profiles |
| `Launcher.UI` | Avalonia desktop launcher |
| `Installer.UI` | Avalonia setup UI (`DanClientSetup`) |
| `Installer` | Extra packaging assets / Inno scripts |

## Data directory

Runtime data lives at:

```text
%AppData%\DanClient\
  cache\
  instances\
  java\
  profiles.json
  account-session.json
```

## Optional setup

- [Microsoft sign-in notes](docs/MicrosoftSignInSetup.md)
- [Discord Rich Presence](docs/DiscordRichPresenceSetup.md) — set `DANCLIENT_DISCORD_APP_ID` if you want presence

```powershell
setx DANCLIENT_DISCORD_APP_ID "your-discord-application-id"
```

Restart the terminal / IDE / app after changing that variable.

## License

Add a license of your choice when you publish the repo.
