# 파일을 저장할 때마다 엔진을 자동 빌드한다. Rhino(Debug 빌드 로드)가 바뀐 dll을 감지해 바로 다시 불러온다.
#   사용: scripts\watch.ps1      (Ctrl+C로 종료)
# 로더(src/Vimhopper)를 고친 경우에는 Rhino를 닫고 dotnet build 해야 한다.
Push-Location (Join-Path $PSScriptRoot "..\src\Vimhopper.Engine")
try { dotnet watch build }
finally { Pop-Location }
