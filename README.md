> [!IMPORTANT]
> This repository is no longer actively developed (last release 2023-03-06).
>
> When I first created this fork of Idle Master in 2017/2018 I aimed to fix a bug plus extend some functionality and never expected it to live this long.
> Sadly I have not been able to find the time to properly take care of it and I have decided to leave this project as-is and archive it. I hope you find some alternative software to fill the void.
>
> Thank you.

## About this fork

This fork keeps Idle Master Extended working on current Windows and Steam:

- builds with the .NET SDK alone (no Visual Studio);
- `steam-idle.exe` is shipped with its `.config` file, without which it crashed on start and no game was idled;
- a crash of the idling helper is written to `error.log` instead of failing silently;
- a network or Steam outage no longer wipes the saved login, the app retries instead;
- badge pages are read in English so that hours are parsed correctly whatever the account language;
- a damaged `user.config` (after a crash or power cut) is set aside instead of breaking the app;
- **Quick Login** opens a dedicated Chrome or Edge window on the Steam login page and reads the session from it.

Steam's `steamLoginSecure` cookie is only valid for about 24 hours. After that the app shows
"not connected": use **Quick Login** again.

### Build

```
dotnet build "Source/IdleMasterExtended/IdleMasterExtended.csproj" -c Release
```

This also builds `steam-idle`. Everything needed is in `Source/IdleMasterExtended/bin/Release/`;
keep all its files together and run `IdleMasterExtended.exe` with Steam open and signed in.

[![Build status](https://ci.appveyor.com/api/projects/status/96wf12emnlbmo4sj?svg=true)](https://ci.appveyor.com/project/JonasNilson/idle-master-extended)
![Github All Releases](https://img.shields.io/github/downloads/JonasNilson/idle_master_extended/total.svg)


# 🚀 Idle Master Extended

Get your [Steam Trading Cards](https://steamcommunity.com/tradingcards/) the quickest possible way.


## ❓ But what is Idle Master Extended?

🔧 This is a fork of the **discontinued** [Idle Master](https://github.com/jshackles/idle_master) project by [jshackles](https://github.com/jshackles) (also known for [Enhanced Steam](https://github.com/jshackles/Enhanced_Steam)):

> This program will determine which of your Steam games still have Steam Trading Card drops remaining, and will go through each application to simulate you being “in-game” so that cards will drop. It will check periodically to see if the game you’re idling has card drops remaining. When only one drop remains, it will start checking more frequently. When the game you’re idling has no more cards, it’ll move on to the next game. When no more cards are available, the program will terminate. 
> 
> This application requires Steam to be open and for you to be logged in.  This program is now being developed exclusively for Microsoft Windows. Non-Windows versions are available in the [Python repository](https://github.com/jshackles/idle_master_py) but may be deprecated or feature incomplete.
> 
> Source: [`jshackles/idle_master`](https://github.com/jshackles/idle_master) 


## 🔽 Download

- https://github.com/JonasNilson/idle_master_extended/releases


## 💭 [Idle Master Extended Wiki](https://github.com/JonasNilson/idle_master_extended/wiki)

- 🧰 [Get started](https://github.com/JonasNilson/idle_master_extended/wiki/Get-started)
- ✨ [Features](https://github.com/JonasNilson/idle_master_extended/wiki/Features)
- 🔄 [FAQ](https://github.com/JonasNilson/idle_master_extended/wiki/FAQ)
- 🔨 [Troubleshooting](https://github.com/JonasNilson/idle_master_extended/wiki/Troubleshooting-and-common-solutions)
- 🎨 [Contribute](https://github.com/JonasNilson/idle_master_extended/wiki/Contribute)
- 🎁 [Donate](https://github.com/JonasNilson/idle_master_extended/wiki/Donate)


## Credits

Idle Master was created by [jshackles](https://github.com/jshackles), based on the original code created by Stumpokapow.

Idle Master was written in `C#` using `Steamworks.NET` and [`CSteamworks`](https://github.com/rlabrecque/CSteamworks), and using open source icons from [Open Iconic](https://github.com/iconic/open-iconic).
