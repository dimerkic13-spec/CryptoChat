# Сборка установщика CryptoChat-Setup-<версия>.exe (папка dist).
# Запуск из корня проекта:
#   powershell -ExecutionPolicy Bypass -File build_installer.ps1
# Нужны: .NET SDK 10, Inno Setup 6 (winget install --id JRSoftware.InnoSetup -e)
# и выгруженная модель в папке models (см. SEMANTIC_SETUP.md). Python для сборки не нужен.
param([string]$InnoSetup = "")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$project = [xml](Get-Content "src\CryptoChat.Cli\CryptoChat.Cli.csproj" -Encoding UTF8)
$version = @($project.Project.PropertyGroup | ForEach-Object { $_.InformationalVersion } | Where-Object { $_ })[0]
if (-not $version) { throw "Не нашёл версию (InformationalVersion) в src\CryptoChat.Cli\CryptoChat.Cli.csproj." }
Write-Host "Установщик CryptoChat $version"

Write-Host "1/5 Модель для поиска по смыслу..."
$model = "models\onnx\cointegrated--rubert-tiny2\model.onnx"
if (-not (Test-Path $model)) {
    throw "Нет модели ($model). Выгрузите её по инструкции SEMANTIC_SETUP.md (шаги 4-8) или скопируйте готовую папку models с другого компьютера."
}

Write-Host "2/5 Inno Setup..."
$candidates = @($InnoSetup)
foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, $env:LOCALAPPDATA)) {
    if ($root) {
        $candidates += Join-Path $root "Inno Setup 6\ISCC.exe"
        $candidates += Join-Path $root "Programs\Inno Setup 6\ISCC.exe"
    }
}
$candidates = $candidates | Where-Object { $_ -and (Test-Path $_) }
$iscc = @($candidates)[0]
if (-not $iscc) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $iscc = $command.Source }
}
if (-not $iscc) {
    throw "Не найден Inno Setup 6 (ISCC.exe). Установите: winget install --id JRSoftware.InnoSetup -e — и запустите скрипт снова в новом окне PowerShell. Или укажите путь: -InnoSetup 'C:\...\ISCC.exe'."
}
Write-Host "    $iscc"

Write-Host "3/5 Сборка CryptoChat.exe и проверка (build_exe.ps1)..."
& "$PSScriptRoot\build_exe.ps1"
if (-not (Test-Path "publish\$model")) { throw "В папке publish нет модели. Сборка остановлена." }

Write-Host "4/5 Проверка поиска по смыслу в собранной программе..."
& .\publish\CryptoChat.exe --check-model
if ($LASTEXITCODE -ne 0) { throw "Модель в собранной программе не прошла проверку (--check-model)." }

Write-Host "5/5 Сборка установщика..."
$source = (Resolve-Path "publish").Path
& $iscc "/DAppVersion=$version" "/DSourceDir=$source" "installer\CryptoChat.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup завершился с ошибкой." }

$setup = Join-Path $PSScriptRoot "dist\CryptoChat-Setup-$version.exe"
$size = [math]::Round((Get-Item $setup).Length / 1MB)
Write-Host ""
Write-Host "Готово: $setup ($size МБ)"
Write-Host "Этот файл передавайте для установки. Папки publish и dist в Git не попадают."
