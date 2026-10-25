#!/usr/bin/env pwsh
# Verifies local markdown links and heading anchors.
# Usage: pwsh scripts/check-docs.ps1 [-ShowOk]
[CmdletBinding()]
param([switch]$ShowOk)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-GitHubSlug {
    param([string]$Heading)

    $text = $Heading
    $text = [regex]::Replace($text, '!\[([^\]]*)\]\([^)]*\)', '$1')
    $text = [regex]::Replace($text, '\[([^\]]*)\]\([^)]*\)', '$1')
    # Strip real HTML tags, but not generic-looking text such as `IEngine<TLayer>`.
    $text = [regex]::Replace(
        $text,
        '</?(?:a|b|i|em|strong|code|br|sub|sup|span|img|div|p)\b[^>]*>',
        '')
    $text = $text.ToLowerInvariant()
    $text = $text.Replace('`', '')
    $text = [regex]::Replace($text, '[^\p{L}\p{Nd}\s\-_]', '')
    $text = [regex]::Replace($text, '\s+', '-')
    return $text.Trim('-')
}

function Get-MarkdownAnchors {
    param([string]$Path)

    $anchors = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $seen = @{}
    $inFence = $false
    $fenceMarker = ''

    foreach ($line in [System.IO.File]::ReadLines($Path)) {
        $trimmed = $line.TrimStart()
        if ($trimmed.StartsWith('```') -or $trimmed.StartsWith('~~~')) {
            $marker = $trimmed.Substring(0, 3)
            if (-not $inFence) {
                $inFence = $true
                $fenceMarker = $marker
            }
            elseif ($marker -eq $fenceMarker) {
                $inFence = $false
                $fenceMarker = ''
            }

            continue
        }

        if ($inFence) {
            continue
        }

        if ($line -match '^#{1,6}\s+(?<heading>.+?)\s*$') {
            $slug = Get-GitHubSlug $Matches['heading']
            if ([string]::IsNullOrEmpty($slug)) {
                continue
            }

            $count = 0
            if ($seen.ContainsKey($slug)) {
                $count = ++$seen[$slug]
                $slug = "$slug-$count"
            }
            else {
                $seen[$slug] = 0
            }

            [void]$anchors.Add($slug)
        }
    }

    return $anchors
}

$files = Get-ChildItem -LiteralPath $repoRoot -Recurse -Filter '*.md' -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.git)[\\/]' } |
    Sort-Object FullName

$anchorCache = @{}
$errors = [System.Collections.Generic.List[string]]::new()
$linkCount = 0

foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($repoRoot, $file.FullName)
    $lineNumber = 0
    $inFence = $false
    $fenceMarker = ''

    foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
        $lineNumber++
        $trimmed = $line.TrimStart()
        if ($trimmed.StartsWith('```') -or $trimmed.StartsWith('~~~')) {
            $marker = $trimmed.Substring(0, 3)
            if (-not $inFence) {
                $inFence = $true
                $fenceMarker = $marker
            }
            elseif ($marker -eq $fenceMarker) {
                $inFence = $false
                $fenceMarker = ''
            }

            continue
        }

        if ($inFence) {
            continue
        }

        $scanLine = [regex]::Replace($line, '`[^`]*`', '')

        foreach ($match in [regex]::Matches($scanLine, '!?\[[^\]]*\]\(\s*(?:<(?<angle>[^>]*)>|(?<bare>[^)\s]+))')) {
            $rawTarget = if ($match.Groups['angle'].Success) {
                $match.Groups['angle'].Value
            }
            else {
                $match.Groups['bare'].Value
            }

            if ([string]::IsNullOrWhiteSpace($rawTarget)) {
                continue
            }

            if ($rawTarget -match '^(https?:|mailto:|tel:|data:)') {
                continue
            }

            $linkCount++
            $target = [System.Uri]::UnescapeDataString($rawTarget)
            $hashIndex = $target.IndexOf('#')
            $pathPart = if ($hashIndex -ge 0) { $target.Substring(0, $hashIndex) } else { $target }
            $anchorPart = if ($hashIndex -ge 0) { $target.Substring($hashIndex + 1) } else { '' }

            $targetFile = $file.FullName
            if (-not [string]::IsNullOrEmpty($pathPart)) {
                $queryIndex = $pathPart.IndexOf('?')
                if ($queryIndex -ge 0) {
                    $pathPart = $pathPart.Substring(0, $queryIndex)
                }

                $targetFile = [System.IO.Path]::GetFullPath(
                    [System.IO.Path]::Combine($file.DirectoryName, $pathPart))

                if (-not (Test-Path -LiteralPath $targetFile)) {
                    $errors.Add("${relative}:${lineNumber}: missing target '$rawTarget'")
                    continue
                }

                if (Test-Path -LiteralPath $targetFile -PathType Container) {
                    continue
                }
            }

            if ([string]::IsNullOrEmpty($anchorPart)) {
                continue
            }

            if (-not $anchorCache.ContainsKey($targetFile)) {
                $anchorCache[$targetFile] = Get-MarkdownAnchors -Path $targetFile
            }

            $anchors = $anchorCache[$targetFile]
            if (-not $anchors.Contains($anchorPart)) {
                $targetRelative = [System.IO.Path]::GetRelativePath($repoRoot, $targetFile)
                $errors.Add("${relative}:${lineNumber}: missing anchor '#$anchorPart' in $targetRelative")
            }
            elseif ($ShowOk) {
                Write-Host "OK $relative -> ${targetRelative}#$anchorPart"
            }
        }
    }
}

if ($errors.Count -gt 0) {
    Write-Host "Documentation link check failed ($($errors.Count) problems):"
    foreach ($errorItem in $errors) {
        Write-Host "  $errorItem"
    }

    exit 1
}

Write-Host "Documentation link check passed: $($files.Count) files, $linkCount local links."
exit 0
