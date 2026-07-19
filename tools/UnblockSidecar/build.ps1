$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

try {
    npm ci --ignore-scripts
    if ($LASTEXITCODE -ne 0) {
        throw "npm ci failed with exit code $LASTEXITCODE"
    }

    npm run build
    if ($LASTEXITCODE -ne 0) {
        throw "Sidecar packaging failed with exit code $LASTEXITCODE"
    }

    $requiredExecutables = @(
        (Join-Path $root 'dist\win-x64\Dopamine.UnblockSidecar.exe'),
        (Join-Path $root 'dist\win-arm64\Dopamine.UnblockSidecar.exe')
    )
    foreach ($executable in $requiredExecutables) {
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf) -or (Get-Item -LiteralPath $executable).Length -le 0) {
            throw "Sidecar executable was not produced: $executable"
        }
    }

    $licenseDirectory = Join-Path $root 'dist\licenses'
    New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'node_modules\@unblockneteasemusic\server\COPYING') -Destination $licenseDirectory -Force
    Copy-Item -LiteralPath (Join-Path $root 'node_modules\@unblockneteasemusic\server\COPYING.LESSER') -Destination $licenseDirectory -Force
    Copy-Item -LiteralPath (Join-Path $root 'package-lock.json') -Destination $licenseDirectory -Force
    Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $licenseDirectory -Force
}
finally {
    Pop-Location
}
