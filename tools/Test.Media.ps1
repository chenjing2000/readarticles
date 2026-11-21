function Copy-TestAudioFolder([string]$SourceRoot, [string]$TargetRoot, [string]$FolderName) {
    $source = Join-Path $SourceRoot $FolderName
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Required test audio folder is missing: $source"
    }
    $target = Join-Path $TargetRoot $FolderName
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Extension -in @('.mp3', '.wav') } |
        Copy-Item -Destination $target
}

function Assert-TestAudio([string]$AudioRoot, [string]$ArticlePath) {
    $vocabularyPath = [IO.Path]::ChangeExtension($ArticlePath, '.vocabulary.json')
    $vocabulary = Get-Content -LiteralPath $vocabularyPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $missing = [Collections.Generic.List[string]]::new()
    $stems = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($word in $vocabulary.words) {
        $stem = [regex]::Replace($word.word.Trim().ToLowerInvariant(), '[^a-z0-9]+', '_').Trim('_')
        if (-not $stem -or -not $stems.Add($stem)) { continue }
        foreach ($accent in @('uk', 'us')) {
            $relativeStem = "audio/$($stem)_$accent"
            if (-not ((Test-Path -LiteralPath (Join-Path $AudioRoot ($relativeStem + '.mp3')) -PathType Leaf) -or
                (Test-Path -LiteralPath (Join-Path $AudioRoot ($relativeStem + '.wav')) -PathType Leaf))) {
                $missing.Add($relativeStem)
            }
        }
    }
    if ($missing.Count) {
        throw "Test audio resource pool is incomplete. Missing $($missing.Count) file(s): $($missing -join ', ')"
    }
}

function Prepare-TestWorkspace([string]$Workspace, [string]$TemporaryDirectory) {
    $librarySource = Join-Path $Workspace 'tests/TestData/Library'
    $audioSource = Join-Path $Workspace 'tests/TestData/Audio'
    $library = Join-Path $TemporaryDirectory 'Library'
    Copy-Item -LiteralPath $librarySource -Destination $library -Recurse

    $articleName = 'When America walks away.json'
    $vocabularyName = 'When America walks away.vocabulary.json'
    $target = Join-Path $library 'The Economist/2026-09-26/When America walks away'
    $articlePath = Join-Path $target $articleName
    if (-not (Test-Path -LiteralPath $articlePath -PathType Leaf)) {
        throw "Main test article is missing: $articlePath"
    }
    Copy-TestAudioFolder $audioSource $target 'audio'
    Assert-TestAudio $target $articlePath

    # Remove legacy vocabulary audio declarations only from the isolated working copy.
    $vocabularyPath = Join-Path $target $vocabularyName
    $vocabulary = Get-Content -LiteralPath $vocabularyPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($word in $vocabulary.words) { $word.PSObject.Properties.Remove('audio') }
    [IO.File]::WriteAllText($vocabularyPath, ($vocabulary | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))

    $sourceBook = Join-Path $TemporaryDirectory 'Source/Book'
    New-Item -ItemType Directory -Path $sourceBook -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $sourceBook 'book.json'), '{}')
    Copy-Item -LiteralPath $articlePath -Destination (Join-Path $sourceBook $articleName)
    Copy-Item -LiteralPath $vocabularyPath -Destination (Join-Path $sourceBook $vocabularyName)
    Copy-TestAudioFolder $audioSource $sourceBook 'audio'

    $env:READARTICLES_TEST_LIBRARY = $library
    $env:READARTICLES_TEST_ARTICLE = Join-Path $sourceBook $articleName
    Write-Host 'Prepared isolated test workspace from tests/TestData; source resources were not modified.'
    return $library
}
