param([string]$GameShaders = 'T:/SteamLibrary/steamapps/common/SpaceEngineers/Content/Shaders', [string]$Fxc,
    [string]$AnomalyRoot = (Join-Path $PSScriptRoot '../..'))
$ErrorActionPreference = 'Stop'
if (-not $Fxc) {
    $Fxc = Get-ChildItem -LiteralPath "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter fxc.exe -Recurse |
        Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Fxc) { throw 'FXC not found; supply -Fxc.' }
$shader = Join-Path ([IO.Path]::GetTempPath()) ('Anomaly-billboard-' + [guid]::NewGuid().ToString('N') + '.cso')
try {
    & $Fxc /nologo /T ps_5_0 /E __pixel_shader /D SOFT_PARTICLE=1 /I $GameShaders /Fo $shader (Join-Path $GameShaders 'Transparent/Billboards.hlsl')
    if ($LASTEXITCODE -ne 0) { throw 'Native billboard shader compilation failed.' }
    & dotnet build (Join-Path $PSScriptRoot 'TransparentBindings.csproj') -c Release -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Binding harness build failed.' }
    $gameBin = Join-Path (Split-Path (Split-Path $GameShaders -Parent) -Parent) 'Bin64'
    & dotnet (Join-Path $PSScriptRoot 'bin/Release/net10.0/TransparentBindings.dll') $shader (Join-Path $AnomalyRoot 'ClientPlugin/bin/Release/net10.0/Anomaly.dll') $gameBin
    if ($LASTEXITCODE -ne 0) { throw 'net10.0 binding checks failed.' }
    & (Join-Path $PSScriptRoot 'bin/Release/net48/TransparentBindings.exe') $shader (Join-Path $AnomalyRoot 'ClientPlugin/bin/Release/net48/Anomaly.dll') $gameBin
    if ($LASTEXITCODE -ne 0) { throw 'net48 binding checks failed.' }
}
finally { if (Test-Path -LiteralPath $shader) { Remove-Item -LiteralPath $shader } }
