$ErrorActionPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent $PSScriptRoot
$appUrl = "http://localhost:8081"
$probeUrl = "http://127.0.0.1:8081"

function Test-Web {
    try {
        return (Invoke-WebRequest -UseBasicParsing $probeUrl -TimeoutSec 3).StatusCode -eq 200
    } catch {
        return $false
    }
}

function Test-Engine {
    docker info *> $null
    return $LASTEXITCODE -eq 0
}

if (-not (Test-Web)) {
    if (-not (Test-Engine)) {
        $dockerDesktop = Join-Path $env:LOCALAPPDATA "Programs\DockerDesktop\Docker Desktop.exe"
        if (-not (Test-Path $dockerDesktop)) { $dockerDesktop = "C:\Program Files\Docker\Docker\Docker Desktop.exe" }
        if (Test-Path $dockerDesktop) { Start-Process $dockerDesktop }
        for ($i = 0; $i -lt 90; $i++) {
            if (Test-Engine) { break }
            Start-Sleep -Seconds 2
        }
    }
    if (Test-Engine) {
        Push-Location $repoRoot
        docker compose up -d --wait *> $null
        Pop-Location
    }
    for ($i = 0; $i -lt 30; $i++) {
        if (Test-Web) { break }
        Start-Sleep -Seconds 2
    }
}

Start-Process $appUrl
