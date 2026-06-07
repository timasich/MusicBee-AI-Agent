# Архитектура MusicBee AI Agent

## Цель

MusicBee AI Agent должен быть не просто чат-окном, которое передает модели несколько треков, а масштабируемым музыкальным агентом для локальной библиотеки MusicBee.

Главный принцип:

> LLM не анализирует всю библиотеку и не управляет MusicBee напрямую. Локальный индекс и retriever-ы находят релевантные данные, LLM планирует, объясняет и выбирает из уже подготовленных кандидатов, а плагин валидирует и выполняет действия только после подтверждения пользователя.

Такая архитектура должна одинаково работать с маленькой библиотекой на сотни треков и с большой библиотекой на десятки тысяч треков.

## Архитектурные роли

### MusicBee API

MusicBee API является источником истины для:

- текущего трека;
- metadata треков;
- очереди Now Playing;
- плейлистов;
- статистики прослушивания;
- операций создания плейлистов и изменения очереди.

MusicBee API не должен использоваться как единственный поисковый механизм на каждый запрос. Для масштабируемости данные должны индексироваться локально.

### Локальный индекс библиотеки

Будущий центральный слой: `LibraryIndex`.

Рекомендуемое хранилище: SQLite.

Задачи:

- кэшировать metadata треков;
- хранить нормализованные поля;
- хранить статистику;
- строить summaries;
- поддерживать быстрый поиск;
- поддерживать staged retrieval;
- отделить тяжелую работу с библиотекой от LLM.

Минимальные таблицы:

- `tracks`;
- `artists`;
- `albums`;
- `genres`;
- `playlists`;
- `playlist_tracks`;
- `track_tokens`;
- `track_scores`;
- `library_profile`;
- `agent_actions`.

Позже:

- `track_features`;
- `track_embeddings`;
- `clusters`;
- `cluster_tracks`;
- `artist_summaries`;
- `genre_summaries`.

### Agent

Agent layer не должен быть просто prompt builder.

Он должен состоять из:

- `IntentParser`;
- `RetrievalPlanner`;
- `ToolDispatcher`;
- `PromptBuilder`;
- `AiResponseParser`;
- `JsonRepairService`;
- `ActionValidator`;
- `ActionExecutor`.

LLM используется как:

- интерпретатор сложного пользовательского запроса;
- помощник в выборе из ограниченного candidate set;
- генератор объяснения;
- генератор структурированного action.

LLM не используется как:

- база данных;
- поисковик по всей библиотеке;
- прямой исполнитель команд MusicBee;
- источник file paths.

## Многоуровневая систематизация библиотеки

### Уровень 0. Raw tracks

Данные из MusicBee:

- file URL;
- title;
- artist;
- album;
- album artist;
- genre;
- year;
- BPM;
- mood;
- rating;
- duration;
- play count;
- skip count;
- last played.

### Уровень 1. Нормализованные сущности

Нужно нормализовать:

- artist keys;
- album artist keys;
- genre tokens;
- mood tokens;
- year ranges;
- BPM ranges;
- rating buckets;
- language/script hints;
- title/album search tokens.

Пример:

- `Rap / Hip-hop / Experimental` -> `rap`, `hip-hop`, `experimental`;
- BPM 92 -> `mid-tempo`;
- year 2019 -> `late-2010s`;
- rating >= 70 -> `high-rated`.

### Уровень 2. Library profile

Сводка по библиотеке:

- общее число треков;
- основные жанры;
- основные артисты;
- самые высоко оцененные артисты;
- часто слушаемые артисты;
- недослушиваемые/часто пропускаемые зоны;
- распределение по годам;
- распределение по BPM;
- объем неизвестной metadata.

Эта сводка полезна для больших библиотек и сложных запросов вроде:

- "что у меня вообще есть для спокойной работы?";
- "собери что-то неочевидное из моей библиотеки";
- "найди недооцененные треки".

### Уровень 3. Кластеры

Кластеры могут строиться локально по metadata и статистике:

- high-rated electronic;
- calm focus music;
- energetic rock;
- Russian indie/pop;
- same-era tracks;
- underplayed but liked;
- recently played neighborhood;
- playlist co-occurrence groups.

На первом этапе кластеры можно делать правилами, без embeddings.

### Уровень 4. Summaries

Для больших библиотек нужны краткие summaries:

- artist summary;
- genre summary;
- cluster summary;
- playlist summary;
- listening profile summary.

Summaries можно генерировать лениво и кэшировать. LLM должна получать summaries до треков, когда запрос широкий.

## Retrieval architecture

Вместо одного поиска нужен набор retriever-ов.

### TextRetriever

Ищет по:

