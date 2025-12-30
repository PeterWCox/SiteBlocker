#!/bin/bash
# SiteBlocker wrapper script
# Makes it easy to run the blocker from anywhere

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT_PATH="$SCRIPT_DIR/siteblocker.csx"

# Ensure dotnet-script is in PATH
export PATH="$PATH:$HOME/.dotnet/tools"

# Run the script with sudo
sudo dotnet-script "$SCRIPT_PATH" "$@"

