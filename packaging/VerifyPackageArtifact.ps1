param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,

    [ValidateSet('Debug', 'Staging', 'Release')]
    [string]$Configuration = 'Release',

    [string]$ExpectedVersion = '',

    [string]$ExpectedPackageId = '',

    [int]$ExpectedPackageCount = -1,

    [switch]$AllowNoPackages,

    [string]$GitHubOutputPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force

if (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $repositoryRoot $ArtifactDirectory
}
$ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
if (-not (Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) {
    throw "Artifact directory '$ArtifactDirectory' does not exist."
}

$packages = @(
    Get-ChildItem -LiteralPath $ArtifactDirectory -Filter '*.nupkg' -File |
        Where-Object {
            -not $_.Name.EndsWith(
                '.symbols.nupkg',
                [System.StringComparison]::OrdinalIgnoreCase
            )
        } |
        Sort-Object Name
)

if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    $packages = @(
        $packages |
            Where-Object {
                (Get-PackageMetadata -PackagePath $_.FullName).Version -eq $ExpectedVersion
            }
    )
}

if (0 -eq $packages.Count -and -not $AllowNoPackages) {
    throw "No NuGet packages were found in '$ArtifactDirectory'."
}
if (0 -le $ExpectedPackageCount -and $ExpectedPackageCount -ne $packages.Count) {
    throw "Expected $ExpectedPackageCount NuGet package(s) in '$ArtifactDirectory'; found $($packages.Count)."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($package in $packages) {
    $metadata = Get-PackageMetadata -PackagePath $package.FullName
    $hasExpectedPackageId = -not [string]::IsNullOrWhiteSpace($ExpectedPackageId)
    if ($hasExpectedPackageId -and $metadata.Id -ne $ExpectedPackageId) {
        throw "Package '$($package.Name)' has ID '$($metadata.Id)'; expected '$ExpectedPackageId'."
    }

    Write-Host "Verifying $($metadata.Id) $($metadata.Version): $($package.FullName)"
    $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        if (0 -eq $archive.Entries.Count) {
            throw "Package '$($package.FullName)' is empty."
        }
        if (-not [string]::IsNullOrWhiteSpace($metadata.Readme)) {
            $readmeEntry = $archive.Entries |
                Where-Object { $_.FullName -eq $metadata.Readme } |
                Select-Object -First 1
            if ($null -eq $readmeEntry) {
                throw "Package '$($package.FullName)' declares missing readme '$($metadata.Readme)'."
            }
        }
    } finally {
        $archive.Dispose()
    }
}

if (-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
    "package_count=$($packages.Count)" >> $GitHubOutputPath
    "has_packages=$((0 -lt $packages.Count).ToString().ToLowerInvariant())" >> $GitHubOutputPath
}

Write-Host "Exact package verification completed successfully for $($packages.Count) package(s) ($Configuration)."
