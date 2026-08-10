# Incremental solution build once per turn, as a Stop hook.
# Catches Avalonia XAML compile errors and nullable warnings-as-errors that only surface at build.
# Exit 2 hands the errors back to Claude to fix before the turn ends.

$ErrorActionPreference = 'Stop'

$payload = [Console]::In.ReadToEnd()

# Claude already got one chance to fix a failing build; a second block would loop forever.
if (-not [string]::IsNullOrWhiteSpace($payload)) {
    try {
        if ((ConvertFrom-Json $payload).stop_hook_active) { exit 0 }
    } catch { }
}

$root = $env:CLAUDE_PROJECT_DIR
if ([string]::IsNullOrWhiteSpace($root)) { $root = $PSScriptRoot | Split-Path | Split-Path }

# Nothing compilable changed -> nothing to check.
$changed = & git -C $root status --porcelain -- '*.cs' '*.axaml' '*.csproj'
if ([string]::IsNullOrWhiteSpace(($changed | Out-String).Trim())) { exit 0 }

$log = & dotnet build "$root\ModManager.slnx" --nologo -v q 2>&1 | Out-String
if ($LASTEXITCODE -eq 0) { exit 0 }

# A running instance holding the output folder is not a code problem -- do not block on it.
if ($log -match 'MSB3021|MSB3027|being used by another process') {
    [Console]::Error.WriteLine("Build skipped: output folder is locked, likely by a running ModManager instance.")
    [Console]::Error.WriteLine("Verify with: dotnet build ModManager.slnx -p:ArtifactsPath=<dir>")
    exit 0
}

$errors = $log -split "`r?`n" |
    Where-Object { $_ -match ': (error|warning) [A-Z]+\d+' } |
    ForEach-Object { $_.Trim() } |
    Select-Object -Unique -First 25
[Console]::Error.WriteLine("Build failed -- fix before finishing:")
foreach ($e in $errors) { [Console]::Error.WriteLine("  $e") }
exit 2
