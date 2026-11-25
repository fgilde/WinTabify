[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',
    [string] $PackageSource
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'src/WinTabify/WinTabify.csproj'
$stagingRoot = [IO.Directory]::CreateDirectory((Join-Path $projectRoot 'artifacts/staging')).FullName
$stage = [IO.Directory]::CreateDirectory((Join-Path $stagingRoot ([guid]::NewGuid().ToString('N')))).FullName
$downloads = [IO.Directory]::CreateDirectory((Join-Path $projectRoot 'docs/downloads')).FullName
$zip = Join-Path $downloads "WinTabify-$Runtime.zip"
$tempZip = Join-Path $downloads (([guid]::NewGuid().ToString('N')) + '.tmp')
try {
    $arguments = @('publish', $project, '-c', 'Release', '-r', $Runtime, '--self-contained', 'true',
        '-o', $stage, '-p:DebugType=None', '-p:DebugSymbols=false', '-p:UseSharedCompilation=false', '-m:1')
    if ($PackageSource) { $arguments += @('--source', $PackageSource, '-p:NuGetAudit=false') }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $tempZip, [IO.Compression.CompressionLevel]::Optimal, $false)
    [IO.File]::Move($tempZip, $zip, $true)
    Get-Item -LiteralPath $zip
} finally {
    if ((Resolve-Path -LiteralPath $stage).Path.StartsWith($stagingRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
    if (Test-Path -LiteralPath $tempZip) { Remove-Item -LiteralPath $tempZip }
}
