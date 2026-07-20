#!/usr/bin/env bash
# CalAssistant environment setup for Linux / macOS.
# Usage: ./setup.sh [--skip-build] [--model qwen3:4b]

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MODEL="qwen3:4b"
OLLAMA_URL="http://localhost:11434"
SKIP_BUILD=0
TOTAL_STEPS=7
STEP=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --skip-build) SKIP_BUILD=1; shift ;;
        --model) MODEL="$2"; shift 2 ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

step() {
    STEP=$((STEP + 1))
    printf '\n[%d/%d] %s\n' "$STEP" "$TOTAL_STEPS" "$1"
}

ok()   { printf '       OK  %s\n' "$1"; }
warn() { printf '       !!  %s\n' "$1"; }
fail() { printf '       XX  %s\n' "$1"; exit 1; }

command_exists() { command -v "$1" >/dev/null 2>&1; }

ensure_dotnet() {
    step "Checking .NET SDK 10"
    if ! command_exists dotnet; then
        warn ".NET SDK not found."
        fail "Install .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0"
    fi
    VERSION="$(dotnet --version)"
    [[ "$VERSION" == 10.* ]] || fail ".NET 10 required (found: $VERSION)"
    ok ".NET SDK $VERSION"
}

ensure_ollama() {
    step "Checking Ollama"
    if ! command_exists ollama; then
        warn "Ollama not found. Installing..."
        curl -fsSL https://ollama.com/install.sh | sh
    fi
    command_exists ollama || fail "Ollama still not on PATH after install"
    ok "Ollama CLI available"
}

ensure_ollama_running() {
    step "Checking Ollama server"
    if ! curl -sf "$OLLAMA_URL/api/tags" >/dev/null 2>&1; then
        warn "Ollama server not responding. Starting ollama serve..."
        nohup ollama serve >/dev/null 2>&1 &
        sleep 4
    fi
    curl -sf "$OLLAMA_URL/api/tags" >/dev/null 2>&1 \
        || fail "Ollama is not running at $OLLAMA_URL. Start it: ollama serve"
    ok "Ollama server is up ($OLLAMA_URL)"
}

ensure_model() {
    step "Checking model '$MODEL'"
    if ! ollama list 2>/dev/null | grep -q "${MODEL%%:*}"; then
        warn "Model not found. Pulling (this may take a few minutes)..."
        ollama pull "$MODEL"
    fi
    ok "Model '$MODEL' is ready"
}

ensure_project_folders() {
    step "Preparing project folders"
    mkdir -p "$ROOT/token-store"
    ok "token-store/ ready"

    if [[ ! -f "$ROOT/credentials.json" ]]; then
        warn "credentials.json not found — Google Calendar won't work until you add it."
        warn "See README.md → Google OAuth Setup."
    else
        ok "credentials.json found"
    fi
}

ensure_build() {
    if [[ "$SKIP_BUILD" -eq 1 ]]; then
        step "Skipping build (--skip-build)"
        ok "Build skipped"
        return
    fi
    step "Restoring and building project"
    cd "$ROOT"
    dotnet restore
    dotnet build -c Release --no-restore
    ok "Project built successfully"
}

print_summary() {
    printf '\n  Setup complete!\n\n'
    printf '  Next steps:\n'
    printf '    1. Add credentials.json if you haven'\''t yet (see README.md)\n'
    printf '    2. Run the app:  dotnet run\n'
    printf '    3. Open:          http://localhost:5136\n\n'
    printf '  Docker alternative:\n'
    printf '    docker compose up --build\n\n'
}

printf '\n  CalAssistant Setup\n'
printf '  ==================\n'

ensure_dotnet
ensure_ollama
ensure_ollama_running
ensure_model
ensure_project_folders
ensure_build
print_summary
