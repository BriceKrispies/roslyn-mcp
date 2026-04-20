#!/usr/bin/env bash
# Downloads the latest dotnet-lsp-mcp binary for this platform and registers
# it with Claude Code at user scope (available in every project).
#
# Usage:
#   ./install.sh              # user-scoped (default)
#   ./install.sh --project    # write .mcp.json to the current directory instead
#
# Requires: a .NET SDK installed (any recent version). The binary is
# self-contained for the .NET runtime, but MSBuildWorkspace still needs
# MSBuild assemblies from an installed SDK to load solutions.

set -euo pipefail

REPO="${REPO:-BriceKrispies/roslyn-mcp}"
SCOPE="user"
for arg in "$@"; do
  case "$arg" in
    --project) SCOPE="project" ;;
    --user)    SCOPE="user" ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done

if [ "$SCOPE" = "user" ]; then
  INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/bin}"
else
  INSTALL_DIR="${INSTALL_DIR:-$(pwd)/bin}"
  MCP_JSON="${MCP_JSON:-$(pwd)/.mcp.json}"
fi

detect_rid() {
  local os arch
  os="$(uname -s)"
  arch="$(uname -m)"
  case "$os" in
    Linux*)  echo "linux-x64" ;;
    Darwin*) [ "$arch" = "arm64" ] && echo "osx-arm64" || echo "osx-x64" ;;
    *)       echo "unsupported" ;;
  esac
}

RID="$(detect_rid)"
if [ "$RID" = "unsupported" ]; then
  echo "Unsupported platform: $(uname -s) $(uname -m). Windows users: run install.ps1." >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Warning: 'dotnet' not found in PATH." >&2
  echo "The server binary will install, but LoadSolution will fail at runtime" >&2
  echo "without a .NET SDK installed. See https://dot.net/download." >&2
fi

echo "Looking up latest release for $REPO..."
TAG="$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest" \
  | grep '"tag_name"' | head -1 | sed 's/.*"tag_name": *"\([^"]*\)".*/\1/')"
if [ -z "$TAG" ]; then
  echo "Could not resolve latest tag. Check REPO=$REPO and that a release exists." >&2
  exit 1
fi

ASSET="dotnet-lsp-mcp-${RID}"
URL="https://github.com/${REPO}/releases/download/${TAG}/${ASSET}"
DEST="${INSTALL_DIR}/dotnet-lsp-mcp"

mkdir -p "$INSTALL_DIR"
echo "Downloading ${URL}"
curl -fL "$URL" -o "$DEST"
chmod +x "$DEST"

echo
echo "Installed ${TAG} for ${RID}: ${DEST}"
echo

if [ "$SCOPE" = "user" ]; then
  if command -v claude >/dev/null 2>&1; then
    echo "Registering with Claude Code at user scope..."
    claude mcp remove dotnet-lsp -s user >/dev/null 2>&1 || true
    claude mcp add dotnet-lsp -s user -- "$DEST"
    echo "Done. The 'dotnet-lsp' MCP server is now available in every project."
  else
    echo "'claude' CLI not found. To register manually, run:"
    echo
    echo "  claude mcp add dotnet-lsp -s user -- \"$DEST\""
    echo
  fi
else
  cat > "$MCP_JSON" <<EOF
{
  "mcpServers": {
    "dotnet-lsp": {
      "type": "stdio",
      "command": "${DEST}"
    }
  }
}
EOF
  echo "Wrote project-scoped config: ${MCP_JSON}"
  echo "Open Claude Code in this directory and approve the prompt to enable the server."
fi
