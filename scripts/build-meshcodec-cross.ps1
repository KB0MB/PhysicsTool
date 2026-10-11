param(
    [Parameter(Mandatory)][string]$Zig,
    [ValidateSet('linux-x64','linux-arm64','osx-x64','osx-arm64','win-arm64')]
    [string[]]$Runtimes = @('linux-x64','linux-arm64','osx-x64','osx-arm64','win-arm64')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$source = Join-Path $root 'PhysicsTool/ThirdParty/MeshCodec'
$targets = @{
    'linux-x64' = 'x86_64-linux-musl'
    'linux-arm64' = 'aarch64-linux-musl'
    'osx-x64' = 'x86_64-macos.12.0'
    'osx-arm64' = 'aarch64-macos.12.0'
    'win-arm64' = 'aarch64-windows-gnu'
}
$cSources = @('common/debug.c','common/entropy_common.c','common/error_private.c',
    'common/fse_decompress.c','common/xxhash.c','common/zstd_common.c',
    'decompress/huf_decompress.c','decompress/zstd_ddict.c',
    'decompress/zstd_decompress.c','decompress/zstd_decompress_block.c')
$sources = @($cSources | ForEach-Object { Join-Path $source "Upstream/lib/zstd/lib/$_" }) +
    @((Get-ChildItem "$source/Upstream/src" -Filter *.cpp).FullName) +
    @("$source/Native/main.cpp")
$includes = @('Upstream/src','Upstream/src/include','Upstream/lib/float16',
    'Upstream/lib/zstd/lib','Upstream/lib/zstd/lib/common','Upstream/lib/zstd/lib/decompress') |
    ForEach-Object { '-I' + (Join-Path $source $_) }
foreach ($rid in $Runtimes) {
    $build = Join-Path $root "artifacts/meshcodec-cross/$rid"
    $destination = Join-Path $source "runtimes/$rid"
    New-Item -ItemType Directory -Path $build,$destination -Force | Out-Null
    $objects = @()
    foreach ($file in $sources) {
        $object = Join-Path $build ((Split-Path $file -Leaf) + '.o')
        $objects += $object
        $cpp = $file.EndsWith('.cpp')
        $arguments = @($(if ($cpp) { 'c++' } else { 'cc' }), '-target', $targets[$rid],
            '-O2', '-DNDEBUG', '-DZSTD_DISABLE_ASM', '-D_CRT_SECURE_NO_WARNINGS', '-c', $file, '-o', $object) + $includes
        if ($cpp) { $arguments += '-std=c++20' }
        & $Zig @arguments
        if ($LASTEXITCODE -ne 0) { throw "Native compile failed: $rid, $file" }
    }
    $exe = 'PhysicsTool.MeshCodec' + $(if ($rid.StartsWith('win-')) { '.exe' } else { '' })
    $arguments = @('c++','-target',$targets[$rid],'-O2','-s','-o',(Join-Path $build $exe)) + $objects
    if ($rid.StartsWith('linux-')) { $arguments += '-static' }
    & $Zig @arguments
    if ($LASTEXITCODE -ne 0) { throw "Native link failed: $rid" }
    Copy-Item -LiteralPath (Join-Path $build $exe) -Destination (Join-Path $destination $exe) -Force
    Write-Output "Built MeshCodec: $rid"
}
