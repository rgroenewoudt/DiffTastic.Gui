# Downloads the pinned difftastic release into tools/difft/.
param([string]$Version = "0.72.0")

$ErrorActionPreference = "Stop"
$target = Join-Path $PSScriptRoot "difft"
$exe = Join-Path $target ($IsLinux ? "difft" : "difft.exe")

if (Test-Path $exe) {
    $current = (& $exe --version | Select-Object -First 1)
    if ($current -match [regex]::Escape($Version)) {
        Write-Output "difft $Version already present."
        return
    }
}

New-Item -ItemType Directory -Force $target | Out-Null
$asset = $IsLinux ? "difft-$Version-x86_64-unknown-linux-gnu.tar.gz" : "difft-$Version-x86_64-pc-windows-msvc.zip"
$archive = Join-Path ([IO.Path]::GetTempPath()) $asset
$url = "https://github.com/Wilfred/difftastic/releases/download/$Version/$asset"

Invoke-WebRequest $url -OutFile $archive
if ($IsLinux) {
    tar -xzf $archive -C $target
    if ($LASTEXITCODE -ne 0) { throw "tar failed" }
} else {
    Expand-Archive $archive -DestinationPath $target -Force
}
Remove-Item $archive
& $exe --version | Select-Object -First 1
