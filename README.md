# WinTabify

WinTabify gathers scattered Windows 11 File Explorer windows into one new Explorer window with one tab per folder. Run it from the notification area or press a global shortcut. It works independently of PowerToys.

[English website and guide](https://fgilde.github.io/WinTabify/) · [Deutsche Website und Anleitung](https://fgilde.github.io/WinTabify/de/)

## Install

1. Download the portable ZIP for Windows x64 or Arm64 from the project's GitHub Pages installation section.
2. Extract the **entire archive** to a permanent folder, for example `%LOCALAPPDATA%\Programs\WinTabify`.
3. Run `WinTabify.exe`. At first launch, review the shortcut and the “Include windows with multiple tabs” option. The included runtime requires no separate installation. Use Windows 11 with native Explorer tabs (22H2 with the tab update or later).
4. To start at sign-in, put a **shortcut** to the executable in `shell:startup`. To uninstall, quit from the tray, remove the extracted folder and any startup shortcut. Preferences are in `%LOCALAPPDATA%\WinTabify\settings.json`.

## Gather folders

The initial shortcut is **Ctrl + Win + E**. If Windows declines it on first launch, WinTabify tries **Alt + Win + E**, then **Ctrl + Shift + F11**. Double-click the tray icon to see or change the active setting. If a saved shortcut becomes unavailable, choose another one or use **Gather Explorer windows** in the tray menu.

| Include windows with multiple tabs | One three-tab window and two single-tab windows |
| --- | --- |
| On (default) | One window containing all distinct locations |
| Off | The original three-tab window and one window containing the two singles' distinct locations |

WinTabify snapshots the registered Explorer shell views, correlates their native tab handles with their parent frame and verifies the target locations before closing sources. A window that changes during the merge or contains an unreadable tab stays open. The tool transfers folder locations. Selection, navigation history and view state belong to the original tabs. Explorer may ask you to confirm closing a tab group. An interrupted merge can leave a partially built destination open.

## Build and test

Install the .NET 10 SDK on Windows and run:

```powershell
dotnet test WinTabify.slnx -c Release
dotnet run --project src/WinTabify
./scripts/package.ps1 -Runtime win-x64
./scripts/package.ps1 -Runtime win-arm64
```

Packages appear in `docs/downloads`. The site is plain HTML, CSS and JavaScript. Open `docs/index.html` for English or `docs/de/index.html` for German. To publish, select **GitHub Actions** under **Settings → Pages** in the repository. The workflow tests the project, produces both self-contained packages and deploys the site with its downloads on pushes to `main`.

The interactive integration test opens its own temporary Explorer windows and exercises both modes. Run it on an unlocked Windows 11 desktop with Explorer tabs. If Explorer prompts before closing several tabs, confirm the test windows' close requests.

```powershell
$env:WINTABIFY_EXPLORER_TEST = '1'
dotnet test --filter FullyQualifiedName~ExplorerDesktopTests
```

## Windows integration

The settings window uses Nextended.UI's `KeyBindChanger`. A message-only window receives `WM_HOTKEY` while the app sits in the tray. Explorer operations run on a dedicated STA thread. The Windows adapter reads `IShellWindows`, resolves per-tab `IShellBrowser` handles and invokes Explorer's new-tab control. Tab integration depends on Explorer's UI and window classes, which can vary between Windows builds.

[Nextended.UI](https://www.nuget.org/packages/Nextended.UI) · [Windows Shell](https://learn.microsoft.com/en-us/windows/win32/api/exdisp/nn-exdisp-ishellwindows)
