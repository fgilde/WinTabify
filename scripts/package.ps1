[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',
    [ValidateSet('bundled', 'slim')]
    [string] $Flavor = 'bundled',
    [version] $Version,
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/WinTabify/WinTabify.csproj'
if (!$Version) { $Version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'docs/downloads' }
$channel = "$Runtime-$Flavor"
$feed = [IO.Directory]::CreateDirectory((Join-Path $OutputDirectory $channel)).FullName
$stageRoot = [IO.Directory]::CreateDirectory((Join-Path $root 'artifacts/staging')).FullName
$stage = [IO.Directory]::CreateDirectory((Join-Path $stageRoot ([guid]::NewGuid().ToString('N')))).FullName
$app = Join-Path $stage 'app'
$packed = Join-Path $stage 'release'
$bundled = $Flavor -eq 'bundled'
Push-Location $root
try {
    $arguments = @('publish', $project, '-c', 'Release', '-r', $Runtime, '--self-contained', $bundled.ToString(),
        '-o', $app, '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
        "-p:EnableCompressionInSingleFile=$bundled", "-p:Version=$Version", "-p:WinTabifyChannel=$channel",
        '-p:DebugType=None', '-p:DebugSymbols=false', '-p:UseSharedCompilation=false', '-m:1')
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    $files = @(Get-ChildItem -LiteralPath $app -File -Recurse)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'WinTabify.exe') {
        throw "Expected a single application executable. Found $($files.Name -join ', ')."
    }

    $arguments = @('tool', 'run', 'vpk', '--', 'pack', '--packId', 'fgilde.WinTabify', '--packVersion', "$Version",
        '--packDir', $app, '--mainExe', 'WinTabify.exe', '--runtime', $Runtime, '--channel', $channel,
        '--packAuthors', 'Florian Gilde', '--packTitle', 'WinTabify', '--icon', 'src/WinTabify/Assets/app.ico',
        '--outputDir', $packed)
    if (!$bundled) { $arguments += @('--framework', "net10.0-$($Runtime.Substring(4))-desktop") }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Velopack packaging failed.' }

    $index = Get-Item -LiteralPath (Join-Path $packed "releases.$channel.json")
    $assets = (Get-Content -LiteralPath $index.FullName -Raw | ConvertFrom-Json).Assets
    $full = @($assets | Where-Object Type -eq 'Full')
    if ($full.Count -ne 1 -or $full[0].Version -ne "$Version") { throw "Invalid release index for $channel." }
    $package = Get-Item -LiteralPath (Join-Path $packed $full[0].FileName)
    if ($package.Length -ne $full[0].Size -or
        (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash -ne $full[0].SHA256) { throw 'Package integrity check failed.' }
    Copy-Item -LiteralPath $package.FullName -Destination $feed
    Copy-Item -LiteralPath $index.FullName -Destination $feed
    $setup = @(Get-ChildItem -LiteralPath $packed -Filter '*-Setup.exe')
    $portable = @(Get-ChildItem -LiteralPath $packed -Filter '*-Portable.zip')
    if ($setup.Count -ne 1 -or $portable.Count -ne 1) { throw 'Setup or portable package missing.' }
    Copy-Item -LiteralPath $setup[0].FullName -Destination (Join-Path $feed 'WinTabify-Setup.exe')
    $suffix = if ($bundled) { '' } else { '-slim' }
    Copy-Item -LiteralPath $portable[0].FullName -Destination (Join-Path $OutputDirectory "WinTabify-$Runtime$suffix.zip")
} finally {
    Pop-Location
    $resolved = (Resolve-Path -LiteralPath $stage).Path
    if ($resolved.StartsWith($stageRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
