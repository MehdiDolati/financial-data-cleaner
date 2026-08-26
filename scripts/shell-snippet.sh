#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Shell snippet — auto-fetch git repos on new terminal sessions
#
# Add the contents below to your ~/.bashrc, ~/.zshrc, or equivalent.
# It silently fetches all repos in known directories when you open a
# new terminal, so you always see up-to-date remote state.
#
# INSTALLATION:
#   Add this line to your shell config (~/.bashrc, ~/.zshrc, ~/.config/fish/config.fish):
#
#     [ -f /path/to/repo/scripts/shell-snippet.sh ] && source /path/to/repo/scripts/shell-snippet.sh
#
# Or just copy the _git_autofetch function and the two lines below it.
# ---------------------------------------------------------------------------

_git_autofetch() {
    # Only run if we're inside a git repo
    git rev-parse --is-inside-work-tree &>/dev/null || return 0

    # Skip if already fetched recently (within 5 minutes)
    local cache_file
    cache_file="/tmp/.git-autofetch-$(git rev-parse --show-toplevel | tr '/' '_' 2>/dev/null)"
    if [ -f "$cache_file" ]; then
        local last_fetch
        last_fetch=$(cat "$cache_file" 2>/dev/null || echo 0)
        local now
        now=$(date +%s)
        if [ $((now - last_fetch)) -lt 300 ]; then
            return 0
        fi
    fi

    # Background fetch — don't block the prompt
    (
        git fetch --quiet --all 2>/dev/null
        date +%s > "$cache_file"
    ) &

    # Check staleness (may use stale data, that's fine — next fetch will be current)
    local behind
    behind=$(git rev-list --count HEAD..@{u} 2>/dev/null || echo 0)
    local ahead
    ahead=$(git rev-list --count @{u}..HEAD 2>/dev/null || echo 0)

    if [ "$behind" -gt 0 ]; then
        echo -e "\033[33m⚠  This branch is $behind commit(s) behind remote. Run: git pull --rebase\033[0m"
    fi
    if [ "$ahead" -gt 0 ]; then
        echo -e "\033[34mℹ  This branch is $ahead commit(s) ahead of remote. Run: git push\033[0m"
    fi
}

# Run auto-fetch on every new terminal session
_git_autofetch
