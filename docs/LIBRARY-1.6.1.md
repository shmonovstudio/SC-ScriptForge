# Библиотека SC ScriptForge 1.6.1

В выпуск включены **22 сценария** из `Data/Scripts`. Точный состав скриптов и их метаданных закреплён в `Build/BundledScripts.props`; пользовательские сценарии и тестовые снимки в него не входят.

Исправленные варианты включены. `Windows/System-Inventory.ps1` взят из текущей библиотеки: добавлен сбор данных о мониторах. Пример `Samples/System-Inventory.ps1` также обновлён до исправленного варианта. Отдельно добавлена безопасная краткая сводка `Windows/System-Info.ps1` из прежнего ScriptForge.

## Состав

| Категория | Сценарий | Название |
| --- | --- | --- |
| Active Directory | `AD-DisplayName-Audit.ps1` | AD DisplayName Audit |
| Active Directory | `AD-Inactive-Users.ps1` | Неактивные учётные записи AD |
| Active Directory | `AD-Missing-Attributes.ps1` | Незаполненные поля сотрудников AD |
| Active Directory | `AD-Name-Quality.ps1` | Проверка ФИО сотрудников в AD |
| Active Directory | `AD-Password-Expiry.ps1` | Истекающие пароли пользователей AD |
| Certificates | `Local-Certificates.ps1` | Local Certificates |
| Files | `FS-Folder-Sizes.ps1` | Размеры подпапок |
| Files | `FS-Largest-Files.ps1` | Самые большие файлы в папке |
| Files | `FS-Ntfs-Permissions.ps1` | Права NTFS на папку |
| Network | `NET-Dns-Records.ps1` | Проверка DNS-записей |
| Network | `NET-Ping-List.ps1` | Проверка доступности хостов из файла |
| Network | `NET-Tcp-Ports.ps1` | Проверка доступности TCP-портов |
| Security | `SEC-Certificate-Expiry.ps1` | Истекающие сертификаты Windows |
| Security | `SEC-Failed-Logons.ps1` | Неудачные попытки входа в Windows |
| Security | `SEC-Local-Administrators.ps1` | Участники локальной группы администраторов |
| Servers | `SRV-Ad-Replication.ps1` | Ошибки репликации Active Directory |
| Servers | `SRV-Windows-Errors.ps1` | Критические события и ошибки Windows |
| Windows | `PC-Disk-Space.ps1` | Свободное место на дисках |
| Windows | `PC-System-Info.ps1` | Характеристики компьютера |
| Windows | `PC-Uptime.ps1` | Время работы после загрузки |
| Windows | `System-Info.ps1` | Краткая сводка системы |
| Windows | `System-Inventory.ps1` | System Inventory |

## Исключены

- `Active Directory/Очистка временных файлов.ps1`: Удаление или очистка данных.
- `Active Directory/Сброс таймера лицензии AnyDesk.ps1`: AnyDesk.

Исходные файлы исключённых сценариев остаются в пользовательской библиотеке. Встроенный набор не читает историю, корзину и каталоги тестов. Сборка использует только явные `EmbeddedResource` из `Build/BundledScripts.props`. Проверка `Tools/Test-ScriptLibrary.ps1` разбирает утверждённые файлы без выполнения и отклоняет AnyDesk, команды удаления данных, динамическое исполнение и ошибки синтаксиса.
