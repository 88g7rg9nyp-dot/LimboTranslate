# LimboTranslate

Переводчик для Windows в стиле QTranslate: выделяешь текст в любом приложении, жмёшь горячую клавишу — получаешь перевод во всплывающем окне.

## Возможности

- Перевод выделенного текста в любом приложении (UI Automation, fallback — буфер обмена)
- Всплывающее окно у курсора и главное окно со сравнением сервисов во вкладках
- Сервисы без ключей и подписок: Google, DeepL
- Озвучка перевода (SAPI), история переводов (SQLite), значок в трее, автозапуск

## Горячие клавиши

| Клавиши | Действие |
| --- | --- |
| `Ctrl+Q` | Перевести выделенный текст |
| двойной `Ctrl` | Всплывающее окно с переводом |
| `Ctrl+E` | Озвучить выделенный текст |
| `Ctrl+Shift+Q` | Главное окно |

Комбинации меняются в настройках.

## Установка

Скачай `LimboTranslate-Setup-x.y.z.exe` из [релизов](../../releases) или портативный `LimboTranslate.exe` — установка не требуется, .NET не нужен.

Требования: Windows 10 1809 x64 или новее.

## Сборка

Сборка идёт в GitHub Actions (`.github/workflows/build.yml`): publish self-contained single-file + установщик Inno Setup. Локально:

```
dotnet publish src/LimboTranslate/LimboTranslate.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
iscc /DMyAppVersion=1.0.0 installer\LimboTranslate.iss
```

Релиз с установщиком публикуется автоматически при пуше тега `vX.Y.Z`.

## Данные

Настройки и история: `%APPDATA%\LimboTranslate` (`settings.json`, `history.db`).
