#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# install-hooks.sh — Install git hooks from scripts/git-hooks/ into .git/hooks/
#
# Run this after cloning or when hooks are updated. Safe to run multiple times.
# Usage: bash scripts/install-hooks.sh
# ---------------------------------------------------------------------------

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOOKS_SOURCE="$SCRIPT_DIR/git-hooks"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
HOOKS_TARGET="$REPO_ROOT/.git/hooks"

if [ ! -d "$HOOKS_SOURCE" ]; then
    echo "Error: Hooks source directory not found: $HOOKS_SOURCE" >&2
    exit 1
fi

if [ ! -d "$HOOKS_TARGET" ]; then
    echo "Error: .git/hooks directory not found. Are you in a git repository?" >&2
    exit 1
fi

installed=0
for hook in "$HOOKS_SOURCE"/*; do
    [ -f "$hook" ] || continue
    name="$(basename "$hook")"
    cp "$hook" "$HOOKS_TARGET/$name"
    chmod +x "$HOOKS_TARGET/$name"
    echo "  ✓ Installed: $name"
    installed=$((installed + 1))
done

echo ""
echo "Installed $installed git hook(s) into $HOOKS_TARGET"
echo "Hooks will run automatically on the next git operation."
