param(
    [string]$IsccPath,
    [string]$SigningThumbprint,
    [string]$SignToolPath,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$windowsRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $windowsRoot 'Tuno.PlaybackProbe\Tuno.PlaybackProbe.csproj'
$nugetConfig = Join-Path $windowsRoot 'NuGet.Config'
$packageCache = Join-Path $windowsRoot '.packages'
$buildProfile = Join-Path $windowsRoot '.build-profile'
$publishDir = Join-Path $windowsRoot 'dist\publish'
$script = Join-Path $PSScriptRoot 'Tuno.iss'
$versionMatch = Select-String -LiteralPath $script -Pattern '^#define AppVersion "([^"]+)"$' | Select-Object -First 1
if (-not $versionMatch) { throw 'Tuno.iss 缺少 AppVersion。' }
$version = $versionMatch.Matches[0].Groups[1].Value
$installer = Join-Path $windowsRoot "dist\Tuno-Setup-$version-win-x64.exe"

if (-not $IsccPath) {
    $found = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($found) { $IsccPath = $found.Source }
}
if (-not $IsccPath) {
    foreach ($candidate in @(
        'C:\Program Files\Inno Setup 7\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 7\ISCC.exe',
        (Join-Path $env:TEMP 'TunoInnoSetup7\ISCC.exe')
    )) {
        if (Test-Path -LiteralPath $candidate) { $IsccPath = $candidate; break }
    }
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw '需要 Inno Setup 7 的 ISCC.exe。请安装官方版本，或用 -IsccPath 指定路径。'
}
$compilerVersion = & $IsccPath --version
if ($LASTEXITCODE -ne 0 -or $compilerVersion -notmatch '^7\.') {
    throw '安装包脚本需要 Inno Setup 7 的 ISCC.exe。'
}

if ($SigningThumbprint) {
    $SigningThumbprint = $SigningThumbprint -replace '\s', ''
    $certificate = Get-ChildItem 'Cert:\CurrentUser\My' | Where-Object Thumbprint -eq $SigningThumbprint | Select-Object -First 1
    if (-not $certificate -or -not $certificate.HasPrivateKey -or
        $certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date) -or
        $certificate.EnhancedKeyUsageList.ObjectId -notcontains '1.3.6.1.5.5.7.3.3') {
        throw '当前用户证书库中没有有效且带私钥的代码签名证书。'
    }
    if (-not $SignToolPath) {
        $found = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if ($found) { $SignToolPath = $found.Source }
    }
    if (-not $SignToolPath) {
        foreach ($kitRoot in @('C:\Program Files (x86)\Windows Kits\10\bin', 'D:\Windows Kits\10\bin')) {
            if (-not (Test-Path -LiteralPath $kitRoot)) { continue }
            $candidate = Get-ChildItem -LiteralPath $kitRoot -Directory |
                Sort-Object Name -Descending |
                ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
                Where-Object { Test-Path -LiteralPath $_ } |
                Select-Object -First 1
            if ($candidate) { $SignToolPath = $candidate; break }
        }
    }
    if (-not $SignToolPath -or -not (Test-Path -LiteralPath $SignToolPath)) {
        throw '未找到 Windows SDK 的 x64 signtool.exe。请用 -SignToolPath 指定路径。'
    }
}

function Invoke-TunoSigning([string]$FilePath) {
    & $SignToolPath sign /sha1 $SigningThumbprint /s My /fd SHA256 /tr $TimestampUrl /td SHA256 /d Tuno $FilePath
    if ($LASTEXITCODE -ne 0) { throw "签名失败：$FilePath" }
    $signature = Get-AuthenticodeSignature -LiteralPath $FilePath
    if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate) {
        throw "签名或时间戳校验失败：$FilePath"
    }
}

New-Item -ItemType Directory -Path $buildProfile -Force | Out-Null
$previousAppData = $env:APPDATA
try {
    $env:APPDATA = $buildProfile
    & dotnet restore $project --configfile $nugetConfig --packages $packageCache -r win-x64 -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'NuGet 还原失败。' }
    $expectedPublishDir = [System.IO.Path]::GetFullPath((Join-Path $windowsRoot 'dist\publish'))
    if ([System.IO.Path]::GetFullPath($publishDir) -ne $expectedPublishDir -or
        -not $expectedPublishDir.StartsWith($windowsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw '发布目录不在 Windows 工作区内。'
    }
    if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
    & dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=false -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw '自包含发布失败。' }
    if ($SigningThumbprint) {
        Invoke-TunoSigning (Join-Path $publishDir 'Tuno.PlaybackProbe.exe')
        Invoke-TunoSigning (Join-Path $publishDir 'Tuno.PlaybackProbe.dll')
    }
}
finally {
    $env:APPDATA = $previousAppData
}

& $IsccPath --quiet-progress $script
if ($LASTEXITCODE -ne 0) { throw '安装包编译失败。' }
if (-not (Test-Path -LiteralPath $installer)) { throw "未找到安装包：$installer" }
if ($SigningThumbprint) { Invoke-TunoSigning $installer }
Get-Item -LiteralPath $installer | Select-Object FullName,Length
