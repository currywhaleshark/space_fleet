param([Parameter(Mandatory)][string]$Csv)
$rows = @(Import-Csv -LiteralPath $Csv)
function Stats($values) {
    $v = @($values | Where-Object {$null -ne $_ -and [string]$_ -ne ''} | ForEach-Object {[double]$_} | Sort-Object)
    if (!$v.Count) { return '— (0판)' }
    $median = ($v[[int][Math]::Floor(($v.Count-1)/2)] + $v[[int][Math]::Floor($v.Count/2)]) / 2
    return ('{0:F1} [{1:F1}–{2:F1}] ({3}판)' -f $median,$v[0],$v[-1],$v.Count)
}
'시각: 초, 중앙값 [최소–최대], 발생한 판만 집계. 피해 시각은 피해 진영 기준.'
'| 첫 사건 | 파랑 | 빨강 |'
'|---|---:|---:|'
foreach ($metric in @('contact','identified','locked','missileLaunch','missileHit','railLaunch','railHit','moduleDestroyedVictim','disabledVictim','destroyedVictim','close')) {
    '| {0} | {1} | {2} |' -f $metric,(Stats ($rows | ForEach-Object {$_.("Blue_"+$metric)})),(Stats ($rows | ForEach-Object {$_.("Red_"+$metric)}))
}
'| 국면 | 60초 이상 나타난 판 | 누적 시간 중앙값 |'
'|---|---:|---:|'
foreach ($phase in @('Approach','Missile','Gunnery','Sniping','Brawl')) {
    $v = @($rows | ForEach-Object {[double]$_."${phase}_seconds"})
    '| {0} | {1}/{2} ({3:F1}%) | {4} |' -f $phase,@($v | Where-Object {$_ -ge 60}).Count,$rows.Count,(100*@($v | Where-Object {$_ -ge 60}).Count/$rows.Count),(Stats $v)
}
$blue = @($rows | Where-Object winner -eq Blue).Count
$red = @($rows | Where-Object winner -eq Red).Count
$draw = @($rows | Where-Object winner -eq Draw).Count
'승리: 파랑 {0}/{3} ({4:F1}%), 빨강 {1}/{3} ({5:F1}%), 무승부 {2}/{3}.' -f $blue,$red,$draw,$rows.Count,(100*$blue/$rows.Count),(100*$red/$rows.Count)
foreach ($m in 0,1) {
    $subset = @($rows | Where-Object mirror -eq "$m")
    'Mirror={0}: 파랑 {1}, 빨강 {2}, 무승부 {3} / {4}판.' -f $m,@($subset | Where-Object winner -eq Blue).Count,@($subset | Where-Object winner -eq Red).Count,@($subset | Where-Object winner -eq Draw).Count,$subset.Count
}
'판정 시각: '+(Stats ($rows.outcomeTime))
'15~20분 판정 {0}/{3}, 10분 전 {1}/{3}, 시간 제한 {2}/{3}.' -f @($rows | Where-Object {[double]$_.outcomeTime -ge 900 -and [double]$_.outcomeTime -le 1200}).Count,@($rows | Where-Object {[double]$_.outcomeTime -lt 600}).Count,@($rows | Where-Object {[double]$_.outcomeTime -ge 1500}).Count,$rows.Count
'아군 충돌 평균: {0:F2}, 판당 실행 시간: {1}초.' -f ($rows.friendlyCollisions | Measure-Object -Average).Average,(Stats $rows.wallSeconds)
