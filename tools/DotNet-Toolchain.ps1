function Get-BoomboxToolchain {
    $sdkLines = @(& dotnet --list-sdks)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot list .NET SDKs.' }
    $sdk = $sdkLines | Where-Object { $_ -match '^10\.' } | Select-Object -Last 1
    if (-not $sdk -or $sdk -notmatch '^(\S+) \[(.+)\]$') {
        throw 'Valheim Boombox tools require an installed .NET SDK 10.'
    }
    $sdkRoot = Join-Path $Matches[2] $Matches[1]
    $dotnetRoot = Split-Path -Parent $Matches[2]
    $refPack = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'packs/Microsoft.NETCore.App.Ref') -Directory |
        Where-Object Name -Match '^10\.\d+\.\d+$' | Sort-Object { [version]$_.Name } | Select-Object -Last 1
    if (-not $refPack) { throw '.NET 10 reference pack is missing.' }
    $runtimes = @(& dotnet --list-runtimes)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot list .NET runtimes.' }
    $runtime = $runtimes | Where-Object { $_ -match '^Microsoft.NETCore.App 10\.\d+\.\d+ ' } |
        Sort-Object { [version]($_.Split(' ')[1]) } | Select-Object -Last 1
    if (-not $runtime) { throw '.NET 10 runtime is missing.' }
    $runtimeJson = @{ runtimeOptions = @{ tfm = 'net10.0'; framework = @{
        name = 'Microsoft.NETCore.App'; version = $runtime.Split(' ')[1]
    } } } | ConvertTo-Json -Depth 5
    [pscustomobject]@{
        Compiler = Join-Path $sdkRoot 'Roslyn/bincore/csc.dll'
        SdkRoot = $sdkRoot
        References = Join-Path $refPack.FullName 'ref/net10.0'
        RuntimeJson = $runtimeJson
    }
}
