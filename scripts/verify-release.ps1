param([string]$Version = '2.1.0', [switch]$SkipSource)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'zip-unix.ps1')
$root = Split-Path $PSScriptRoot
$directory = Join-Path $root "artifacts/releases/$Version"
function Read-Entry($Archive, [string]$Name) {
    $entry = $Archive.GetEntry($Name)
    if ($null -eq $entry) { throw "Archive entry missing: $Name" }
    $stream = $entry.Open(); $memory = [IO.MemoryStream]::new()
    try { $stream.CopyTo($memory); return ,$memory.ToArray() }
    finally { $stream.Dispose(); $memory.Dispose() }
}
function Test-Architecture([byte[]]$Bytes, [string]$Rid) {
    if ($Rid.StartsWith('win-')) {
        $header = [BitConverter]::ToInt32($Bytes, 0x3c)
        $machine = [BitConverter]::ToUInt16($Bytes, $header + 4)
        $expected = if ($Rid.EndsWith('arm64')) { 0xaa64 } else { 0x8664 }
    } elseif ($Rid.StartsWith('linux-')) {
        if ([Text.Encoding]::ASCII.GetString($Bytes, 1, 3) -ne 'ELF') { throw 'Not an ELF binary.' }
        $machine = [BitConverter]::ToUInt16($Bytes, 18)
        $expected = if ($Rid.EndsWith('arm64')) { 183 } else { 62 }
    } else {
        if ([BitConverter]::ToUInt32($Bytes, 0) -ne 0xfeedfacfL) { throw 'Not a 64-bit Mach-O binary.' }
        $machine = [BitConverter]::ToUInt32($Bytes, 4)
        $expected = if ($Rid.EndsWith('arm64')) { 0x100000c } else { 0x1000007 }
    }
    if ($machine -ne $expected) { throw "Wrong binary architecture for $Rid ($machine)." }
}
foreach ($rid in @('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64')) {
    $file = Join-Path $directory "PhysicsTool-$Version-$rid.zip"
    Set-ZipUnixOrigin $file -VerifyOnly
    $zip = [IO.Compression.ZipFile]::OpenRead($file)
    try {
        $prefix = "PhysicsTool-$Version-$rid/"
        $app = $prefix + $(if ($rid.StartsWith('osx-')) { 'PhysicsTool.app/Contents/MacOS/' } else { '' })
        $resources = if ($rid.StartsWith('osx-')) { $prefix + 'PhysicsTool.app/Contents/Resources/' } else { $app }
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -match '(^/|(^|/)\.\.(/|$)|^[A-Za-z]:)' -or
                $entry.FullName -match '(?i)(\.pdb$|/settings\.json$|/actor-folders\.json$|/actor-catalog-[^/]+\.json$|\.(hkcl|bphcl|bphyssb|bphhb|hkrg|sbfres|bfres|pack|sbactorpack|txtg|zs)$)') { throw "Unexpected private/unsafe file: $($entry.FullName)" }
        }
        foreach ($required in @('LICENSE','THIRD_PARTY_NOTICES.txt','Licenses/Inter-OFL.txt','Licenses/DotNetRuntime/LICENSE.TXT',
            'Licenses/DotNetRuntime/THIRD-PARTY-NOTICES.TXT','Licenses/MeshCodec-source.zip','Licenses/Tkmm.BymlLibrary-1.2.3-source.zip')) {
            $base = if ($required.StartsWith('Licenses/')) { $resources } else { $app }
            if (-not $zip.GetEntry($base + $required)) { throw "Missing notice or source: $rid $required" }
        }
        $helperSourceBytes = Read-Entry $zip ($resources + 'Licenses/MeshCodec-source.zip')
        $helperSourceStream = [IO.MemoryStream]::new($helperSourceBytes, $false)
        $helperSource = [IO.Compression.ZipArchive]::new($helperSourceStream)
        try {
            if ($helperSource.Entries | Where-Object FullName -Like '*SwitchToolbox.MeshCodecNative.cpp') { throw 'Obsolete wrapper in helper source archive.' }
            if (-not $helperSource.GetEntry('LOCAL_CHANGES.md')) { throw 'Helper provenance notice missing.' }
        } finally { $helperSource.Dispose(); $helperSourceStream.Dispose() }
        $suffix = if ($rid.StartsWith('win-')) { '.exe' } else { '' }
        foreach ($binary in @("PhysicsTool$suffix", "Tools/MeshCodec/$rid/PhysicsTool.MeshCodec$suffix")) {
            $bytes = Read-Entry $zip ($app + $binary)
            Test-Architecture $bytes $rid
            if (-not $rid.StartsWith('win-') -and (($zip.GetEntry($app + $binary).ExternalAttributes -shr 16) -band 0x49) -ne 0x49) {
                throw "Unix executable mode missing: $rid $binary"
            }
        }
        foreach ($binary in @('PhysicsTool.dll', 'PhysicsTool.Core.dll', "Tools/MeshCodec/$rid/PhysicsTool.MeshCodec$suffix")) {
            $text = [Text.Encoding]::UTF8.GetString((Read-Entry $zip ($app + $binary)))
            foreach ($privatePath in @($root, $root.Replace('\','/'), $HOME, $HOME.Replace('\','/'))) {
                if ($text.Contains($privatePath, [StringComparison]::OrdinalIgnoreCase)) { throw "Developer path leaked into $rid $binary" }
            }
        }
        if ($zip.Entries | Where-Object { $_.FullName -match '/Tools/MeshCodec/([^/]+)/' -and $Matches[1] -ne $rid }) { throw "Foreign native helper in $rid" }
        if ($rid.StartsWith('osx-')) {
            [xml]$plist = [Text.Encoding]::UTF8.GetString((Read-Entry $zip ($prefix + 'PhysicsTool.app/Contents/Info.plist')))
            if (-not $zip.GetEntry($prefix + 'PhysicsTool.app/Contents/Resources/PhysicsTool.icns')) { throw 'macOS icon missing.' }
        }
        Write-Output "PASS: $rid architecture, executable permissions, license/source notices and privacy checks."
    } finally { $zip.Dispose() }
}
if (-not $SkipSource) {
$source = [IO.Compression.ZipFile]::OpenRead((Join-Path $directory "PhysicsTool-$Version-source.zip"))
try {
    foreach ($entry in $source.Entries) {
        if ($entry.FullName -match '/(\.git|artifacts|bin|obj|VCRuntime|NativeFbx)/' -or $entry.FullName -match '\.(pdb|log|hkcl|bphcl|sbfres|sbactorpack)$') { throw "Unexpected source archive file: $($entry.FullName)" }
    }
    if (-not $source.GetEntry("PhysicsTool-$Version-source/PhysicsTool.slnx")) { throw 'Primary solution missing from source archive.' }
} finally { $source.Dispose() }
}
foreach ($line in Get-Content (Join-Path $directory 'SHA256SUMS.txt')) {
    $hash, $name = $line -split '  ', 2
    if ((Get-FileHash (Join-Path $directory $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "Checksum mismatch: $name" }
}
Write-Output 'PASS: all SHA-256 checksums and requested source archive checks.'
