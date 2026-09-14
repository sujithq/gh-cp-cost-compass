#!/usr/bin/env bash
set -euo pipefail

required_version="$(jq -er '.sdk.version' global.json)"
actual_version="$(dotnet --version)"
if [[ "$actual_version" != "$required_version" ]]; then
    printf 'Expected .NET SDK %s from global.json; found %s. Update the dev container SDK version and rebuild.\n' "$required_version" "$actual_version" >&2
    exit 1
fi

sudo chown "$(id -u):$(id -g)" node_modules
dotnet nuget update source nuget.org --source "${COMPASS_NUGET_SOURCE:-https://packagefeedproxy.microsoft.io/nuget/v3/index.json}"
dotnet restore CopilotUsageSimulator.slnx