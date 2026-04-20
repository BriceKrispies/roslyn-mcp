$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'DotNet47App.csproj'
$exePath     = Join-Path $PSScriptRoot 'bin\Debug\DotNet47App.exe'
Write-Host "Project: $projectPath"

$refPath = "C:\Windows\Microsoft.NET\Framework\v4.0.30319"
$buildArgs = @(
    $projectPath,
    '/t:Build',
    '/p:Configuration=Debug',
    '/nologo',
    '/v:m'
)

if ($refPath) {
    $buildArgs += "/p:FrameworkPathOverride=`"$refPath`""
}

Write-Host "Building with: dotnet msbuild $($buildArgs -join ' ')"
& dotnet msbuild @buildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found: $exePath"
    exit 1
}

Write-Host "Running: $exePath"
& $exePath


