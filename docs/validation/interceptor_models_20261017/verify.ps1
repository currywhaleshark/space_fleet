param([string]$Mode='checks')
$ErrorActionPreference='Stop'
$projectRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$env:APPDATA=Join-Path $projectRoot 'shots\interceptor-model-user'
New-Item -ItemType Directory -Force $env:APPDATA | Out-Null
$engine=Join-Path $env:USERPROFILE 'AppData\Local\Programs\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe'
if($Mode -eq 'checks'){
 $cases=@('model','hangar','progression') | ForEach-Object {@{Name="$_-test";Args=@("--$_-test");Check=$true}}
} else {
 $cases=@('interceptor_t3','mars_interceptor_t3') | ForEach-Object {@{Name="$_-hangar";Args=@('--menu=hangar',"--hangar-hull=$_");Check=$false}}
}
foreach($case in $cases){
 $stem=Join-Path $PSScriptRoot $case.Name
 $arguments=@('--path',"`"$projectRoot`"",'--resolution','1600x900','--log-file',"`"$stem.log`"",'--fixed-fps','60','--')+$case.Args
 if(-not $case.Check){$arguments+=@("--shot=`"$stem.png`"",'--frames=120')}
 $process=Start-Process -FilePath $engine -ArgumentList $arguments -WindowStyle Hidden -PassThru
 if(-not $process.WaitForExit(180000)){Stop-Process -Id $process.Id -Force;throw "Timed out: $($case.Name)"}
 $result=Get-Content -Encoding UTF8 -LiteralPath "$stem.log"
 if($process.ExitCode -ne 0 -or ($result -match '^ERROR:') -or ($case.Check -and -not($result -match 'PASS:'))){throw ($result -join "`n")}
 if($case.Check){$result | Select-String 'PASS:' | ForEach-Object {$_.Line}}
 else {Write-Output "Captured $($case.Name)"}
}
