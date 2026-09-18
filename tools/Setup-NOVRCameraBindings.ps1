<#
.SYNOPSIS
    Guided setup for NOVR's camera recenter and camera move bindings.

.DESCRIPTION
    Sets the "Camera Move" bindings (move your head around the cockpit) and the recenter binding.
    Run this while Nuclear Option (with NOVR) is running, ideally sitting in the main menu.
    The script speaks each step out loud, so you can keep the headset on and the game focused.
    For each step, press a key or joystick button, or push an analog stick, in the game.
    NOVR reports the input to BepInEx/LogOutput.log ("Log Controller Input"), this script reads it and
    writes the binding into BepInEx/config/deltawing.novr.cfg. NOVR picks up the changed config automatically.

    Press Backspace (in game) to skip a step. A step is also skipped after 30 seconds without input.
#>
param(
    [string]$GameDir = $(if (Test-Path (Join-Path $PSScriptRoot 'NuclearOption.exe')) { $PSScriptRoot } else { 'G:\SteamLibrary\steamapps\common\Nuclear Option' }),
    [int]$StepTimeoutSeconds = 30,
    [switch]$Quiet,
    [switch]$SkipGameCheck
)

$ErrorActionPreference = 'Stop'

$ConfigPath = Join-Path $GameDir 'BepInEx\config\deltawing.novr.cfg'
$LogPath = Join-Path $GameDir 'BepInEx\LogOutput.log'
$MoveSection = 'Camera Move'
$InputSection = 'Input'
$AxisThreshold = 0.7

# ---------------------------------------------------------------- speech / output

$Speech = $null
if (-not $Quiet) {
    try {
        Add-Type -AssemblyName System.Speech
        $Speech = New-Object System.Speech.Synthesis.SpeechSynthesizer
        $Speech.Rate = 1
    } catch {
        $Speech = $null
    }
}

function Say([string]$Text, [ConsoleColor]$Color = 'White') {
    Write-Host $Text -ForegroundColor $Color
    if ($Speech) { $Speech.Speak($Text) }
}

# ---------------------------------------------------------------- config file editing

function Read-ConfigLines {
    return [System.Collections.Generic.List[string]]([System.IO.File]::ReadAllLines($ConfigPath))
}

function Set-ConfigValue([string]$Section, [string]$Name, [string]$Value) {
    $lines = Read-ConfigLines
    $inSection = $false
    $pattern = '^\s*' + [regex]::Escape($Name) + '\s*='
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^\s*\[(.+)\]\s*$') {
            $inSection = ($Matches[1] -eq $Section)
            continue
        }
        if ($inSection -and $line -match $pattern) {
            $lines[$i] = "$Name = $Value"
            [System.IO.File]::WriteAllLines($ConfigPath, $lines, (New-Object System.Text.UTF8Encoding($true)))
            return
        }
    }
    throw "Setting '$Name' in section [$Section] was not found in $ConfigPath. Start the game once with the updated NOVR so it writes the new settings."
}

function Get-ConfigValue([string]$Section, [string]$Name) {
    $inSection = $false
    $pattern = '^\s*' + [regex]::Escape($Name) + '\s*=\s*(.*)$'
    foreach ($line in Read-ConfigLines) {
        if ($line -match '^\s*\[(.+)\]\s*$') { $inSection = ($Matches[1] -eq $Section); continue }
        if ($inSection -and $line -match $pattern) { return $Matches[1].Trim() }
    }
    return $null
}

# ---------------------------------------------------------------- log tailing

$script:LogReader = $null

function Open-Log {
    $stream = New-Object System.IO.FileStream($LogPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]'ReadWrite, Delete')
    $stream.Seek(0, [System.IO.SeekOrigin]::End) | Out-Null
    $script:LogReader = New-Object System.IO.StreamReader($stream)
}

function Read-NewLogLines {
    $result = @()
    while ($true) {
        $line = $script:LogReader.ReadLine()
        if ($null -eq $line) { break }
        $result += $line
    }
    return $result
}

# Control paths look like "/<device name>/hat/up". The device is the first path segment.
function Get-DeviceOfPath([string]$Path) {
    $parts = $Path.TrimStart('/').Split('/')
    return $parts[0]
}

function New-InputEvent([int]$Frame, [string]$Type, [string]$Key, [string]$Path, [double]$Value) {
    $device = if ($Path) { Get-DeviceOfPath $Path } else { $null }
    return [pscustomobject]@{ Frame = $Frame; Type = $Type; Key = $Key; Path = $Path; Device = $device; Value = $Value }
}

