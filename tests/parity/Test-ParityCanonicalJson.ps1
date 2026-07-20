[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'ParityCanonicalJson.ps1')

$vectorPath = Join-Path $PSScriptRoot 'canonical-json-vectors.json'
$vectorDocument = [Text.Json.JsonDocument]::Parse(
    [IO.File]::ReadAllText($vectorPath))
try {
    $root = $vectorDocument.RootElement
    if ($root.GetProperty('schemaVersion').GetInt32() -ne 1 -or
        $root.GetProperty('ordering').GetString() -cne
            'utf16-code-unit-ordinal-recursive' -or
        $root.GetProperty('encoding').GetString() -cne
            'utf8-no-bom-no-normalization') {
        throw 'Shared canonical JSON vector identity is invalid.'
    }
    $vectors = @($root.GetProperty('vectors').EnumerateArray())
    if ($vectors.Count -ne 1 -or
        $vectors[0].GetProperty('name').GetString() -cne
            'nested-unicode-escaping-and-int64') {
        throw 'Shared canonical JSON reviewed vector is missing.'
    }
    $expectedHash =
        '8a3c187ebd3846533be418f811bb87a34a45ec4dba008c7f8e1db7c299a04d33'
    if ($vectors[0].GetProperty('sha256').GetString().ToLowerInvariant() -cne
        $expectedHash) {
        throw 'Shared canonical JSON reviewed digest changed.'
    }
    $expectedBytes = [Convert]::FromBase64String(
        $vectors[0].GetProperty(
            'canonicalUtf8Base64').GetString())
    $actualBytes = ConvertTo-ParityCanonicalJsonBytes `
        -Element $vectors[0].GetProperty('value')
    if ([Convert]::ToBase64String($actualBytes) -cne
        [Convert]::ToBase64String($expectedBytes)) {
        throw 'PowerShell canonical JSON bytes differ from the shared vector.'
    }
    $actualHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $actualBytes)
    ).ToLowerInvariant()
    if ($actualHash -cne $expectedHash) {
        throw (
            "PowerShell canonical JSON digest is $actualHash, " +
            "expected $expectedHash.")
    }

    $invalidJsonTexts = @(
        $root.GetProperty('invalidJsonTexts').EnumerateArray() |
            ForEach-Object { $_.GetString() })
    if ($invalidJsonTexts.Count -ne 2) {
        throw 'Shared invalid-Unicode canonical JSON vectors changed.'
    }
    foreach ($invalidJson in $invalidJsonTexts) {
        $invalid = [Text.Json.JsonDocument]::Parse($invalidJson)
        try {
            try {
                ConvertTo-ParityCanonicalJsonBytes `
                    -Element $invalid.RootElement |
                    Out-Null
            } catch {
                continue
            }
            throw (
                'PowerShell canonical JSON accepted invalid Unicode: ' +
                $invalidJson)
        } finally {
            $invalid.Dispose()
        }
    }
} finally {
    $vectorDocument.Dispose()
}

foreach ($invalidNumber in @(
        '1.5',
        '1e3',
        '-2E-2',
        '9223372036854775808',
        '-9223372036854775809')) {
    $invalid = [Text.Json.JsonDocument]::Parse(
        "{`"scenario`":`"test.scenario`",`"value`":$invalidNumber}")
    try {
        try {
            ConvertTo-ParityCanonicalJsonBytes `
                -Element $invalid.RootElement | Out-Null
        } catch {
            continue
        }
        throw (
            'PowerShell parity canonical JSON accepted invalid number ' +
            "$invalidNumber.")
    } finally {
        $invalid.Dispose()
    }
}

$duplicate = [Text.Json.JsonDocument]::Parse(
    '{"duplicate":1,"duplicate":2}')
try {
    $duplicateRejected = $false
    try {
        ConvertTo-ParityCanonicalJsonBytes `
            -Element $duplicate.RootElement `
            -Projection @('duplicate') |
            Out-Null
    } catch {
        $duplicateRejected = $true
    }
    if (-not $duplicateRejected) {
        throw (
            'PowerShell parity canonical JSON accepted duplicate ' +
            'projection properties.')
    }
} finally {
    $duplicate.Dispose()
}

Write-Host 'PowerShell parity canonical JSON checks passed.'
