param(
    [string]$OutputRoot = "artifacts",
    [string]$Channel = "win",
    [switch]$SkipTests,
    [switch]$UploadToGitHub,
    [string]$GitHubToken
)

$ErrorActionPreference = 'Stop'

$metadataJson = dotnet msbuild -nologo `
    -getProperty:Version `
    -getProperty:PopVelopackVersion `
    -getProperty:PopVelopackPackId `
    -getProperty:PopPublishRuntimeIdentifier `
    -getProperty:RepositoryUrl `
    -getProperty:Product `
    -getProperty:Authors `
    .\Pop.App\Pop.App.csproj

$metadata = $metadataJson | ConvertFrom-Json
$version = $metadata.Properties.Version
$velopackVersion = $metadata.Properties.PopVelopackVersion
$packId = $metadata.Properties.PopVelopackPackId
$runtime = $metadata.Properties.PopPublishRuntimeIdentifier
$repoUrl = $metadata.Properties.RepositoryUrl
$product = $metadata.Properties.Product
$authors = $metadata.Properties.Authors

$publishDir = Join-Path $OutputRoot 'publish'
$releaseDir = Join-Path $OutputRoot 'Releases'
$toolPath = Join-Path $env:TEMP 'vpk-tools'
$vpkExe = Join-Path $toolPath 'vpk.exe'

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

if (Test-Path $releaseDir) {
    Remove-Item $releaseDir -Recurse -Force
}

if (-not $SkipTests) {
    dotnet test .\Pop.sln --configuration Release
}

dotnet publish .\Pop.App\Pop.App.csproj `
    --configuration Release `
    --runtime $runtime `
    --self-contained true `
    --output $publishDir

if (-not (Test-Path $vpkExe)) {
    New-Item -ItemType Directory -Path $toolPath -Force | Out-Null
    dotnet tool install --tool-path $toolPath vpk --version $velopackVersion | Out-Null
}

if ($UploadToGitHub) {
    if ([string]::IsNullOrWhiteSpace($GitHubToken)) {
        throw 'GitHubToken is required when -UploadToGitHub is set.'
    }

    try {
        & $vpkExe download github `
            --repoUrl $repoUrl `
            --token $GitHubToken `
            --channel $Channel `
            --outputDir $releaseDir
    }
    catch {
        Write-Warning "Skipping previous-release download: $($_.Exception.Message)"
    }
}

& $vpkExe pack `
    --packId $packId `
    --packVersion $version `
    --packDir $publishDir `
    --mainExe Pop.App.exe `
    --packTitle $product `
    --packAuthors $authors `
    --runtime $runtime `
    --channel $Channel `
    --noPortable `
    --outputDir $releaseDir

Get-ChildItem $releaseDir -Filter *-Portable.zip | Remove-Item -Force

if ($UploadToGitHub) {
    & $vpkExe upload github `
        --repoUrl $repoUrl `
        --token $GitHubToken `
        --channel $Channel `
        --outputDir $releaseDir `
        --releaseName "$product v$version" `
        --tag "v$version" `
        --merge `
        --publish
}

Write-Host "Created release artifacts in $releaseDir"
