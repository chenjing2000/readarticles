$ErrorActionPreference = 'Stop'
$exeName = 'ReadArticles.exe'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('publish-tests-' + [Guid]::NewGuid().ToString('N'))
$scriptPath = Join-Path $PSScriptRoot 'Publish.ps1'
$script:passed = 0
$script:failed = 0

# Mock only dotnet publish. All path, copy, replace, and cleanup operations are real.
function global:dotnet {
    $outputIndex = [Array]::IndexOf($args, '-o')
    if ($outputIndex -lt 0) { throw 'Publish did not provide a staged output directory.' }
    $output = $args[$outputIndex + 1]
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $output $exeName), 'new-release')
    if ($global:publishTestBuildMode -eq 'extra') {
        [IO.File]::WriteAllText((Join-Path $output 'unexpected.dll'), 'dependency')
    }
    if ($global:publishTestBuildMode -eq 'symbols') {
        [IO.File]::WriteAllText((Join-Path $output 'ReferencedProject.pdb'), 'debug-symbols')
        [IO.File]::WriteAllText((Join-Path $output 'Package.xml'), 'documentation')
    }
    $global:LASTEXITCODE = if ($global:publishTestBuildMode -eq 'failure') { 1 } else { 0 }
}

function Check($condition, $message) {
    if (-not $condition) { throw $message }
}

function Run-Case($name, [scriptblock]$body) {
    $caseRoot = Join-Path $testRoot ([Guid]::NewGuid().ToString('N'))
    $tools = Join-Path $caseRoot 'tools'
    $dist = Join-Path $caseRoot 'dist'
    $app = Join-Path $dist 'ReadArticles'
    $userData = Join-Path $app 'userdata'
    $config = Join-Path $userData 'config'
    $backupDirectory = Join-Path $userData 'backups\vocabulary'
    New-Item -ItemType Directory -Path $tools, $config, $backupDirectory -Force | Out-Null
    Copy-Item -LiteralPath $scriptPath -Destination (Join-Path $tools 'Publish.ps1')
    $release = Join-Path $app $exeName
    $settings = Join-Path $config 'settings.json'
    $backup = Join-Path $backupDirectory 'one.vocabulary.json.bak'
    [IO.File]::WriteAllText($release, 'old-release')
    [IO.File]::WriteAllText($settings, 'user-settings')
    [IO.File]::WriteAllText($backup, 'user-backup')
    [IO.File]::WriteAllText((Join-Path $app 'user-notes.txt'), 'keep-me')
    [IO.File]::WriteAllText((Join-Path $dist 'other-user-file.txt'), 'keep-root-file')
    $global:publishTestBuildMode = 'success'
    try {
        & $body
        Check ((Get-Content -LiteralPath (Join-Path $app 'user-notes.txt') -Raw) -eq 'keep-me') 'Other user files were changed.'
        Check ((Get-Content -LiteralPath (Join-Path $dist 'other-user-file.txt') -Raw) -eq 'keep-root-file') 'Other files in dist changed.'
        Check (@(Get-ChildItem -LiteralPath $dist -Directory -Filter '.publish-*').Count -eq 0) 'Staging directory leaked.'
        Check (@(Get-ChildItem -LiteralPath $app -Directory -Filter '.userdata-migrate-*').Count -eq 0) 'Migration staging directory leaked.'
        $script:passed++
        Write-Host "PASS $name"
    }
    catch {
        $script:failed++
        Write-Host "FAIL ${name}: $_"
    }
}

function Check-UserData {
    Check ((Get-Content -LiteralPath $settings -Raw) -eq 'user-settings') 'Settings changed.'
    Check ((Get-Content -LiteralPath $backup -Raw) -eq 'user-backup') 'Vocabulary backup changed.'
}