- title;
- artist;
- album;
- genre;
- mood;
- normalized tokens.

### SimilarityRetriever

Ищет похожие треки по:

- artist;
- album artist;
- genre overlap;
- mood overlap;
- BPM proximity;
- year proximity;
- rating;
- play count;
- skip count.

### ListeningStatsRetriever

Использует:

- high-rated;
- often played;
- rarely played;
- recently played;
- not played recently;
- high skip penalty.

### PlaylistCooccurrenceRetriever

Позже:

- какие треки пользователь уже группировал вместе;
- какие артисты часто встречаются в одних плейлистах;
- какие жанры смешиваются в пользовательских подборках.

### ClusterRetriever

Для больших библиотек:

- сначала выбирает релевантные кластеры;
- затем берет top candidates из каждого;
- потом отдает их reranker-у.

### ExternalInfoRetriever

Будущий модуль:

- web search;
- artist info;
- album info;
- new music recommendations.

Этот модуль должен быть отделен от MusicBee API и вызываться только как явный read-only tool.

## Ranking architecture

Retriever-ы возвращают candidates с partial scores.

`CandidateRanker` объединяет результаты:

- metadata score;
- text score;
- similarity score;
- listening score;
- playlist co-occurrence score;
- diversity penalty;
- duplicate penalty;
- queue exclusion penalty;
- duration fit score.

Результат:

- top N candidates;
- score;
- reason;
- source retrievers.

LLM получает не "сырые" тысячи треков, а уже ранжированный candidate set с объяснимыми причинами.

## Адаптация к размеру библиотеки

### Маленькая библиотека: до 500 треков

Стратегия:

- можно сканировать почти всю библиотеку;
- можно быстро построить top candidates без сложных кластеров;
- LLM получает компактный shortlist кандидатов, обычно 20-40 треков.

### Средняя библиотека: 500-5000 треков

Стратегия:

- использовать SQLite index;
- сначала применять filters/facets;
- затем ranking;
- LLM получает только top candidates;
- summaries используются для широких запросов.

### Большая библиотека: 5000-50000+ треков

Стратегия:

- не сканировать MusicBee API на каждый запрос;
- использовать индекс;
- сначала определить intent;
- получить library profile;
- выбрать релевантные кластеры;
- взять candidates из нескольких retriever-ов;
- rerank;
- отправить LLM только финальный shortlist.

Для сложных запросов агент может делать несколько итераций, но каждая итерация работает с summaries или shortlist, а не со всей библиотекой.

## Agent workflow

### Model-directed workflow

Цель: единое агентное поведение без пользовательских режимов модели.

1. Пользователь пишет запрос.
2. История чата и текущий MusicBee context передаются модели.
3. Модель классифицирует задачу и планирует работу оркестратора.
4. Оркестратор выполняет read-only retrieval/tools и возвращает summaries/candidates.
5. Модель может запросить дополнительный read-only шаг.
6. CandidateRanker и валидатор проверяют итоговый shortlist/action.
7. Модель предлагает response/action.
8. UI показывает preview для write actions.
9. Пользователь подтверждает.
10. ActionExecutor вызывает MusicBee API.

## Safety architecture

Разрешенные write actions MVP:

- `create_playlist`;
- `queue_tracks_last`;
- `queue_tracks_next`;
- `play_track_now`.

Запрещено:

- удалять файлы;
- удалять плейлисты;
- перезаписывать пользовательские плейлисты;
- очищать очередь;
- менять теги;
- коммитить теги в аудиофайлы.

Будущий безопасный механизм:

- AI-owned playlist registry;
- разрешение обновлять только AI-owned плейлисты;
- action risk levels;
- action history;
- optional undo для plugin-owned действий.

## External lookup architecture

- Модель может запросить только явные read-only internet tools.
- `lookup_listenbrainz_similar_artists` используется для похожих исполнителей.
- `lookup_wikipedia` используется для биографий, истории и внешних фактов.
- Локальные file paths не отправляются модели.
- Write actions всегда проходят через preview и подтверждение пользователя.

## Что уже реализовано

- MusicBee plugin shell.
- Settings.
- Chat window.
- OpenAI-compatible provider.
- Current track context.
- Basic library search.
- Basic similarity scoring.
- Action preview.
- Confirmed actions.
- Model-directed orchestration workflow.
- JSON repair pass.

## Что нужно спроектировать и реализовать следующим

1. SQLite `LibraryIndex`.
2. Indexing pipeline.
3. Normalization layer.
4. Library profile.
5. Multi-retriever architecture.
6. CandidateRanker.
7. Adaptive retrieval budgets.
8. AI-owned playlist registry.
9. Unit-testable validator/ranker/index modules.
