[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$legacyPath = Join-Path $repoRoot "legacy\reference\SmartPositionAutoTrader_M5_MicroSwingPrecision_HedgedVolume.cs"
$robotDirectory = Join-Path $repoRoot "src\CTTraderBot.CTrader\Robot"
$robotEntry = Join-Path $robotDirectory "SmartPositionAutoTrader.Robot.cs"

foreach ($path in @($legacyPath, $robotEntry, $robotDirectory)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required cBot contract source is missing: $path"
    }
}

function Get-ParameterContracts {
    param([Parameter(Mandatory = $true)][string]$Path)

    $source = Get-Content -LiteralPath $Path -Raw
    # Require the full attribute terminator )] so a parenthesis inside a display-name string
    # does not end the capture early.
    $pattern = '(?s)\[Parameter\((?<arguments>.*?)\)\]\s*public\s+(?<type>[\w?<>]+)\s+(?<name>\w+)\s*\{\s*get;\s*set;\s*\}'
    $matches = [regex]::Matches($source, $pattern)
    $contracts = New-Object System.Collections.ArrayList

    foreach ($match in $matches) {
        [void]$contracts.Add([pscustomobject]@{
            Name = $match.Groups["name"].Value
            Type = $match.Groups["type"].Value
            Arguments = $match.Groups["arguments"].Value.Trim()
        })
    }

    return $contracts.ToArray()
}

$legacyContracts = @(Get-ParameterContracts -Path $legacyPath)
$currentContracts = @(Get-ParameterContracts -Path $robotEntry)

if ($legacyContracts.Count -eq 0) {
    throw "No public [Parameter] declarations were found in the reference source."
}
if ($currentContracts.Count -eq 0) {
    throw "No public [Parameter] declarations were found in the current cBot entry source."
}

$legacyByName = @{}
foreach ($contract in $legacyContracts) {
    if ($legacyByName.ContainsKey($contract.Name)) {
        throw "Duplicate reference parameter name: $($contract.Name)"
    }
    $legacyByName[$contract.Name] = $contract
}

$currentByName = @{}
foreach ($contract in $currentContracts) {
    if ($currentByName.ContainsKey($contract.Name)) {
        throw "Duplicate current cBot parameter name: $($contract.Name)"
    }
    $currentByName[$contract.Name] = $contract
}

$missing = New-Object System.Collections.ArrayList
$changed = New-Object System.Collections.ArrayList
foreach ($legacy in $legacyContracts) {
    if (-not $currentByName.ContainsKey($legacy.Name)) {
        [void]$missing.Add($legacy.Name)
        continue
    }

    $current = $currentByName[$legacy.Name]
    if ($legacy.Type -cne $current.Type -or $legacy.Arguments -cne $current.Arguments) {
        [void]$changed.Add(("{0}: type or full Parameter attribute changed" -f $legacy.Name))
    }
}

if ($missing.Count -gt 0 -or $changed.Count -gt 0) {
    $details = @()
    if ($missing.Count -gt 0) {
        $details += "Missing reference parameters: " + ($missing -join ", ")
    }
    if ($changed.Count -gt 0) {
        $details += "Changed parameter contracts: " + ($changed -join "; ")
    }
    throw ("cBot parameter contract verification failed:" + [Environment]::NewLine + ($details -join [Environment]::NewLine))
}

$legacyNames = @($legacyContracts | ForEach-Object { $_.Name })
$additional = @($currentContracts | Where-Object { $legacyNames -notcontains $_.Name })

$robotSources = Get-ChildItem -LiteralPath $robotDirectory -Filter "*.cs" -File |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }
$combinedRobotSource = [string]::Join([Environment]::NewLine, [string[]]$robotSources)

# There is one reviewed market-entry call. Any future order-path redesign must update this gate deliberately.
$marketOrderCalls = [regex]::Matches(
    $combinedRobotSource,
    '(?s)\bExecuteMarketOrder\s*\((?<arguments>.*?)\)\s*;')

if ($marketOrderCalls.Count -ne 1) {
    throw "Expected one reviewed ExecuteMarketOrder path, found $($marketOrderCalls.Count). Review the order architecture and update this contract check intentionally if needed."
}

$orderArguments = $marketOrderCalls[0].Groups["arguments"].Value.Split(',')
if ($orderArguments.Count -lt 6 -or $orderArguments[5].Trim() -cne "null") {
    throw "Broker take-profit contract changed: the market order take-profit argument must remain explicitly null because TP is chart-guide only."
}

if ([regex]::IsMatch($combinedRobotSource, '\.\s*ModifyTakeProfitPrice\s*\(')) {
    throw "Broker take-profit modification was detected. Review the chart-guide-only TP contract before proceeding."
}

Write-Host ("cBot contract verification passed: {0} reference parameters preserved; {1} additional current parameters; broker TP remains null." -f $legacyContracts.Count, $additional.Count)
