# postfx re-enable sweep: runs the concert once per mask configuration,
# collects the trace and snap pairs per step, and zips the whole set.
# usage: right-click -> run with powershell, or: powershell -File benchmark-postfx.ps1
# the exe auto-opens selection.json (song/stage), the bench flag quits the
# player after the song clock passes the stop second, unattended.

$ErrorActionPreference = "Stop"
$exe_dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $exe_dir "UV2.exe"
if (-not (Test-Path $exe)) { Write-Error "UV2.exe not found beside the script"; exit 1 }

# stop after the last snap point (f5960 @ ~99s) plus margin.
$bench_seconds = 105
# one step per feature letter; "raw" is the all-off baseline.
$steps = @(
    @{ name = "raw"; mask = "" },
    @{ name = "bloom"; mask = "b" },
    @{ name = "dof"; mask = "d" },
    @{ name = "film"; mask = "f" },
    @{ name = "grade"; mask = "g" },
    @{ name = "tilt"; mask = "t" },
    @{ name = "fogfade"; mask = "o" },
    @{ name = "radial"; mask = "r" },
    @{ name = "lens"; mask = "l" },
    @{ name = "ballblur"; mask = "c" }
)

$stamp = Get-Date -Format "yyyyMMdd_HHmm"
$out_root = Join-Path $exe_dir "bench_$stamp"
New-Item -ItemType Directory -Path $out_root -Force | Out-Null

foreach ($step in $steps) {
    $name = $step.name
    $mask = $step.mask
    Write-Host ""
    Write-Host "=== step $name (mask '$mask') ===" -ForegroundColor Cyan

    $env:UV2_POSTFX_MASK = $mask

    # clean the previous run's artifacts so each step only keeps its own.
    Remove-Item (Join-Path $exe_dir "uv2_trace.log") -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $exe_dir "snap_*.png") -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $exe_dir "snapraw_*.png") -ErrorAction SilentlyContinue

    $proc = Start-Process -FilePath $exe -ArgumentList "-uv2bench", "$bench_seconds" -WorkingDirectory $exe_dir -PassThru
    # hard ceiling: open can take a while on cold caches; 10 min per step.
    if (-not $proc.WaitForExit(600000)) {
        Write-Warning "${name}: player did not exit in 10 min, killing"
        Stop-Process -Id $proc.Id -Force
    }

    $step_dir = Join-Path $out_root $name
    New-Item -ItemType Directory -Path $step_dir -Force | Out-Null

    $trace = Join-Path $exe_dir "uv2_trace.log"
    if (Test-Path $trace) { Move-Item $trace (Join-Path $step_dir "uv2_trace.log") -Force }
    Get-ChildItem -Path $exe_dir -Filter "snap*.png" -ErrorAction SilentlyContinue |
        Move-Item -Destination $step_dir -Force
    $count = (Get-ChildItem -Path $step_dir -Filter "*.png").Count
    Write-Host "step $name done: $count snap(s) + trace collected"
}

$env:UV2_POSTFX_MASK = ""

# single archive to send back.
$zip = Join-Path $exe_dir "bench_$stamp.zip"
Compress-Archive -Path (Join-Path $out_root "*") -DestinationPath $zip -Force
Write-Host ""
Write-Host "sweep complete. send this file back: $zip" -ForegroundColor Green
