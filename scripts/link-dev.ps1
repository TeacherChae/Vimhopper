# Grasshopper Libraries 폴더에 .ghlink를 만들어 빌드 출력 폴더를 직접 로드하게 한다.
# 빌드할 때마다 복사할 필요 없음. 단, Rhino가 켜져 있으면 .gha가 잠겨서 빌드가 실패하므로 Rhino를 닫고 빌드할 것.
#   사용: scripts\link-dev.ps1            (연결)
#         scripts\link-dev.ps1 -Remove    (해제)
param(
    [string]$Configuration = "Debug",
    [switch]$Remove
)

$link = Join-Path $env:APPDATA "Grasshopper\Libraries\Vimhopper.ghlink"

if ($Remove) {
    Remove-Item $link -ErrorAction SilentlyContinue
    "removed $link"
    return
}

$output = Join-Path $PSScriptRoot "..\src\Vimhopper\bin\$Configuration\net7.0-windows" | Resolve-Path -ErrorAction Stop
Set-Content -Path $link -Value $output.Path -Encoding ascii
"linked $link -> $($output.Path)"
