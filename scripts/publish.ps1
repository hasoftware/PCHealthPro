# Run from PowerShell on Windows with the .NET 10 SDK.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$props = [xml](Get-Content -Raw (Join-Path $root 'Directory.Build.props'))
$version = $props.Project.PropertyGroup.Version
$product = $props.Project.PropertyGroup.Product
$out = Join-Path $root 'artifacts'
$stage = Join-Path $out ('build-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $stage 'portable'
$name = "PCHealthPro-$version-win-x64"
Push-Location $root
try {
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    dotnet tool restore
    if ($LASTEXITCODE) { throw 'WiX restore failed.' }
    dotnet publish src/PCHealthPro.App -c Release -r win-x64 --self-contained true -o $payload -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -p:BaseOutputPath=bin/package/ -m:1
    if ($LASTEXITCODE) { throw 'Publish failed.' }
    foreach ($required in 'PCHealthPro.App.exe','coreclr.dll','Microsoft.ui.xaml.dll','PCHealthPro.App.pri') {
        if (!(Test-Path -LiteralPath (Join-Path $payload $required))) { throw "Missing runtime payload: $required" }
    }
    $msi = Join-Path $stage "$name.msi"
    dotnet tool run wix -- build installer/Package.wxs -arch x64 -d "Payload=$payload" -d "Version=$version" -d "Product=$product" -intermediateFolder (Join-Path $stage 'obj') -o $msi
    if ($LASTEXITCODE) { throw 'MSI build failed.' }
    $zip = Join-Path $stage "$name.zip"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip)
    $hashes = foreach ($file in $zip,$msi) {
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([IO.Path]::GetFileName($file))"
    }
    $sum = Join-Path $stage "$name.sha256"
    [IO.File]::WriteAllLines($sum, [string[]]$hashes)
    foreach ($file in $zip,$msi,$sum) { Copy-Item -LiteralPath $file -Destination $out -Force }
    Write-Output "Packages: $out"
} finally {
    Pop-Location
    if (Test-Path -LiteralPath $stage) {
        $resolved = (Resolve-Path -LiteralPath $stage).Path
        if ([IO.Path]::GetDirectoryName($resolved) -eq $out -and [IO.Path]::GetFileName($resolved) -match '^build-[a-f0-9]{32}$') {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
