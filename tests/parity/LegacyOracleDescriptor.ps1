Set-StrictMode -Version 3.0

function Assert-VersionedLegacyOracleDescriptorPath {
    param(
        [Parameter(Mandatory)]
        [object] $Descriptor,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $versionId = [string] $Descriptor.versionId
    if ($versionId -cnotmatch
        '^[a-z0-9][a-z0-9.-]*-v([1-9][0-9]*)$') {
        throw (
            "$Description versionId must end in an explicit -vN " +
            'version suffix.')
    }
    $versionDirectory = "v$($Matches[1])"
    $requiredPrefix =
        "tests/parity/legacy-oracle/$versionDirectory/"
    foreach ($propertyName in @('source', 'buildRecipe')) {
        $path = [string] $Descriptor.$propertyName
        $segments = [string[]] $path.Split('/')
        if (-not $path.StartsWith(
                $requiredPrefix,
                [StringComparison]::Ordinal) -or
            $path.Length -eq $requiredPrefix.Length -or
            $path.Contains('\') -or
            @($segments | Where-Object {
                    $_ -in @('', '.', '..')
                }).Count -ne 0) {
            throw (
                "$Description $propertyName must be under exact " +
                "'$requiredPrefix' for versionId '$versionId'.")
        }
    }
}

function Add-LegacyOracleDescriptorVersionBinding {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [Collections.Generic.Dictionary[string, object]]
        $DescriptorsByVersion,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [Collections.Generic.Dictionary[string, object]]
        $CaseIdsByVersion,

        [Parameter(Mandatory)]
        [object] $Descriptor,

        [Parameter(Mandatory)]
        [string] $CaseId
    )

    $versionId = [string] $Descriptor.versionId
    if ($DescriptorsByVersion.ContainsKey($versionId)) {
        $existing = $DescriptorsByVersion[$versionId]
        foreach ($propertyName in @(
                'adapterId',
                'versionId',
                'source',
                'sourceSha256',
                'buildRecipe',
                'buildRecipeSha256')) {
            if ([string] $existing.$propertyName -cne
                [string] $Descriptor.$propertyName) {
                throw (
                    "Legacy oracle versionId '$versionId' maps to more " +
                    'than one descriptor.')
            }
        }
        $CaseIdsByVersion[$versionId].Add($CaseId)
        return
    }

    $DescriptorsByVersion.Add($versionId, $Descriptor)
    $caseList = [Collections.Generic.List[string]]::new()
    $caseList.Add($CaseId)
    $CaseIdsByVersion.Add($versionId, $caseList)
}
