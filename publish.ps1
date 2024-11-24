## @file
# Publishes the NuGet packages to the NuGet.org repository.
#
# Copyright (c) 2024, Cory Bennett. All rights reserved.
# SPDX-License-Identifier: Apache-2.0
##

$key = $env:NUGET_KEY;
if (-not $key) {
  $key = Get-Content -Path .env |
    Select-String -Pattern 'NUGET_KEY=(\w+)' |
    ForEach-Object { $_.Matches.Groups[1].Value };
}

$packages = Get-ChildItem -Path publish -Filter *.nupkg, *.snupkg;
foreach ($package in $packages) {
  dotnet nuget push $package.FullName `
    --api-key $key `
    --source https://api.nuget.org/v3/index.json
}