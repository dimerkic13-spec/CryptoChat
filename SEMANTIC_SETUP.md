# Поиск по смыслу: как включить на новом компьютере

Инструкция для компьютера с Windows, на котором есть **только** скачанный с GitHub архив C#-бота
и больше ничего не установлено. Команды вводятся в PowerShell **по одной**.

Без модели бот работает по ключевым словам и при запуске пишет «Поиск по смыслу недоступен: Модель не найдена…».
Модель (`cointegrated/rubert-tiny2`, около 120 МБ) в GitHub не хранится — её нужно один раз получить
одним из двух способов:

- **Способ А (проще):** скопировать готовую папку `models` с компьютера, где поиск по смыслу уже работает.
  Python не нужен. → шаги 1, 2, 3, затем 9–11.
- **Способ Б:** выгрузить модель на этом компьютере. Нужен интернет и Python 3.13. → шаги 1–11 по порядку.

Для передачи клиенту без установки чего-либо есть и третий путь: собрать на своём компьютере папку
`publish` (шаг 11) и передать её целиком — в ней `CryptoChat.exe`, база знаний и модель, ставить ничего не нужно.

---

## Шаг 1. Распаковать бота

1. Откройте https://github.com/dimerkic13-spec/CryptoChat, выберите нужную ветку (например, `v0.2.9a`),
   нажмите **Code → Download ZIP**.
2. Распакуйте архив в папку **без русских букв и пробелов в пути**, например `C:\CryptoChat`.
   Внутри должна быть папка вида `CryptoChat-0.2.9a` с файлами `README.md`, `build_exe.ps1` и папками `src`, `content`, `dicts`, `tools`.
3. Откройте PowerShell и перейдите в эту папку:

```powershell
cd "C:\CryptoChat\CryptoChat-0.2.9a"
```

Дальше все команды выполняются **из этой папки**, если не сказано другое.

## Шаг 2. Установить .NET SDK 10

```powershell
winget install --id Microsoft.DotNet.SDK.10 -e
```

Если `winget` не найден — скачайте установщик .NET SDK 10 (x64) с https://dotnet.microsoft.com/download и установите.

**Закройте PowerShell и откройте заново** (чтобы появилась команда `dotnet`), снова перейдите в папку бота (шаг 1, п. 3) и проверьте:

```powershell
dotnet --version
```

Должно быть `10.…`.

## Шаг 3. Проверить, что бот собирается и работает

```powershell
dotnet build
```

В конце — `Сборка успешно завершена` (или `Build succeeded`), ошибок 0. Первая сборка скачивает библиотеки, это может занять пару минут.

> **Способ А:** скопируйте папку `models` с другого компьютера (из папки бота, где поиск по смыслу уже работает)
> в `C:\CryptoChat\CryptoChat-0.2.9a\models` — внутри должна быть `models\onnx\cointegrated--rubert-tiny2\model.onnx`.
> Затем сразу переходите к **шагу 9**.

## Шаг 4. Установить Python 3.13 (только способ Б)

Нужна именно версия **3.13**: для более новых (3.14, 3.15) ещё не все библиотеки выпущены, и установка
падает с ошибкой «Microsoft Visual C++ 14.0 or greater is required».

```powershell
winget install --id Python.Python.3.13 -e
```

Если `winget` не найден — скачайте Python 3.13 (Windows installer 64-bit) с https://www.python.org/downloads/windows/,
при установке отметьте **«Add python.exe to PATH»** и **«py launcher»**.

**Закройте PowerShell и откройте заново**, перейдите в папку бота и проверьте:

```powershell
py -3.13 --version
```

Должно быть `Python 3.13.…`. Если пишет, что версия не найдена, — посмотрите список установленных: `py list`
(или `py -0`) и установите 3.13.

## Шаг 5. Создать окружение для выгрузки модели

Отдельное окружение `.venv` внутри папки бота — чтобы не трогать остальной Python на компьютере.

```powershell
py -3.13 -m venv .venv
```

Проверка — должно быть `Python 3.13.…`:

```powershell
.\.venv\Scripts\python.exe --version
```

## Шаг 6. Установить библиотеки (один раз, несколько сотен МБ)

```powershell
.\.venv\Scripts\python.exe -m pip install --upgrade pip
```

```powershell
.\.venv\Scripts\python.exe -m pip install "sentence-transformers>=3.0" onnx onnxruntime
```

В конце должно быть `Successfully installed …` и **ни одной строки** `error`. Проверка:

```powershell
.\.venv\Scripts\python.exe -c "import torch, sentence_transformers, onnx, onnxruntime; print('OK')"
```

Должно напечатать `OK`.

