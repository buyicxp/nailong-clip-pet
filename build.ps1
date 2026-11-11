# 一键编译奶龙剪贴宠物（只依赖系统自带的 .NET Framework + csc，不需要 VS / SDK）
$ErrorActionPreference = "Stop"

$fw = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path -LiteralPath $fw)) {
    throw "找不到 $fw，请装 .NET Framework 4.x（Win10/11 自带）"
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root "src"
$bin  = Join-Path $root "bin"

New-Item -ItemType Directory -Force -Path $bin | Out-Null

$refs = @(
    (Join-Path $fw "WPF\PresentationFramework.dll"),
    (Join-Path $fw "WPF\PresentationCore.dll"),
    (Join-Path $fw "WPF\WindowsBase.dll"),
    (Join-Path $fw "System.Xaml.dll"),
    (Join-Path $fw "System.dll"),
    (Join-Path $fw "System.Drawing.dll"),
    (Join-Path $fw "System.Windows.Forms.dll")
)
$refArgs = $refs | ForEach-Object { "/r:$_" }

$sources = @(
    "Program.cs","Types.cs","NailongArt.cs","PetCanvas.cs","PetWindow.cs",
    "ClipboardMonitor.cs","KeyHook.cs","Audio.cs","MouseHook.cs","Log.cs","PetRuntime.cs","Panel.cs"
) | ForEach-Object { Join-Path $src $_ }

& "$fw\csc.exe" /target:winexe /out:"$bin\NailongClipPet.exe" /nologo /optimize+ `
    /win32icon:"$(Join-Path $root 'Assets\nailong.ico')" @refArgs @sources
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

# 程序按 exe 旁边的 Assets 找形象和音效，编译完顺手拷过去
$binAssets = Join-Path $bin "Assets"
New-Item -ItemType Directory -Force -Path (Join-Path $binAssets "art") | Out-Null
Copy-Item -Force -LiteralPath (Join-Path $root "Assets\art\nailong.png") -Destination (Join-Path $binAssets "art")
Copy-Item -Recurse -Force -LiteralPath (Join-Path $root "Assets\art\anim") -Destination (Join-Path $binAssets "art")
Copy-Item -Force -LiteralPath (Join-Path $root "Assets\nailong.ico") -Destination $binAssets
if (Test-Path -LiteralPath (Join-Path $root "Assets\sounds")) {
    Copy-Item -Recurse -Force -LiteralPath (Join-Path $root "Assets\sounds") -Destination $binAssets
}

Write-Host "编译成功 -> $bin\NailongClipPet.exe"
Write-Host "直接双击运行即可（素材已拷到 $binAssets）"
