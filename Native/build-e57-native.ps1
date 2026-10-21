param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$libE57Commit = "2b84ff0d4ee451d9c191a5eab48a889db8823cc6"
$xercesVersion = "3.3.0"
$nativeRoot = Join-Path $PSScriptRoot "EasyTryE57Native"
$buildRoot = Join-Path $PSScriptRoot ".build-e57"
$downloadRoot = Join-Path $buildRoot "downloads"
$sourceRoot = Join-Path $buildRoot "sources"
$installRoot = Join-Path $buildRoot "install"

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw "Visual Studio Installer vswhere.exe was not found."
}

$visualStudio = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
if ([string]::IsNullOrWhiteSpace($visualStudio)) {
    throw "Visual Studio 2022 with the x64 C++ toolchain was not found."
}

$cmake = Join-Path $visualStudio `
    "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
if (-not (Test-Path -LiteralPath $cmake)) {
    throw "The Visual Studio bundled CMake executable was not found."
}

New-Item -ItemType Directory -Path $downloadRoot, $sourceRoot, $installRoot -Force |
    Out-Null

function Get-SourceArchive {
    param(
        [Parameter(Mandatory)]
        [string]$Uri,
        [Parameter(Mandatory)]
        [string]$Destination
    )

    if (Test-Path -LiteralPath $Destination) {
        return
    }

    & curl.exe --fail --location --connect-timeout 20 --max-time 600 `
        --retry 5 --retry-all-errors --output $Destination $Uri
    if ($LASTEXITCODE -ne 0) {
        throw "Source download failed: $Uri"
    }
}

$xercesArchive = Join-Path $downloadRoot "xerces-c-$xercesVersion.zip"
Get-SourceArchive `
    "https://codeload.github.com/apache/xerces-c/zip/refs/tags/v$xercesVersion" `
    $xercesArchive

$xercesSource = Join-Path $sourceRoot "xerces-c-$xercesVersion"
$libE57Source = Join-Path $sourceRoot "libE57Format"
if (-not (Test-Path -LiteralPath $xercesSource)) {
    Expand-Archive -LiteralPath $xercesArchive -DestinationPath $sourceRoot
}
if (-not (Test-Path -LiteralPath (Join-Path $libE57Source ".git"))) {
    & git clone --no-checkout https://github.com/asmaloney/libE57Format.git `
        $libE57Source
    if ($LASTEXITCODE -ne 0) { throw "The libE57Format clone failed." }
}
$libE57GitRoot = & git -c safe.directory="*" -C $libE57Source `
    rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0) { throw "The libE57Format repository is invalid." }
$safeDirectoryOption = "safe.directory=$libE57GitRoot"
& git -c $safeDirectoryOption -C $libE57Source cat-file -e `
    "$libE57Commit`^{commit}"
if ($LASTEXITCODE -ne 0) {
    & git -c $safeDirectoryOption -C $libE57Source fetch --depth 1 `
        origin $libE57Commit
    if ($LASTEXITCODE -ne 0) {
        throw "The pinned libE57Format commit fetch failed."
    }
}
& git -c $safeDirectoryOption -C $libE57Source checkout --detach `
    $libE57Commit
if ($LASTEXITCODE -ne 0) {
    throw "The pinned libE57Format commit checkout failed."
}
& git -c $safeDirectoryOption -C $libE57Source submodule update `
    --init --recursive --depth 1
if ($LASTEXITCODE -ne 0) {
    throw "The libE57Format submodule update failed."
}

$xercesBuild = Join-Path $buildRoot "xerces-build"
& $cmake -S $xercesSource -B $xercesBuild -G "Visual Studio 17 2022" -A x64 `
    "-DCMAKE_INSTALL_PREFIX=$installRoot" `
    -DBUILD_SHARED_LIBS=OFF -Dnetwork=OFF -Dtranscoder=windows `
    -Dmessage-loader=inmemory
if ($LASTEXITCODE -ne 0) { throw "Xerces CMake configuration failed." }
& $cmake --build $xercesBuild --config $Configuration --target install --parallel
if ($LASTEXITCODE -ne 0) { throw "Xerces build failed." }

$libE57Build = Join-Path $buildRoot "libe57-build"
& $cmake -S $libE57Source -B $libE57Build -G "Visual Studio 17 2022" -A x64 `
    "-DCMAKE_INSTALL_PREFIX=$installRoot" `
    "-DCMAKE_PREFIX_PATH=$installRoot" `
    -DBUILD_SHARED_LIBS=OFF -DE57_BUILD_TEST=OFF `
    -DE57_ENABLE_DIAGNOSTIC_OUTPUT=OFF -DE57_RELEASE_LTO=OFF
if ($LASTEXITCODE -ne 0) { throw "libE57Format CMake configuration failed." }
& $cmake --build $libE57Build --config $Configuration --target install --parallel
if ($LASTEXITCODE -ne 0) { throw "libE57Format build failed." }

$bridgeBuild = Join-Path $buildRoot "bridge-build"
& $cmake -S $nativeRoot -B $bridgeBuild -G "Visual Studio 17 2022" -A x64 `
    "-DCMAKE_PREFIX_PATH=$installRoot"
if ($LASTEXITCODE -ne 0) { throw "EasyTry E57 bridge CMake configuration failed." }
& $cmake --build $bridgeBuild --config $Configuration --parallel
if ($LASTEXITCODE -ne 0) { throw "EasyTry E57 bridge build failed." }

$builtDll = Join-Path $bridgeBuild "$Configuration\EasyTryE57Native.dll"
$destination = Join-Path $PSScriptRoot `
    "..\EasyTry\native\x64\EasyTryE57Native.dll"
Copy-Item -LiteralPath $builtDll -Destination $destination -Force
Write-Output "The EasyTry E57 native bridge was updated: $destination"

$builtReader = Join-Path $bridgeBuild "$Configuration\EasyTryE57Reader.exe"
$pythonReaderDestination = Join-Path $PSScriptRoot `
    "..\..\1_PointCloudprocessing\native\x64\EasyTryE57Reader.exe"
Copy-Item -LiteralPath $builtReader -Destination $pythonReaderDestination -Force
Write-Output "The EasyTry E57 reader was updated: $pythonReaderDestination"
