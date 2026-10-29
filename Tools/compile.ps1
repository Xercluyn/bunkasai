# Compile-checks the project's scripts with Unity's bundled Roslyn (no Unity needed).
# Usage: powershell -File Tools/compile.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$editorData = 'C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Data'
$dotnet = Join-Path $editorData 'DotNetSdk\dotnet.exe'
$csc = Join-Path $editorData 'DotNetSdk\sdk\8.0.318\Roslyn\bincore\csc.dll'
$out = Join-Path $env:TEMP ('bunkasai-compile-' + [guid]::NewGuid().ToString('N').Substring(0, 8)) # unique: parallel runs
New-Item -ItemType Directory -Force $out | Out-Null

function Get-ProjInfo($proj) {
    [xml]$x = Get-Content (Join-Path $root $proj) -Raw
    $ns = @{ m = $x.DocumentElement.NamespaceURI }
    $refs = @($x.SelectNodes('//*[local-name()="HintPath"]') | ForEach-Object { $_.InnerText }) | Where-Object { $_ -and (Test-Path $_) -and ($_ -notmatch 'Assembly-CSharp') }
    $defs = ($x.SelectNodes('//*[local-name()="DefineConstants"]') | Select-Object -First 1).InnerText
    @{ Refs = $refs; Defs = $defs }
}

$all = Get-ChildItem (Join-Path $root 'Assets') -Recurse -Filter *.cs | ForEach-Object FullName
$runtimeSrc = $all | Where-Object { $_ -notmatch '\\Editor\\' }
$editorSrc = $all | Where-Object { $_ -match '\\Editor\\' }

function Invoke-Csc($name, $srcs, $info, $extraRefs) {
    $rsp = Join-Path $out "$name.rsp"
    $lines = @('-nologo', '-target:library', '-nostdlib-', '-langversion:9.0', '-nowarn:0169,0414,0649,0618,0067,0219,0162', "-out:$(Join-Path $out "$name.dll")", "-define:$($info.Defs)")
    $lines += ($info.Refs + $extraRefs) | ForEach-Object { "-r:`"$_`"" }
    $lines += $srcs | ForEach-Object { "`"$_`"" }
    Set-Content -Path $rsp -Value $lines -Encoding utf8
    & $dotnet $csc "@$rsp" 2>&1 | Where-Object { $_ -match 'error|warning CS' -and $_ -notmatch 'warning CS8019' }
    "${name}: exit $LASTEXITCODE"
}

Invoke-Csc 'Assembly-CSharp' $runtimeSrc (Get-ProjInfo 'Assembly-CSharp.csproj') @()
Invoke-Csc 'Assembly-CSharp-Editor' $editorSrc (Get-ProjInfo 'Assembly-CSharp-Editor.csproj') @((Join-Path $out 'Assembly-CSharp.dll'))
