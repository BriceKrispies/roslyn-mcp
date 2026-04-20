# Downloads the latest dotnet-lsp-mcp binary for Windows and registers it
# with Claude Code at user scope (available in every project).
#
# Usage:
#   .\install.ps1              # user-scoped (default)
#   .\install.ps1 -Project     # write .mcp.json to the current directory instead
#
# Requires: a .NET SDK installed (any recent version). The binary is
# self-contained for the .NET runtime, but MSBuildWorkspace still needs
# MSBuild assemblies from an installed SDK to load solutions.

param(
    [switch]$Project,
    [switch]$User
)

$ErrorActionPreference = 'Stop'

$Repo = if ($env:REPO) { $env:REPO } else { 'BriceKrispies/roslyn-mcp' }
$Scope = if ($Project) { 'project' } else { 'user' }

if ($Scope -eq 'user') {
    $InstallDir = if ($env:INSTALL_DIR) { $env:INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\dotnet-lsp-mcp' }
} else {
    $InstallDir = if ($env:INSTALL_DIR) { $env:INSTALL_DIR } else { Join-Path (Get-Location) 'bin' }
    $McpJson    = if ($env:MCP_JSON)    { $env:MCP_JSON }    else { Join-Path (Get-Location) '.mcp.json' }
}

$Rid = 'win-x64'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Warning "'dotnet' not found in PATH."
    Write-Warning "The server binary will install, but LoadSolution will fail at runtime"
    Write-Warning "without a .NET SDK installed. See https://dot.net/download."
}

Write-Host "Looking up latest release for $Repo..."
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ 'User-Agent' = 'dotnet-lsp-mcp-installer' }
    $Tag = $release.tag_name
} catch {
    Write-Error "Could not resolve latest tag. Check REPO=$Repo and that a release exists."
    exit 1
}

if (-not $Tag) {
    Write-Error "Could not resolve latest tag. Check REPO=$Repo and that a release exists."
    exit 1
}

$Asset = "dotnet-lsp-mcp-$Rid.exe"
$Url   = "https://github.com/$Repo/releases/download/$Tag/$Asset"
$Dest  = Join-Path $InstallDir 'dotnet-lsp-mcp.exe'

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Write-Host "Downloading $Url"
Invoke-WebRequest -Uri $Url -OutFile $Dest -UseBasicParsing

Write-Host ""
Write-Host "Installed $Tag for $Rid`: $Dest"
Write-Host ""

if ($Scope -eq 'user') {
    if (Get-Command claude -ErrorAction SilentlyContinue) {
        Write-Host "Registering with Claude Code at user scope..."
        & claude mcp remove dotnet-lsp -s user *> $null
        & claude mcp add dotnet-lsp -s user -- $Dest
        Write-Host "Done. The 'dotnet-lsp' MCP server is now available in every project."
    } else {
        Write-Host "'claude' CLI not found. To register manually, run:"
        Write-Host ""
        Write-Host "  claude mcp add dotnet-lsp -s user -- `"$Dest`""
        Write-Host ""
    }
} else {
    $escapedDest = $Dest -replace '\\', '\\'
    $json = @"
{
  "mcpServers": {
    "dotnet-lsp": {
      "type": "stdio",
      "command": "$escapedDest"
    }
  }
}
"@
    Set-Content -Path $McpJson -Value $json -Encoding utf8
    Write-Host "Wrote project-scoped config: $McpJson"
    Write-Host "Open Claude Code in this directory and approve the prompt to enable the server."
}
