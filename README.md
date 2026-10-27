# CryptoChat 0.1a

Консольный помощник по вопросам КриптоПро. Отвечает готовыми ответами из базы знаний (папка `content`),
к каждому ответу прикладывает ссылку на источник. Если не уверен, переспрашивает; если не нашёл, отправляет в поддержку.

## Запуск из исходников

    dotnet run --project src/CryptoChat.Cli

Полезные ключи (после `--`): `--debug`, `--validate`, `--selftest`, `--version`.

    dotnet run --project src/CryptoChat.Cli -- --selftest

## Тесты

    dotnet test

## Сборка .exe

    dotnet publish src/CryptoChat.Cli -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o publish

В папке `publish` появятся `CryptoChat.exe` и папка `content`. Копируйте их **вместе**:
программа читает данные из `content` рядом с собой, поэтому вопросы можно править без пересборки.

## Структура

    src/CryptoChat.Core   вся логика (тексты, поиск, диалог)
    src/CryptoChat.Cli    консоль
    tests/                тесты
    content/              данные: taxonomy.json, synonyms.json, config.json, questions/*.json, acceptance.json

## Как добавить продукт (например, CSP)

1. В `content/taxonomy.json` под темой «Технические вопросы» добавить узел `"kind": "product"` с ключевыми словами продукта.
2. Создать файл `content/questions/2_1_csp.json` с вопросами (id начинается с номера узла, например `2.1.1.1`).
3. Добавить в `content/acceptance.json` несколько проверочных вопросов.
4. Выполнить `dotnet run --project src/CryptoChat.Cli -- --validate` и `-- --selftest`, затем `dotnet test`.
