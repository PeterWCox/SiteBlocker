#!/bin/bash
# Installation script for SiteBlocker
# Adds the focus command to your shell

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FOCUS_SCRIPT="$SCRIPT_DIR/focus.sh"

# Detect shell
if [ -n "$ZSH_VERSION" ]; then
    SHELL_RC="$HOME/.zshrc"
elif [ -n "$BASH_VERSION" ]; then
    SHELL_RC="$HOME/.bashrc"
else
    SHELL_RC="$HOME/.profile"
fi

# Check if alias already exists
if grep -q "alias focus=" "$SHELL_RC" 2>/dev/null; then
    echo "✓ Alias 'focus' already exists in $SHELL_RC"
    echo "  To update it, remove the old line and run this script again"
else
    # Add alias
    echo "" >> "$SHELL_RC"
    echo "# SiteBlocker alias" >> "$SHELL_RC"
    echo "alias focus=\"$FOCUS_SCRIPT activate\"" >> "$SHELL_RC"
    echo "✓ Added 'focus' alias to $SHELL_RC"
    echo ""
    echo "To use it, run:"
    echo "  source $SHELL_RC"
    echo "  focus"
fi

echo ""
echo "You can also use the wrapper script directly:"
echo "  $FOCUS_SCRIPT activate"
echo "  $FOCUS_SCRIPT status"
echo "  $FOCUS_SCRIPT deactivate"

