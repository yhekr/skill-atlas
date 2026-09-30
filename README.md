# Skill Atlas

[![CI](https://github.com/yhekr/skill-atlas/actions/workflows/ci.yml/badge.svg)](https://github.com/yhekr/skill-atlas/actions/workflows/ci.yml)

CLI на **C# / .NET 10**, который находит `SKILL.md` в GitHub-репозитории или локальной папке. Выводит название, описание, расположение и ссылку на каждый скилл.

```text
skill-atlas scan https://github.com/JetBrains/kotlin
```

## Быстрый запуск

Нужны [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) и [Git 2.25+](https://git-scm.com/downloads). Для локальных папок Git не требуется.

Из корня проекта:

```powershell
dotnet run --project src/SkillAtlas.Cli -- scan https://github.com/JetBrains/kotlin
```

## Веб-интерфейс

```powershell
dotnet run --project src/SkillAtlas.Web
```

Откройте [localhost:5178](http://localhost:5178), введите адрес GitHub-репозитория и нажмите **Run scan**. Интерфейс оформлен в стиле терминала: тёмная палитра, моноширинный шрифт и строка `$ skill-atlas scan`. На широком экране список скиллов показывается слева, Markdown и исходный `SKILL.md` — справа; на узком экране панели расположены друг под другом. Есть фильтр результатов, выбор ветки/тега через **--ref**, отмена поиска и ссылки на GitHub.

Веб-приложение использует тот же `SkillAtlas.Core`, что и CLI. Содержимое файлов привязано к commit SHA сканирования; повторно скачивать репозиторий для чтения каждого скилла не нужно. Результаты хранятся в памяти до 20 минут, допускаются два параллельных сканирования. HTML из репозитория не выполняется, Markdown очищается перед отображением.

Веб-версия предназначена для локального запуска и принимает GitHub-репозитории. Для локальных папок используйте CLI. Веб-приложение не развёртывается публично автоматически.

## Установка команды

Соберите NuGet-пакет и установите .NET tool из локальной папки:

```powershell
dotnet pack src/SkillAtlas.Cli -c Release -o artifacts
dotnet tool install --global SkillAtlas.Tool --source ./artifacts --version 1.0.1
skill-atlas scan https://github.com/JetBrains/kotlin
```

Если каталог глобальных .NET tools отсутствует в `PATH`, перезапустите терминал либо используйте `$HOME\.dotnet\tools\skill-atlas.exe` на Windows. Для установки только в папку проекта замените `--global` на `--tool-path ./.tools` и запускайте `.\.tools\skill-atlas.exe`.

## Примеры

```powershell
# Ветка по умолчанию; URL, короткое имя и SSH-форма GitHub
skill-atlas scan https://github.com/JetBrains/kotlin
skill-atlas scan JetBrains/kotlin
skill-atlas scan github.com/JetBrains/MPS
skill-atlas scan git@github.com:JetBrains/kotlin.git

# Выбранная ветка или тег
skill-atlas scan JetBrains/kotlin --ref master

# Поиск по названию, описанию или пути, без учёта регистра
skill-atlas scan JetBrains/kotlin --query gradle

# Локальный каталог, включая скрытые .claude, .agents и .github
skill-atlas scan .
skill-atlas scan ./fixtures/scan
skill-atlas scan 'C:\Projects\my-repo'

# Машиночитаемый результат и простой вывод
skill-atlas scan JetBrains/kotlin --json > skills.json
skill-atlas scan JetBrains/kotlin --no-color
skill-atlas --help
```

В интерактивном терминале вывод цветной, ссылки `SKILL.md` кликабельны в терминалах с поддержкой гиперссылок. Длинные описания сокращаются до 180 символов. При перенаправлении, с `--no-color` или переменной `NO_COLOR` выводится простой текст с полными описаниями и URL. В JSON описания тоже сохраняются полностью.

## Как работает поиск

1. Создаёт временный shallow partial clone (`--depth=1 --filter=blob:none --no-checkout`). Получает дерево файлов без рабочей копии и полной истории.
2. Находит файлы с именем `SKILL.md` без учёта регистра, исключая продуктовые ресурсы, тестовые данные и зависимости. Загружает только подходящие blob-объекты.
3. Читает `name` и `description` из YAML front matter. Поддерживает кавычки, многострочные `|` / `>` значения и UTF-8 BOM. Без метаданных использует имя папки и первый абзац Markdown; для корневого файла — заголовок.
4. Объединяет зеркальные копии из `.agents/skills` и `.claude/skills` внутри одного проекта: относительный путь скилла и SHA-256 всего содержимого должны совпадать. BOM и различие CRLF/LF не учитываются. Ссылка ведёт к `.agents` как к первому пути по порядку сортировки. Одинаковое имя при разных инструкциях не объединяется.
5. Привязывает GitHub-ссылки к точному commit SHA и удаляет временный репозиторий.

GitHub REST API и API-токен не требуются. Все варианты GitHub-адреса нормализуются в HTTPS. Приватные репозитории используют уже настроенные **HTTPS Git credentials** (например, Git Credential Manager); SSH-ключ сам по себе не используется. Интерактивные запросы авторизации отключены.

Содержимое скиллов — данные: приложение не выполняет инструкции, скрипты, Git hooks, checkout-фильтры и команды из `SKILL.md`. Оно не устанавливает найденные скиллы.

## Границы и диагностика

- Поддерживаются GitHub.com и локальные папки; URL страницы `/tree/...` не принимается — используйте `--ref`.
- `--ref` принимает ветку или тег, а не произвольный commit SHA. Без него используется ветка репозитория по умолчанию.
- Подмодули, символические ссылки и junctions не обходятся.
- В обоих режимах пропускаются каталоги `.git`, `.svn`, `.hg`, `node_modules`, `bin`, `obj`, `.venv`, `venv`, `product`, `products`, `resources`, `test`, `tests`, `testdata`, `test-data`, `test_data`, `testresources`, `test-resources`, `test_resources`, `fixtures`, `__fixtures__` (без учёта регистра).
- Это фильтр по соглашениям о каталогах: `resources` считается содержимым продукта, а `tests` — тестовыми данными. Он не анализирует смысл Markdown. Чтобы просканировать такой локальный каталог намеренно, передайте его непосредственно как корень поиска.
- Скрытые `.agents/skills`, `.claude/skills`, `.github/skills`, а также `agent/skills` включены. `SKILL.md` в других неисключённых папках тоже находится.
- Пустой Git-репозиторий успешно возвращает ноль скиллов и `revision: null`.
- Файлы больше 1 MiB пропускаются с предупреждением. Для удалённого файла Git сначала может загрузить blob, чтобы определить его размер.
- Ошибки YAML, недоступные локальные файлы и проблемы очистки выводятся как предупреждения. Результат в таком случае может быть неполным: проверяйте `warnings` в JSON или stderr.
- Каждая Git-операция ограничена пятью минутами. `Ctrl+C` отменяет поиск и завершает дочерние процессы. Git-дерево ограничено 64 Mi символов вывода; превышение завершает сканирование ошибкой.
- При каждом удалённом запуске создаётся новый временный clone; постоянного кэша нет.

JSON содержит `source`, `revision`, `skills` (`name`, `description`, `path`, `url`) и `warnings`. Диагностика идёт в stderr и не загрязняет JSON в stdout.

Коды завершения: `0` — поиск завершён, в том числе с нулём совпадений или предупреждениями; `1` — ошибка сканирования; `2` — неверные аргументы; `130` — отмена.

## Разработка

```powershell
dotnet build -c Release
dotnet test -c Release
dotnet pack src/SkillAtlas.Cli -c Release -o artifacts
```

- `src/SkillAtlas.Core` — поиск в GitHub и локальных каталогах, чтение YAML, фильтрация.
- `src/SkillAtlas.Cli` — аргументы, консольный интерфейс Spectre.Console и JSON.
- `src/SkillAtlas.Web` — ASP.NET Core API и веб-интерфейс.
- `tests/SkillAtlas.Tests` — тесты парсинга, источников, поиска, очистки, консольного вывода и аргументов. Интеграционные тесты создают временные настоящие Git-репозитории с ветками и тегами и запускают CLI отдельным процессом. Автотесты требуют Git, но не требуют сети.
- `tests/SkillAtlas.Web.Tests` — HTTP-интеграционные тесты API, валидация, ошибки, защита от сторонних запросов и безопасное отображение Markdown.

Сценарии поведения со слайда закреплены в `DiscoveryBehaviorTests.cs`; одинаковое поведение локального и Git-поиска проверяет `GitIntegrationTests.cs`.

В `fixtures/scan` находится небольшой пример со слайда: две зеркальные копии, отдельный `agent/skills`, продуктовые скиллы и тестовые ресурсы. `skill-atlas scan ./fixtures/scan` должен вернуть ровно **2** скилла: `review-change` и `build-project`.

## GitHub Actions

[CI на GitHub](https://github.com/yhekr/skill-atlas/actions/workflows/ci.yml) запускается на каждом push, pull request и вручную через **Run workflow**. Обе задачи — `ubuntu-latest` и `windows-latest` — проверяют форматирование, собирают всё решение, запускают тесты, создают NuGet-пакет CLI и публикуют веб-приложение в артефакты запуска.

TRX-отчёты сохраняются даже при падении тестов. Успешный запуск содержит скачиваемые пакеты `skill-atlas-ubuntu-latest` и `skill-atlas-windows-latest`. Критерии готовности проекта перечислены в `AGENTS.md`.

### Запуск CI после каждого коммита

Установите хук один раз для каждого клона:

~~~powershell
./scripts/Install-GitHooks.ps1
~~~

После каждого `git commit` хук `.githooks/post-commit` запускает `scripts/Run-CI.ps1` в фоне. Скрипт отправляет точный SHA коммита в текущую ветку `origin` обычным push через HTTPS, затем проверяет GitHub Actions **каждые 120 секунд** до завершения (не более 45 минут). Команда commit не ждёт CI. Неотправляемый локальный коммит можно сделать с `SKILL_ATLAS_SKIP_CI=1`.

Нужны PowerShell 7 (либо Windows PowerShell 5.1), Git и настроенная HTTPS-авторизация GitHub через Git Credential Manager. Для API также поддерживаются переменные `GH_TOKEN` / `GITHUB_TOKEN`; авторизация push настраивается в Git отдельно. Токены не записываются в логи. `origin` не изменяется, SSH-адрес нормализуется в HTTPS только для этой операции.

Скрипт отслеживает только `ci.yml` для своего SHA и ветки. Если запуск от push ещё не появился, он ждёт две минуты; затем при необходимости вызывает `workflow_dispatch`, предварительно проверив SHA удалённой ветки. Уже существующий запуск используется повторно. Успехом считается только `success`; ошибка, отмена, отсутствие доступа и таймаут записываются как ошибка. Если следующий push отменил предыдущий запуск по настройке concurrency, предыдущий наблюдатель сообщит `cancelled`.

Логи и JSON-статус каждого коммита находятся в Git-каталоге `ci-watch` (обычно `.git/ci-watch`). При ошибке push локальный коммит сохраняется; автоматического force push нет.

~~~powershell
# Запустить вручную и дождаться результата (код выхода 0 — успех, 1 — ошибка)
./scripts/Run-CI.ps1

# Или запустить в фоне
./scripts/Run-CI.ps1 -Background

# Прочитать статус текущего коммита
$sha = git rev-parse HEAD
Get-Content (Join-Path (git rev-parse --absolute-git-dir) "ci-watch/$sha.json")

# Пропустить один коммит
$env:SKILL_ATLAS_SKIP_CI = '1'
git commit -m "Local work"
Remove-Item Env:SKILL_ATLAS_SKIP_CI

# Отключить установленный здесь хук
git config --local --unset core.hooksPath

# Проверить скрипт без сети и реального ожидания
./scripts/Test-CI.ps1
~~~

Установщик меняет только локальный `core.hooksPath` этого репозитория и отказывается перезаписывать другую настройку hooksPath или существующий post-commit hook. Git не переносит эту настройку при клонировании.
