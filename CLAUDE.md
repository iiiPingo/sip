# Echoa

Volume mixer for the Windows tray (replaces EarTrumpet). C# WinForms, .NET Framework 4.8, no packages. This repo is named `sip` because Echoa was called Sip until 1.4; Minibar checks `echoa` first, then `sip`.

## Build and test
- `cd src && dotnet build -c Release`. On Linux it builds against Mono's reference assemblies (`mono-devel`).
- Version lives in `src/Echoa.csproj` (`<Version>`). Bump it every release; Minibar compares it with the GitHub tag.
- Release asset: `Echoa-x.y.z.zip` containing `Echoa/Echoa.exe`, `Echoa.exe.config`, `README.txt`.
- Settings: `%APPDATA%\Echoa\settings.ini`. Errors go to `crash.log` there. Never touch these in code or commits.

## Gotchas
- Core Audio (COM) runs on one dedicated MTA worker thread with a work queue. Callbacks only enqueue; never do COM work in a callback.
- Tag your own changes (event-context GUID) so you ignore their echoes.
- Now playing uses raw WinRT vtable calls through `RoGetActivationFactory`. Verify IIDs and slot numbers before changing anything there.
- `TextRenderer.MeasureText` with EndEllipsis + `Size.Empty` collapses on real Windows. Use `NoPrefix | SingleLine | NoPadding`.
- Own `Shell_NotifyIcon` (version 4) instead of `NotifyIcon`, for Win+B/Enter, scroll and TaskbarCreated re-add.
- `Echoa.exe --exit` closes it cleanly (window message `Echoa.Exit.v1`). Minibar's Stop and updates rely on it. Keep it working.
- Logo scripts are not in this repo; `src/echoa.ico` is the built icon.

## Working rules (all Pingo apps)
- Apps: Echoa, Fizz, Juice, Pour, Pulp, Minibar. Minibar is the hub that finds, installs, starts, stops and updates the others from GitHub releases (iiiPingo/<app>). Don't change how an app is named, found, installed, updated or closed without checking it still works with Minibar.
- Never rely on the exe's file name. Discord and browsers rename downloads to `Echoa (2).exe`. Identify an app by its version resource (ProductName), not by `<App>.exe`.
- Every release needs a zip with the app name in its file name, containing `<App>.exe`. Bump the exe's version every release.
- Never touch user data (settings.ini, logs, history, databases).
- This repo is public: never commit secrets, tokens, personal paths, build output (`bin/`, `obj/`), crash logs, settings.ini or nuget.config.
- Work on a branch. Don't push, open a PR or publish a release until the owner says so.
- Add a test for each fix that can be tested off Windows. At the end, list what can only be checked on real Windows (COM, WinRT, tray, UAC, shortcuts).
- If unsure a fix is safe, report it instead of changing it.
- Keep README text in plain words; mention what's new each version.
- Explain changes simply, in plain language.
