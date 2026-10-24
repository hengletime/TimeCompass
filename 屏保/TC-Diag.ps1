# ============================================================
# TimeCompass screensaver - target PC diagnostic
# Run:  right-click -> "Run with PowerShell"   (or: powershell -ExecutionPolicy Bypass -File TC-Diag.ps1)
# NOTE: content is pure ASCII on purpose - non-ASCII inside .ps1 gets
#       corrupted under some code pages and breaks parsing.
# ============================================================

$d = Join-Path $env:TEMP 'tcdiag'
New-Item -ItemType Directory -Path $d -Force | Out-Null
$report = New-Object System.Text.StringBuilder
function W($s) { Write-Host $s; [void]$report.AppendLine($s) }

W '=============================================='
W ' TimeCompass diagnostic  (screensaver blocked?)'
W '=============================================='
W ('USER       = ' + $env:USERNAME)
W ('TEMP       = ' + $env:TEMP)
W ('PS version = ' + $PSVersionTable.PSVersion.ToString())
W ''

W '=== [1] Can this shell write to the disk? ==='
$writeOk = $false
try {
    Set-Content -Path (Join-Path $d 'probe.txt') -Value ok -ErrorAction Stop
    Remove-Item (Join-Path $d 'probe.txt') -Force -ErrorAction SilentlyContinue
    $writeOk = $true
    W '   [OK]   write test SUCCEEDED  -> the machine itself is NOT read-only'
} catch {
    W ('   [FAIL] write test failed: ' + $_.Exception.Message)
    W '          -> the whole session is restricted (very unusual)'
}
W ''

W '=== [2] Security / management software installed ==='
try {
    $av = Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction Stop
    if ($av) { foreach ($a in $av) { W ('   AV: ' + $a.displayName) } } else { W '   (none reported)' }
} catch { W ('   (query failed: ' + $_.Exception.Message + ')') }
foreach ($p in @('360Tray','360Safe','HipsTray','HipsDaemon','QQPCTray','kxetray','wsctrl','SogouPinyin','HuaweiThpService')) {
    if (Get-Process -Name $p -ErrorAction SilentlyContinue) { W ('   RUNNING: ' + $p) }
}
W ''

W '=== [3] Is Edge present? ==='
$edge = $null
foreach ($c in @("$env:ProgramFiles(x86)\Microsoft\Edge\Application\msedge.exe",
                 "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
                 "$env:LOCALAPPDATA\Microsoft\Edge\Application\msedge.exe")) {
    if (Test-Path $c) { W ('   FOUND: ' + $c); if (-not $edge) { $edge = $c } }
}
if (-not $edge) { W '   NOT FOUND in the 3 standard locations' }
$now = @(Get-Process msedge -ErrorAction SilentlyContinue).Count
W ('   msedge processes running NOW = ' + $now + '   (>0 means Edge is already open)')
W ''

W '=== [4] Can Edge run kiosk with its own profile in TEMP? ==='
if (-not $edge) {
    W '   skipped (Edge not found)'
} else {
    $prof = Join-Path $d 'p'
    $url  = 'data:text/html,<h1 style=''color:red;font-size:64px''>TCDIAG - Edge OK</h1>'
    W '   launching Edge kiosk for 8 seconds (a red page will cover your screen) ...'
    Start-Process -FilePath $edge -ArgumentList @('--kiosk', $url, ('--user-data-dir=' + $prof), '--no-first-run', '--no-default-browser-check')
    Start-Sleep -Seconds 8
    $after = @(Get-Process msedge -ErrorAction SilentlyContinue).Count
    $full = 0
    foreach ($pr in (Get-Process msedge -ErrorAction SilentlyContinue)) {
        if ($pr.MainWindowHandle -ne 0) { $full++ }
    }
    W ('   msedge processes AFTER = ' + $after + '   windows with a MainWindow = ' + $full)
    W '   >>> If you SAW the red TCDIAG page: Edge kiosk works fine on this PC.'
    W '   >>> press Alt+F4 (or Ctrl+W) to close it.'
    W ''
    W '   profile dir written? ' + (Test-Path $prof)
}
W ''
W '=============================================='
W ' COPY THE WHOLE OUTPUT BACK TO THE ASSISTANT'
W '=============================================='
$out = Join-Path $d 'TC-Diag-Result.txt'
$report.ToString() | Set-Content -Path $out -Encoding UTF8
Write-Host ''
Write-Host ('Report also saved to: ' + $out)
