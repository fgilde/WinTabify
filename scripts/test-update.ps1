[CmdletBinding()]
param([Parameter(Mandatory)][version] $Version)

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Run this desktop installation test on an isolated GitHub-hosted Windows runner.'
}
$root = Split-Path $PSScriptRoot -Parent
$downloads = (Resolve-Path -LiteralPath (Join-Path $root 'docs/downloads')).Path
$sessionDir = [IO.Directory]::CreateTempSubdirectory('WinTabify-update-test-').FullName
$oldPackages = Join-Path $sessionDir 'previous'
$installed = Join-Path $sessionDir 'portable'
$priorFeed = $env:WinTabifyFeedRoot
try {
    $env:WinTabifyFeedRoot = $downloads
    & (Join-Path $PSScriptRoot 'package.ps1') -Runtime win-x64 -Flavor slim -Version 0.1.1 -OutputDirectory $oldPackages
} finally { $env:WinTabifyFeedRoot = $priorFeed }

Expand-Archive -LiteralPath (Join-Path $oldPackages 'WinTabify-win-x64-slim.zip') -DestinationPath $installed
$exe = (Get-ChildItem -LiteralPath $installed -Filter WinTabify.exe -File -Recurse |
    Where-Object { $_.Directory.Name -eq 'current' } | Select-Object -First 1).FullName
if (!$exe) { throw 'The portable app executable is missing.' }
$old = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
$limit = [Diagnostics.Stopwatch]::StartNew()
try {
    do {
        if (!$old.HasExited) {
            $old.Refresh()
            if ($old.MainWindowHandle -ne 0) { [void]$old.CloseMainWindow() }
        }
        $active = @(Get-Process -Name WinTabify -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe -and $_.Id -ne $old.Id })
        try { $versionOnDisk = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion }
        catch [IO.FileNotFoundException] { $versionOnDisk = $null }
        if ($active.Count -eq 1 -and ($versionOnDisk -split '\+')[0] -eq "$Version") {
            Write-Host "The portable app updated from 0.1.1 to $versionOnDisk and restarted."
            return
        }
        Start-Sleep -Milliseconds 300
    } while ($limit.Elapsed.TotalSeconds -lt 90)
    throw "The staged update did not complete. Current version: $versionOnDisk. Old process exited: $($old.HasExited)."
} finally {
    Get-Process -Name WinTabify -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force
}
