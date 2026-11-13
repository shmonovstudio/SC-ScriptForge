# Сторонние компоненты

Лицензия [MIT](LICENSE) относится к коду проекта. Условия сторонних компонентов сохраняются отдельно.

| Компонент | Назначение | Где находятся сведения |
| --- | --- | --- |
| Avalonia | Интерфейс приложения | Сведения о лицензии в NuGet-пакетах Avalonia |
| Lucide.Avalonia / Lucide | Значки | [MIT](Assets/Licenses/Lucide-Avalonia-MIT.txt), [ISC](Assets/Licenses/Lucide-ISC.txt) |
| CommunityToolkit.Mvvm | Модели представления | Сведения о лицензии в NuGet-пакете |
| Microsoft.Extensions.* / System.Management | Инфраструктура .NET и Windows | Сведения о лицензии в соответствующих NuGet-пакетах |
| Manrope | Шрифт | [SIL OFL](Assets/Fonts/Manrope/OFL.txt), [источник](Assets/Fonts/Manrope/SOURCE.md) |
| Onest | Шрифт | [SIL OFL](Assets/Fonts/Onest/OFL.txt), [источник](Assets/Fonts/Onest/SOURCE.md) |
| JetBrains Mono | Шрифт кода | [SIL OFL](Assets/Fonts/JetBrainsMono/OFL.txt), [источник](Assets/Fonts/JetBrainsMono/SOURCE.md) |
| Qwen | Скачиваемые модели локального ИИ | [Apache 2.0](SC.AICore/Licenses/Qwen-Apache-2.0.txt) |
| llama.cpp | Скачиваемый движок локального ИИ | [MIT](SC.AICore/Licenses/llama.cpp-MIT.txt) |

Версии NuGet-зависимостей закреплены в `.csproj` и `packages*.lock.json`. При распространении скачиваемого движка сохраняйте также уведомления и лицензии из его официального архива. Модели, движок, SDK и NuGet-кеш в исходный репозиторий не включены.
