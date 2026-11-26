# Native AOT の発行フォルダーから、配布用 ZIP と Web インストーラーを作る。
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).ProviderPath
$gameExe = Join-Path $publishPath 'TakeTaiko.exe'
$gameVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($gameExe).FileVersion
if (-not $gameVersion) { throw '先にゲームを Native AOT で発行してください。' }

$files = @('TakeTaiko.exe', 'bass.dll', 'bassmix.dll', 'basswasapi.dll', 'bassasio.dll',
    'raylib.dll', 'raylib_angle.dll', 'libEGL.dll', 'libGLESv2.dll', 'vulkan-1.dll',
    'THIRD-PARTY-NOTICES.txt', 'Assets/Fonts/NotoSansJP-Regular.ttf', '遊び方.pdf')
$required = @('TakeTaiko.exe', 'bass.dll', 'raylib.dll', 'Assets/Fonts/NotoSansJP-Regular.ttf')
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath $name) -PathType Leaf)) {
        throw "発行フォルダーに $name がありません。"
    }
}

$dest = (New-Item -ItemType Directory -Force -Path $OutputDirectory).FullName
$zipPath = Join-Path $dest "TakeTaiko-$gameVersion-win-x64.zip"
$output = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
try {
    $zip = [IO.Compression.ZipArchive]::new($output, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $files) {
            $file = Join-Path $publishPath $name
            if (Test-Path -LiteralPath $file -PathType Leaf) {
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file, $name)
            }
        }
        foreach ($folder in @('Skins/TaikoSim-Simple', 'HitSounds')) {
            $folderPath = Join-Path $publishPath $folder
            if (-not (Test-Path -LiteralPath $folderPath -PathType Container)) { continue }
            foreach ($file in Get-ChildItem -LiteralPath $folderPath -Recurse -File) {
                $name = [IO.Path]::GetRelativePath($publishPath, $file.FullName).Replace('\','/')
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $name)
            }
        }
    }
    finally { $zip.Dispose() }
}
finally { $output.Dispose() }

Copy-Item -LiteralPath $gameExe -Destination (Join-Path $dest 'TakeTaikoSetup.exe')
Get-Item -LiteralPath $zipPath, (Join-Path $dest 'TakeTaikoSetup.exe')
