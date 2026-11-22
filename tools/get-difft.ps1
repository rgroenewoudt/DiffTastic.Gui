# Downloads the pinned difftastic release into tools/difft/.
param([string]$Version = "0.72.0")

$ErrorActionPreference = "Stop"
$target = Join-Path $PSScriptRoot "difft"
$exe = Join-Path $target "difft.exe"

if (Test-Path $exe) {
    $current = (& $exe --version | Select-Object -First 1)
    if ($current -match [regex]::Escape($Version)) {
        Write-Output "difft $Version already present."
        return
    }
}

New-Item -ItemType Directory -Force $target | Out-Null
$zip = Join-Path ([IO.Path]::GetTempPath()) "difft-$Version.zip"
$url = "https://github.com/Wilfred/difftastic/releases/download/$Version/difft-$Version-x86_64-pc-windows-msvc.zip"

Invoke-WebRequest $url -OutFile $zip
Expand-Archive $zip -DestinationPath $target -Force
Remove-Item $zip
& $exe --version | Select-Object -First 1
