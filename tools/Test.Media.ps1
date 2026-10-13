function Prepare-ArticleFixture([string]$SourceDirectory, [string]$Workspace, [string]$TemporaryDirectory) {
    $library = Join-Path $TemporaryDirectory 'Library'
    Copy-Item -LiteralPath (Join-Path $Workspace 'ReadArticles.Tests/Fixtures/Library') -Destination $library -Recurse
    $target = Join-Path $library 'The Economist/2026-09-26/When America walks away'
    $articleName = 'When America walks away.json'
    $vocabularyName = 'When America walks away.vocabulary.json'
    $article = Get-Content -LiteralPath (Join-Path $target $articleName) -Raw -Encoding UTF8 | ConvertFrom-Json
    $vocabulary = Get-Content -LiteralPath (Join-Path $target $vocabularyName) -Raw -Encoding UTF8 | ConvertFrom-Json
    # Strip obsolete declarations only in the test copy.
    foreach ($paragraph in $article.paragraphs) {
        foreach ($segment in $paragraph.paragraph) { $segment.PSObject.Properties.Remove('audio') }
    }
    foreach ($word in $vocabulary.words) { $word.PSObject.Properties.Remove('audio') }
    [IO.File]::WriteAllText((Join-Path $target $articleName), ($article | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $target $vocabularyName), ($vocabulary | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    $required = @()
    foreach ($paragraph in $article.paragraphs) {
        foreach ($segment in $paragraph.paragraph) {
            if ($segment.sid -eq 's011') { continue }
            foreach ($accent in @('uk', 'us')) { $required += "audio_sentences/$($segment.sid)_$accent" }
        }
    }
    $stems = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($word in $vocabulary.words) {
        $stem = [regex]::Replace($word.word.Trim().ToLowerInvariant(), '[^a-z0-9]+', '_').Trim('_')
        if ($stem -and $stems.Add($stem)) {
            foreach ($accent in @('uk', 'us')) { $required += "audio/$($stem)_$accent" }
        }
    }
    foreach ($relativeStem in $required) {
        $found = $false
        foreach ($extension in @('.mp3', '.wav')) {
            $relativePath = $relativeStem + $extension
            $source = Join-Path $SourceDirectory $relativePath
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
            $fullTarget = Join-Path $target $relativePath
            New-Item -ItemType Directory -Path (Split-Path -Parent $fullTarget) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $fullTarget
            $found = $true
        }
        if (-not $found) { throw "Required MP3/WAV test audio is missing: $relativeStem" }
    }
    $sourceBook = Join-Path $TemporaryDirectory 'Source/Book'
    New-Item -ItemType Directory -Path $sourceBook -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $sourceBook 'book.json'), '{}')
    foreach ($name in @($articleName, $vocabularyName)) { Copy-Item -LiteralPath (Join-Path $SourceDirectory $name) -Destination (Join-Path $sourceBook $name) }
    foreach ($folder in @('audio', 'audio_sentences')) {
        $sourceFolder = Join-Path $SourceDirectory $folder
        if (Test-Path -LiteralPath $sourceFolder -PathType Container) {
            $targetFolder = Join-Path $sourceBook $folder
            New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
            Get-ChildItem -LiteralPath $sourceFolder -File | Where-Object { $_.Extension -in @('.mp3', '.wav') } | Copy-Item -Destination $targetFolder
        }
    }
    $env:READARTICLES_TEST_ARTICLE = Join-Path $sourceBook $articleName
    Write-Host 'Prepared temporary article and MP3/WAV fixtures; original files were not modified.'
    return $library
}
