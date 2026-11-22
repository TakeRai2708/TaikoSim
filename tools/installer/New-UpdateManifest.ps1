# 配布担当者用。PowerShell 7 で実行し、秘密鍵はリポジトリの外に保管する。
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][uri]$DownloadUrl,
    [Parameter(Mandatory)][string]$PrivateKeyPath,
    [ValidateRange(1, 720)][int]$ValidHours = 72,
    [ValidateLength(0, 1600)][string]$Notes = ''
)

$ErrorActionPreference = 'Stop'
if (-not $DownloadUrl.IsAbsoluteUri -or $DownloadUrl.Scheme -ne 'https' -or
    $DownloadUrl.UserInfo -or $DownloadUrl.Fragment) {
    throw 'ダウンロード先は HTTPS の URL を指定してください。'
}

$package = [IO.File]::OpenRead((Resolve-Path -LiteralPath $PackagePath).ProviderPath)
try {
    if ($package.Length -le 0 -or $package.Length -gt 4GB) {
        throw 'ZIP のサイズは 1 バイト以上、4 GiB 以下にしてください。'
    }

    $archive = [IO.Compression.ZipArchive]::new($package, [IO.Compression.ZipArchiveMode]::Read, $true)
    try {
        $entries = @($archive.Entries | Where-Object { $_.FullName -ieq 'TakeTaiko.exe' })
        if ($entries.Count -ne 1 -or $entries[0].Length -le 0 -or $entries[0].Length -gt 256MB) {
            throw 'ZIP の直下に有効な TakeTaiko.exe が1つ必要です。'
        }

        $tempExe = [IO.Path]::GetTempFileName()
        try {
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0], $tempExe, $true)
            $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($tempExe)
            if (-not $fileVersion.FileVersion) {
                throw 'ZIP 内の exe に Windows のファイルバージョンがありません。'
            }
            $version = [version]::new($fileVersion.FileMajorPart, $fileVersion.FileMinorPart,
                $fileVersion.FileBuildPart, $fileVersion.FilePrivatePart)
        }
        finally {
            [IO.File]::Delete($tempExe)
        }
    }
    finally {
        $archive.Dispose()
    }

    $package.Position = 0
    $payload = @{
        schema = 1
        product = 'TakeTaikoSimulator/win-x64'
        version = $version.ToString()
        downloadUrl = $DownloadUrl.AbsoluteUri
        size = $package.Length
        sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($package))
        expiresUtc = [DateTimeOffset]::UtcNow.AddHours($ValidHours).ToString('O')
        notes = $Notes
    } | ConvertTo-Json -Compress

    $key = [Security.Cryptography.RSA]::Create()
    try {
        $key.ImportFromPem([IO.File]::ReadAllText((Resolve-Path -LiteralPath $PrivateKeyPath).ProviderPath))
        if ($key.KeySize -lt 2048) {
            throw 'RSA 鍵は 2048 ビット以上を使用してください。'
        }
        $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
        $signature = $key.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pss)
        @{
            payload = [Convert]::ToBase64String($bytes)
            signature = [Convert]::ToBase64String($signature)
        } | ConvertTo-Json -Compress
    }
    finally {
        $key.Dispose()
    }
}
finally {
    $package.Dispose()
}
