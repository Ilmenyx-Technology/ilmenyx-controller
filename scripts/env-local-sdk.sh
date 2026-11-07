# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
# Optional helper for this session's isolated SDK. Source from the repository root.
# Requires the official SDK archive already extracted in /tmp/silex-dotnet.
export DOTNET_ROOT=/tmp/silex-dotnet
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME=/tmp/silex-cli
export NUGET_PACKAGES=/tmp/silex-nuget
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1
