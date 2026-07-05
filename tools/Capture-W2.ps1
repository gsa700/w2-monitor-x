<#
.SYNOPSIS  Capture raw Elecraft W2 query/response replies for protocol validation.
.DESCRIPTION
    The W2 is query/response (unlike the LP-100A's free-running stream): you send a
    single command char and it answers with a ';'-terminated field. This polls the
    read-only status commands V/F/R/S/I each cycle and logs the raw replies, showing
    the I-string in BOTH printable and hex form so we can pin down the payload bytes
    the app doesn't yet use (b[0] and b[4]) and confirm value scaling.

    SAFE: only V/F/R/S/I are sent -- these read meter state and do NOT key the radio
    or change any setting (no N/Y toggles). To exercise value scaling, key a steady
    carrier into a DUMMY LOAD during the capture and step through ranges/sensors.

.EXAMPLE
    ./Capture-W2.ps1 -Port COM8 -Seconds 30
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Port,
    [int]$Seconds = 30
)

$log = "$PSScriptRoot\w2-capture-$(Get-Date -Format yyyyMMdd-HHmmss).log"

$sp = New-Object System.IO.Ports.SerialPort $Port, 9600, ([System.IO.Ports.Parity]::None), 8, ([System.IO.Ports.StopBits]::One)
$sp.Handshake = [System.IO.Ports.Handshake]::None
$sp.DtrEnable = $true; $sp.RtsEnable = $true
$sp.ReadTimeout = 200; $sp.WriteTimeout = 200
$sp.Open()
Start-Sleep -Milliseconds 120
$sp.DiscardInBuffer()

function Query([char]$cmd) {
    $sp.DiscardInBuffer()
    $sp.Write([string]$cmd)
    $resp = ''
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt 200) {
        while ($sp.BytesToRead -gt 0) {
            $ch = [char]$sp.ReadByte()
            $resp += $ch
            if ($ch -eq ';') { break }
        }
        if ($resp -match ';') { break }
        Start-Sleep -Milliseconds 3
    }
    return $resp
}

function ToHex([string]$s) {
    ($s.ToCharArray() | ForEach-Object { '{0:X2}' -f [int][char]$_ }) -join ' '
}

Write-Host "Capturing $Seconds s on $Port at 9600 8N1." -ForegroundColor Green
Write-Host "Key a steady carrier into a DUMMY LOAD and step through ranges/sensors to exercise all fields." -ForegroundColor Yellow
Add-Content $log "# W2 capture $Port  $(Get-Date -Format s)"
Add-Content $log ("# firmware (V): " + (Query 'V'))

$infoSeen = @{}
$deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
$lastLine = ''

try {
    while ([DateTime]::UtcNow -lt $deadline) {
        $f = Query 'F'; $r = Query 'R'; $s = Query 'S'; $i = Query 'I'
        $stamp = (Get-Date).ToString('HH:mm:ss.fff')
        Add-Content $log ("{0}  F={1,-10} R={2,-10} S={3,-8} I={4,-14} Ihex=[{5}]" -f $stamp, $f, $r, $s, $i, (ToHex $i))

        # Track distinct I payloads (printable) so byte positions can be mapped afterward.
        $ip = ($i -replace '[^\x20-\x7E]', '')
        if ($ip) { $infoSeen[$ip] = ($infoSeen[$ip] + 1) }

        $line = "F=$f R=$r S=$s I=$ip"
        if ($line -ne $lastLine) { Write-Host "  $line"; $lastLine = $line }
        Start-Sleep -Milliseconds 80
    }
} finally {
    if ($sp.IsOpen) { $sp.Close() }
    $sp.Dispose()
}

Write-Host ""
Write-Host "=== Distinct I-strings seen ===" -ForegroundColor Cyan
$infoSeen.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
    "  {0,5}x  {1}   (payload after 'I': {2})" -f $_.Value, $_.Key, ($_.Key.TrimEnd(';').Substring([math]::Min(1,$_.Key.Length))) | Write-Host
}
Write-Host ""
Write-Host "Log: $log" -ForegroundColor DarkGray
Write-Host "Cross-check against W2FrameParser byte map: payload [1]=range [2]=auto [3]=type [5]=leds [6]=active." -ForegroundColor DarkGray
