#!/usr/bin/env bash
set -euo pipefail
dotnet build ZEN/dotnet/RevReady.Parallel -c Release
dotnet build ZEN/dotnet/RevReady.Console -c Release
dotnet build ZEN/dotnet/RevReady.Tests -c Release
