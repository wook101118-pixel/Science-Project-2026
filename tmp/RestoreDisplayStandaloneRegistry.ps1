param([string]$BackupFile = (Join-Path $PSScriptRoot 'DisplaySettingsStandaloneRegistryBackup.json'))
$ErrorActionPreference = 'Stop'
$backup = Get-Content -LiteralPath $backupFile -Raw | ConvertFrom-Json
$expectedPath = 'HKCU:/Software/DefaultCompany/Scince'
if ($backup.path -ne $expectedPath) { throw 'Unexpected player preference registry target.' }

if (Test-Path -LiteralPath $expectedPath) {
    $registryKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\DefaultCompany\Scince', $true)
    $originalNames = @($backup.values | ForEach-Object { $_.name })
    foreach ($valueName in @($registryKey.GetValueNames())) {
        if ($originalNames -notcontains $valueName) { $registryKey.DeleteValue($valueName, $false) }
    }
} elseif ($backup.existed) {
    $null = New-Item -Path $expectedPath -Force
    $registryKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\DefaultCompany\Scince', $true)
}

foreach ($original in @($backup.values)) {
    $kind = [Microsoft.Win32.RegistryValueKind]::$($original.kind)
    $value = $original.value
    switch ($original.kind) {
        'Binary' { $value = [byte[]]$value }
        'DWord' { $value = [int]$value }
        'QWord' { $value = [long]$value }
        'MultiString' { $value = [string[]]$value }
        default { $value = [string]$value }
    }
    $registryKey.SetValue([string]$original.name, $value, $kind)
}
if ($null -ne $registryKey) { $registryKey.Close() }

if (-not $backup.existed -and (Test-Path -LiteralPath $expectedPath)) {
    $checkKey = Get-Item -LiteralPath $expectedPath
    $empty = $checkKey.ValueCount -eq 0 -and $checkKey.SubKeyCount -eq 0
    $checkKey.Close()
    if (-not $empty) { throw 'The newly created test preference key is not empty.' }
    Remove-Item -LiteralPath $expectedPath
}
if ((Test-Path -LiteralPath $expectedPath) -ne [bool]$backup.existed) {
    throw 'Original preference key presence was not restored.'
}
$backup.restored = $true
$backup | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $backupFile -Encoding UTF8
[pscustomobject]@{ Restored = $true; OriginalKeyExisted = $backup.existed; OriginalValues = @($backup.values).Count } | ConvertTo-Json
