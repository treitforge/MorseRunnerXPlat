Set-StrictMode -Version 3.0

function Assert-ParityWellFormedUtf16 {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Value
    )

    for ($index = 0; $index -lt $Value.Length; $index++) {
        $codeUnit = [int] $Value[$index]
        if ($codeUnit -ge 0xD800 -and $codeUnit -le 0xDBFF) {
            if ($index + 1 -ge $Value.Length) {
                throw 'Parity JSON contains an unpaired Unicode surrogate.'
            }
            $low = [int] $Value[$index + 1]
            if ($low -lt 0xDC00 -or $low -gt 0xDFFF) {
                throw 'Parity JSON contains an unpaired Unicode surrogate.'
            }
            $index++
        } elseif ($codeUnit -ge 0xDC00 -and
            $codeUnit -le 0xDFFF) {
            throw 'Parity JSON contains an unpaired Unicode surrogate.'
        }
    }
}

function Add-ParityCanonicalJsonString {
    param(
        [Parameter(Mandatory)]
        [Text.StringBuilder] $Builder,

        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Value
    )

    Assert-ParityWellFormedUtf16 -Value $Value
    [void] $Builder.Append('"')
    for ($index = 0; $index -lt $Value.Length; $index++) {
        $character = $Value[$index]
        $escape = switch ([int] $character) {
            0x08 { '\b' }
            0x09 { '\t' }
            0x0A { '\n' }
            0x0C { '\f' }
            0x0D { '\r' }
            0x22 { '\"' }
            0x5C { '\\' }
            default { $null }
        }
        if ($null -ne $escape) {
            [void] $Builder.Append($escape)
        } elseif ([int] $character -lt 0x20) {
            [void] $Builder.Append(
                '\u' + ([int] $character).ToString('x4'))
        } else {
            [void] $Builder.Append($character)
        }
    }
    [void] $Builder.Append('"')
}

function Add-ParityCanonicalJsonElement {
    param(
        [Parameter(Mandatory)]
        [Text.StringBuilder] $Builder,

        [Parameter(Mandatory)]
        [Text.Json.JsonElement] $Element
    )

    switch ($Element.ValueKind) {
        ([Text.Json.JsonValueKind]::Object) {
            $names = [string[]] @(
                $Element.EnumerateObject() |
                    ForEach-Object { $_.Name })
            $uniqueNames = [Collections.Generic.HashSet[string]]::new(
                [StringComparer]::Ordinal)
            foreach ($name in $names) {
                Assert-ParityWellFormedUtf16 -Value $name
                if (-not $uniqueNames.Add($name)) {
                    throw (
                        'Parity JSON contains a duplicate object property.')
                }
            }
            [Array]::Sort($names, [StringComparer]::Ordinal)
            [void] $Builder.Append('{')
            for ($index = 0; $index -lt $names.Count; $index++) {
                if ($index -gt 0) {
                    [void] $Builder.Append(',')
                }
                $name = $names[$index]
                Add-ParityCanonicalJsonString `
                    -Builder $Builder `
                    -Value $name
                [void] $Builder.Append(':')
                Add-ParityCanonicalJsonElement `
                    -Builder $Builder `
                    -Element $Element.GetProperty($name)
            }
            [void] $Builder.Append('}')
            break
        }
        ([Text.Json.JsonValueKind]::Array) {
            [void] $Builder.Append('[')
            $index = 0
            foreach ($item in $Element.EnumerateArray()) {
                if ($index -gt 0) {
                    [void] $Builder.Append(',')
                }
                Add-ParityCanonicalJsonElement `
                    -Builder $Builder `
                    -Element $item
                $index++
            }
            [void] $Builder.Append(']')
            break
        }
        ([Text.Json.JsonValueKind]::String) {
            Add-ParityCanonicalJsonString `
                -Builder $Builder `
                -Value $Element.GetString()
            break
        }
        ([Text.Json.JsonValueKind]::Number) {
            $raw = $Element.GetRawText()
            if ($raw -cnotmatch '^-?(0|[1-9][0-9]*)$') {
                throw (
                    'Parity JSON numbers must be signed Int64 literals. ' +
                    'Use strings for fractional values.')
            }
            try {
                $number = [long]::Parse(
                    $raw,
                    [Globalization.NumberStyles]::AllowLeadingSign,
                    [Globalization.CultureInfo]::InvariantCulture)
            } catch {
                throw (
                    'Parity JSON number is outside signed Int64 range: ' +
                    $raw)
            }
            [void] $Builder.Append(
                $number.ToString(
                    [Globalization.CultureInfo]::InvariantCulture))
            break
        }
        ([Text.Json.JsonValueKind]::True) {
            [void] $Builder.Append('true')
            break
        }
        ([Text.Json.JsonValueKind]::False) {
            [void] $Builder.Append('false')
            break
        }
        ([Text.Json.JsonValueKind]::Null) {
            [void] $Builder.Append('null')
            break
        }
        default {
            throw (
                'Parity JSON contains an unsupported value kind: ' +
                $Element.ValueKind)
        }
    }
}

function ConvertTo-ParityCanonicalJsonBytes {
    param(
        [Parameter(Mandatory)]
        [Text.Json.JsonElement] $Element,

        [string[]] $Projection
    )

    $builder = [Text.StringBuilder]::new()
    if ($null -ne $Projection -and
        $Projection.Count -gt 0) {
        if ($Element.ValueKind -ne
            [Text.Json.JsonValueKind]::Object) {
            throw 'Canonical JSON projection source is not an object.'
        }
        $sourceNames = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            Assert-ParityWellFormedUtf16 -Value $property.Name
            if (-not $sourceNames.Add($property.Name)) {
                throw 'Parity JSON contains a duplicate object property.'
            }
        }
        $names = [string[]] @($Projection)
        [Array]::Sort($names, [StringComparer]::Ordinal)
        $uniqueNames = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($name in $names) {
            if (-not $uniqueNames.Add($name)) {
                throw 'Canonical JSON projection fields are invalid.'
            }
        }
        if ($names.Count -eq 0 -or
            $uniqueNames.Count -ne $names.Count) {
            throw 'Canonical JSON projection fields are invalid.'
        }
        [void] $builder.Append('{')
        for ($index = 0; $index -lt $names.Count; $index++) {
            if ($index -gt 0) {
                [void] $builder.Append(',')
            }
            $name = $names[$index]
            Add-ParityCanonicalJsonString `
                -Builder $builder `
                -Value $name
            [void] $builder.Append(':')
            Add-ParityCanonicalJsonElement `
                -Builder $builder `
                -Element $Element.GetProperty($name)
        }
        [void] $builder.Append('}')
    } else {
        Add-ParityCanonicalJsonElement `
            -Builder $builder `
            -Element $Element
    }
    $encoding = [Text.UTF8Encoding]::new(
        $false,
        $true)
    return ,$encoding.GetBytes($builder.ToString())
}
