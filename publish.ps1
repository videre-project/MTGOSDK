## @file
# Publishes the NuGet packages to the NuGet.org repository.
#
# Copyright (c) 2024, Cory Bennett. All rights reserved.
# SPDX-License-Identifier: Apache-2.0
##

$key = $env:NUGET_API_KEY;
if (-not $key) {
  $key = Get-Content -Path .env |
    Select-String -Pattern 'NUGET_API_KEY=(\w+)' |
    ForEach-Object { $_.Matches.Groups[1].Value };
}

$githubKey = $env:GITHUB_TOKEN;
if (-not $githubKey) {
  $githubKey = Get-Content -Path .env |
    Select-String -Pattern 'GITHUB_TOKEN=(\w+)' |
    ForEach-Object { $_.Matches.Groups[1].Value };
}

# Publish .nupkg files to GitHub Packages
$packages = Get-ChildItem -Path publish -Filter *.nupkg;
foreach ($package in $packages) {
  dotnet nuget push $package.FullName `
    --api-key $githubKey `
    --source "https://nuget.pkg.github.com/videre-project/index.json" `
    --skip-duplicate;
}

# Publish .nupkg and .snupkg files to NuGet.org
$packages += Get-ChildItem -Path publish -Filter *.snupkg;
foreach ($package in $packages) {
  dotnet nuget push $package.FullName `
    --api-key $key `
    --source "https://api.nuget.org/v3/index.json" `
    --skip-duplicate;
}
