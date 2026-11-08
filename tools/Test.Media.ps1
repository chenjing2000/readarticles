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
    $article = Get-Content -LiteralPath $ArticlePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $vocabularyPath = [IO.Path]::ChangeExtension($ArticlePath, '.vocabulary.json')
    $vocabulary = Get-Content -LiteralPath $vocabularyPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $missing = [Collections.Generic.List[string]]::new()
    foreach ($paragraph in $article.paragraphs) {
        foreach ($segment in $paragraph.paragraph) {
            if ($segment.sid -eq 's011') { continue }
            foreach ($accent in @('uk', 'us')) {
                $stem = "audio_sentences/$($segment.sid)_$accent"
                if (-not ((Test-Path -LiteralPath (Join-Path $AudioRoot ($stem + '.mp3')) -PathType Leaf) -or
                    (Test-Path -LiteralPath (Join-Path $AudioRoot ($stem + '.wav')) -PathType Leaf))) {
                    $missing.Add($stem)
                }
            }
        }
    }
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
    foreach ($accent in @('uk', 'us')) {
        foreach ($extension in @('.mp3', '.wav')) {
            $unexpected = Join-Path $AudioRoot ("audio_sentences/s011_$accent" + $extension)
            if (Test-Path -LiteralPath $unexpected -PathType Leaf) {
                throw "The s011 $accent recording must remain absent in the source audio pool: $unexpected"
            }
        }
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
    foreach ($folder in @('audio', 'audio_sentences')) {
        Copy-TestAudioFolder $audioSource $target $folder
    }
    Assert-TestAudio $target $articlePath

    # Remove obsolete audio declarations only from the isolated working copy.
    $article = Get-Content -LiteralPath $articlePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $vocabularyPath = Join-Path $target $vocabularyName
    $vocabulary = Get-Content -LiteralPath $vocabularyPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($paragraph in $article.paragraphs) {
        foreach ($segment in $paragraph.paragraph) { $segment.PSObject.Properties.Remove('audio') }
    }
    foreach ($word in $vocabulary.words) { $word.PSObject.Properties.Remove('audio') }
    [IO.File]::WriteAllText($articlePath, ($article | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($vocabularyPath, ($vocabulary | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))

    $sourceBook = Join-Path $TemporaryDirectory 'Source/Book'
    New-Item -ItemType Directory -Path $sourceBook -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $sourceBook 'book.json'), '{}')
    Copy-Item -LiteralPath $articlePath -Destination (Join-Path $sourceBook $articleName)
    Copy-Item -LiteralPath $vocabularyPath -Destination (Join-Path $sourceBook $vocabularyName)
    foreach ($folder in @('audio', 'audio_sentences')) {
        Copy-TestAudioFolder $audioSource $sourceBook $folder
    }

    $env:READARTICLES_TEST_LIBRARY = $library
    $env:READARTICLES_TEST_ARTICLE = Join-Path $sourceBook $articleName
    Write-Host 'Prepared isolated test workspace from tests/TestData; source resources were not modified.'
    return $library
}
