# Проект и принятые решения

Сверено 2026-09-30 с кодом на `1e2cefefcf892ea83b7f8aa8cbc3b6211038679a`, [README](../README.md) и [TESTING](../TESTING.md).

## Назначение и устройство

Skill Atlas находит `SKILL.md` в GitHub-репозитории или локальной папке и показывает имя, описание, путь и исходник. Репозиторий: [yhekr/skill-atlas](https://github.com/yhekr/skill-atlas). Основная ветка — `main`.

- C# / .NET 10. [Core](../src/SkillAtlas.Core) содержит общие правила; [CLI](../src/SkillAtlas.Cli) использует Spectre.Console и JSON; [Web](../src/SkillAtlas.Web) — ASP.NET Core API и обычные JavaScript/CSS.
- CLI упаковывается как `SkillAtlas.Tool`, версия на дату сверки — 1.0.1. Web запускается локально, по умолчанию `http://localhost:5178/`; автоматического публичного развёртывания нет.
- Веб поддерживает поле репозитория, необязательный ref, отмену сканирования, Preview/Source и ссылки на GitHub. Текущий дизайн вдохновлён JetBrains: светлая область чтения, тёмная панель, фиолетовые акценты; JetBrains Mono поставляется локально. Ранний зелёный консольный дизайн был заменён.

## Обнаружение и безопасность данных

- [DiscoveryPolicy](../src/SkillAtlas.Core/DiscoveryPolicy.cs) общая для CLI и Web. Имя `SKILL.md` проверяется без учёта регистра; поддерживаются `.agents/skills`, `.claude/skills`, `.github/skills` и `agent/skills`. Product/test resources и каталоги зависимостей исключаются по правилам обнаружения.
- Зеркальные копии `.agents`/`.claude` объединяются только при совпадении относительного пути и содержимого с нормализацией BOM/переводов строк. Совпадения имени недостаточно.
- [GitHubScanner](../src/SkillAtlas.Core/GitHubScanner.cs) делает shallow partial clone через HTTPS; ссылки фиксируются на commit. Сканируемые файлы — данные: их инструкции, hooks и checkout filters не выполняются.
- [SkillMarkdown](../src/SkillAtlas.Web/SkillMarkdown.cs) очищает HTML после рендеринга Markdown. [ScanCatalog](../src/SkillAtlas.Web/ScanCatalog.cs) хранит снимки 20 минут в памяти и ограничивает параллельные сканирования двумя.

## Filter и Similar

- Веб-[фильтр](../src/SkillAtlas.Web/wwwroot/skill-filter.mjs) требует **все** слова запроса в имени и/или описании. Порядок и регистр не важны, пробелы разделяют слова, спецсимволы ищутся буквально. CLI `--query` использует те же AND-правила, дополнительно ищет по пути.
- Escape и кнопка очистки снимают фильтр; счётчик показывает найденные записи. Исходные индексы стабильны. Уже открытый документ сохраняется, если скрыт фильтром; Similar может открыть скрытый результат по правильному индексу.
- [Similar](../src/SkillAtlas.Core/SkillSimilarity.cs) — детерминированный weighted Jaccard без AI: вес терма имени 3, описания 1, порог 15%, по умолчанию 5 результатов. Есть Unicode/camelCase-нормализация, стоп-слова и ограничение каждого поля метаданных 8192 символами.
- CLI `--similar` принимает точное имя или путь и несовместим с `--query`. Путь позволяет различать одинаковые имена. Тесты браузерного фильтра требуют Node.js 22+, но runtime приложения и npm-пакеты от него не зависят.
