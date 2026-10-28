# Сборка CryptoChat.exe и папки publish\ для передачи другим.
# Запуск из корня проекта:
#   powershell -ExecutionPolicy Bypass -File build_exe.ps1
$ErrorActionPreference = "Stop"

Write-Host "1/4 Тесты..."
dotnet test
if ($LASTEXITCODE -ne 0) { throw "Тесты не прошли. Сборка остановлена." }

Write-Host "2/4 Сборка CryptoChat.exe..."
Stop-Process -Name CryptoChat -Force -ErrorAction SilentlyContinue
if (Test-Path publish) { Remove-Item publish -Recurse -Force }
# IncludeNativeLibrariesForSelfExtract кладёт внутрь .exe библиотеку SQLite (база бесед).
dotnet publish src/CryptoChat.Cli -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с ошибкой." }
# Файлы .lib из пакета ONNX Runtime нужны только компилятору C++, программе они не нужны.
Remove-Item publish\*.lib -ErrorAction SilentlyContinue

Write-Host "3/4 Проверка собранной программы..."
& .\publish\CryptoChat.exe --validate
if ($LASTEXITCODE -ne 0) { throw "Собранная программа не прошла проверку данных." }
& .\publish\CryptoChat.exe --selftest
if ($LASTEXITCODE -ne 0) { throw "Собранная программа не прошла самопроверку." }

Write-Host "4/4 Готово."
Write-Host "Папка publish: CryptoChat.exe, content (база знаний) и dicts (словари морфологии)."
if (Test-Path publish\models) {
    Write-Host "Модель для поиска по смыслу: publish\models."
} else {
    Write-Host "Модели для поиска по смыслу нет (папка models): бот работает по ключевым словам. Как получить — README, «Поиск по смыслу»."
}
Write-Host "Передавайте папку publish целиком (например, в zip): без content и dicts программа не запустится как надо."
