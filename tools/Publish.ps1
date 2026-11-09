$ErrorActionPreference = 'Stop'
$projectPath = 'ReadArticles\ReadArticles.csproj'
$exeName = 'ReadArticles.exe'
$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$distDirectory = Join-Path $workspace 'dist'
$releaseDirectory = Join-Path $distDirectory 'ReadArticles'
$stagingDirectory = Join-Path $distDirectory ('.publish-' + [Guid]::NewGuid().ToString('N'))
$destination = Join-Path $releaseDirectory $exeName
$legacyUserData = Join-Path $distDirectory 'userdata'
$newUserData = Join-Path $releaseDirectory 'userdata'
$legacySettings = Join-Path $distDirectory 'ReadArticles.settings.json'
$newLegacySettings = Join-Path $releaseDirectory 'ReadArticles.settings.json'
$migrationTemporary = Join-Path $releaseDirectory ('.userdata-migrate-' + [Guid]::NewGuid().ToString('N'))

function Assert-NoReparsePoints([string]$path) {
    $items = @(Get-Item -LiteralPath $path -Force) + @(Get-ChildItem -LiteralPath $path -Force -Recurse)
    if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) {
        throw "Refusing to copy or clean a directory that contains a reparse point: $path"
    }
}

Push-Location $workspace
try {
    dotnet publish $projectPath -p:PublishProfile=Portable -o $stagingDirectory --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'Publishing failed; existing program and userdata were preserved.'
    }

    # A portable single-file release must not have loose dependencies or build artifacts.
    Get-ChildItem -LiteralPath $stagingDirectory -File | Where-Object {
        $_.Extension -in @('.xml', '.pdb')
    } | Remove-Item -Force
    $output = @(Get-ChildItem -LiteralPath $stagingDirectory -Force)
    if ($output.Count -ne 1 -or $output[0].Name -ne $exeName -or $output[0].PSIsContainer) {
        throw 'Expected exactly one EXE in staging; the installed program was preserved.'
    }

    if (Test-Path -LiteralPath $destination -PathType Leaf) {
        $handle = [IO.File]::Open($destination, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $handle.Dispose()
    }

    New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

    # One-time non-destructive migration from the earlier dist/userdata location.
    # Never overwrite an existing userdata directory in the new portable release.
    if ((Test-Path -LiteralPath $legacyUserData -PathType Container) -and
        -not (Test-Path -LiteralPath $newUserData)) {
        Assert-NoReparsePoints $legacyUserData
        Copy-Item -LiteralPath $legacyUserData -Destination $migrationTemporary -Recurse
        Assert-NoReparsePoints $migrationTemporary
        $sourceFiles = @(Get-ChildItem -LiteralPath $legacyUserData -File -Recurse)
        foreach ($file in $sourceFiles) {
            $relative = $file.FullName.Substring($legacyUserData.Length).TrimStart('\')
            $copied = Join-Path $migrationTemporary $relative
            if (-not (Test-Path -LiteralPath $copied -PathType Leaf) -or
                (Get-FileHash -LiteralPath $copied -Algorithm SHA256).Hash -ne
                (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
                throw "Userdata migration verification failed: $relative"
            }
        }
        [IO.Directory]::Move($migrationTemporary, $newUserData)
    }

    # Retain the old executable-sibling settings file, if it exists.
    if ((Test-Path -LiteralPath $legacySettings -PathType Leaf) -and
        -not (Test-Path -LiteralPath $newLegacySettings)) {
        Copy-Item -LiteralPath $legacySettings -Destination $newLegacySettings
        if ((Get-FileHash -LiteralPath $legacySettings -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $newLegacySettings -Algorithm SHA256).Hash) {
            Remove-Item -LiteralPath $newLegacySettings -Force
            throw 'Legacy settings migration verification failed.'
        }
    }

    $readmePath = Join-Path $releaseDirectory 'README.txt'
    if (-not (Test-Path -LiteralPath $readmePath)) {
        $readme = @(
            'ReadArticles portable release',
            'Run ReadArticles.exe from this directory.',
            'Place this directory where your Windows user has write permission.',
            'Requires .NET 10 Windows Desktop Runtime x64 (10.0.11 or later patch).',
            'Install the Desktop Runtime from https://dotnet.microsoft.com/download/dotnet/10.0 if missing.',
            'Keep userdata/ beside ReadArticles.exe when moving or upgrading.',
            'Your selected article library stays at its original external location.',
            'Do not remove userdata/ during upgrades.'
        ) -join [Environment]::NewLine
        [IO.File]::WriteAllText($readmePath, $readme + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    }

    # The previous executable is retained for manual recovery, never touched userdata.
    if (Test-Path -LiteralPath $destination -PathType Leaf) {
        $previous = Join-Path $releaseDirectory ('ReadArticles.exe.previous-' + [Guid]::NewGuid().ToString('N'))
        [IO.File]::Replace($output[0].FullName, $destination, $previous)
    }
    else {
        [IO.File]::Move($output[0].FullName, $destination)
    }

    Write-Host "Published: $destination"
}
finally {
    $resolvedStaging = [IO.Path]::GetFullPath($stagingDirectory)
    $expectedRoot = [IO.Path]::GetFullPath($distDirectory).TrimEnd('\') + '\'
    if (-not $resolvedStaging.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Staging directory escaped dist; cleanup refused.'
    }
    try {
        if (Test-Path -LiteralPath $resolvedStaging) {
            Assert-NoReparsePoints $resolvedStaging
            Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
        }
        if (Test-Path -LiteralPath $migrationTemporary) {
            Assert-NoReparsePoints $migrationTemporary
            Remove-Item -LiteralPath $migrationTemporary -Recurse -Force
        }
    }
    finally { Pop-Location }
}
