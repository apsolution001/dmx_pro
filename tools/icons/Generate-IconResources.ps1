<#
.SYNOPSIS
    Generates src/DmxPro.App/Themes/Icons.xaml from the SVG files in src/DmxPro.App/Assets/Icons.

.DESCRIPTION
    Every <name>.svg (kebab-case) becomes a frozen <Geometry x:Key="Icon.<PascalName>"> resource whose path markup is
    the concatenated "d" data of all <path> elements in the file. fill-rule="evenodd" maps to the WPF prefix F0,
    otherwise F1 (nonzero, the SVG default). The SVGs must use a 0 0 24 24 viewBox; DmxIcon scales from 24x24.

    Output is deterministic (files sorted ordinally by name), so re-running without SVG changes produces no diff.
    Works in Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
    pwsh tools/icons/Generate-IconResources.ps1
    powershell -ExecutionPolicy Bypass -File tools\icons\Generate-IconResources.ps1
#>
[CmdletBinding()]
param(
    [string]$IconsDir,
    [string]$OutputFile
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $IconsDir) { $IconsDir = Join-Path $repoRoot 'src\DmxPro.App\Assets\Icons' }
if (-not $OutputFile) { $OutputFile = Join-Path $repoRoot 'src\DmxPro.App\Themes\Icons.xaml' }

function ConvertTo-PascalCase([string]$kebab) {
    $parts = $kebab -split '[-_ ]+' | Where-Object { $_ }
    ($parts | ForEach-Object { $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1).ToLowerInvariant() }) -join ''
}

$files = @(Get-ChildItem -Path $IconsDir -Filter '*.svg' -File)
if ($files.Count -eq 0) { throw "No SVG files found in $IconsDir" }
$names = [string[]]($files | ForEach-Object { $_.Name })
[Array]::Sort($names, [StringComparer]::Ordinal)

$allowedPathData = '^[MmLlHhVvCcSsQqTtAaZz0-9 .,eE+\-]+$'
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<!-- Generated - do not edit; run tools/icons/Generate-IconResources.ps1 -->')
[void]$sb.AppendLine('<!-- Source: Microsoft Fluent UI System Icons (MIT), see Assets/Icons/README.md -->')
[void]$sb.AppendLine('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"')
[void]$sb.AppendLine('                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"')
[void]$sb.AppendLine('                    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"')
[void]$sb.AppendLine('                    xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options"')
[void]$sb.AppendLine('                    mc:Ignorable="po">')

$seenKeys = @{}
foreach ($name in $names) {
    $file = Join-Path $IconsDir $name
    $xml = New-Object System.Xml.XmlDocument
    $xml.XmlResolver = $null
    $xml.Load($file)
    $root = $xml.DocumentElement
    if ($root.LocalName -ne 'svg') { throw "$name : root element is not <svg>" }
    $viewBox = $root.GetAttribute('viewBox')
    if ($viewBox -and $viewBox -ne '0 0 24 24') { throw "$name : viewBox must be '0 0 24 24' (found '$viewBox')" }

    $paths = @($root.GetElementsByTagName('path'))
    if ($paths.Count -eq 0) { throw "$name : no <path> elements" }

    $data = New-Object System.Collections.Generic.List[string]
    $evenOdd = $false
    $nonZero = $false
    foreach ($p in $paths) {
        $d = ($p.GetAttribute('d') -replace '\s+', ' ').Trim()
        if (-not $d) { continue }
        if ($d -notmatch $allowedPathData) { throw "$name : unsupported characters in path data" }
        if ($p.GetAttribute('fill-rule') -eq 'evenodd') { $evenOdd = $true } else { $nonZero = $true }
        $data.Add($d)
    }
    if ($evenOdd -and $nonZero) {
        Write-Warning "$name mixes fill rules; using evenodd (F0) for the whole icon."
    }
    $prefix = if ($evenOdd) { 'F0' } else { 'F1' }

    $key = 'Icon.' + (ConvertTo-PascalCase ([System.IO.Path]::GetFileNameWithoutExtension($name)))
    if ($seenKeys.ContainsKey($key)) { throw "$name : duplicate resource key $key" }
    $seenKeys[$key] = $true

    [void]$sb.AppendLine("    <Geometry x:Key=`"$key`" po:Freeze=`"True`">$prefix $($data -join ' ')</Geometry>")
}
[void]$sb.AppendLine('</ResourceDictionary>')

$outDir = Split-Path $OutputFile -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
# UTF-8 without BOM and LF line endings keep the output byte-identical across PowerShell editions.
$text = $sb.ToString() -replace "`r`n", "`n"
[System.IO.File]::WriteAllText($OutputFile, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Wrote $($names.Count) icons to $OutputFile"
