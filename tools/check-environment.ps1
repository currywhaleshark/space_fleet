param()
$ErrorActionPreference='Stop'
. "$PSScriptRoot/godot.ps1"
New-Item -ItemType Directory -Force (Join-Path $ProjectDir 'shots/environment_stage1') | Out-Null
foreach($sfTest in @('environment-test','contact-test')) {
    $sfLog=Join-Path $ProjectDir "shots/environment_stage1/stage6-$sfTest.log"
    $sfArgs=@('--path',('"'+$ProjectDir+'"'),'--fixed-fps','60','--log-file',('"'+$sfLog+'"'),'--',"--$sfTest")
    $sfProcess=Start-Process -FilePath $Godot -ArgumentList $sfArgs -WindowStyle Hidden -PassThru
    if(!$sfProcess.WaitForExit(60000)) { Stop-Process -Id $sfProcess.Id; throw "$sfTest timed out" }
    $sfOutput=Get-Content -LiteralPath $sfLog -Encoding UTF8 -Raw
    Write-Output $sfOutput
    if($sfProcess.ExitCode -ne 0 -or $sfOutput -match 'ERROR:|Exception' -or $sfOutput -notmatch 'PASS:') { throw "$sfTest failed" }
}
