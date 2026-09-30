# Проверка Skill Atlas 1.0.1

## Веб и CI

После добавления веб-интерфейса локально прошли **110 тестов**: 88 тестов Core/CLI и 22 теста веб-API/Markdown. Веб-тесты проверяют страницу с полем репозитория, цикл scan/read, ссылки на commit, некорректные адреса и ветки, истёкшие результаты, отмену, занятость сканера и очистку HTML от исполняемых элементов.

[Актуальный CI в GitHub Actions](https://github.com/yhekr/skill-atlas/actions/workflows/ci.yml) проверяет Windows и Linux и прикладывает TRX-отчёты и собранные приложения к каждому запуску. Проверка веб-интерфейса через Computer Use была остановлена инструментом из-за невозможности надёжно определить URL браузера; успешная ручная UI-проверка не заявляется.

Ниже сохранён исходный отчёт проверки Core/CLI; его покрытие не включает новый веб-проект.

Проверено 30 сентября 2026 года на Windows, .NET SDK 10.0.401, Git 2.45.1.windows.1.

## Итог

- **88 тестов: 88 прошли, 0 ошибок, 0 пропущенных.**
- Покрытие строк Core + CLI: **93,37%** (409 из 438); ветвей: **87,03%**.
- `dotnet format --verify-no-changes --no-restore` — успешно.
- Пакет `SkillAtlas.Tool.1.0.1.nupkg` собран, глобальная команда обновлена и проверена.

## Сценарии со слайда

| Поведение | Проверка |
|---|---|
| Копии в `.agents` и `.claude` дают одну запись | `DiscoveryBehaviorTests.MirroredAgentsAndClaudeCopiesProduceOneEntry` |
| Разные инструкции при одинаковых метаданных сохраняются отдельно | `DiscoveryBehaviorTests.SameMetadataWithDifferentInstructionsRemainsTwoEntries` |
| `agent/skills` находится | `DiscoveryBehaviorTests.AgentSkillsDirectoryIsDiscovered` |
| Продуктовые ресурсы и тестовые данные исключаются | `DiscoveryBehaviorTests.ProductSkillsAndTestResourcesAreExcluded` |
| Локальный и удалённый поиск применяют одинаковые правила | `GitIntegrationTests.RemoteAndLocalDiscoveryUseSameExclusionsAndDeduplication` |
| Запуск CLI на настоящем fixture | `CliIntegrationTests.CheckedInFixtureMatchesTheSlideThroughActualCli` |

## Крайние случаи

Проверены пустая папка, отсутствующий путь, Git-репозиторий без коммитов, репозиторий без скиллов, ветка со слешем в имени, тег, отсутствующая ветка, UTF-8 BOM, CRLF, Unicode, пробелы, скобки и `#` в путях, одинаковые названия разных скиллов, пустые метаданные, YAML `null`, кавычки, многострочные значения, некорректные типы, повторяющиеся ключи, неопределённые YAML aliases, незакрытый front matter, Markdown без метаданных, размер ровно 1 MiB и превышение лимита в UTF-8 байтах, заблокированный файл, junction/symlink, коды ошибок, отмена работающего Git с дочерним процессом, таймаут, ограничение объёма вывода, очистка временного clone, чистый JSON в stdout и предупреждения в stderr, имена с дефисом через `--`, терминалы шириной 40/80/160 колонок, экранирование разметки и сохранность emoji при сокращении описаний.

Git-интеграционные тесты создают локальные репозитории и используют настоящий Git; доступ в интернет им не нужен. CLI-интеграционные тесты запускают отдельный процесс `dotnet`.

## Реальные репозитории

Проверена установленная команда `skill-atlas`, а не только вызов классов из тестов.

| Команда | Результат | Commit |
|---|---|---|
| `skill-atlas scan https://github.com/JetBrains/kotlin` | **6 скиллов**, как на первом слайде | `c823f9e564fdd88249844cdc0389a9ef58b7c5eb` |
| `skill-atlas scan github.com/JetBrains/MPS --json` | **41 скилл**, без дублей и предупреждений | `49d37b63488a0a8e42eb0130cb867fd508f398ac` |
| `skill-atlas scan octocat/Hello-World --json` | **0 скиллов**, успешное завершение | `7fd1a60b01f91b314f59955a4e4d4e80d8edf11d` |
| `skill-atlas scan ./fixtures/scan` | **2 скилла**: `review-change`, `build-project` | Локальные данные |

В MPS до применения правил было 114 файлов: 82 в зеркальных `.agents`/`.claude` и 32 в ресурсах продукта. После исключения ресурсов и объединения одинаковых копий осталось 41.

Вывод реальных запусков сохранён локально в `artifacts/kotlin-scan.txt`, `artifacts/mps-scan.json`, `artifacts/empty-scan.json`. Результаты тестов — `artifacts/test-results/tests.trx`, покрытие — `artifacts/test-results/*/coverage.cobertura.xml`. Каталог `artifacts` исключён из Git.

## Повторный запуск

```powershell
dotnet test -c Release --collect:"XPlat Code Coverage" --results-directory artifacts/test-results
dotnet format --verify-no-changes --no-restore
skill-atlas scan ./fixtures/scan
skill-atlas scan https://github.com/JetBrains/kotlin
skill-atlas scan github.com/JetBrains/MPS --json
```

Результаты Linux-проверок доступны в GitHub Actions. Результаты реальных репозиториев относятся к указанным коммитам; новые изменения могут изменить количество скиллов. Исключения продуктовых и тестовых каталогов основаны на именах папок и подробно перечислены в README.
