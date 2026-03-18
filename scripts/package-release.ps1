param(
    [string]$OutputRoot = "artifacts",
    [string]$Channel = "win",
    [switch]$SkipTests,
    [switch]$UploadToGitHub,
    [string]$GitHubToken,
    [switch]$PublishUpdateFeed
)

$ErrorActionPreference = 'Stop'

$metadataJson = dotnet msbuild -nologo `
    -getProperty:Version `
    -getProperty:PopVelopackVersion `
    -getProperty:PopVelopackPackId `
    -getProperty:PopPublishRuntimeIdentifier `
    -getProperty:RepositoryUrl `
    -getProperty:PopUpdateFeedBranch `
    -getProperty:PopUpdateFeedPath `
    -getProperty:Product `
    -getProperty:Authors `
    .\src\Pop.App.Windows\Pop.App.Windows.csproj

$metadata = $metadataJson | ConvertFrom-Json
$version = $metadata.Properties.Version
$velopackVersion = $metadata.Properties.PopVelopackVersion
$packId = $metadata.Properties.PopVelopackPackId
$runtime = $metadata.Properties.PopPublishRuntimeIdentifier
$repoUrl = $metadata.Properties.RepositoryUrl
$repoSlug = ($repoUrl -replace '^https://github\.com/', '').TrimEnd('/')
$updateFeedBranch = $metadata.Properties.PopUpdateFeedBranch
$updateFeedPath = $metadata.Properties.PopUpdateFeedPath
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

dotnet publish .\src\Pop.App.Windows\Pop.App.Windows.csproj `
    --configuration Release `
    --runtime $runtime `
    --self-contained true `
    --output $publishDir

if (-not (Test-Path $vpkExe)) {
    New-Item -ItemType Directory -Path $toolPath -Force | Out-Null
    dotnet tool install --tool-path $toolPath vpk --version $velopackVersion | Out-Null
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
    if ([string]::IsNullOrWhiteSpace($GitHubToken)) {
        throw 'GitHubToken is required when -UploadToGitHub is set.'
    }

    $env:GH_TOKEN = $GitHubToken
    $setupAsset = Get-ChildItem $releaseDir -Filter '*-Setup.exe' | Select-Object -First 1
    if (-not $setupAsset) {
        throw 'Unable to find the generated Setup.exe asset.'
    }

    $releaseTag = "v$version"
    $releaseExists = $true
    gh release view $releaseTag --repo $repoSlug *> $null
    if ($LASTEXITCODE -ne 0) {
        $releaseExists = $false
    }

    if (-not $releaseExists) {
        $createArgs = @(
            'release', 'create', $releaseTag, $setupAsset.FullName,
            '--repo', $repoSlug,
            '--title', "$product v$version"
        )

        if ($version.Contains('-')) {
            $createArgs += '--prerelease'
        }

        & gh @createArgs
    }
    else {
        & gh release upload $releaseTag $setupAsset.FullName --repo $repoSlug --clobber
    }

    $releaseData = gh release view $releaseTag --repo $repoSlug --json assets | ConvertFrom-Json
    $assetsToDelete = @($releaseData.assets | Where-Object { $_.name -ne $setupAsset.Name })
    foreach ($asset in $assetsToDelete) {
        & gh release delete-asset $releaseTag $asset.name --repo $repoSlug --yes
    }
}

if ($PublishUpdateFeed) {
    if ([string]::IsNullOrWhiteSpace($GitHubToken)) {
        throw 'GitHubToken is required when -PublishUpdateFeed is set.'
    }

    $feedFiles = @(Get-ChildItem $releaseDir -File | Where-Object { $_.Name -notlike '*-Setup.exe' -and $_.Name -ne 'assets.win.json' })
    if ($feedFiles.Count -eq 0) {
        throw 'Unable to find any update-feed artifacts to publish.'
    }

    $feedRoot = Join-Path $env:TEMP 'pop-update-feed'
    $feedRepoPrefix = "https://x-access-token:$GitHubToken@github.com/"
    $feedRepoUrl = $repoUrl -replace '^https://github\.com/', $feedRepoPrefix

    if (Test-Path $feedRoot) {
        Remove-Item $feedRoot -Recurse -Force
    }

    git clone --branch $updateFeedBranch --single-branch $feedRepoUrl $feedRoot *> $null
    if ($LASTEXITCODE -ne 0) {
        if (Test-Path $feedRoot) {
            Remove-Item $feedRoot -Recurse -Force
        }

        New-Item -ItemType Directory -Path $feedRoot -Force | Out-Null
        Push-Location $feedRoot
        git init | Out-Null
        git checkout --orphan $updateFeedBranch | Out-Null
        git remote add origin $feedRepoUrl
        Pop-Location
    }

    $channelDir = Join-Path $feedRoot $updateFeedPath
    if (Test-Path $channelDir) {
        Remove-Item $channelDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $channelDir -Force | Out-Null
    foreach ($file in $feedFiles) {
        Copy-Item $file.FullName (Join-Path $channelDir $file.Name) -Force
    }

    Push-Location $feedRoot
    git config user.name 'github-actions[bot]'
    git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
    git add --all $updateFeedPath
    git diff --cached --quiet
    if ($LASTEXITCODE -ne 0) {
        git commit -m "Publish update feed for $version" | Out-Null
        git push origin $updateFeedBranch | Out-Null
    }
    Pop-Location
}

Write-Host "Created release artifacts in $releaseDir"
