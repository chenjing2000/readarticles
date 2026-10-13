function Prepare-ArticleFixture([string]$SourceDirectory, [string]$Workspace, [string]$TemporaryDirectory) {
    $library = Join-Path $TemporaryDirectory 'Library'
    Copy-Item -LiteralPath (Join-Path $Workspace 'ReadArticles.Tests/Fixtures/Library') -Destination $library -Recurse
    $target = Join-Path $library 'The Economist/2026-09-26/When America walks away'
    $articleName = 'When America walks away.json'
    $vocabularyName = 'When America walks away.vocabulary.json'
    $article = Get-Content -LiteralPath (Join-Path $target $articleName) -Raw -Encoding UTF8 | ConvertFrom-Json
    $sourceArticle = Get-Content -LiteralPath (Join-Path $SourceDirectory $articleName) -Raw -Encoding UTF8 | ConvertFrom-Json
    $sourceBook = Join-Path $TemporaryDirectory 'Source/Book'
    New-Item -ItemType Directory -Path $sourceBook -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $sourceBook 'book.json'), '{}')
    # Normalize only the temporary copy; the original library is never written.
    foreach ($paragraph in $sourceArticle.paragraphs) {
        foreach ($segment in $paragraph.paragraph) {
            foreach ($accent in @('uk', 'us')) {
                if ($segment.audio.$accent -like 'audio/*') {
                    $segment.audio.$accent = $segment.audio.$accent.Replace('audio/', 'audio_segments/')
                }
            }
        }
    }
    $env:READARTICLES_TEST_ARTICLE = Join-Path $sourceBook $articleName
    [IO.File]::WriteAllText($env:READARTICLES_TEST_ARTICLE, ($sourceArticle | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $SourceDirectory $vocabularyName) -Destination (Join-Path $sourceBook $vocabularyName)
    $vocabulary = Get-Content -LiteralPath (Join-Path $target $vocabularyName) -Raw -Encoding UTF8 | ConvertFrom-Json
    $required = @()
    foreach ($paragraph in $article.paragraphs) {
        foreach ($segment in $paragraph.paragraph) {
            # The existing regression fixture deliberately lacks this sentence's audio.
            if ($segment.sid -eq 's011') { continue }
            $required += @($segment.audio.uk, $segment.audio.us)
        }
    }
    foreach ($word in $vocabulary.words) { $required += @($word.audio.uk, $word.audio.us) }
    foreach ($relativePath in ($required | Select-Object -Unique)) {
        if (-not $relativePath) { throw 'The supplied article has a missing audio declaration.' }
        $fullTarget = [IO.Path]::GetFullPath((Join-Path $target $relativePath))
        if (-not $fullTarget.StartsWith([IO.Path]::GetFullPath($target) + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'An audio declaration escapes the temporary article directory.'
        }
        $source = Join-Path $SourceDirectory $relativePath
        if (-not (Test-Path -LiteralPath $source) -and $relativePath -like 'audio_segments/*') {
            $source = Join-Path (Join-Path $SourceDirectory 'audio') ([IO.Path]::GetFileName($relativePath))
        }
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required test audio is missing: $relativePath" }
        New-Item -ItemType Directory -Path (Split-Path -Parent $fullTarget) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $fullTarget
    }
    foreach ($folder in @('audio', 'audio_segments', 'audio_vocabulary')) {
        $sourceFolder = Join-Path $SourceDirectory $folder
        if (Test-Path -LiteralPath $sourceFolder -PathType Container) {
            $targetFolder = Join-Path $sourceBook $(if ($folder -eq 'audio') { 'audio_segments' } else { $folder })
            New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
            Get-ChildItem -LiteralPath $sourceFolder -File -Filter '*.mp3' | Copy-Item -Destination $targetFolder
        }
    }
    Write-Host 'Prepared temporary article and MP3 fixtures; original files were not modified.'
    return $library
}
