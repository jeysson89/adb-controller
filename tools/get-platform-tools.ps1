<#
.SYNOPSIS
    Готовит папку tools\platform-tools с бинарниками adb для встраивания в сборку.

.DESCRIPTION
    Порядок действий:
      1. Ищет уже установленный platform-tools (PATH, ANDROID_HOME, типовые места)
         и копирует из него adb.exe и нужные DLL.
      2. Если ничего не нашлось — скачивает официальный архив platform-tools
         с dl.google.com и распаковывает нужные файлы.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\get-platform-tools.ps1
#>

[CmdletBinding()]
param(
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot в значении по умолчанию может быть пустым, поэтому путь
# вычисляем в теле скрипта.
if (-not $OutputDir) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $OutputDir = Join-Path $scriptDir 'platform-tools'
}

# Файлы, которые нужны для работы adb на Windows
$RequiredFiles = @(
    'adb.exe',
    'AdbWinApi.dll',
    'AdbWinUsbApi.dll',
    'libwinpthread-1.dll',
    'source.properties',
    'NOTICE.txt'
)

function Find-InstalledPlatformTools {
    $candidates = New-Object System.Collections.Generic.List[string]

    foreach ($envName in @('ANDROID_HOME', 'ANDROID_SDK_ROOT', 'ANDROID_SDK_HOME')) {
        $root = [Environment]::GetEnvironmentVariable($envName)
        if ($root) { $candidates.Add((Join-Path $root 'platform-tools')) }
    }

    $candidates.Add((Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools'))
    $candidates.Add('C:\platform-tools')
    $candidates.Add('C:\adb')
    $candidates.Add('C:\Android\platform-tools')

    foreach ($dir in ($env:PATH -split [IO.Path]::PathSeparator)) {
        if ($dir) { $candidates.Add($dir.Trim()) }
    }

    foreach ($dir in $candidates) {
        if ($dir -and (Test-Path (Join-Path $dir 'adb.exe'))) { return $dir }
    }
    return $null
}

function Copy-PlatformTools {
    param([string]$Source, [string]$Dest)

    Write-Host "Копирование из $Source" -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $Dest | Out-Null

    foreach ($name in $RequiredFiles) {
        $src = Join-Path $Source $name
        if (Test-Path $src) {
            Copy-Item $src (Join-Path $Dest $name) -Force
        }
        elseif ($name -eq 'NOTICE.txt') {
            Write-Host "  пропущен $name (нет в исходнике)" -ForegroundColor DarkGray
        }
    }
}

function Download-PlatformTools {
    param([string]$Dest)

    $url = 'https://dl.google.com/android/repository/platform-tools-latest-windows.zip'
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("platform-tools-" + [Guid]::NewGuid().ToString('N'))

    Write-Host "Скачивание $url" -ForegroundColor Cyan
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $url -OutFile "$tmp.zip" -UseBasicParsing

        Expand-Archive -Path "$tmp.zip" -DestinationPath $tmp -Force

        $source = Join-Path $tmp 'platform-tools'
        if (-not (Test-Path (Join-Path $source 'adb.exe'))) {
            throw "В архиве не найден adb.exe"
        }

        Copy-PlatformTools -Source $source -Dest $Dest
    }
    finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item "$tmp.zip" -Force -ErrorAction SilentlyContinue
    }
}

Write-Host '=== Подготовка platform-tools ===' -ForegroundColor Green

$existing = Find-InstalledPlatformTools
if ($existing) {
    Write-Host "Найден установленный platform-tools: $existing" -ForegroundColor Green
    Copy-PlatformTools -Source $existing -Dest $OutputDir
}
else {
    Write-Host 'Установленный platform-tools не найден, качаю официальный архив...' -ForegroundColor Yellow
    Download-PlatformTools -Dest $OutputDir
}

if (-not (Test-Path (Join-Path $OutputDir 'adb.exe'))) {
    Write-Error "adb.exe не появился в папке $OutputDir"
    exit 1
}

Write-Host ''
Write-Host "Готово: $OutputDir" -ForegroundColor Green
Get-ChildItem $OutputDir | Select-Object Name, Length | Format-Table -AutoSize