function Convert-LogLine([string]$Line) {
    if ($Line -match 'Input: f=(\d+) Key (\w+) pressed') {
        return New-InputEvent ([int]$Matches[1]) 'Key' $Matches[2] $null 1.0
    }
    if ($Line -match 'Input: f=(\d+) Button (.+) pressed\s*$') {
        return New-InputEvent ([int]$Matches[1]) 'Button' $null $Matches[2] 1.0
    }
    if ($Line -match 'Input: f=(\d+) Axis (.+) = (-?[\d.]+)\s*$') {
        $value = [double]::Parse($Matches[3], [System.Globalization.CultureInfo]::InvariantCulture)
        return New-InputEvent ([int]$Matches[1]) 'Axis' $null $Matches[2] $value
    }
    return $null
}

# Reads new log lines and returns the input events in them. Frames where one joystick reports many controls
# changing at once are dropped: that's a misread HID report (or the initial state dump), not a real input.
function Read-InputEvents {
    $events = @(Read-NewLogLines | ForEach-Object { Convert-LogLine $_ } | Where-Object { $null -ne $_ })
    if ($events.Count -eq 0) { return @() }

    $burstKeys = @{}
    $events | Where-Object { $_.Device } | Group-Object { "$($_.Frame)|$($_.Device)" } | Where-Object { $_.Count -ge 4 } |
        ForEach-Object { $burstKeys[$_.Name] = $true }

    return @($events | Where-Object { -not $_.Device -or -not $burstKeys.ContainsKey("$($_.Frame)|$($_.Device)") })
}

# Throws away anything logged in the next few seconds (e.g. the stick returning to center).
function Clear-PendingInput([double]$Seconds = 2.5) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        Read-NewLogLines | Out-Null
        Start-Sleep -Milliseconds 100
    }
}

# Waits for one input. Returns $null when skipped (Backspace or timeout).
function Wait-Input([bool]$AllowAxis) {
    Clear-PendingInput 0.3
    $deadline = (Get-Date).AddSeconds($StepTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($inputEvent in Read-InputEvents) {
            if ($inputEvent.Type -eq 'Key' -and $inputEvent.Key -eq 'Backspace') { return $null }
            # Escape is used to navigate game menus, so it's never taken as a binding.
            if ($inputEvent.Type -eq 'Key' -and $inputEvent.Key -eq 'Escape') { continue }
            if ($inputEvent.Type -eq 'Axis') {
                if (-not $AllowAxis -or [math]::Abs($inputEvent.Value) -lt $AxisThreshold) { continue }
            }
            return $inputEvent
        }
        Start-Sleep -Milliseconds 100
    }
    return $null
}

# Short, speakable name: "hat up on EVO R" instead of the full control path.
function Describe($inputEvent) {
    if ($inputEvent.Type -eq 'Key') { return "key $($inputEvent.Key)" }
    $control = $inputEvent.Path.TrimStart('/').Substring($inputEvent.Device.Length).Trim('/').Replace('/', ' ')
    $deviceWords = @($inputEvent.Device.Trim() -split '\s+')
    $shortDevice = ($deviceWords | Select-Object -Last 2) -join ' '
    return "$control on $shortDevice"
}

# ---------------------------------------------------------------- binding steps

$script:Summary = New-Object System.Collections.Generic.List[string]

function Record([string]$What, $inputEvent) {
    $text = "$What = $(Describe $inputEvent)"
    $script:Summary.Add($text)
    Say "Got it: $(Describe $inputEvent)." Green
    Clear-PendingInput
}

# One step for a simple action (key or joystick button).
function Invoke-ActionStep([string]$Prompt, [string]$What, [string]$Section, [string]$KeySetting, [string]$ButtonSetting) {
    Say $Prompt Cyan
    $inputEvent = Wait-Input $false
    if ($null -eq $inputEvent) { Say 'Skipped.' DarkGray; return }
    if ($inputEvent.Type -eq 'Key') {
        Set-ConfigValue $Section $KeySetting $inputEvent.Key
    } else {
        Set-ConfigValue $Section $ButtonSetting $inputEvent.Path
    }
    Record $What $inputEvent
}

# One direction pair (e.g. forward/back). Pushing an analog stick binds both directions at once.
function Invoke-DirectionStep(
    [string]$PositiveName, [string]$NegativeName,
    [string]$AxisSetting, [string]$InvertSetting,
    [string]$PositiveButtonSetting, [string]$NegativeButtonSetting,
    [string]$PositiveKeySetting, [string]$NegativeKeySetting) {

    Say "Push the stick, or press the key or button, for moving your head $PositiveName." Cyan
    $inputEvent = Wait-Input $true
    if ($null -eq $inputEvent) { Say 'Skipped.' DarkGray; return $false }

    if ($inputEvent.Type -eq 'Axis') {
        Set-ConfigValue $MoveSection $AxisSetting $inputEvent.Path
        Set-ConfigValue $MoveSection $InvertSetting $(if ($inputEvent.Value -lt 0) { 'true' } else { 'false' })
        Set-ConfigValue $MoveSection $PositiveButtonSetting ''
        Set-ConfigValue $MoveSection $NegativeButtonSetting ''
        Record "Move $PositiveName and $NegativeName" $inputEvent
        return $true
    }

    if ($inputEvent.Type -eq 'Key') {
        Set-ConfigValue $MoveSection $PositiveKeySetting $inputEvent.Key
    } else {
        Set-ConfigValue $MoveSection $AxisSetting ''
        Set-ConfigValue $MoveSection $PositiveButtonSetting $inputEvent.Path
    }
    Record "Move $PositiveName" $inputEvent

    Say "Now press the key or button for moving your head $NegativeName." Cyan
    $inputEvent = Wait-Input $false
    if ($null -eq $inputEvent) { Say 'Skipped.' DarkGray; return $false }
    if ($inputEvent.Type -eq 'Key') {
        Set-ConfigValue $MoveSection $NegativeKeySetting $inputEvent.Key
    } else {
        Set-ConfigValue $MoveSection $NegativeButtonSetting $inputEvent.Path
    }
    Record "Move $NegativeName" $inputEvent
    return $false
}

