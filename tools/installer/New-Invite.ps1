# PowerShell 7。生成した設定は招待された人への専用インストーラーにだけ使う。
[CmdletBinding()]
param(
    [Parameter(Mandatory)][uri]$RelayUrl,
    [Parameter(Mandatory)][string]$PublicKeyPath,
    [Parameter(Mandatory)][string]$ConfigPath,
    [Parameter(Mandatory)][string]$AllowlistPath
)

$ErrorActionPreference = 'Stop'
if (-not $RelayUrl.IsAbsoluteUri -or $RelayUrl.Scheme -ne 'https' -or $RelayUrl.UserInfo -or
    $RelayUrl.Query -or $RelayUrl.Fragment) {
    throw 'HTTPS の配信先を指定してください。'
}
if (Test-Path -LiteralPath $ConfigPath) { throw '招待設定の出力先は新規ファイルを指定してください。' }

$publicKey = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $PublicKeyPath).ProviderPath)
if ($publicKey -notmatch '\A\s*-----BEGIN PUBLIC KEY-----[A-Za-z0-9+/=\s]+-----END PUBLIC KEY-----\s*\z') {
    throw 'BEGIN PUBLIC KEY 形式の公開鍵を指定してください。'
}
$code = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($code)))
@{
    relayUrl = $RelayUrl.AbsoluteUri
    inviteCode = $code
    publicKeyPem = $publicKey
} | ConvertTo-Json | Set-Content -LiteralPath $ConfigPath -Encoding utf8NoBOM
Add-Content -LiteralPath $AllowlistPath -Encoding utf8NoBOM -Value $hash
Write-Output "招待を作成しました。失効用の識別子: $hash"