try {
    Run-Case 'successful portable update retains userdata and a recovery EXE' {
        & (Join-Path $tools 'Publish.ps1')
        Check ((Get-Content -LiteralPath $release -Raw) -eq 'new-release') 'Release was not updated.'
        Check-UserData
        Check ((Test-Path -LiteralPath (Join-Path $app 'README.txt'))) 'Portable readme missing.'
        $oldCopies = @(Get-ChildItem -LiteralPath $app -File -Filter 'ReadArticles.exe.previous-*')
        Check ($oldCopies.Count -eq 1) 'Expected one prior executable for recovery.'
        Check ((Get-Content -LiteralPath $oldCopies[0].FullName -Raw) -eq 'old-release') 'Old executable was not preserved.'
    }
    Run-Case 'debug symbols and XML docs are excluded' {
        $global:publishTestBuildMode = 'symbols'
        & (Join-Path $tools 'Publish.ps1')
        Check ((Get-Content -LiteralPath $release -Raw) -eq 'new-release') 'Release with symbols was rejected.'
        Check (-not (Test-Path -LiteralPath (Join-Path $app 'ReferencedProject.pdb'))) 'Symbols were distributed.'
        Check-UserData
    }
    Run-Case 'failed build preserves existing executable and userdata' {
        $global:publishTestBuildMode = 'failure'
        $rejected = $false
        try { & (Join-Path $tools 'Publish.ps1') } catch { $rejected = $true }
        Check $rejected 'Failed build was accepted.'
        Check ((Get-Content -LiteralPath $release -Raw) -eq 'old-release') 'Failed build replaced executable.'
        Check-UserData
    }
    Run-Case 'unexpected dependency rejects release' {
        $global:publishTestBuildMode = 'extra'
        $rejected = $false
        try { & (Join-Path $tools 'Publish.ps1') } catch { $rejected = $true }
        Check $rejected 'Unexpected DLL was accepted.'
        Check ((Get-Content -LiteralPath $release -Raw) -eq 'old-release') 'Invalid release replaced executable.'
        Check-UserData
    }
    Run-Case 'locked executable remains unchanged' {
        $lock = [IO.File]::Open($release, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        try {
            $rejected = $false
            try { & (Join-Path $tools 'Publish.ps1') } catch { $rejected = $true }
            Check $rejected 'Locked executable was accepted.'
        }
        finally { $lock.Dispose() }
        Check ((Get-Content -LiteralPath $release -Raw) -eq 'old-release') 'Locked release changed.'
        Check-UserData
    }
    Run-Case 'first publication creates a standalone portable folder' {
        Remove-Item -LiteralPath $release -Force
        & (Join-Path $tools 'Publish.ps1')
        Check ((Get-Content -LiteralPath $release -Raw) -eq 'new-release') 'First release was not installed.'
        Check ((Test-Path -LiteralPath (Join-Path $app 'README.txt'))) 'Portable readme missing.'
        Check (@(Get-ChildItem -LiteralPath $app -File -Filter 'ReadArticles.exe.previous-*').Count -eq 0) 'First release should not contain previous executable.'
        Check-UserData
    }
    Run-Case 'legacy userdata migrates once without deleting its original' {
        $oldData = Join-Path $dist 'userdata'
        $oldConfig = Join-Path $oldData 'config'
        New-Item -ItemType Directory -Path $oldConfig -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $oldConfig 'settings.json'), 'legacy-settings')
        [IO.File]::WriteAllText((Join-Path $oldData 'important.txt'), 'legacy-data')
        Remove-Item -LiteralPath $userData -Recurse -Force
        & (Join-Path $tools 'Publish.ps1')
        Check ((Get-Content -LiteralPath $settings -Raw) -eq 'legacy-settings') 'Old settings were not copied.'
        Check ((Get-Content -LiteralPath (Join-Path $userData 'important.txt') -Raw) -eq 'legacy-data') 'Other userdata was not copied.'
        Check ((Get-Content -LiteralPath (Join-Path $oldConfig 'settings.json') -Raw) -eq 'legacy-settings') 'Old settings were destroyed.'
        [IO.File]::WriteAllText($settings, 'new-settings')
        & (Join-Path $tools 'Publish.ps1')
        Check ((Get-Content -LiteralPath $settings -Raw) -eq 'new-settings') 'Update overwrote migrated settings.'
    }
    Run-Case 'existing userdata has precedence over legacy userdata' {
        $oldData = Join-Path $dist 'userdata'
        $oldConfig = Join-Path $oldData 'config'
        New-Item -ItemType Directory -Path $oldConfig -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $oldConfig 'settings.json'), 'older-settings')
        & (Join-Path $tools 'Publish.ps1')
        Check-UserData
        Check ((Get-Content -LiteralPath (Join-Path $oldConfig 'settings.json') -Raw) -eq 'older-settings') 'Old source settings were changed.'
    }
}
finally {
    Remove-Item Function:\dotnet
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path.' }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
Write-Host "Passed: $script:passed; Failed: $script:failed"
if ($script:failed) { exit 1 }
