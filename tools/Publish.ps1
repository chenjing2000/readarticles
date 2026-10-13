$ErrorActionPreference = 'Stop'
$projectPath = 'ReadArticles\ReadArticles.csproj'
$exeName = 'ReadArticles.exe'
$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$publishDirectory = Join-Path $workspace 'dist'
$stagingDirectory = Join-Path $publishDirectory ('.publish-' + [Guid]::NewGuid().ToString('N'))
$destination = Join-Path $publishDirectory $exeName

Push-Location $workspace
try {
    dotnet publish $projectPath -p:PublishProfile=Portable -o $stagingDirectory --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'Publishing failed; the existing EXE and configuration have been preserved.'
    }

    # Debug symbols and XML documentation are not runtime dependencies; retain every DLL for validation.
    Get-ChildItem -LiteralPath $stagingDirectory -File | Where-Object { $_.Extension -in @('.xml', '.pdb') } | Remove-Item -Force
    $output = @(Get-ChildItem -LiteralPath $stagingDirectory -Force)
    if ($output.Count -ne 1 -or $output[0].Name -ne $exeName -or $output[0].PSIsContainer) {
        throw 'Expected a single EXE; the existing release has been preserved.'
    }

    # Fail before replacement if the current executable is in use.
    if (Test-Path -LiteralPath $destination) {
        $handle = [IO.File]::Open($destination, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $handle.Dispose()
    }

    # Same-volume replacement leaves the previous EXE intact when replacement fails.
    if (Test-Path -LiteralPath $destination) {
        [IO.File]::Replace($output[0].FullName, $destination, [System.Management.Automation.Language.NullString]::Value)
    }
    else {
        [IO.File]::Move($output[0].FullName, $destination)
    }
    Write-Host "Published: $destination"
}
finally {
    $resolvedStaging = [IO.Path]::GetFullPath($stagingDirectory)
    $expectedRoot = [IO.Path]::GetFullPath($publishDirectory).TrimEnd('\') + '\'
    if (-not $resolvedStaging.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Staging directory is outside dist; cleanup refused.'
    }
    try {
        if (Test-Path -LiteralPath $resolvedStaging) {
            $items = @(Get-Item -LiteralPath $resolvedStaging) + @(Get-ChildItem -LiteralPath $resolvedStaging -Force -Recurse)
            if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) {
                throw 'Staging directory contains a reparse point; cleanup refused.'
            }
            Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
        }
    }
    finally { Pop-Location }
}
