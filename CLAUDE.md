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

## Releasing
When a change is ready to ship, leave it fully done, so the release can go out without rebuilding or repackaging:
1. Bump the version (see Build and test), add the "what's new" to the README and to `README.txt` in the zip (a "NEW IN x.y.z" section at the top, plain words), and run the tests.
2. Build the exe from the final source, last, after the final change. The exe you leave is the one that ships. Copy it to `C:\Claude Projects\Echoa\Echoa.exe` so it can be tried.
3. Build the full release zip `Echoa-x.y.z.zip`: one top folder `Echoa/` with `Echoa.exe`, `Echoa.exe.config` and `README.txt`. Save it to `C:\Claude Projects\Releases\Echoa-x.y.z\`. A zip with only the exe is not a release zip.
4. Push the branch. Push to `main` when the owner says push or release. Never force-push. Tags are plain versions (`vX.Y.Z`) and are created on GitHub when the owner publishes.
5. Do not create the GitHub release or tag yourself. The owner attaches the zip and clicks Publish. The "My project apps" chat prepares the release page and checks it afterwards; it does not rebuild.
6. Finish with one message: what changed in short plain bullets (these become the release notes, no dashes), the version, the exe's SHA256, the zip path, the tests run, and what only real Windows can check.

## Working rules (all Pingo apps)
- Apps: Echoa, Fizz, Juice, Pour, Pulp, Minibar. Minibar is the hub that finds, installs, starts, stops and updates the others from GitHub releases (iiiPingo/<app>). Don't change how an app is named, found, installed, updated or closed without checking it still works with Minibar.
- Never rely on the exe's file name. Discord and browsers rename downloads to `Echoa (2).exe`. Identify an app by its version resource (ProductName), not by `<App>.exe`.
- Every release needs a zip with the app name in its file name, containing `<App>.exe`. Bump the exe's version every release.
- Never touch user data (settings.ini, logs, history, databases).
- This repo is public: never commit secrets, tokens, personal paths, build output (`bin/`, `obj/`), crash logs, settings.ini or nuget.config.
- Work on a branch. Don't push, open a PR or publish a release until the owner says so. "Push", "release" and "update GitHub" count as that go-ahead: then push to `main`.
- Add a test for each fix that can be tested off Windows. At the end, list what can only be checked on real Windows (COM, WinRT, tray, UAC, shortcuts).
- If unsure a fix is safe, report it instead of changing it.
- Keep README text in plain words; mention what's new each version.
- Explain changes simply, in plain language.
