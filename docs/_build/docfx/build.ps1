## @file
# Copyright (c) 2026, Cory Bennett. All rights reserved.
# SPDX-License-Identifier: Apache-2.0
##

param(
    [string]$DocFxDir,
    [string]$VersionMain,
    [string]$VersionSuffix,
    [string]$ShortHash,
    [string]$CommitUrl
)

# Read the JavaScript template
$jsTemplate = Get-Content "$DocFxDir/docs/_build/docfx/main.js.template" -Raw

# Inject version information
$jsContent = $jsTemplate -replace '\{\{VERSION_MAIN\}\}', $VersionMain `
                         -replace '\{\{VERSION_SUFFIX\}\}', $VersionSuffix `
                         -replace '\{\{COMMIT_HASH\}\}', $ShortHash `
                         -replace '\{\{COMMIT_URL\}\}', $CommitUrl

# Construct the Footer HTML
$footerTemplate = Get-Content "$DocFxDir/docs/_build/docfx/footer.html.template" -Raw
$footerHtml = $footerTemplate -replace '\{\{YEAR\}\}', (Get-Date).Year

# Combine HTML and Script
$fullFooter = $footerHtml + '<script>' + $jsContent + '</script>'

# Escape for CLI argument
$safeFooter = $fullFooter -replace '"', '\"'

# Read existing docfx.json
$ErrorActionPreference = "Stop"

try {
    Write-Host "Starting DocFX build script..."
    
    # Read existing docfx.json
    $docfxJsonPath = Join-Path $DocFxDir "docfx.json"
    if (-not (Test-Path $docfxJsonPath)) {
        throw "docfx.json not found at $docfxJsonPath"
    }
    Write-Host "Reading config from $docfxJsonPath"
    $docfxConfig = Get-Content $docfxJsonPath | ConvertFrom-Json

    # Inject metadata
    $metadata = $docfxConfig.build.globalMetadata
    if ($null -eq $metadata) {
        $metadata = New-Object PSObject
        $docfxConfig.build | Add-Member -MemberType NoteProperty -Name "globalMetadata" -Value $metadata
    }

    $metadata | Add-Member -MemberType NoteProperty -Name "_appName" -Value "MTGOSDK" -Force
    $metadata | Add-Member -MemberType NoteProperty -Name "_versionMain" -Value $VersionMain -Force
    $metadata | Add-Member -MemberType NoteProperty -Name "_versionSuffix" -Value $VersionSuffix -Force
    $metadata | Add-Member -MemberType NoteProperty -Name "_commitHash" -Value $ShortHash -Force
    $metadata | Add-Member -MemberType NoteProperty -Name "_commitUrl" -Value $CommitUrl -Force

    # Construct the Footer HTML
    $footerTemplatePath = Join-Path $DocFxDir "docs/_build/docfx/footer.html.template"
    if (-not (Test-Path $footerTemplatePath)) {
        throw "Footer template not found at $footerTemplatePath"
    }
    $footerTemplate = Get-Content $footerTemplatePath -Raw
    $footerHtml = $footerTemplate -replace '\{\{YEAR\}\}', (Get-Date).Year

    # Read the JavaScript template
    $jsTemplatePath = Join-Path $DocFxDir "docs/_build/docfx/main.js.template"
    if (-not (Test-Path $jsTemplatePath)) {
        throw "JS template not found at $jsTemplatePath"
    }
    $jsTemplate = Get-Content $jsTemplatePath -Raw

    # Inject version information
    $jsContent = $jsTemplate -replace '\{\{VERSION_MAIN\}\}', $VersionMain `
                             -replace '\{\{VERSION_SUFFIX\}\}', $VersionSuffix `
                             -replace '\{\{COMMIT_HASH\}\}', $ShortHash `
                             -replace '\{\{COMMIT_URL\}\}', $CommitUrl

    # Combine HTML and Script
    $fullFooter = $footerHtml + '<script>' + $jsContent + '</script>'
    $metadata | Add-Member -MemberType NoteProperty -Name "_appFooter" -Value $fullFooter -Force

    # Save config to root to preserve relative paths
    $tempConfigPath = Join-Path $DocFxDir "docfx.build.json"
    Write-Host "Saving build config to $tempConfigPath"
    $docfxConfig | ConvertTo-Json -Depth 10 | Set-Content $tempConfigPath

    # Execute DocFX Build
    Write-Host "Running DocFX Build..."
    $processArgs = @("docfx", "build", $tempConfigPath)
    Write-Host "Command: dotnet $processArgs"
    
    & dotnet @processArgs

    if ($LASTEXITCODE -ne 0) { 
        throw "DocFX build failed with exit code $LASTEXITCODE" 
    }
    
    Write-Host "Build script completed successfully."
}
catch {
    Write-Error "Build script failed: $_"
    exit 1
}
