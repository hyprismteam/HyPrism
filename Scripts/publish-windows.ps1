# Copyright (C) 2026 HyPrism Launcher
# SPDX-License-Identifier: GPL-3.0-only

[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $PSCommandPath
$projectRoot = Split-Path -Parent $scriptRoot
$projectFile = Join-Path $projectRoot 'Sources/HyPrism.Desktop/HyPrism.Desktop.csproj'
$wixSource = Join-Path $projectRoot 'Packaging/windows'
$targets = [System.Collections.Generic.List[string]]::new()
$version = ''
$outputDirectory = Join-Path $projectRoot 'dist'

function Show-Usage {
    @'
Usage: bash Scripts/publish.sh <target> [<target>...] [options]

Targets:
  all   Build portable ZIP, MSI, and EXE installer
  zip   Build a portable ZIP
  msi   Build an MSI installer
  exe   Build an EXE bootstrapper installer

Options:
  --version <version>   Version embedded in artifact names
  --output <directory>  Artifact directory, defaults to dist
'@ | Write-Output
}

for ($index = 0; $index -lt $Arguments.Count; $index++) {
    switch ($Arguments[$index]) {
        '--version' {
            if (++$index -ge $Arguments.Count) { throw '--version requires a value' }
            $version = $Arguments[$index]
        }
        '--output' {
            if (++$index -ge $Arguments.Count) { throw '--output requires a directory' }
            $outputDirectory = $Arguments[$index]
        }
        '--help' { Show-Usage; exit 0 }
        '-h' { Show-Usage; exit 0 }
        'all' { $targets.Add('all') }
        'zip' { $targets.Add('zip') }
        'msi' { $targets.Add('msi') }
        'exe' { $targets.Add('exe') }
        default { throw "Unknown Windows publish target: $($Arguments[$index])" }
    }
}

if ($targets.Count -eq 0 -or $targets.Contains('all')) {
    $targets = [System.Collections.Generic.List[string]]@('zip', 'msi', 'exe')
}

if ([string]::IsNullOrWhiteSpace($version)) {
    if ($env:GITHUB_REF_NAME -like 'v*') {
        $version = $env:GITHUB_REF_NAME.Substring(1)
    } elseif (-not [string]::IsNullOrWhiteSpace($env:GITHUB_RUN_NUMBER)) {
        $version = "ci-$env:GITHUB_RUN_NUMBER"
    } else {
        $version = 'local'
    }
}

$artifactVersion = $version -replace '[^0-9A-Za-z._+-]', '-'
$installerVersion = $version.TrimStart('v').Split('-')[0]
if ($installerVersion -notmatch '^\d+(\.\d+){0,2}$') {
    $installerVersion = '0.0.0'
}
$installerVersion = ($installerVersion.Split('.') + '0', '0', '0')[0..2] -join '.'

if (-not [System.IO.Path]::IsPathRooted($outputDirectory)) {
    $outputDirectory = Join-Path $projectRoot $outputDirectory
}
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$buildRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("hyprism-publish-" + [Guid]::NewGuid())
$publishDirectory = Join-Path $buildRoot 'publish'
$wixToolDirectory = Join-Path $buildRoot 'wix'

try {
    New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
    dotnet publish $projectFile `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $publishDirectory

    foreach ($host in 'HyPrism.Desktop.exe', 'HyPrism.LocalNode.exe') {
        if (-not (Test-Path (Join-Path $publishDirectory $host))) {
            throw "Expected Windows apphost was not published: $host"
        }
    }

    if ($targets.Contains('zip')) {
        Compress-Archive `
            -Path (Join-Path $publishDirectory '*') `
            -DestinationPath (Join-Path $outputDirectory "HyPrism-win-x64-$artifactVersion.zip") `
            -Force
    }

    if ($targets.Contains('msi') -or $targets.Contains('exe')) {
        dotnet tool install --tool-path $wixToolDirectory wix
        $wix = Join-Path $wixToolDirectory 'wix.exe'
        & $wix extension add -g WixToolset.BootstrapperApplications.wixext
        if ($LASTEXITCODE -ne 0) { throw 'Could not install the WiX bootstrapper extension' }

        $msiPath = if ($targets.Contains('msi')) {
            Join-Path $outputDirectory "HyPrism-win-x64-$artifactVersion.msi"
        } else {
            Join-Path $buildRoot 'HyPrism.msi'
        }
        & $wix build (Join-Path $wixSource 'HyPrism.msi.wxs') `
            -arch x64 `
            -d "PublishDir=$publishDirectory" `
            -d "ProductVersion=$installerVersion" `
            -o $msiPath
        if ($LASTEXITCODE -ne 0) { throw 'WiX could not build the MSI package' }

        if ($targets.Contains('exe')) {
            & $wix build (Join-Path $wixSource 'HyPrism.bundle.wxs') `
                -arch x64 `
                -ext WixToolset.BootstrapperApplications.wixext `
                -d "MsiPath=$msiPath" `
                -d "ProductVersion=$installerVersion" `
                -o (Join-Path $outputDirectory "HyPrism-win-x64-$artifactVersion-setup.exe")
            if ($LASTEXITCODE -ne 0) { throw 'WiX could not build the EXE installer' }
        }
    }
} finally {
    if (Test-Path $buildRoot) {
        Remove-Item -Recurse -Force $buildRoot
    }
}

Write-Output "Published Windows artifacts to $outputDirectory"
