# WinTabify

Gather scattered Windows 11 File Explorer windows into one tab strip with a global shortcut. WinTabify keeps the largest eligible window with unique tabs, opens only missing folders and closes confirmed source windows, including those whose folders were all duplicates.

[Download and English guide](https://fgilde.github.io/WinTabify/#install) · [Download und deutsche Anleitung](https://fgilde.github.io/WinTabify/de/#install)

## Install

Choose **x64** for Intel or AMD, or **Arm64** for a Snapdragon PC. Both architectures offer two editions.

| Edition | Runtime |
| --- | --- |
| Bundled | Includes the .NET runtime |
| Slim | Uses the matching .NET 10 Desktop Runtime. Setup offers to install it if needed. |

The EXE installer installs the single-file application and its updater for the current user. Alternatively, extract the complete portable ZIP to a writable folder and run its launcher. Both distributions receive updates. Windows 11 with native File Explorer tabs is required.

**Upgrading from the original 0.1 ZIP:** Quit the old copy from the tray before starting the new one. Both versions read the same shortcut and tab preferences from `%LOCALAPPDATA%\WinTabify\settings.json`. Replace any existing startup shortcut with the installed Start menu shortcut or the new portable launcher.

## Gather folders

The initial shortcut is **Ctrl + Win + E**. If Windows declines it on first launch, WinTabify tries **Alt + Win + E**, then **Ctrl + Shift + F11**. Double-click the tray icon to edit it, or select **Gather Explorer windows** from the tray menu.

| Include windows with multiple tabs | One three-tab window and two single-tab windows |
| --- | --- |
| On (default) | One window containing all distinct locations |
| Off | The original three-tab window stays open, and the singles form their own group |

WinTabify opens missing tabs through the Explorer tab window and directs navigation through the Shell. It verifies all destination locations before requesting closure of the source frames. If every eligible window contains duplicate tabs, it builds a clean destination first. Unreadable or changed sources may stay open. Existing tabs in the retained window keep their history and selection. Newly opened tabs restore the folder location. The tray shows one notification after the operation.

## Updates

WinTabify checks its Velopack feed at startup and every six hours. It downloads the matching architecture and runtime edition, then applies the update and restarts when no merge or settings edit is in progress. **Settings → Install updates automatically** controls background updates. **Check for updates** in the tray also works when automatic updates are disabled. A failed download leaves the installed version usable.

## Build, test and package

Install the .NET 10 SDK and PowerShell 7 on Windows.

```powershell
dotnet tool restore
dotnet test WinTabify.slnx -c Release
dotnet run --project src/WinTabify
./scripts/package.ps1 -Runtime win-x64 -Flavor bundled -Version 0.2.0
./scripts/package.ps1 -Runtime win-x64 -Flavor slim -Version 0.2.0
```

Use `win-arm64` for Arm64. Each package contains a bundled application executable. Velopack adds its installer or portable launcher and update support. The installer, versioned package and `releases.<channel>.json` are written under `docs/downloads/<runtime>-<flavor>/`. Portable ZIPs are written to `docs/downloads/`.

The workflow publishes all four channels and the bilingual site together after unit tests, package checks and a Windows 11 Arm64 desktop test pass. Published versions use `0.2.<workflow run number>`; re-running an attempt retains its version. The desktop test creates its own windows, exercises both inclusion modes, and verifies cleanup of multi-tab windows with duplicate paths. To run it locally on an unlocked Windows 11 desktop:

```powershell
$env:WINTABIFY_EXPLORER_TEST = '1'
dotnet test --filter FullyQualifiedName~ExplorerDesktopTests
```

[Nextended.UI](https://www.nuget.org/packages/Nextended.UI) · [Windows Shell](https://learn.microsoft.com/en-us/windows/win32/api/exdisp/nn-exdisp-ishellwindows) · [Velopack](https://velopack.io/)
