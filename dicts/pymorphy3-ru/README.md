# Словари морфологии (pymorphy3-dicts-ru)

Файлы взяты без изменений из пакета [pymorphy3-dicts-ru](https://pypi.org/project/pymorphy3-dicts-ru/)
версии 2.4.417150.4580142 — тех же словарей, что использует Python-версия бота (библиотека pymorphy3).
Не нужен только файл `gramtab-opencorpora-ext.json`, его здесь нет.

- Пакет pymorphy3-dicts-ru: лицензия MIT.
- Данные словаря: проект [OpenCorpora](https://opencorpora.org) (ревизия словаря 417150, корпуса 4580142).

Программа ищет эту папку рядом с собой (`dicts/pymorphy3-ru`) или по пути из переменной окружения
`CRYPTOCHAT_DICTS`. Если папки нет, режим `"morphology": "auto"` работает со стеммером.