# ---------------------------------------------------------------- main

Write-Host ''
Write-Host 'NOVR camera bindings setup' -ForegroundColor Yellow
Write-Host '--------------------------' -ForegroundColor Yellow

if (-not (Test-Path $ConfigPath)) { Write-Host "Config not found: $ConfigPath" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $LogPath)) { Write-Host "Log not found: $LogPath" -ForegroundColor Red; exit 1 }
if ($null -eq (Get-ConfigValue $MoveSection 'Axis Forward Back')) {
    Write-Host 'The config has no camera binding settings yet. Start the game once with the updated NOVR, then run this again.' -ForegroundColor Red
    exit 1
}
if (-not $SkipGameCheck -and -not (Get-Process -Name 'NuclearOption' -ErrorAction SilentlyContinue)) {
    Write-Host 'Nuclear Option is not running. Start the game (the main menu is a good place to be), then run this again.' -ForegroundColor Red
    exit 1
}

$backupPath = "$ConfigPath.$(Get-Date -Format 'yyyyMMdd-HHmmss').bak"
Copy-Item $ConfigPath $backupPath
Write-Host "Config backed up to $backupPath" -ForegroundColor DarkGray

Open-Log
Set-ConfigValue $InputSection 'Log Controller Input' 'true'

try {
    Write-Host ''
    Write-Host 'Tips: do this from the main menu. Press Backspace in game to skip a step.' -ForegroundColor DarkGray
    Write-Host "      Steps skip themselves after $StepTimeoutSeconds seconds without input." -ForegroundColor DarkGray
    Write-Host ''
    Say 'Switch to the game window now. Starting in 5 seconds.' Yellow
    if (-not $SkipGameCheck) { Start-Sleep -Seconds 5 }

    Invoke-ActionStep 'Press the key or joystick button you want for recentering the view.' 'Recenter' $InputSection 'Recenter Shortcut' 'Recenter Button'

    Invoke-DirectionStep 'forward' 'back' 'Axis Forward Back' 'Invert Axis Forward Back' 'Button Forward' 'Button Back' 'Key Forward' 'Key Back' | Out-Null
    Invoke-DirectionStep 'right' 'left' 'Axis Left Right' 'Invert Axis Left Right' 'Button Right' 'Button Left' 'Key Right' 'Key Left' | Out-Null

    Say 'For up and down, you can hold a joystick button so the forward and back input moves your head up and down instead. Press that button now, or press Backspace to bind up and down separately.' Cyan
    $modifier = Wait-Input $false
    if ($null -ne $modifier -and $modifier.Type -eq 'Button') {
        Set-ConfigValue $MoveSection 'Button Vertical Modifier' $modifier.Path
        Record 'Hold for up and down' $modifier
    } else {
        if ($null -ne $modifier) { Say 'That was a key, not a joystick button, so I will ask for up and down separately.' DarkGray }
        Set-ConfigValue $MoveSection 'Button Vertical Modifier' ''
        Invoke-DirectionStep 'up' 'down' 'Axis Up Down' 'Invert Axis Up Down' 'Button Up' 'Button Down' 'Key Up' 'Key Down' | Out-Null
    }

    Invoke-ActionStep 'Last one. Press the key or joystick button to reset your head position to the default, or Backspace to skip.' 'Reset head position' $MoveSection 'Key Reset' 'Button Reset'
}
finally {
    Set-ConfigValue $InputSection 'Log Controller Input' 'false'
    if ($script:LogReader) { $script:LogReader.Dispose() }
}

Write-Host ''
Write-Host 'Bindings saved:' -ForegroundColor Yellow
foreach ($line in $script:Summary) { Write-Host "  $line" }
if ($script:Summary.Count -eq 0) {
    Write-Host '  (nothing - no input was seen. Is the game focused and running the updated NOVR?)' -ForegroundColor DarkYellow
}
Say 'All done. The new bindings are active now. Try them in the cockpit.' Green
