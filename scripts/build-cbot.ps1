[CmdletBinding()]
param(
    [switch]$AllowScaffoldBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "CT-TraderBot.sln"
$robotEntry = Join-Path $repoRoot "src\CTTraderBot.CTrader\Robot\SmartPositionAutoTrader.Robot.cs"
$algoName = "CTTraderBot.algo"
$robotsDirectory = Join-Path $env:USERPROFILE "Documents\cAlgo\Sources\Robots"
$publishedAlgo = Join-Path $robotsDirectory $algoName

if (-not (Test-Path $solution)) {
    throw "Solution not found: $solution"
}

if (-not (Test-Path $robotEntry) -and -not $AllowScaffoldBuild) {
    throw @"
The real cBot entry file has not been imported and decomposed yet:
  $robotEntry

This checkout currently contains the repository/build scaffold only. No tradable cBot .algo can be produced safely.
Do not start or attach a placeholder bot. Pull the next commit after the source migration phase.
For a scaffold-only compile check, rerun with -AllowScaffoldBuild.
"@
}

Push-Location $repoRoot
try {
    dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

    dotnet build $solution --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

    $testProject = Join-Path $repoRoot "tests\CTTraderBot.Core.Tests\CTTraderBot.Core.Tests.csproj"
    dotnet test $testProject --configuration Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Core unit tests failed with exit code $LASTEXITCODE" }

    if (Test-Path $robotEntry) {
        if (-not (Test-Path $publishedAlgo)) {
            throw @"
The solution compiled but the expected published cBot was not found:
  $publishedAlgo
Check the cTrader.Automate build output and the value of AlgoPublish.
Do not treat a successful library build as proof that an .algo artifact was produced.
"@
        }

        Write-Host ""
        Write-Host "cBot build succeeded."
        Write-Host "Published artifact: $publishedAlgo"
    }
    else {
        Write-Host ""
        Write-Warning "Scaffold compile only. No .algo file was expected or produced."
    }
}
finally {
    Pop-Location
}
