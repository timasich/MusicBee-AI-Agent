# План работ по MusicBee AI Agent

## Текущий статус

Проект находится на стадии рабочего MVP.

Готово:

- плагин подключается в MusicBee;
- есть окно чата;
- есть настройки OpenAI-compatible provider;
- читается текущий трек;
- работает базовый поиск по библиотеке;
- создается плейлист после preview и подтверждения;
- можно добавлять выбранные треки в очередь;
- есть model-directed orchestration workflow;
- есть JSON repair pass.

Главное ограничение текущего MVP:

> Плагин пока не имеет полноценного локального индекса и систематизации библиотеки. Поиск и similarity являются временным слоем, достаточным для MVP, но недостаточным для больших библиотек.

## Новый стратегический фокус

Следующий этап разработки должен быть не про добавление новых команд в чат, а про создание масштабируемого локального слоя:

1. `LibraryIndex`;
2. нормализация metadata;
3. library profile;
4. retriever-ы;
5. candidate ranking;
6. adaptive retrieval budgets;
7. тестируемый слой безопасности.

Только после этого имеет смысл расширять агентные сценарии.

## Этап 1. Зафиксировать текущий MVP

Статус: почти готово.

- [x] MusicBee plugin shell.
- [x] DLL `MB_AI_Agent.dll`.
- [x] x86 build.
- [x] Standalone chat window.
- [x] Settings window.
- [x] OpenAI-compatible provider.
- [x] Current track context.
- [x] Basic library search.
- [x] Basic action preview.
- [x] Create playlist.
- [x] Queue last.
- [x] Queue next.
- [x] Play now.
- [x] Model-directed orchestration workflow.
- [x] JSON repair pass.
- [x] README.
- [x] Manual smoke-test checklist.
- [x] Basic troubleshooting guide.

Цель этапа:

- сделать текущий MVP переносимым на другой компьютер;
- зафиксировать известные ограничения;
- не расширять behavior, пока не построен индекс.

## Этап 2. Вынести локальную модель данных

Статус: не готово.

Нужно создать отдельные модели, не завязанные напрямую на MusicBee API:

- [x] `TrackRecord`;
- [x] `ArtistRecord`;
- [x] `AlbumRecord`;
- [x] `PlaylistRecord`;
- [x] `TrackFeatures`;
- [ ] `TrackStats`;
- [ ] `SearchIntent`;
- [x] `CandidateTrack`;
- [x] `CandidateScore`;
- [x] `ActionRequest`.

Зачем:

- отделить данные от UI;
- отделить MusicBee API от поиска;
- сделать validator/ranker тестируемыми;
- подготовить SQLite.

## Этап 3. SQLite LibraryIndex

Статус: не готово.

Создать `LibraryIndex` как локальное хранилище.

Минимальные таблицы:

- [x] `tracks`;
- [x] `artists`;
- [x] `albums`;
- [x] `genres`;
- [x] `playlists`;
- [x] `playlist_tracks`;
- [x] `track_tokens`;
- [x] `index_state`;
- [x] `agent_actions`.

Минимальные операции:

- [x] upsert track;
- [ ] delete missing track;
- [ ] get track by id;
- [ ] find by tokens;
- [ ] get tracks by artist;
- [ ] get tracks by genre;
- [ ] get top rated;
- [ ] get recently played;
- [ ] get rarely played;
- [x] get library count;
- [x] get index freshness.

Цель:

- больше не сканировать MusicBee API на каждый пользовательский запрос;
- сделать поиск быстрым на больших библиотеках.

## Этап 4. Indexing pipeline

Статус: не готово.

Нужно реализовать:

- [x] initial full indexing;
- [ ] progress UI or background task message;
- [ ] cancellation;
- [ ] incremental sync через MusicBee events;
- [ ] incremental sync через `Library_GetSyncDelta`, если доступно;
- [x] fallback manual reindex;
- [ ] index health check.

Правило:

- MusicBee API остается источником истины;
- SQLite является рабочим индексом;
- если индекс устарел, агент должен знать это и либо обновить его, либо честно ограничить функциональность.

## Этап 5. Normalization layer

Статус: не готово.

Нужно нормализовать:

- [x] artist names;
- [x] album artist;
- [x] title tokens;
- [x] album tokens;
- [x] genre tokens;
- [x] mood tokens;
- [x] BPM buckets;
- [x] year ranges;
- [x] rating buckets;
- [x] duration buckets;
- [ ] language/script hints.

Примеры:

- `Rap / Hip-hop / Experimental` -> `rap`, `hip-hop`, `experimental`;
- `2019` -> `late-2010s`;
- BPM `92` -> `mid-tempo`;
- rating `80` -> `high-rated`.

Цель:

- улучшить поиск на русском/английском;
- сделать запросы пользователя менее зависимыми от точных тегов;
- подготовить clustering.

## Этап 6. Library profile

Статус: не готово.

Нужно построить агрегированную сводку:

- [x] total tracks;
- [x] top artists;
- [ ] top album artists;
- [x] top genres;
- [ ] high-rated genres;
- [ ] high-rated artists;
- [ ] most played artists;
- [ ] rarely played high-rated tracks;
- [ ] year distribution;
- [ ] BPM distribution;
- [ ] missing metadata report.

Использование:

- широкие запросы;
- большие библиотеки;
- explainability;
- выбор retrieval strategy.

## Этап 7. Multi-retriever architecture

Статус: частично готово.

Реализовать retriever-ы:

- [x] `TextRetriever`;
- [x] `SimilarityRetriever`;
- [ ] `ListeningStatsRetriever`;
- [ ] `GenreMoodRetriever`;
- [ ] `DurationRetriever`;
- [ ] `PlaylistCooccurrenceRetriever`;
- [ ] `ClusterRetriever`;
- [x] `LibraryProfileRetriever`.

