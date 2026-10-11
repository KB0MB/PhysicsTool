param(
    [string]$Version = '2.0',
    [ValidateSet('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64')]
    [string[]]$Runtimes = @('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64'),
    [switch]$SkipSource
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'zip-unix.ps1')
if ($Version -notmatch '^\d+\.\d+(\.\d+)?(-[A-Za-z0-9.-]+)?$') { throw 'Invalid version label.' }
$root = Split-Path $PSScriptRoot
$output = Join-Path $root "artifacts/releases/$Version"
$work = Join-Path $root ('artifacts/package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output,$work -Force | Out-Null
function Zip-Folder([string]$Directory, [string]$File) {
    $stream = [IO.File]::Open($File, [IO.FileMode]::Create, [IO.FileAccess]::Write)
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in Get-ChildItem -LiteralPath $Directory -Recurse -File | Sort-Object FullName) {
            $relative = [IO.Path]::GetRelativePath($Directory, $item.FullName).Replace('\','/')
            $entry = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $item.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal)
            $executable = $item.Name -in @('PhysicsTool','PhysicsTool.MeshCodec') -or $item.Extension -in @('.sh','.dylib','.so')
            $entry.ExternalAttributes = $(if ($executable) { 0x81ED } else { 0x81A4 }) -shl 16
        }
    } finally { $archive.Dispose(); $stream.Dispose() }
    Set-ZipUnixOrigin $File
}
foreach ($rid in $Runtimes) {
    $helperName = 'PhysicsTool.MeshCodec' + $(if ($rid.StartsWith('win-')) { '.exe' } else { '' })
    $helper = Join-Path $root "PhysicsTool/ThirdParty/MeshCodec/runtimes/$rid/$helperName"
    if (-not (Test-Path -LiteralPath $helper)) { throw "Build the $rid MeshCodec helper first. Refusing to package missing model support." }
    $raw = Join-Path $work "$rid-publish"
    & dotnet publish (Join-Path $root 'src/PhysicsTool.Desktop/PhysicsTool.Desktop.csproj') -c Release -r $rid --self-contained true -o $raw -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -v quiet /clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $rid" }
    $stage = Join-Path $work "$rid-stage"
    $package = Join-Path $stage "PhysicsTool-$Version-$rid"
    $app = $package
    if ($rid.StartsWith('osx-')) { $app = Join-Path $package 'PhysicsTool.app/Contents/MacOS' }
    New-Item -ItemType Directory -Path $app -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $raw -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($raw, $file.FullName).Replace('\','/')
        if ($file.Extension -eq '.pdb') { continue }
        if ($file.Name -match '^(settings|actor-folders|actor-catalog-.*)\.json$') { throw "Private file in publish: $relative" }
        if ($relative.StartsWith('Tools/MeshCodec/') -and -not $relative.StartsWith("Tools/MeshCodec/$rid/")) { continue }
        $target = Join-Path $app $relative
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    Copy-Item (Join-Path $root 'docs/RELEASE_2.0.md') (Join-Path $package 'RELEASE_NOTES.md')
    $deps = Get-Content (Join-Path $app 'PhysicsTool.deps.json') -Raw | ConvertFrom-Json
    $runtime = @($deps.libraries.PSObject.Properties.Name | Where-Object { $_.StartsWith('runtimepack.Microsoft.NETCore.App.Runtime.') })[0]
    if (-not $runtime) { throw 'Self-contained runtime not found in dependency manifest.' }
    $runtimePath = $runtime.Replace('runtimepack.', '').ToLowerInvariant()
    $assets = Get-Content (Join-Path $root 'src/PhysicsTool.Desktop/obj/project.assets.json') -Raw | ConvertFrom-Json
    $runtimeDirectory = @($assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ $runtimePath } | Where-Object { Test-Path -LiteralPath $_ })[0]
    if (-not $runtimeDirectory) { throw "Runtime license source missing: $runtime" }
    $runtimeNotices = Join-Path $app 'Licenses/DotNetRuntime'
    New-Item -ItemType Directory -Path $runtimeNotices -Force | Out-Null
    foreach ($notice in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) { Copy-Item (Join-Path $runtimeDirectory $notice) $runtimeNotices }
    Copy-Item (Join-Path $root 'docs/RELEASING.md') (Join-Path $package 'BUILD_AND_LICENSES.md')
    if ($rid.StartsWith('osx-')) {
        $icon = [IO.File]::ReadAllBytes((Join-Path $root 'PhysicsTool/Resources/Branding/PhysicsTool.ico'))
        $png = $null
        for ($i = 0; $i -lt [BitConverter]::ToUInt16($icon, 4); $i++) {
            $offset = 6 + $i * 16
            if ($icon[$offset] -ne 0 -or $icon[$offset + 1] -ne 0) { continue }
            $length = [BitConverter]::ToInt32($icon, $offset + 8)
            $start = [BitConverter]::ToInt32($icon, $offset + 12)
            if ([Text.Encoding]::ASCII.GetString($icon, $start + 1, 3) -eq 'PNG') { $png = $icon[$start..($start + $length - 1)]; break }
        }
        if ($null -eq $png) { throw 'The application ICO needs a 256px PNG representation for macOS.' }
        $resources = Join-Path $package 'PhysicsTool.app/Contents/Resources'
        New-Item -ItemType Directory -Path $resources -Force | Out-Null
        $size = [BitConverter]::GetBytes([int]($png.Length + 16)); [Array]::Reverse($size)
        $chunkSize = [BitConverter]::GetBytes([int]($png.Length + 8)); [Array]::Reverse($chunkSize)
        [IO.File]::WriteAllBytes((Join-Path $resources 'PhysicsTool.icns'),
            [byte[]]([Text.Encoding]::ASCII.GetBytes('icns') + $size + [Text.Encoding]::ASCII.GetBytes('ic08') + $chunkSize + $png))
        $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>PhysicsTool</string>
<key>CFBundleDisplayName</key><string>PhysicsTool</string>
<key>CFBundleIdentifier</key><string>org.physicstool.desktop</string>
<key>CFBundleExecutable</key><string>PhysicsTool</string>
<key>CFBundleIconFile</key><string>PhysicsTool.icns</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>$Version</string>
<key>CFBundleVersion</key><string>$Version</string>
<key>NSHighResolutionCapable</key><true/>
<key>LSMinimumSystemVersion</key><string>15.0</string>
</dict></plist>
"@
        [IO.File]::WriteAllText((Join-Path $package 'PhysicsTool.app/Contents/Info.plist'), $plist)
        if ($IsMacOS) {
            & chmod +x (Join-Path $app 'PhysicsTool') (Join-Path $app "Tools/MeshCodec/$rid/$helperName")
            & codesign --force --deep --sign - (Join-Path $package 'PhysicsTool.app')
            if ($LASTEXITCODE -ne 0) { throw 'Ad-hoc macOS bundle signing failed.' }
        }
    }
    $exe = Join-Path $app ('PhysicsTool' + $(if ($rid.StartsWith('win-')) { '.exe' } else { '' }))
    foreach ($required in @($exe, (Join-Path $app 'LICENSE'), (Join-Path $app 'Licenses/Inter-OFL.txt'),
        (Join-Path $app 'Licenses/MeshCodec-source.zip'), (Join-Path $app "Tools/MeshCodec/$rid/$helperName"))) {
        if (-not (Test-Path -LiteralPath $required)) { throw "Missing release file: $required" }
    }
    Zip-Folder $stage (Join-Path $output "PhysicsTool-$Version-$rid.zip")
    Write-Output "Packaged $rid"
}
if (-not $SkipSource) {
    $sourceStage = Join-Path $work 'source'
    $sourceRoot = Join-Path $sourceStage "PhysicsTool-$Version-source"
    Push-Location $root
    try { $files = @(git -c core.quotepath=false ls-files -c --exclude-standard) | Sort-Object -Unique }
    finally { Pop-Location }
    foreach ($relative in $files) {
        if ($relative -match '^(artifacts/|PhysicsTool/NativeFbx/|PhysicsTool/ThirdParty/VCRuntime/)' -or
            $relative -match 'tegra_swizzle_x64\.dll$' -or $relative -eq 'PhysicsTool.WinForms.sln') { continue }
        $source = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
        $target = Join-Path $sourceRoot $relative
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
    }
    Zip-Folder $sourceStage (Join-Path $output "PhysicsTool-$Version-source.zip")
}
$hashes = Get-ChildItem -LiteralPath $output -Filter *.zip | Sort-Object Name | ForEach-Object {
    ((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()) + '  ' + $_.Name
}
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), $hashes)
Write-Output "Release archives: $output"
Write-Output "Build staging (not a release): $work"
