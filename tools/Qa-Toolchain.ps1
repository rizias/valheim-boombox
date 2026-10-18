$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'DotNet-Toolchain.ps1')
$qaToolchain = Get-BoomboxToolchain
$qaCompiler = $qaToolchain.Compiler
$qaReferences = $qaToolchain.References
$qaRuntimeJson = $qaToolchain.RuntimeJson
$env:VALHEIM_QA_COMPILER=$qaCompiler
$env:VALHEIM_QA_REFERENCES=$qaReferences
$env:VALHEIM_QA_POWERSHELL=(Get-Process -Id $PID).Path

$qaSourceRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
function Get-QaRuntimePath([string]$RelativePath) {
    $qaRootValue=$env:VALHEIM_QA_ROOT
    if(
        !$qaRootValue -or ![IO.Path]::IsPathRooted($qaRootValue) -or !(Test-Path -LiteralPath $qaRootValue -PathType Container)
    ){
        throw 'Set VALHEIM_QA_ROOT to an absolute existing external QA directory.'
    }
    $qaResolvedRoot=[IO.Path]::GetFullPath($qaRootValue).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if(
        $qaResolvedRoot -eq $qaSourceRoot -or $qaResolvedRoot.StartsWith(
            $qaSourceRoot+[IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase
        )
    ){
        throw 'VALHEIM_QA_ROOT must be outside the source repository.'
    }
    Join-Path $qaResolvedRoot $RelativePath
}
