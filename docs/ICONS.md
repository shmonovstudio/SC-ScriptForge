# Готовые иконки Lucide · SC ScriptForge 1.4.1

Подключён NuGet-пакет [Lucide.Avalonia 0.2.27](https://www.nuget.org/packages/Lucide.Avalonia/0.2.27). Версия фиксируется в `SC.ScriptForge.csproj` и `packages.lock.json`. Проверена работа пакета с Avalonia 12.1.4 / .NET 10, включая single-file публикацию. Библиотека и данные иконок входят в приложение; отдельная установка шрифта, загрузка SVG или интернет для отображения не нужны.

## Интеграция

`Desktop/Ui.cs` теперь возвращает настоящий `LucideIcon` вместо вручную нарисованных `Path`. Семантические ключи сохранены, поэтому один источник обслуживает навигацию, карточки системы, библиотеку, меню, SC AI, ответы и значки действий. `WithIcon` собирает готовый значок с текстовой кнопкой, сохраняя её имя для accessibility и обработчик действия.

В `Desktop/MainWindow.axaml` заменены собственная геометрия лупы и текстовые символы крестиков / темы. На главной странице заменены стрелки перехода и плюсы создания; индикатор обновления использует Activity. В контекстном меню библиотеки появились значки сведений, версий, проверки синтаксиса, копирования, экспорта, удаления и SC AI. GPU и BIOS используют отдельные готовые значки Gpu / CircuitBoard.

Логотип SC остаётся фирменным векторным знаком. Кнопки заголовка, стрелки ComboBox, CheckBox и прочие элементы управления Avalonia сохраняют штатные готовые значки темы. Текст диагностических журналов, парольные маркеры и диаграммы не являются иконками и не менялись.

## Основные соответствия

| Элемент | Готовая иконка |
| --- | --- |
| Главная / компьютер / скрипты | House / Monitor / CodeXml |
| Избранное / категории / инструменты | Star / Folder / Wrench |
| Корзина / журнал / настройки | Trash / RotateCcwClock / Settings |
| CPU / память / диски | Cpu / MemoryStick / HardDrive |
| GPU / BIOS / сеть | Gpu / CircuitBoard / Network |
| SC AI / новый диалог | Sparkles / SquarePen |
| Запуск / остановка / отправка | Play / Square / ArrowUp |
| Поиск / закрыть / тема | Search / X / SunMoon |
| Копировать / редактировать / ещё | Copy / Pencil / Ellipsis |
| Импорт / экспорт / создать | Import / Download / Plus |

## Размеры и темы

Используется исходный viewport библиотеки 24×24, округлённые окончания и соединения линий, единая толщина `StrokeWidth=1.8`. `Size` задаёт логический размер в пикселях. Декоративные иконки не перехватывают клики; на кнопках сохранены текстовые подписи, подсказки и AutomationProperties.Name.

Стили `LucideIcon.glyph` в `Desktop/App.axaml` задают цвета через `Foreground`: обычный ScMuted, активная навигация и карточки ScAccentText, основной мятный action — тёмный `#092D30`. Цвет меняется вместе с темой, без повторного создания геометрии. Существующие анимации кнопок и reduced motion сохранены.

## Лицензии

- [Lucide.Avalonia](https://github.com/dme-compunet/Lucide.Avalonia), MIT, Compunet. Исходники API сверены с commit `4da32a90887a1ef8bb1b505d911fb3d8a00a8939` установленного пакета.
- [Lucide](https://github.com/lucide-icons/lucide/blob/main/LICENSE), ISC; производные Feather включают MIT Cole Bemis.

Полные тексты находятся в `Assets/Licenses/Lucide-Avalonia-MIT.txt` и `Assets/Licenses/Lucide-ISC.txt`, копируются в `Licenses` автономного выпуска.

## Проверка

**131 PASS, 0 FAIL**: 86 регрессионных проверок приложения и движения, 33 проверок SC AI и 12 проверок нативного окна. Проверены обе темы, 960×640, 150% масштаб, работа поиска в заголовке, состояния активных кнопок, меню и действия диалога.

```powershell
& .\.tools\dotnet\dotnet.exe Tests\bin\Release\net10.0-windows\SC.ScriptForge.Checks.dll --icons
& .\.tools\dotnet\dotnet.exe Tests\bin\Release\net10.0-windows\SC.ScriptForge.Checks.dll --icons --ai-design
& .\.tools\dotnet\dotnet.exe Tests\bin\Release\net10.0-windows\SC.ScriptForge.Checks.dll --icons --native-ui
```

Отчёты: `artifacts/icons-validation/results.txt`, `artifacts/icons-ai-validation/results.txt`, `artifacts/icons-validation/native/results.txt`.

Debug и Release собраны без предупреждений и ошибок. Готовый выпуск — `dist/SC.ScriptForge.exe` версии 1.4.1.0 и `artifacts/SC.ScriptForge-1.4.1-win-x64.zip` (45 файлов, 63,28 МиБ). Иконки входят в основной EXE; native runtime ИИ и DLL сохраняются рядом. Все файлы ZIP сверены с dist по SHA256; Data и GGUF исключены. Автономный EXE создал отзывчивое нативное окно, сохранил ленивую загрузку ИИ и штатно завершился с exit 0.

Из 28 файлов Data 27 не изменились. В диагностический журнал добавлена одна служебная JSONL-запись (133 байта); SHA256 всего исходного префикса совпал с манифестом до работ. Скрипты, метаданные, профили и настройки не менялись. Предыдущий EXE 1.4 сохранён в `artifacts/SC.ScriptForge-1.4-before-icons.exe`. Отчёты выпуска: `artifacts/icons-release-verification.json`, `artifacts/icons-native-startup.json`; повторяемая упаковка и проверка — `artifacts/verify-icons-release.ps1`.

![SC AI с Lucide, нативное окно](../artifacts/icons-validation/native/ai-native-dark.png)
![Библиотека, светлая тема](../artifacts/icons-validation/native/library-native-light.png)
