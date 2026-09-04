param(
    [ValidateSet('all', 'clean', 'restore', 'build', 'test', 'pack', 'validate')]
    [string]$Section = 'all',

    [ValidateSet('Debug', 'Staging', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force
$solutionPath = Get-RepositorySolution -RepositoryRoot $repositoryRoot
$artifactDirectory = Join-Path $repositoryRoot 'artifacts'

function Invoke-Clean {
    Invoke-DotNet -Arguments @('clean', $solutionPath, '-c', $Configuration)
}

function Invoke-Restore {
    Invoke-DotNet -Arguments @('restore', $solutionPath)
}

function Invoke-Build {
    Invoke-DotNet -Arguments @('build', $solutionPath, '-c', $Configuration, '--no-restore')
}

function Invoke-Test {
    Invoke-DotNet -Arguments @(
        'test',
        $solutionPath,
        '-c', $Configuration,
        '--no-build',
        '--no-restore'
    )
}

function Invoke-Pack {
    New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
    Invoke-DotNet -Arguments @(
        'pack',
        $solutionPath,
        '-c', $Configuration,
        '--no-build',
        '--no-restore',
        '-o', $artifactDirectory
    )
}

function Invoke-Validate {
    & (Join-Path $PSScriptRoot 'VerifyPackageArtifact.ps1') `
        -ArtifactDirectory $artifactDirectory `
        -Configuration $Configuration `
        -ExpectedPackageId 'Icod.UtilLinux.Tools' `
        -ExpectedPackageCount 1
}

Push-Location $repositoryRoot
try {
    switch ($Section) {
        'all' {
            Invoke-Clean
            Invoke-Restore
            Invoke-Build
            Invoke-Test
            Invoke-Pack
            Invoke-Validate
        }
        'clean' { Invoke-Clean }
        'restore' { Invoke-Restore }
        'build' { Invoke-Build }
        'test' { Invoke-Test }
        'pack' { Invoke-Pack }
        'validate' { Invoke-Validate }
    }
} finally {
    Pop-Location
}
