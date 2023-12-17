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

$NUGET_PATH add MTGOSDK/bin/Release/MTGOSDK.*.nupkg -source "$PACKAGE_DIR"
