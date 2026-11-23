<p align="center"><img src="docs/logo.png" width="96" alt="Echoa logo"></p>
<h1 align="center">Echoa</h1>
<p align="center">A small, fast volume mixer for the Windows tray. A cleaner take on EarTrumpet.</p>
<p align="center"><a href="../../releases/latest"><b>Download the latest release</b></a></p>

Formerly called **Sip** (renamed in 1.4). Your old Sip settings are copied over the first time Echoa starts.

## Why
EarTrumpet couldn't change the Windows default output, its device list was huge, and a headset kept stealing the default whenever it connected. Echoa fixes all three.

## Features
- **One click to switch output.** Click a device to make it the Windows default (console, multimedia and communications).
- **Per-app volumes** under the default device, with app icons. Other devices' apps open with a "3 apps" chip, no switching needed.
- **"Don't let it take over"**: stops a device (like a USB headset) from grabbing the default when it connects.
- **Remembers volumes**: per device (restored on reconnect, even if a Bluetooth headset pushes its own level) and per app.
- **Auto-switch rules**: make a device the output when it connects, pick a fallback when it disconnects.
- **Now playing** strip with cover art and previous / play-pause / next.
- **Main window** with Inputs (default mic + mute), Devices (rename, rules, volume limits, test sound), Apps and Settings.
- **Minimal mode arrow**: a small arrow on the main output folds the other outputs away (or back) with a smooth animation.
- Accent colour picker, scroll on the tray icon to change volume.
- No hotkeys, almost no background work while the flyout is closed (one small audio service check every 5 seconds), and errors go to `crash.log`.

## New in 1.7.2
- Alt+F4 on the flyout no longer breaks the tray icon, Exit or Minibar's Stop.
- Touchpad scrolling changes the volume at the normal speed.
- Device names stay on the right device, and interrupted slider drags let go.
- A locked or unreadable settings file is never replaced with defaults.
- Echoa reconnects by itself when the Windows Audio service restarts.
- The Inputs page scrolls, plus fixes for the flyout, tooltip, dropdown, window size and maximize.

## Install
1. Download `Echoa-1.7.2.zip` from [Releases](../../releases/latest) and unzip it into its own folder.
2. Run `Echoa.exe`. If SmartScreen warns, click **More info > Run anyway** (the exe is unsigned).

Runs on Windows 10/11 with .NET Framework 4.8, which is already built in. Nothing to install.

Settings live in `%APPDATA%\Echoa\settings.ini`.

## Build
```
cd src
dotnet build -c Release
```
On Windows this uses the .NET Framework 4.8 reference assemblies; on Linux it builds against Mono's (`mono-devel`).
The logo scripts live in the Minibar project notes; `src/echoa.ico` is the built icon.

## How it works
C# WinForms on .NET Framework 4.8. Core Audio through COM (`IMMDeviceEnumerator`, `IAudioSessionManager2`, `IPolicyConfig` for the default device), all on one MTA worker thread. Its own `Shell_NotifyIcon` tray icon, and raw WinRT calls to the Global System Media Transport Controls for Now playing.

## License
MIT
