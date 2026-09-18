[CmdletBinding()]
param(
    [string]$LogPath,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [Parameter(Mandatory=$true)][string]$Scene,
    [Parameter(Mandatory=$true)][ValidateSet('Native','FSR','DLSS','Native+FG','FSR+FG','DLSS+FG')][string]$RenderingMode
)
$ErrorActionPreference='Stop'
if (!$LogPath) {
    $latest=Get-ChildItem -LiteralPath 'T:\SteamLibrary\steamapps\common\SpaceEngineers\Profile' -Filter 'SpaceEngineers_*.log' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (!$latest) { throw 'No game log found. Supply -LogPath.' }
    $LogPath=$latest.FullName
}
$source=(Resolve-Path -LiteralPath $LogPath).Path
$destination=[IO.Path]::GetFullPath($OutputPath)
if ($source -eq $destination) { throw 'Output must not overwrite the game log.' }
$pattern='Anomaly volume sample: frame=(\d+) size=(\d+)x(\d+) quality=(\d+) providers=(\d+) distance=([\d.]+) shadowsMs=(\S+) lightMs=(\S+) injectMs=(\S+) integrateMs=(\S+) reconstructMs=(\S+) interiors=(.*)$'
$culture=[Globalization.CultureInfo]::InvariantCulture
function Read-Milliseconds([string]$Value) {
    if ($Value -eq 'pending') { return $null }
    $number=[double]::Parse($Value,$culture)
    if ([double]::IsNaN($number) -or [double]::IsInfinity($number) -or $number -lt 0) { throw "Invalid GPU timing: $Value" }
    return $number
}
$records=@(foreach($line in [IO.File]::ReadLines($source)) {
    if ($line -match $pattern) {
        [ordered]@{
            Frame=[uint32]$Matches[1]; Width=[int]$Matches[2]; Height=[int]$Matches[3]
            Quality=[int]$Matches[4]; Providers=[int]$Matches[5]; DistanceMeters=[double]::Parse($Matches[6],$culture)
            ShadowsMs=(Read-Milliseconds $Matches[7]); LightMs=(Read-Milliseconds $Matches[8])
            InjectionMs=(Read-Milliseconds $Matches[9]); IntegrationMs=(Read-Milliseconds $Matches[10])
            ReconstructionMs=(Read-Milliseconds $Matches[11]); Interiors=$Matches[12]; LogLine=$line
        }
    }
})
if ($records.Count -eq 0) { throw 'No shared-volume timing samples found. Enable Atmosphere and let it render for at least 10 seconds.' }
$report=[ordered]@{
    SchemaVersion=1; CapturedUtc=[DateTime]::UtcNow.ToString('o'); SourceLog=$source
    Scene=$Scene; RenderingMode=$RenderingMode
    Limitation='Scene and rendering mode are operator labels. Each pass is its latest completed asynchronous query, sampled every 10 seconds; these are not a synchronized per-frame benchmark or proof of visual acceptance. Preserve separate logs for each scene/mode.'
    Samples=$records
}
$parent=[IO.Path]::GetDirectoryName($destination)
[IO.Directory]::CreateDirectory($parent) | Out-Null
[IO.File]::WriteAllText($destination,($report | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
Write-Output "Saved $($records.Count) volume samples to $destination"
