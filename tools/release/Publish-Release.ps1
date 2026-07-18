[CmdletBinding()]
param(
    [string[]] $RuntimeIdentifiers = @(
        'win-x64',
        'linux-x64',
        'osx-x64',
        'osx-arm64'
    ),

    [string] $Configuration = 'Release',

    [string] $OutputRoot = 'artifacts\release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$resolvedOutputRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot $OutputRoot)
)
$requiredPrefix = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\release')
)
if (-not $resolvedOutputRoot.StartsWith(
    $requiredPrefix,
    [System.StringComparison]::OrdinalIgnoreCase
)) {
    throw "Release output must remain under '$requiredPrefix'."
}

$projects = [ordered]@{
    'app' = 'src\MorseRunner.App\MorseRunner.App.csproj'
    'cli' = 'src\MorseRunner.Cli\MorseRunner.Cli.csproj'
    'engine-host' = 'src\MorseRunner.EngineHost\MorseRunner.EngineHost.csproj'
    'tui' = 'src\MorseRunner.Tui\MorseRunner.Tui.csproj'
}

New-Item -ItemType Directory -Force -Path $resolvedOutputRoot | Out-Null
foreach ($runtimeIdentifier in $RuntimeIdentifiers) {
    $runtimeRoot = Join-Path $resolvedOutputRoot $runtimeIdentifier
    if (Test-Path -LiteralPath $runtimeRoot) {
        $resolvedRuntimeRoot = (Resolve-Path $runtimeRoot).Path
        if (-not $resolvedRuntimeRoot.StartsWith(
            $requiredPrefix,
            [System.StringComparison]::OrdinalIgnoreCase
        )) {
            throw "Refusing to remove unexpected path '$resolvedRuntimeRoot'."
        }

        Remove-Item -LiteralPath $resolvedRuntimeRoot -Recurse -Force
    }

    foreach ($entry in $projects.GetEnumerator()) {
        $destination = Join-Path $runtimeRoot $entry.Key
        & dotnet publish (Join-Path $repositoryRoot $entry.Value) `
            --configuration $Configuration `
            --runtime $runtimeIdentifier `
            --self-contained true `
            --output $destination `
            -p:PublishSingleFile=false `
            -p:DebugType=None `
            -p:DebugSymbols=false
        if ($LASTEXITCODE -ne 0) {
            throw "Publish failed for $($entry.Key) on $runtimeIdentifier."
        }

        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') `
            -Destination $destination
        Copy-Item -LiteralPath (
            Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md'
        ) -Destination $destination
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') `
            -Destination $destination
    }

    $archive = Join-Path $resolvedOutputRoot (
        "MorseRunnerXPlat-$runtimeIdentifier.zip"
    )
    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }

    Compress-Archive -Path (Join-Path $runtimeRoot '*') `
        -DestinationPath $archive `
        -CompressionLevel Optimal
}
