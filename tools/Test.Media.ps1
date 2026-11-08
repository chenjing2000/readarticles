function New-TestWaveFile([string]$Path, [int]$DurationMilliseconds) {
    $sampleRate = 22050
    $sampleCount = [int][Math]::Ceiling($sampleRate * $DurationMilliseconds / 1000.0)
    $dataLength = $sampleCount * 2
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $writer = $null
    try {
        $writer = [IO.BinaryWriter]::new($stream)
        $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
        $writer.Write([int](36 + $dataLength))
        $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVE'))
        $writer.Write([Text.Encoding]::ASCII.GetBytes('fmt '))
        $writer.Write([int]16)
        $writer.Write([System.Int16]1)
        $writer.Write([System.Int16]1)
        $writer.Write([int]$sampleRate)
        $writer.Write([int]($sampleRate * 2))
        $writer.Write([System.Int16]2)
        $writer.Write([System.Int16]16)
        $writer.Write([Text.Encoding]::ASCII.GetBytes('data'))
        $writer.Write([int]$dataLength)
        for ($i = 0; $i -lt $sampleCount; $i++) {
            $sample = [int]([Math]::Sin(2.0 * [Math]::PI * 440.0 * $i / $sampleRate) * 1800)
            $writer.Write([System.Int16]$sample)
        }
        $writer.Flush()
    }
    finally {
        if ($writer) { $writer.Dispose() } else { $stream.Dispose() }
    }
}

function Get-TestVocabularyStem([string]$Word) {
    return [regex]::Replace($Word.Trim().ToLowerInvariant(), '[^a-z0-9]+', '_').Trim('_')
}

function New-TestAudioPool([string]$AudioRoot, [string]$ArticlePath) {
    $article = Get-Content -LiteralPath $ArticlePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $vocabularyPath = [IO.Path]::ChangeExtension($ArticlePath, '.vocabulary.json')
    if (-not (Test-Path -LiteralPath $vocabularyPath -PathType Leaf)) {
        throw "Main test vocabulary is missing: $vocabularyPath"
    }
    $vocabulary = Get-Content -LiteralPath $vocabularyPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $sentenceTemplate = Join-Path $AudioRoot 'sentence-template.wav'
    $wordTemplate = Join-Path $AudioRoot 'word-template.wav'
    New-TestWaveFile $sentenceTemplate 5000
    New-TestWaveFile $wordTemplate 2000
    $stems = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($paragraph in $article.paragraphs) {
        foreach ($segment in $paragraph.paragraph) {
            if ($segment.sid -eq 's011') { continue }
            foreach ($accent in @('uk', 'us')) {
                $destination = Join-Path $AudioRoot "audio_sentences/$($segment.sid)_$accent.wav"
                New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
                Copy-Item -LiteralPath $sentenceTemplate -Destination $destination
            }
        }
    }
    foreach ($word in $vocabulary.words) {
        $stem = Get-TestVocabularyStem $word.word
        if (-not $stem -or -not $stems.Add($stem)) { continue }
        foreach ($accent in @('uk', 'us')) {
            $destination = Join-Path $AudioRoot "audio/$($stem)_$accent.wav"
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath $wordTemplate -Destination $destination
        }
    }
    Remove-Item -LiteralPath $sentenceTemplate, $wordTemplate -Force
    $sentenceFiles = @(Get-ChildItem -LiteralPath (Join-Path $AudioRoot 'audio_sentences') -File -Filter '*.wav')
    $wordFiles = @(Get-ChildItem -LiteralPath (Join-Path $AudioRoot 'audio') -File -Filter '*.wav')
    if ($sentenceFiles.Count -ne 122 -or $wordFiles.Count -ne 240) {
        throw "Unexpected generated audio pool: $($sentenceFiles.Count) sentence WAV files, $($wordFiles.Count) vocabulary WAV files. Expected 122 and 240."
    }
    Write-Host "Generated shared WAV pool: $($sentenceFiles.Count) sentence files (5 s), $($wordFiles.Count) vocabulary files (2 s); s011 intentionally absent."
}

function Copy-TestAudioFolder([string]$SourceRoot, [string]$TargetRoot, [string]$FolderName) {
    $source = Join-Path $SourceRoot $FolderName
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Generated test audio folder is missing: $source"
    }
    $target = Join-Path $TargetRoot $FolderName
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Get-ChildItem -LiteralPath $source -File -Filter '*.wav' | Copy-Item -Destination $target
}

function Prepare-TestWorkspace([string]$Workspace, [string]$TemporaryDirectory) {
    $librarySource = Join-Path $Workspace 'tests/TestData/Library'
    if (-not (Test-Path -LiteralPath $librarySource -PathType Container)) {
        throw "Unified test library is missing: $librarySource"
    }
    $library = Join-Path $TemporaryDirectory 'Library'
    Copy-Item -LiteralPath $librarySource -Destination $library -Recurse

    $articleName = 'When America walks away.json'
    $vocabularyName = 'When America walks away.vocabulary.json'
    $target = Join-Path $library 'The Economist/2026-09-26/When America walks away'
    $articlePath = Join-Path $target $articleName
    if (-not (Test-Path -LiteralPath $articlePath -PathType Leaf)) {
        throw "Main test article is missing: $articlePath"
    }

    $audioPool = Join-Path $TemporaryDirectory 'Audio'
    New-TestAudioPool $audioPool $articlePath
    foreach ($folder in @('audio', 'audio_sentences')) {
        Copy-TestAudioFolder $audioPool $target $folder
    }

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
        Copy-TestAudioFolder $audioPool $sourceBook $folder
    }

    $env:READARTICLES_TEST_LIBRARY = $library
    $env:READARTICLES_TEST_ARTICLE = Join-Path $sourceBook $articleName
    Write-Host 'Prepared isolated test workspace from tests/TestData; source resources were not modified.'
    return $library
}
