#Requires -Version 5.1
<#
.SYNOPSIS
    CalAssistant environment setup for Windows.

.DESCRIPTION
    Checks and installs prerequisites (.NET SDK 10, Ollama, qwen3:4b model),
    prepares local folders, and builds the project.

.EXAMPLE
    .\setup.ps1
    .\setup.ps1 -SkipBuild
#>
param(
    [switch]$SkipBuild,
    [string]$Model = "qwen3:4b",
    [string]$OllamaUrl = "http://localhost:11434"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$TotalSteps = 7
$Step = 0

function Write-Title {
    Write-Host ""
    Write-Host "  CalAssistant Setup" -ForegroundColor Cyan
    Write-Host "  ==================" -ForegroundColor DarkCyan
    Write-Host ""
}

function Write-Step([string]$Message) {
    script:Step++
    Write-Host ("[{0}/{1}] {2}" -f $Step, $TotalSteps, $Message) -ForegroundColor Yellow
}

function Write-Ok([string]$Message) {
    Write-Host "       OK  $Message" -ForegroundColor Green
}

function Write-WarnLine([string]$Message) {
    Write-Host "       !!  $Message" -ForegroundColor DarkYellow
}

function Write-Fail([string]$Message) {
    Write-Host "       XX  $Message" -ForegroundColor Red
    exit 1
}

function Test-Command([string]$Name) {
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Invoke-Quiet([scriptblock]$Block) {
    try { return & $Block } catch { return $null }
}

function Ensure-DotNet {
    Write-Step "Checking .NET SDK 10"

    if (-not (Test-Command "dotnet")) {
        Write-WarnLine ".NET SDK not found. Attempting install via winget..."
        if (Test-Command "winget") {
            winget install --id Microsoft.DotNet.SDK.10 --accept-package-agreements --accept-source-agreements
        }
        else {
            Write-Fail "Install .NET 10 SDK manually: https://dotnet.microsoft.com/download/dotnet/10.0"
        }
    }

    $version = (dotnet --version 2>$null).Trim()
    if ($version -notmatch '^10\.') {
        Write-Fail ".NET 10 SDK required (found: $version). Install from https://dotnet.microsoft.com/download/dotnet/10.0"
    }

    Write-Ok ".NET SDK $version"
}

function Ensure-Ollama {
    Write-Step "Checking Ollama"

    if (-not (Test-Command "ollama")) {
        Write-WarnLine "Ollama not found. Attempting install via winget..."
        if (Test-Command "winget") {
            winget install --id Ollama.Ollama --accept-package-agreements --accept-source-agreements
            $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                        [System.Environment]::GetEnvironmentVariable("Path", "User")
        }
        else {
            Write-Fail "Install Ollama manually: https://ollama.com/download/windows"
        }
    }

    if (-not (Test-Command "ollama")) {
        Write-Fail "Ollama still not on PATH. Restart your terminal and run setup again."
    }

    Write-Ok "Ollama CLI available"
}

function Ensure-OllamaRunning {
    Write-Step "Checking Ollama server"

    $reachable = Invoke-Quiet {
        Invoke-RestMethod -Uri "$OllamaUrl/api/tags" -TimeoutSec 3 | Out-Null
        $true
    }

    if (-not $reachable) {
        Write-WarnLine "Ollama server not responding. Starting ollama serve..."
        Start-Process -FilePath "ollama" -ArgumentList "serve" -WindowStyle Hidden
        Start-Sleep -Seconds 4

        $reachable = Invoke-Quiet {
            Invoke-RestMethod -Uri "$OllamaUrl/api/tags" -TimeoutSec 5 | Out-Null
            $true
        }
    }

    if (-not $reachable) {
        Write-Fail "Ollama is not running at $OllamaUrl. Start it manually: ollama serve"
    }

    Write-Ok "Ollama server is up ($OllamaUrl)"
}

function Ensure-Model([string]$ModelName) {
    Write-Step "Checking model '$ModelName'"

    $list = ollama list 2>$null
    $hasModel = $list -match [regex]::Escape($ModelName.Split(':')[0])

    if (-not $hasModel) {
        Write-WarnLine "Model not found. Pulling (this may take a few minutes)..."
        ollama pull $ModelName
        if ($LASTEXITCODE -ne 0) { Write-Fail "Failed to pull model '$ModelName'" }
    }

    Write-Ok "Model '$ModelName' is ready"
}

function Ensure-ProjectFolders {
    Write-Step "Preparing project folders"

    $tokenStore = Join-Path $Root "token-store"
    if (-not (Test-Path $tokenStore)) {
        New-Item -ItemType Directory -Path $tokenStore | Out-Null
        Write-Ok "Created token-store/"
    }
    else {
        Write-Ok "token-store/ exists"
    }

    $creds = Join-Path $Root "credentials.json"
    if (-not (Test-Path $creds)) {
        Write-WarnLine "credentials.json not found — Google Calendar won't work until you add it."
        Write-WarnLine "See README.md → Google OAuth Setup."
    }
    else {
        Write-Ok "credentials.json found"
    }
}

function Ensure-Build {
    if ($SkipBuild) {
        Write-Step "Skipping build (-SkipBuild)"
        Write-Ok "Build skipped"
        return
    }

    Write-Step "Restoring and building project"

    Push-Location $Root
    try {
        dotnet restore
        if ($LASTEXITCODE -ne 0) { Write-Fail "dotnet restore failed" }

        dotnet build -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { Write-Fail "dotnet build failed" }
    }
    finally {
        Pop-Location
    }

    Write-Ok "Project built successfully"
}

function Write-Summary {
    Write-Host ""
    Write-Host "  Setup complete!" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Next steps:" -ForegroundColor Cyan
    Write-Host "    1. Add credentials.json if you haven't yet (see README.md)"
    Write-Host "    2. Run the app:  dotnet run"
    Write-Host "    3. Open:          http://localhost:5136"
    Write-Host ""
    Write-Host "  Docker alternative:" -ForegroundColor Cyan
    Write-Host "    docker compose up --build"
    Write-Host ""
}

Write-Title
Ensure-DotNet
Ensure-Ollama
Ensure-OllamaRunning
Ensure-Model -ModelName $Model
Ensure-ProjectFolders
Ensure-Build
Write-Summary
