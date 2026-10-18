$ErrorActionPreference='Stop'
$buildRoot=[IO.Path]::GetFullPath($PSScriptRoot)
$vs='C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\VC\Auxiliary\Build\vcvarsall.bat'
$lines=& cmd.exe /d /s /c ('call "'+$vs+'" x64 >nul && set')
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
foreach($line in $lines){
    $split=$line.IndexOf('=')
    if($split -lt 1){continue}
    $name=$line.Substring(0,$split)
    if($name -in @('PATH','INCLUDE','LIB','LIBPATH','VCToolsInstallDir','WindowsSdkDir','WindowsSDKVersion','VSCMD_ARG_TGT_ARCH')){[Environment]::SetEnvironmentVariable($name,$line.Substring($split+1),'Process')}
}
& 'C:/Program Files/Git/bin/bash.exe' (Join-Path $buildRoot 'build-local.sh') *> (Join-Path $buildRoot 'ffmpeg-build.log')
$buildCode=$LASTEXITCODE
Get-Content -LiteralPath (Join-Path $buildRoot 'ffmpeg-build.log') -Tail 12
exit $buildCode
