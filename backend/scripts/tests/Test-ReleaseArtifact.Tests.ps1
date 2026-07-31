$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$scriptRoot = Split-Path $PSScriptRoot -Parent
$validatorPath = Join-Path $scriptRoot 'Test-ReleaseArtifact.ps1'
$cleanupPath = Join-Path $scriptRoot 'Remove-ForbiddenReleaseArtifactContent.ps1'
$manifestPath = Join-Path $scriptRoot 'release-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('7dpanel-release-validation-' + [Guid]::NewGuid().ToString('N'))

function New-FixtureFile {
    param(
        [Parameter(Mandatory = $true)] [string] $Root,
        [Parameter(Mandatory = $true)] [string] $RelativePath,
        [string] $Content = 'fixture'
    )

    $path = Join-Path $Root $RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $parent = Split-Path $path -Parent
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    Set-Content -LiteralPath $path -Value $Content -Encoding UTF8
}

function New-ValidArtifact {
    param([Parameter(Mandatory = $true)] [string] $Root)

    New-Item -ItemType Directory -Path $Root -Force | Out-Null
    foreach ($relativePath in @(
        @($manifest.productAssemblies) +
        @($manifest.requiredManagedAssemblies) +
        @($manifest.requiredFiles) +
        @($manifest.requiredNativeAssets)
    )) {
        $content = if ($relativePath -eq 'config.example.json') { '{"port":18080}' } else { 'fixture' }
        New-FixtureFile $Root $relativePath $content
    }
    New-FixtureFile $Root $manifest.admin.index '<!doctype html><html></html>'
    New-FixtureFile $Root ($manifest.admin.assetsDirectory + '/app.12345678.js') 'export default true'
}

function Assert-ValidationFails {
    param(
        [Parameter(Mandatory = $true)] [string] $Artifact,
        [Parameter(Mandatory = $true)] [string] $ExpectedMessage,
        [string] $ReleaseManifest = $manifestPath
    )

    try {
        & $validatorPath -ArtifactPath $Artifact -ManifestPath $ReleaseManifest | Out-Null
        throw "Expected release validation to fail with '$ExpectedMessage'."
    }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") {
            throw "Expected failure containing '$ExpectedMessage', got: $($_.Exception.Message)"
        }
    }
}

