# Speaks each scene's line with the built-in Windows voice into scene WAV files, and writes their lengths.
param([string]$Dir = $PSScriptRoot)
Add-Type -AssemblyName System.Speech
$scenes = Get-Content (Join-Path $Dir 'scenes.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voice.SelectVoice('Microsoft Zira Desktop')
$voice.Rate = 0
$lengths = @{}
foreach ($s in $scenes) {
    $wav = Join-Path $Dir "$($s.id).wav"
    $voice.SetOutputToWaveFile($wav)
    $voice.Speak($s.say)
    $voice.SetOutputToNull()
    $bytes = (Get-Item $wav).Length
    # 16-bit mono 22.05 kHz from SAPI's default format; the header is 44 bytes.
    $reader = New-Object System.IO.BinaryReader([System.IO.File]::OpenRead($wav))
    $reader.BaseStream.Seek(28, 'Begin') | Out-Null
    $byteRate = $reader.ReadInt32()
    $reader.Close()
    $lengths[$s.id] = [math]::Round(($bytes - 44) / $byteRate, 2)
}
$lengths | ConvertTo-Json | Set-Content (Join-Path $Dir 'lengths.json') -Encoding UTF8
$lengths
