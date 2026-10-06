param(
    [string]$SourceRevision
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    throw '.NET Framework C# compiler was not found.'
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Dopamine-Logging-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $sources = @(
        'Dopamine.Core/Logging/AppLog.cs'
        'Dopamine.Core/Settings/LoggingSettings.cs'
        'Dopamine.Core/Settings/SettingDefaults.cs'
    )
    $compileSources = foreach ($source in $sources) {
        if ($SourceRevision) {
            $text = & git -C $repoRoot show "${SourceRevision}:$source"
            if ($LASTEXITCODE -ne 0) { throw "Cannot read baseline source: $source" }
            $destination = Join-Path $testRoot ([IO.Path]::GetFileName($source))
            [IO.File]::WriteAllText($destination, ($text -join "`n"))
            $destination
        }
        else {
            Join-Path $repoRoot $source
        }
    }
    $foundation = Join-Path $repoRoot 'Dopamine\Libraries\Digimezzo.Foundation.Core.dll'
    Copy-Item -LiteralPath $foundation -Destination $testRoot
    $executable = Join-Path $testRoot 'LoggingRegression.exe'
    & $compilerPath /nologo /target:exe "/out:$executable" "/reference:$foundation" `
        /reference:System.Xml.Linq.dll $compileSources (Join-Path $PSScriptRoot 'LoggingRegression.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Logging regression harness compilation failed.' }

    [IO.File]::WriteAllText((Join-Path $testRoot 'BaseSettings.xml'), @'
<Settings>
  <Namespace Name="Configuration">
    <Setting Name="Version"><Value>1</Value></Setting>
    <Setting Name="IsPortable"><Value>True</Value></Setting>
  </Namespace>
  <Namespace Name="Appearance">
    <Setting Name="EnableLogging"><Value>False</Value></Setting>
    <Setting Name="OtherSetting"><Value>False</Value></Setting>
  </Namespace>
</Settings>
'@)

    foreach ($mode in @('disabled', 'toggle', 'restart')) {
        & $executable $mode
        if ($LASTEXITCODE -ne 0) { throw "Logging regression failed: $mode" }
    }

    Remove-Item -LiteralPath (Join-Path $testRoot 'BaseSettings.xml')
    # A new process must tolerate unavailable settings without leaking an exception.
    # Use a separate sandbox so the previous scenarios' logs cannot mask creation.
    $unavailableRoot = Join-Path $testRoot 'Unavailable'
    New-Item -ItemType Directory -Path $unavailableRoot | Out-Null
    Copy-Item -LiteralPath $executable,$foundation -Destination $unavailableRoot
    & (Join-Path $unavailableRoot 'LoggingRegression.exe') 'settings-unavailable'
    if ($LASTEXITCODE -ne 0) { throw 'Logging regression failed: settings-unavailable' }
}
finally {
    # Delete only the verified, uniquely created sandbox under the temp directory.
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedRoot) -notlike 'Dopamine-Logging-*') {
        throw "Refusing to remove an unexpected test directory: $resolvedRoot"
    }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