try {
    $validArtifact = Join-Path $temporaryRoot 'valid'
    New-ValidArtifact $validArtifact
    & $validatorPath -ArtifactPath $validArtifact -ManifestPath $manifestPath | Out-Null

    foreach ($productAssembly in @($manifest.productAssemblies)) {
        $artifact = Join-Path $temporaryRoot ('missing-' + $productAssembly)
        New-ValidArtifact $artifact
        Remove-Item -LiteralPath (Join-Path $artifact $productAssembly) -Force
        Assert-ValidationFails $artifact $productAssembly
    }

    foreach ($nativeAsset in @($manifest.requiredNativeAssets)) {
        $artifact = Join-Path $temporaryRoot ('missing-native-' + [Guid]::NewGuid().ToString('N'))
        New-ValidArtifact $artifact
        Remove-Item -LiteralPath (Join-Path $artifact $nativeAsset.Replace('/', [System.IO.Path]::DirectorySeparatorChar)) -Force
        Assert-ValidationFails $artifact $nativeAsset

        $misplacedArtifact = Join-Path $temporaryRoot ('misplaced-native-' + [Guid]::NewGuid().ToString('N'))
        New-ValidArtifact $misplacedArtifact
        New-FixtureFile $misplacedArtifact ('runtimes/unapproved/native/' + [System.IO.Path]::GetFileName($nativeAsset))
        Assert-ValidationFails $misplacedArtifact 'misplaced native assets'
    }

    foreach ($forbiddenName in @($manifest.forbiddenFileNames)) {
        $artifact = Join-Path $temporaryRoot ('forbidden-' + [Guid]::NewGuid().ToString('N'))
        New-ValidArtifact $artifact
        New-FixtureFile $artifact ('nested/' + $forbiddenName)
        Assert-ValidationFails $artifact 'forbidden assemblies or assets'
    }

    foreach ($forbiddenPath in @($manifest.forbiddenRelativePaths)) {
        $artifact = Join-Path $temporaryRoot ('forbidden-path-' + [Guid]::NewGuid().ToString('N'))
        New-ValidArtifact $artifact
        New-FixtureFile $artifact $forbiddenPath
        Assert-ValidationFails $artifact $forbiddenPath
    }

    $referenceArtifact = Join-Path $temporaryRoot 'reference-content'
    New-ValidArtifact $referenceArtifact
    New-FixtureFile $referenceArtifact 'nested/7dtd-reference/private-evidence.dll'
    Assert-ValidationFails $referenceArtifact "forbidden path segment '7dtd-reference'"

    $missingAdminIndexArtifact = Join-Path $temporaryRoot 'missing-admin-index'
    New-ValidArtifact $missingAdminIndexArtifact
    Remove-Item -LiteralPath (Join-Path $missingAdminIndexArtifact 'wwwroot/index.html') -Force
    Assert-ValidationFails $missingAdminIndexArtifact 'Admin index is missing'

    $emptyAdminAssetsArtifact = Join-Path $temporaryRoot 'empty-admin-assets'
    New-ValidArtifact $emptyAdminAssetsArtifact
    Remove-Item -LiteralPath (Join-Path $emptyAdminAssetsArtifact 'wwwroot/assets/app.12345678.js') -Force
    Assert-ValidationFails $emptyAdminAssetsArtifact 'Admin assets are missing or empty'

    $invalidConfigArtifact = Join-Path $temporaryRoot 'invalid-config'
    New-ValidArtifact $invalidConfigArtifact
    Set-Content -LiteralPath (Join-Path $invalidConfigArtifact 'config.example.json') -Value '{' -Encoding UTF8
    Assert-ValidationFails $invalidConfigArtifact 'config example is not valid JSON'

    $invalidManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $invalidManifest.productAssemblies = @($invalidManifest.productAssemblies | Select-Object -First 7)
    $invalidManifestPath = Join-Path $temporaryRoot 'invalid-release-manifest.json'
    $invalidManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $invalidManifestPath -Encoding UTF8
    Assert-ValidationFails $validArtifact 'exactly the eight current product assemblies' $invalidManifestPath

    $malformedManifestPath = Join-Path $temporaryRoot 'malformed-release-manifest.json'
    Set-Content -LiteralPath $malformedManifestPath -Value '{' -Encoding UTF8
    Assert-ValidationFails $validArtifact 'Release manifest is not valid JSON' $malformedManifestPath

    $missingAdminManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $missingAdminManifest.PSObject.Properties.Remove('admin')
    $missingAdminManifestPath = Join-Path $temporaryRoot 'missing-admin-release-manifest.json'
    $missingAdminManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $missingAdminManifestPath -Encoding UTF8
    Assert-ValidationFails $validArtifact "property 'admin' is missing" $missingAdminManifestPath

    $unsafePathManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $unsafePathManifest.forbiddenRelativePaths = @($unsafePathManifest.forbiddenRelativePaths) + 'payload:stream'
    $unsafePathManifestPath = Join-Path $temporaryRoot 'unsafe-path-release-manifest.json'
    $unsafePathManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $unsafePathManifestPath -Encoding UTF8
    Assert-ValidationFails $validArtifact 'unsafe relative path' $unsafePathManifestPath

    $cleanupArtifact = Join-Path $temporaryRoot 'cleanup'
    New-ValidArtifact $cleanupArtifact
    New-FixtureFile $cleanupArtifact 'nested/Newtonsoft.Json.dll'
    New-FixtureFile $cleanupArtifact 'e_sqlite3.dll'
    New-FixtureFile $cleanupArtifact 'runtimes/win-arm/native/e_sqlite3.dll'
    New-FixtureFile $cleanupArtifact 'runtimes/win-x86/native/e_sqlite3.dll'

    & $cleanupPath -ArtifactPath $cleanupArtifact -ManifestPath $manifestPath | Out-Null

    foreach ($removedPath in @(
        'nested/Newtonsoft.Json.dll',
        'e_sqlite3.dll',
        'runtimes/win-arm/native/e_sqlite3.dll',
        'runtimes/win-x86/native/e_sqlite3.dll'
    )) {
        if (Test-Path -LiteralPath (Join-Path $cleanupArtifact $removedPath.Replace(
            '/',
            [System.IO.Path]::DirectorySeparatorChar))) {
            throw "Release cleanup did not remove: $removedPath"
        }
    }
    & $validatorPath -ArtifactPath $cleanupArtifact -ManifestPath $manifestPath | Out-Null

    Assert-ValidationFails ([System.IO.Path]::GetPathRoot($validArtifact)) 'must not be a filesystem root'

    Write-Output 'Release artifact validator tests passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
