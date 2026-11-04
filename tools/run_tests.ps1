# Runs the game's tests in batch Unity. Only one batch Unity at a time, with the project closed in the editor.
#   tools\run_tests.ps1             short run: every rule (EditMode) and PlayMode without the tests marked Slow, a few minutes
#   tools\run_tests.ps1 -Full       everything, only when asked for
#   tools\run_tests.ps1 -Filter X   only the tests matching X (a name or a regular expression), in both modes
param([switch]$Full, [string]$Filter = "")

$project = Split-Path -Parent $PSScriptRoot
$version = (Select-String -Path "$project\ProjectSettings\ProjectVersion.txt" -Pattern "m_EditorVersion: (.+)").Matches[0].Groups[1].Value
$unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
if (-not (Get-Process "Unity Hub" -ErrorAction SilentlyContinue)) {
    # Batch Unity needs the Hub for its licence
    Start-Process "shell:AppsFolder\UnityTechnologies.UnityHub_2vrhnee42bhxm!UnityHub"
    Start-Sleep 8
}

$failed = 0
$started = Get-Date
foreach ($mode in "EditMode", "PlayMode") {
    $xml = "$project\Logs\tests_$mode.xml"
    $log = "$project\Logs\tests_$mode.log"
    if (Test-Path $xml) { Remove-Item $xml }
    $arguments = @("-batchmode", "-projectPath", "`"$project`"", "-runTests", "-testPlatform", $mode, "-testResults", "`"$xml`"", "-logFile", "`"$log`"")
    if ($Filter) { $arguments += @("-testFilter", "`"$Filter`"") }
    elseif (-not $Full -and $mode -eq "PlayMode") { $arguments += @("-testCategory", "`"!Slow`"") }
    $process = Start-Process $unity -ArgumentList $arguments -PassThru
    $process.WaitForExit()
    Select-String -Path $log -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
    if (-not (Test-Path $xml)) { "$mode`: no results, see $log"; $failed++; continue }
    [xml]$results = Get-Content $xml
    $run = $results.'test-run'
    $seconds = [math]::Round([double]::Parse(("$($run.duration)" -replace ",", "."), [Globalization.CultureInfo]::InvariantCulture)) # (Unity writes it the local way)
    "$mode`: $($run.passed) passed, $($run.failed) failed, $($run.skipped) skipped of $($run.total), $seconds s"
    $results.SelectNodes("//test-case[@result='Failed']") | ForEach-Object {
        $_.fullname
        "  " + $_.failure.message.'#cdata-section'
    }
    $failed += [int]$run.failed
}
"Took $([math]::Round(((Get-Date) - $started).TotalMinutes, 1)) min"
if ($failed -gt 0) { exit 1 }
exit 0
