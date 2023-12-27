#!/usr/bin/env bash

## @file
# Builds a local NuGet package feed for all publishable projects in the SDK.
#
# Copyright (c) 2023, Cory Bennett. All rights reserved.
# SPDX-License-Identifier: Apache-2.0
##

# Change CWD for imports
__PWD__=$(pwd); cd "$(realpath "$(dirname "${BASH_SOURCE[0]}")")"

NUGET_PATH="./nuget.exe"
NUGET_URL="https://dist.nuget.org/win-x86-commandline/latest/nuget.exe"
PACKAGE_DIR="${1:-packages}"

# Arrange for the binary to be deleted when the script terminates
trap 'rm -f "$NUGET_PATH" 2>/dev/null' 0
trap 'exit $?' 1 2 3 15

curl.exe -sSL -k "$NUGET_URL" > "$NUGET_PATH"

#
# Projects have to be bundled explicitly for packaging due to a bug with
# NuGet that expects project references to be published separately.
# Tracking issue: https://github.com/NuGet/Home/issues/3891
#
$NUGET_PATH add MTGOSDK/bin/Release/*.nupkg -source "$PACKAGE_DIR"
$NUGET_PATH add MTGOSDK.Win32/bin/Release/*.nupkg -source "$PACKAGE_DIR"
$NUGET_PATH add third_party/RemoteNET/src/RemoteNET/bin/Release/*.nupkg -source "$PACKAGE_DIR"
$NUGET_PATH add third_party/RemoteNET/src/ScubaDiver.API/bin/Release/*.nupkg -source "$PACKAGE_DIR"
