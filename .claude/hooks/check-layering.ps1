# Mechanical layering/style checks on a single edited C# file.
# Run as a PostToolUse hook; exit 2 sends the findings back to Claude to fix.
# Judgment calls (DI lifetimes, unregistered services, zip-slip guards) are not checkable
# here -- that is what the layering-reviewer agent is for.

$ErrorActionPreference = 'Stop'

$payload = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($payload)) { exit 0 }

try { $filePath = (ConvertFrom-Json $payload).tool_input.file_path } catch { exit 0 }
if ([string]::IsNullOrWhiteSpace($filePath)) { exit 0 }
if ($filePath -notmatch '\.cs$') { exit 0 }
if ($filePath -match '[\\/](obj|bin)[\\/]') { exit 0 }
if (-not (Test-Path $filePath)) { exit 0 }

$lines = Get-Content -Path $filePath
$findings = New-Object System.Collections.Generic.List[string]

# 1. Team standard: explicit types, never `var`.
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    if ($line.TrimStart().StartsWith('//')) { continue }
    if ($line -match '(^|[\s(])var\s+[A-Za-z_]') {
        $findings.Add("  line $($i + 1): 'var' - this repo requires explicit types")
    }
}

# 2. Infrastructure may only be imported by the UI's composition root.
if ($filePath -match 'ModManager\.Ui' -and $filePath -notmatch 'App\.axaml\.cs$') {
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*using\s+ModManager\.Infrastructure') {
            $findings.Add("  line $($i + 1): imports ModManager.Infrastructure - only App.axaml.cs may; depend on the Application-layer interface instead")
        }
    }
}

# 3. Domain models belong in Application, not Infrastructure.
if ($filePath -match 'ModManager\.Infrastructure[\\/]Models[\\/]') {
    $findings.Add("  this file is in Infrastructure/Models - domain models belong in ModManager.Application/Models; Infrastructure holds persistence DTOs and adapters only")
}

if ($findings.Count -gt 0) {
    [Console]::Error.WriteLine("Layering/style check on ${filePath}:")
    foreach ($f in $findings) { [Console]::Error.WriteLine($f) }
    exit 2
}

exit 0
