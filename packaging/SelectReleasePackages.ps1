param(
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [Parameter(Mandatory=$true)][string]$DestinationDirectory,
    [Parameter(Mandatory=$true)][string]$ExpectedVersion,
    [string]$GitHubOutputPath=''
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repositoryRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force
foreach($name in @('SourceDirectory','DestinationDirectory')){ $value=Get-Variable -Name $name -ValueOnly; if(-not [System.IO.Path]::IsPathRooted($value)){ $value=Join-Path $repositoryRoot $value }; Set-Variable -Name $name -Value ([System.IO.Path]::GetFullPath($value)) }
if(Test-Path -LiteralPath $DestinationDirectory){ Remove-Item -LiteralPath $DestinationDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
$selected=@()
foreach($package in @(Get-ChildItem -LiteralPath $SourceDirectory -Filter '*.nupkg' -File | Where-Object { -not $_.Name.EndsWith('.symbols.nupkg',[System.StringComparison]::OrdinalIgnoreCase) } | Sort-Object Name)){
    $metadata=Get-PackageMetadata -PackagePath $package.FullName
    if($metadata.Version -ne $ExpectedVersion){ continue }
    $destination=Join-Path $DestinationDirectory $package.Name
    Copy-Item -LiteralPath $package.FullName -Destination $destination
    $selected += $destination
}
$hasPackages=0 -lt $selected.Count
if(-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)){ "has_packages=$($hasPackages.ToString().ToLowerInvariant())" >> $GitHubOutputPath; "package_count=$($selected.Count)" >> $GitHubOutputPath }
Write-Host "Selected $($selected.Count) NuGet package(s) for release $ExpectedVersion."
