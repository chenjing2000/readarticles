param(
    [ValidateSet('Core', 'UI', 'Full')][string]$Suite = 'Core',
    [string]$ArticleDirectory
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test.Common.ps1')
. (Join-Path $PSScriptRoot 'Test.Media.ps1')
$workspace = Split-Path -Parent $PSScriptRoot
$mediaCopy = $null
$previousLibrary = $env:READARTICLES_TEST_LIBRARY
$previousArticle = $env:READARTICLES_TEST_ARTICLE
Push-Location $workspace
try {
    Invoke-TestCheck 'English-only PowerShell source and syntax' { Test-PowerShellSource (Split-Path -Parent $PSScriptRoot) }
    if ($ArticleDirectory) {
        $mediaCopy = New-TestDirectory
        $env:READARTICLES_TEST_LIBRARY = Prepare-ArticleFixture $ArticleDirectory $workspace $mediaCopy
    }
    Invoke-TestCheck 'Publishing safeguards' {
        Invoke-TestCommand 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'tools/Test-Publish.ps1')
    }
    if ($Suite -eq 'Full' -and $ArticleDirectory) {
        Invoke-TestCheck 'Complete article and real MP3 suite' { Invoke-TestCommand 'dotnet' @('run', '--project', 'ReadArticles.Tests', '-c', 'Release') }
        Invoke-TestCheck 'Current supplied article and vocabulary' { Invoke-TestCommand 'dotnet' @('run', '--project', 'ReadArticles.Tests', '-c', 'Release', '--', '--source-only') }
    }
    else {
        foreach ($category in @('--rendering-only', '--tree-only', '--editing-only')) {
            Invoke-TestCheck $category { Invoke-TestCommand 'dotnet' @('run', '--project', 'ReadArticles.Tests', '-c', 'Release', '--', $category) }
        }
        if ($Suite -ne 'Core') {
            Invoke-TestCheck 'Window icons and settings' { Invoke-TestCommand 'dotnet' @('run', '--project', 'ReadArticles.Tests', '-c', 'Release', '--', '--icons-only') }
            if ($ArticleDirectory) {
                Invoke-TestCheck 'Article window' { Invoke-TestCommand 'dotnet' @('run', '--project', 'ReadArticles.Tests', '-c', 'Release', '--', '--window-only') }
            }
            else { Skip-TestCheck 'Audio-dependent window and playback checks: supply -ArticleDirectory to enable them.' }
        }
    }
    Complete-TestRun
}
finally {
    $env:READARTICLES_TEST_LIBRARY = $previousLibrary
    $env:READARTICLES_TEST_ARTICLE = $previousArticle
    Remove-TestDirectory $mediaCopy
    Pop-Location
}
