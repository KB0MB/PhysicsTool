param([string]$Assets = 'src/PhysicsTool.Desktop/obj/project.assets.json')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$assetsPath = Join-Path $root $Assets
$data = Get-Content $assetsPath -Raw | ConvertFrom-Json
$packages = @($data.packageFolders.PSObject.Properties.Name)[0]
$destination = Join-Path $root 'Licenses'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$inventory = @()
$downloaded = @{}
$vendored = @{
    'BCnEncoder.Net' = 'BCnEncoder'; 'Acornima' = 'Acornima'; 'Jint' = 'Jint'
    'MIConvexHull' = 'MIConvexHull'; 'Revrs' = 'Revrs'; 'VYaml' = 'VYaml'
    'VYaml.Annotations' = 'VYaml'; 'ZstdSharp.Port' = 'ZstdSharp'; 'Tkmm.BymlLibrary' = 'BymlLibrary'
}
foreach ($p in $data.libraries.PSObject.Properties) {
    if ($p.Value.type -ne 'package') { continue }
    $directory = Join-Path $packages $p.Value.path
    [xml]$spec = Get-Content (Get-ChildItem $directory -Filter *.nuspec | Select-Object -First 1).FullName
    $m = $spec.package.metadata
    $name = $p.Name.Replace('/', '-')
    $target = Join-Path $destination $name
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    $files = @($p.Value.files | Where-Object { $_ -match '(?i)(^|/)(licen[cs]e|third.party.notices|ofl)(\.[^/]*)?$' })
    foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $directory $file) -Destination (Join-Path $target (Split-Path $file -Leaf)) -Force }
    $repo = [string]$m.repository.url
    $commit = [string]$m.repository.commit
    $packageId = $p.Name.Split('/')[0]
    if (-not $files.Count -and $vendored.ContainsKey($packageId)) {
        Copy-Item (Join-Path $root ("PhysicsTool/ThirdParty/" + $vendored[$packageId] + '-LICENSE.txt')) (Join-Path $target 'LICENSE.txt') -Force
    } elseif (-not $files.Count) {
        $repo = $repo.TrimEnd('/').Replace('.git','')
        if ($repo -eq 'https://github.com/krenyy/HKX2') { $repo = 'https://github.com/kronerm/HKX2' }
        $ref = if ($commit) { $commit } else { 'HEAD' }
        $key = "$repo/$ref"
        if ($downloaded.ContainsKey($key)) {
            Copy-Item -LiteralPath $downloaded[$key] -Destination (Join-Path $target 'LICENSE.txt') -Force
        } else {
            if (-not $repo.StartsWith('https://github.com/')) { throw "No local license or supported source for $name" }
            $raw = $repo.Replace('https://github.com/', 'https://raw.githubusercontent.com/')
            $found = $false
            foreach ($candidate in @('LICENSE','LICENSE.md','LICENSE.txt','licence.md','License.txt','LICENSE.TXT','COPYING')) {
                try {
                    Invoke-WebRequest "$raw/$ref/$candidate" -OutFile (Join-Path $target 'LICENSE.txt')
                    $downloaded[$key] = Join-Path $target 'LICENSE.txt'
                    $found = $true
                    break
                } catch { if ($_.Exception.Response.StatusCode.value__ -notin @(404,400)) { throw } }
            }
            if (-not $found) { throw "License not found: $name ($repo)" }
        }
    }
    $inventory += [ordered]@{ Package = $p.Name; License = [string]$m.license.'#text'; Source = $repo; Commit = $commit }
}
$inventory | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $destination 'NuGet-inventory.json') -Encoding utf8
Get-ChildItem (Join-Path $root 'PhysicsTool/ThirdParty') -Filter '*LICENSE.txt' |
    Where-Object Name -NotMatch 'DirectX|Tegra' |
    Copy-Item -Destination $destination -Force
Copy-Item (Join-Path $root 'PhysicsTool/Formats/Collision/IceHail/LICENSE.txt') (Join-Path $destination 'Ice-Hail-LICENSE.txt') -Force
Invoke-WebRequest 'https://raw.githubusercontent.com/rsms/inter/master/LICENSE.txt' -OutFile (Join-Path $destination 'Inter-OFL.txt')
Invoke-WebRequest 'https://raw.githubusercontent.com/lucide-icons/lucide/main/LICENSE' -OutFile (Join-Path $destination 'Lucide-ISC.txt')
Invoke-WebRequest 'https://codeload.github.com/TKMM-Team/Tkmm.BymlLibrary/zip/6f6ac2d38410d8e797262b7132fde08982938e53' -OutFile (Join-Path $destination 'Tkmm.BymlLibrary-1.2.3-source.zip')
Write-Output "Collected licenses for $($inventory.Count) NuGet packages, Inter, Lucide and vendored code."
