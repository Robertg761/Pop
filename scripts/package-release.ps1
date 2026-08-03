param(
    [string]$OutputRoot = "artifacts",
    [string]$Channel = "win",
    [switch]$SkipTests,
    [switch]$UploadToGitHub,
    [string]$GitHubToken,
    [switch]$PublishUpdateFeed
)

$ErrorActionPreference = 'Stop'

function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$Description,
        [Parameter(Mandatory)][scriptblock]$Command
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot $OutputRoot
}

$solutionPath = Join-Path $repoRoot 'Pop.sln'
$projectPath = Join-Path $repoRoot 'src\Pop.App.Windows\Pop.App.Windows.csproj'

$metadataJson = Invoke-Native 'Reading release metadata via dotnet msbuild' {
    dotnet msbuild -nologo `
        -getProperty:Version `
        -getProperty:PopVelopackVersion `
        -getProperty:PopVelopackPackId `
        -getProperty:PopPublishRuntimeIdentifier `
        -getProperty:RepositoryUrl `
        -getProperty:PopUpdateFeedBranch `
        -getProperty:PopUpdateFeedPath `
        -getProperty:Product `
        -getProperty:Authors `
        $projectPath
}

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

if ($PublishUpdateFeed -and $Channel -ne 'win') {
    throw "The update feed path '$updateFeedPath' serves installed clients on the default 'win' channel. Refusing to publish channel '$Channel' artifacts to the stable feed."
}

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
    Invoke-Native 'dotnet test' {
        dotnet test $solutionPath --configuration Release
    }
}

Invoke-Native 'dotnet publish' {
    dotnet publish $projectPath `
        --configuration Release `
        --runtime $runtime `
        --self-contained true `
        --output $publishDir
}

if (-not (Test-Path $vpkExe)) {
    New-Item -ItemType Directory -Path $toolPath -Force | Out-Null
    Invoke-Native 'dotnet tool install vpk' {
        dotnet tool install --tool-path $toolPath vpk --version $velopackVersion | Out-Null
    }
}

Invoke-Native 'vpk pack' {
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
}

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

    $setupHash = (Get-FileHash $setupAsset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $setupChecksumPath = "$($setupAsset.FullName).sha256"
    [System.IO.File]::WriteAllText($setupChecksumPath, "$setupHash  $($setupAsset.Name)`n")

    $releaseTag = "v$version"
    $releaseExists = $true
    gh release view $releaseTag --repo $repoSlug *> $null
    if ($LASTEXITCODE -ne 0) {
        $releaseExists = $false
    }

    if (-not $releaseExists) {
        $createArgs = @(
            'release', 'create', $releaseTag, $setupAsset.FullName, $setupChecksumPath,
            '--repo', $repoSlug,
            '--title', "$product v$version"
        )

        if ($version.Contains('-')) {
            $createArgs += '--prerelease'
        }

        & gh @createArgs
        if ($LASTEXITCODE -ne 0) {
            Invoke-Native 'gh release upload' {
                & gh release upload $releaseTag $setupAsset.FullName $setupChecksumPath --repo $repoSlug --clobber
            }
        }
    }
    else {
        Invoke-Native 'gh release upload' {
            & gh release upload $releaseTag $setupAsset.FullName $setupChecksumPath --repo $repoSlug --clobber
        }
    }

    $releaseData = Invoke-Native 'gh release view' {
        gh release view $releaseTag --repo $repoSlug --json assets
    } | ConvertFrom-Json
    $assetsToDelete = @($releaseData.assets | Where-Object {
        $_.name -ne $setupAsset.Name -and
        $_.name -ne "$($setupAsset.Name).sha256" -and
        $_.name -notlike 'Pop-macos-arm64-*.zip*' -and
        $_.name -notlike 'Pop-macos-arm64-*.dmg*' -and
        $_.name -notlike 'Pop-linux-x64-*.AppImage*' -and
        $_.name -notlike 'Pop-linux-x64-*.tar.gz*'
    })
    foreach ($asset in $assetsToDelete) {
        Invoke-Native 'gh release delete-asset' {
            & gh release delete-asset $releaseTag $asset.name --repo $repoSlug --yes
        }
    }
}

if ($PublishUpdateFeed) {
    if ([string]::IsNullOrWhiteSpace($GitHubToken)) {
        throw 'GitHubToken is required when -PublishUpdateFeed is set.'
    }

    $feedFiles = @(Get-ChildItem $releaseDir -File | Where-Object { $_.Name -notlike '*-Setup.exe*' -and $_.Name -ne "assets.$Channel.json" })
    if ($feedFiles.Count -eq 0) {
        throw 'Unable to find any update-feed artifacts to publish.'
    }

    $feedRoot = Join-Path $env:TEMP 'pop-update-feed'
    # Pass the token through a per-invocation header instead of embedding it in the
    # remote URL so it never persists in the temp clone's .git/config.
    $authToken = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("x-access-token:$GitHubToken"))
    $authConfig = "http.extraheader=AUTHORIZATION: basic $authToken"

    if (Test-Path $feedRoot) {
        Remove-Item $feedRoot -Recurse -Force
    }

    try {
        git -c $authConfig clone --branch $updateFeedBranch --single-branch $repoUrl $feedRoot *> $null
        if ($LASTEXITCODE -ne 0) {
            if (Test-Path $feedRoot) {
                Remove-Item $feedRoot -Recurse -Force
            }

            New-Item -ItemType Directory -Path $feedRoot -Force | Out-Null
            Push-Location $feedRoot
            try {
                Invoke-Native 'git init' { git init | Out-Null }
                Invoke-Native 'git checkout --orphan' { git checkout --orphan $updateFeedBranch | Out-Null }
                Invoke-Native 'git remote add' { git remote add origin $repoUrl }
            }
            finally {
                Pop-Location
            }
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
        try {
            Invoke-Native 'git config' { git config user.name 'github-actions[bot]' }
            Invoke-Native 'git config' { git config user.email '41898282+github-actions[bot]@users.noreply.github.com' }
            Invoke-Native 'git add' { git add --all $updateFeedPath }
            git diff --cached --quiet
            if ($LASTEXITCODE -ne 0) {
                Invoke-Native 'git commit' { git commit -m "Publish update feed for $version" | Out-Null }
                Invoke-Native 'git push' { git -c $authConfig push origin $updateFeedBranch | Out-Null }
            }
        }
        finally {
            Pop-Location
        }
    }
    finally {
        if (Test-Path $feedRoot) {
            Remove-Item $feedRoot -Recurse -Force
        }
    }
}

Write-Host "Created release artifacts in $releaseDir"
