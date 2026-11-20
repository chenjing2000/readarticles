$ErrorActionPreference = 'Stop'
$projectPath = 'ReadArticles\ReadArticles.csproj'
$exeName = 'ReadArticles.exe'
$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$distDirectory = Join-Path $workspace 'dist'
$releaseDirectory = Join-Path $distDirectory 'ReadArticles'
$stagingDirectory = Join-Path $distDirectory ('.publish-' + [Guid]::NewGuid().ToString('N'))
$destination = Join-Path $releaseDirectory $exeName
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
    }
    finally { Pop-Location }
}
