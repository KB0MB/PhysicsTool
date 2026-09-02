param(
    [string]$FbxSdkRoot,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($FbxSdkRoot)) {
    $sdkBase = "C:\Program Files\Autodesk\FBX\FBX SDK"
    $FbxSdkRoot = Get-ChildItem -LiteralPath $sdkBase -Directory |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if ([string]::IsNullOrWhiteSpace($FbxSdkRoot) -or -not (Test-Path -LiteralPath (Join-Path $FbxSdkRoot "include\fbxsdk.h"))) {
    throw "Autodesk FBX SDK was not found. Install the Windows C++ SDK or pass -FbxSdkRoot."
}

$buildDirectory = Join-Path $PSScriptRoot "build"
cmake -S $PSScriptRoot -B $buildDirectory -A x64 "-DFBX_SDK_ROOT=$FbxSdkRoot"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
cmake --build $buildDirectory --config $Configuration --parallel
exit $LASTEXITCODE