Каждый retriever должен возвращать:

- track id;
- partial score;
- reason;
- source name.

Цель:

- не искать "похожее" одним правилом;
- объединять разные сигналы;
- делать результаты объяснимыми.

Текущий результат:

- поиск агента использует SQLite index, если он построен;
- если индекс недоступен, используется fallback на MusicBee API;
- полноценные независимые retriever-классы еще не выделены.

## Этап 8. CandidateRanker

Статус: частично готово.

Нужно заменить текущий scoring на отдельный `CandidateRanker`.

Текущий результат:

- `CandidateRanker` выделен в отдельный класс;
- агентный поиск через `LibrarySearchService` использует ranker;
- старый scoring в `MusicBeeApiAdapter` остается fallback-слоем.

Сигналы:

- [x] artist;
- [x] album artist;
- [x] genre;
- [x] mood;
- [x] BPM proximity;
- [x] rating;
- [x] play count;
- [x] skip count;
- [ ] playlist co-occurrence;
- [ ] cluster relevance;
- [ ] diversity penalty;
- [ ] duplicate penalty;
- [ ] current queue exclusion;
- [ ] recently played penalty;
- [ ] exact duration fit.

Цель:

- локальная логика должна выдавать хороший shortlist даже без сильной LLM;
- LLM должна выбирать из качественных candidates, а не спасать слабый поиск.

## Этап 9. Adaptive retrieval budgets

Статус: частично готово.

Нужно адаптировать стратегию к размеру библиотеки.

### До 500 треков

- [x] можно сканировать весь индекс;
- [x] candidate budget: 20-40.

### 500-5000 треков

- [x] использовать filters/facets;
- [x] candidate budget: 20-40;
- [x] library profile для широких запросов.

### 5000-50000+ треков

- [x] сначала library profile;
- [ ] затем cluster/retriever selection;
- [x] затем top candidates из нескольких retriever-ов;
- [x] затем rerank;
- [x] затем LLM shortlist.

Цель:

- маленькие библиотеки отвечают быстро;
- большие библиотеки не перегружают модель;
- сложные запросы получают больше локальных итераций.

## Этап 10. Agent planner v2

Статус: частично готово.

Нужно разделить:

- [ ] `RetrievalPlanner`;
- [ ] `ToolDispatcher`;
- [ ] `JsonRepairService`;
- [ ] `ActionExecutor`.
- [x] `IntentParser`;
- [ ] `RetrievalPlanner`;
- [ ] `ToolDispatcher`;
- [x] `PromptBuilder`;
- [x] `AiResponseParser`;
- [ ] `JsonRepairService`;
- [x] `ActionValidator`;
- [ ] `ActionExecutor`.

Model-directed workflow:

- [x] один read-only tool loop;
- [ ] несколько read-only итераций;
- [ ] retrieval planning через summaries;
- [ ] structured tool results;
- [ ] strict action schema.

## Этап 11. Safety v2

Статус: частично готово.

Готово:

- [x] whitelist action types;
- [x] validation of trackIds;
- [x] confirmation before writes;
- [x] no destructive actions.

Нужно:

- [x] AI-owned playlist registry;
- [ ] action risk levels;
- [ ] safe replacement only for AI-owned playlists;
- [ ] action history;
- [ ] audit log;
- [ ] optional undo for plugin-owned actions;
- [ ] external tool audit log.

## Этап 12. UI v2

Статус: частично готово.

Готово:

- [x] chat window;
- [x] settings window;
- [x] action preview;
- [x] selectable tracks.

Нужно:

- [ ] index status indicator;
- [ ] reindex button;
- [ ] candidate reasons display improvements;
- [ ] preview sorting/filtering;
- [ ] error details panel;
- [ ] copy logs button;
- [ ] provider test button;
- [ ] dockable panel, if this UI direction returns.

## Этап 13. Testing

Статус: почти не готово.

Нужно:

- [x] unit tests for JSON parser;
- [ ] unit tests for JSON repair;
- [x] unit tests for action validator;
- [ ] unit tests for normalization;
- [x] unit tests for ranking;
- [ ] unit tests for duration optimizer;
- [ ] mock MusicBee API adapter;
- [ ] integration smoke checklist;
- [ ] model compatibility matrix.

Модели для проверки:

- [ ] Qwen local;
- [ ] Llama local;
- [ ] сильная online OpenAI-compatible model.

## Этап 14. Packaging

Статус: частично готово.

Готово:

- [x] install script;
- [x] `.gitignore`.

Нужно:

- [x] README;
- [ ] build script;
- [ ] release zip;
- [ ] release notes;
- [ ] GitHub Actions workflow;
- [ ] troubleshooting page.

## Рекомендуемый ближайший порядок выполнения

1. Зафиксировать текущий MVP документацией и README.
2. Вынести domain models из текущих UI/API классов.
3. Добавить SQLite `LibraryIndex`.
4. Реализовать initial indexing.
5. Реализовать normalization layer.
6. Реализовать library profile.
7. Перенести текущий поиск на индекс.
8. Вынести scoring в `CandidateRanker`.
9. Добавить adaptive retrieval budgets.
10. Добавить AI-owned playlist registry.
11. Разделить AgentController на planner/parser/validator/executor.
12. Добавить unit tests для parser/validator/ranker.
13. Только после этого расширять сценарии агента и UI.
# Current implementation note: hybrid IntentParser

Status: done.

- `IntentParser` now combines deterministic local rules with an optional LLM intent extraction pass.
- The LLM pass returns only compact intent JSON and cannot choose tracks or execute actions.
- `RetrievalQuery` is used by local indexed search when available, improving multilingual and mixed-language requests.
- Details and manual test ideas are documented in `docs/intent-parser.md`.