## Шаг 7. Выгрузить модель

```powershell
.\.venv\Scripts\python.exe tools\export_onnx.py --cache-dir .export-cache
```

Что происходит: скрипт скачивает модель из интернета (с сайта huggingface.co, около 120 МБ) в папку `.export-cache`
и сохраняет её для C# в `models\onnx\cointegrated--rubert-tiny2` (`model.onnx`, `tokenizer.json`, `cryptochat-model.json`).

В конце должны быть строки:

```
Проверка ONNX: худшая похожесть с PyTorch 0.99999… (должно быть больше 0.9999)
Готово: …\models\onnx\cointegrated--rubert-tiny2
```

Предупреждения по дороге — **не ошибки**, их можно не читать: `unauthenticated requests to the HF Hub`,
`symlinks … not support`, таблица `UNEXPECTED`, `DeprecationWarning`, `TracerWarning`.

## Шаг 8. Убрать временные файлы (необязательно)

После строки «Готово» окружение и скачанная исходная модель больше не нужны (около 1 ГБ):

```powershell
Remove-Item -Recurse -Force .venv, .export-cache
```

Папку `models` **не удаляйте** — это и есть модель для бота.

## Шаг 9. Собрать бота с моделью

При сборке папка `models` копируется рядом с программой.

```powershell
dotnet build
```

Проверка, что модель скопировалась (должно быть `True`):

```powershell
Test-Path ".\src\CryptoChat.Cli\bin\Debug\net10.0\models\onnx\cointegrated--rubert-tiny2\model.onnx"
```

Если `False` — скопируйте вручную:

```powershell
Copy-Item -Recurse -Force ".\models" ".\src\CryptoChat.Cli\bin\Debug\net10.0\"
```

## Шаг 10. Проверить модель и запустить бота

```powershell
dotnet run --project src/CryptoChat.Cli -- --check-model
```

Должно быть: **«Модель работает так же, как в Python-версии.»**

Запуск бота:

```powershell
dotnet run --project src/CryptoChat.Cli
```

При запуске — «Загружаю модель для поиска по смыслу…», строки «Поиск по смыслу недоступен» больше нет.
Проверка: вопрос «не могу зайти в аккаунт» — бот предложит «Забыл логин/пароль от личного кабинета».

Проверка качества на 2280 тестовых запросах (минута-две; результат — в файле `synthetic.txt`):

```powershell
dotnet run --project src/CryptoChat.Cli -- --synthetic > synthetic.txt
```

В строке «Всего:» должно быть `НЕВЕРНЫЙ ОТВЕТ   0`.

## Шаг 11. Собрать CryptoChat.exe для передачи (по желанию)

```powershell
powershell -ExecutionPolicy Bypass -File build_exe.ps1
```

Скрипт прогонит тесты и соберёт папку `publish`: `CryptoChat.exe`, `content`, `dicts` и `models`.
В конце должно быть «Модель для поиска по смыслу: publish\models». Эту папку можно передавать целиком
(например, в zip) — на другом компьютере ничего ставить не нужно, запуск: `CryptoChat.exe`.

---

## Если что-то пошло не так

| Сообщение | Что делать |
|---|---|
| `dotnet : Имя "dotnet" не распознано…` | .NET SDK не установлен или PowerShell не перезапущен — шаг 2 |
| `py : Имя "py" не распознано…` | Python не установлен или не отмечен «py launcher» — шаг 4 |
| `Microsoft Visual C++ 14.0 or greater is required` (при установке pyyaml) | окружение создано не на Python 3.13 — удалите `.venv` (`Remove-Item -Recurse -Force .venv`) и повторите шаги 5–6 |
| `No matching distribution found for torch` | то же: нужен Python 3.13 |
| `Module onnx is not installed!` | не выполнена команда шага 6 — выполните её и повторите шаг 7 |
| `нужны torch и sentence-transformers` | скрипт запущен не тем Python: команда должна начинаться с `.\.venv\Scripts\python.exe` |
| Ошибка соединения / `ConnectionError` / `403` при скачивании | нет доступа к huggingface.co (часто закрыт в рабочей сети) — попробуйте другую сеть или способ А |
| `Модель не найдена в …\bin\Debug\net10.0\models\…` | модель не выгрузилась (нет строки «Готово» на шаге 7) или не скопировалась — шаг 9 |
| `--check-model`: похожесть меньше 0.9999 | модель повреждена — удалите папку `models` и повторите шаг 7 |

## Если поиск по смыслу не нужен

В `content\config.json`, раздел `"semantic"`, поставьте `"enabled": false`. Бот работает по ключевым словам,
сообщение о модели не появляется.
