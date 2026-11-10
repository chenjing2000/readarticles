param(
    [ValidateSet('Core', 'UI', 'Full')][string]$Suite = 'Core'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test.Common.ps1')
. (Join-Path $PSScriptRoot 'Test.Media.ps1')
$workspace = Split-Path -Parent $PSScriptRoot
$testWorkspace = $null
$previousLibrary = $env:READARTICLES_TEST_LIBRARY
$previousArticle = $env:READARTICLES_TEST_ARTICLE
$startedAt = [DateTimeOffset]::Now
$stamp = $startedAt.ToString('yyyyMMdd-HHmmss')
$reportDirectory = Join-Path $workspace 'artifacts/reports'
$logDirectory = Join-Path $workspace 'artifacts/logs'
New-Item -ItemType Directory -Path $reportDirectory, $logDirectory -Force | Out-Null
$reportPath = Join-Path $reportDirectory "$Suite-$stamp.md"
$logPath = Join-Path $logDirectory "$Suite-$stamp.log"
$transcriptStarted = $false
Push-Location $workspace
try {
    Start-Transcript -LiteralPath $logPath -Force | Out-Null
    $transcriptStarted = $true
    Invoke-TestCheck 'English-only PowerShell source and syntax' { Test-PowerShellSource $workspace }

    Write-Host 'RUN Unified test data integrity and isolated workspace'
    $testWorkspace = New-TestDirectory
    try {
        Prepare-TestWorkspace $workspace $testWorkspace | Out-Null
        $script:passedChecks++
        Write-Host 'PASS Unified test data integrity and isolated workspace'
    }
    catch {
        $script:failedChecks++
        Write-Host "FAIL Unified test data integrity and isolated workspace: $_"
        throw
    }

    Invoke-TestCheck 'Publishing safeguards' {
        Invoke-TestCommand 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'tools/Test-Publish.ps1')
    }

    Invoke-TestCheck 'Runtime logging safeguards' {
        Invoke-TestCommand 'dotnet' @('run', '--project', 'ReadArticles.Tests', '-c', 'Release', '--', '--logging-only')
    }

    if ($Suite -eq 'Full') {
        Invoke-TestCheck 'Complete application regression suite' {
            Invoke-TestCommand 'dotnet' @('run', '--project', 'ReadArticles.Tests', '-c', 'Release')
        }
        Invoke-TestCheck 'Unified test article and vocabulary remain unchanged' {
            Invoke-TestCommand 'dotnet' @('run', '--project', 'ReadArticles.Tests', '-c', 'Release', '--', '--source-only')
        }
    }
    else {
        foreach ($category in @('--content-only', '--rendering-only', '--tree-only', '--editing-only', '--notifications-only', '--lazy-tree-only')) {
            Invoke-TestCheck $category { Invoke-TestCommand 'dotnet' @('run', '--project', 'ReadArticles.Tests', '-c', 'Release', '--', $category) }
        }
        if ($Suite -eq 'UI') {
            foreach ($category in @('--icons-only', '--window-only', '--controls-only')) {
                Invoke-TestCheck $category { Invoke-TestCommand 'dotnet' @('run', '--project', 'ReadArticles.Tests', '-c', 'Release', '--', $category) }
            }
        }
    }

    Complete-TestRun
}
finally {
    $env:READARTICLES_TEST_LIBRARY = $previousLibrary
    $env:READARTICLES_TEST_ARTICLE = $previousArticle
    Remove-TestDirectory $testWorkspace
    $gitCommit = (& git rev-parse HEAD 2>$null | Select-Object -First 1)
    if (-not $gitCommit) { $gitCommit = 'unavailable' }
    $dotnetVersion = (& dotnet --version 2>$null | Select-Object -First 1)
    if (-not $dotnetVersion) { $dotnetVersion = 'unavailable' }
    $report = @(
        '# Test report', '', "- Suite: $Suite",
        "- Started: $($startedAt.ToString('yyyy-MM-dd HH:mm:ss zzz'))",
        "- Git commit: $gitCommit", "- .NET SDK: $dotnetVersion",
        "- Passed groups: $script:passedChecks", "- Failed groups: $script:failedChecks",
        "- Skipped groups: $script:skippedChecks", "- Log: ../logs/$Suite-$stamp.log", '',
        'The test resource source is tests/TestData. Execution uses an isolated temporary workspace.'
    ) -join [Environment]::NewLine
    [IO.File]::WriteAllText($reportPath, $report + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Host "Report: $reportPath"
    if ($transcriptStarted) { Stop-Transcript | Out-Null }
    Pop-Location
}

# Do not leak a stale native-command exit code from the PowerShell host.
# All test failures are tracked explicitly by Complete-TestRun.
if ($script:failedChecks -gt 0) {
    exit 1
}
exit 0
