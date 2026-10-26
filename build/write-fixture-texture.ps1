#!/usr/bin/env pwsh
# Writes the deterministic 2x2 RGBA PNG used by the Bork2d.BakeFixture asset tree.
#
# The bytes are embedded as base64 text so no binary fixture is committed. The script is idempotent:
# when the target already holds exactly these bytes it writes nothing, so the prepared fixture copy
# keeps stable timestamps and the bake's content hashes stay unchanged between builds.
#
# Decoded pixels (row-major, top-left origin, straight alpha, RGBA8):
#   (0,0) = 255,   0,   0, 255   red, opaque
#   (1,0) =   0, 255,   0, 128   green, half transparent
#   (0,1) =   0,   0, 255,  64   blue, quarter transparent
#   (1,1) = 255, 255, 255,   0   white, fully transparent
#
# Those are exactly the pixels `TextureImageFactory.RgbaPng()` generates for the bake tests, so a
# baked fixture pack has the documented `width x height x 4` payload
# `[255,0,0,255, 0,255,0,128, 0,0,255,64, 255,255,255,0]`.
#
# Usage: pwsh build/write-fixture-texture.ps1 -Output <path-to-player.png>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Output
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# A 2x2 8-bit RGBA PNG: signature, IHDR (color type 6), one zlib IDAT, and IEND.
$pngBase64 = 'iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAF0lEQVR42mP4z8DwHwgbGIC0w////xkAQBgHul5CkSMAAAAASUVORK5CYII='

$bytes = [System.Convert]::FromBase64String($pngBase64)
$fullPath = [System.IO.Path]::GetFullPath($Output)
$directory = [System.IO.Path]::GetDirectoryName($fullPath)
if (-not [string]::IsNullOrEmpty($directory)) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}

if ([System.IO.File]::Exists($fullPath)) {
    $existing = [System.IO.File]::ReadAllBytes($fullPath)
    $identical = $existing.Length -eq $bytes.Length
    if ($identical) {
        for ($index = 0; $index -lt $bytes.Length; $index++) {
            if ($existing[$index] -ne $bytes[$index]) {
                $identical = $false
                break
            }
        }
    }

    if ($identical) {
        Write-Verbose "Bork2d fixture texture is up to date: $fullPath"
        exit 0
    }
}

[System.IO.File]::WriteAllBytes($fullPath, $bytes)
Write-Verbose "Wrote the Bork2d fixture texture: $fullPath"
exit 0
