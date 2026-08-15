# ============================================================
# 同步原项目 Common 逻辑到 CLI 的 Core
# 
# 用法:  powershell -ExecutionPolicy Bypass -File sync-upstream.ps1
#
# 这个脚本会:
#   1. 拉取原项目 (upstream) 最新代码
#   2. 对比原项目 Common 目录与本地 Core 的差异
#   3. 报告哪些文件有变化（忽略你为 CLI 做的定制）
# ============================================================

$ErrorActionPreference = "Stop"
$git = "C:\Program Files\Git\cmd\git.exe"
$originals = @(
    "CallExternalProgram.cs", "FeaturesHelper.cs", "GetDevicesInfo.cs",
    "Global.cs", "StringHelper.cs", "FileHelper.cs", "FrpHelper.cs",
    "CryptoHelper.cs", "ADBPairHelper.cs", "PartModel.cs"
)
$core = "src\UotanToolbox.Core"
$tmp = Join-Path $env:TEMP "upstream_common"

Write-Host "== 1. 拉取原项目最新代码 ==" -ForegroundColor Cyan
& $git fetch upstream main
if ($LASTEXITCODE -ne 0) { Write-Host "拉取失败" -ForegroundColor Red; exit 1 }

Write-Host "`n== 2. 对比 Common 目录差异 ==" -ForegroundColor Cyan
if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

$changed = @()
foreach ($f in $originals) {
    & $git show "upstream/main:UotanToolbox/Common/$f" > "$tmp\$f" 2>$null
    $minePath = Join-Path $core $f
    if (-not (Test-Path $minePath)) { $changed += "$f (Core 中缺失)"; continue }

    # 忽略换行/BOM/访问修饰符差异后逐行对比
    $m = (Get-Content $minePath -Raw).Replace("`r`n","`n").Replace("`r","`n") -replace "\uFEFF","" -replace "internal class","public class"
    $u = (Get-Content "$tmp\$f" -Raw).Replace("`r`n","`n").Replace("`r","`n") -replace "\uFEFF","" -replace "internal class","public class"
    $ml = $m -split "`n"
    $ul = $u -split "`n"
    $onlyU = $ul | Where-Object { $ml -notcontains $_ }
    $onlyM = $ml | Where-Object { $ul -notcontains $_ }

    if ($onlyU.Count -eq 0 -and $onlyM.Count -eq 0) {
        Write-Host "  [未变] $f" -ForegroundColor DarkGray
    } else {
        Write-Host "  [已变] $f  (原项目独有 $($onlyU.Count) 行, 你的独有 $($onlyM.Count) 行)" -ForegroundColor Yellow
        $changed += $f
    }
}

Write-Host "`n== 3. 结果 ==" -ForegroundColor Cyan
if ($changed.Count -eq 0) {
    Write-Host "  所有 Common 文件都是最新，无需同步。" -ForegroundColor Green
} else {
    Write-Host "  以下文件有差异，需要手动检查是否同步:" -ForegroundColor Yellow
    foreach ($f in $changed) {
        Write-Host "    - $f"
        Write-Host "      查看差异:  powershell -Command ""Compare-Object (Get-Content 'src\UotanToolbox.Core\$f') (Get-Content '$tmp\$f')"""
    }
    Write-Host "  注意: 部分差异是你的 CLI 定制（去 UI 依赖、改 public），不一定要同步。"
    Write-Host "  建议人工核对每条差异后再决定是否更新。"
}
