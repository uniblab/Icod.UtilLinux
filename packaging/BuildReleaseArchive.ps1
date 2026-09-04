param(
    [Parameter(Mandatory=$true)][string]$RuntimeIdentifier,
    [Parameter(Mandatory=$true)][string]$Version,
    [ValidateSet('Debug','Staging','Release')][string]$Configuration='Release',
    [string]$ArchiveBaseName=''
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repositoryRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force
$solutionPath=Get-RepositorySolution -RepositoryRoot $repositoryRoot
$projects=@(Get-SolutionProjects -SolutionPath $solutionPath -RepositoryRoot $repositoryRoot)
$executables=@(Get-ExecutableProjects -ProjectPaths $projects -Configuration $Configuration)
if(0 -eq $executables.Count){ throw 'The solution contains no executable projects to archive.' }
if([string]::IsNullOrWhiteSpace($ArchiveBaseName)){ $ArchiveBaseName=Split-Path $repositoryRoot -Leaf }
$releaseRoot=Join-Path $repositoryRoot 'artifacts/release'
$stageName="$ArchiveBaseName-$Version-$RuntimeIdentifier"
$stageParent=Join-Path $releaseRoot 'stage'
$stage=Join-Path $stageParent $stageName
$archive=Join-Path $releaseRoot "$stageName.zip"
if(Test-Path -LiteralPath $stage){ Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Push-Location $repositoryRoot
try {
    Invoke-DotNet -Arguments @('restore',$solutionPath,'-r',$RuntimeIdentifier)
    foreach($executable in $executables){
        $publish=Join-Path $releaseRoot "publish/$RuntimeIdentifier/$($executable.AssemblyName)"
        Invoke-DotNet -Arguments @('publish',$executable.ProjectPath,'-c',$Configuration,'-r',$RuntimeIdentifier,'--no-restore','--self-contained','false','-p:PublishSingleFile=true','-p:PublishTrimmed=false','-p:DebugType=None','-p:DebugSymbols=false','-p:ContinuousIntegrationBuild=true','-o',$publish)
        $file=if($RuntimeIdentifier.StartsWith('win-')){"$($executable.AssemblyName).exe"}else{$executable.AssemblyName}
        Copy-Item -LiteralPath (Join-Path $publish $file) -Destination (Join-Path $stage $file)
    }
    foreach($support in @('README.md','LICENSE')){ $source=Join-Path $repositoryRoot $support; if(Test-Path -LiteralPath $source){ Copy-Item $source -Destination (Join-Path $stage $support) } }
    if($RuntimeIdentifier.StartsWith('win-')){ Compress-Archive -LiteralPath $stage -DestinationPath $archive -CompressionLevel Optimal } else {
        Get-ChildItem -LiteralPath $stage -File | Where-Object { $_.Name -in @('kill','renice') } | ForEach-Object { & chmod +x $_.FullName }
        Push-Location $stageParent; try { & zip -r -q $archive $stageName; if(0 -ne $LASTEXITCODE){ throw 'zip failed' } } finally { Pop-Location }
    }
    Write-Host "Created release archive: $archive"
} finally { Pop-Location }
