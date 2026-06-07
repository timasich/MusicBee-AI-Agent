# Фичи MusicBee AI Agent

## Готово

### Плагин MusicBee

- DLL собирается как `MB_AI_Agent.dll`.
- Проект собирается под `x86`.
- Плагин подключается в MusicBee.
- Добавлены пункты меню:
  - `MusicBee AI Agent - Open Chat`;
  - `MusicBee AI Agent - Settings`.

### Настройки AI provider

- OpenAI-compatible Base URL.
- API Key.
- Model name.
- Max tokens.
- Timeout.

### Чат

- Отдельное WinForms-окно.
- История сообщений.
- Поле ввода.
- Отправка по кнопке и Enter.
- Отображение ошибок.

### MusicBee context

- Чтение текущего трека.
- Чтение базовой metadata:
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

### Локальный поиск

- Поиск по библиотеке через MusicBee API.
- Базовый scoring:
  - текстовые совпадения;
  - тот же artist;
  - тот же album artist;
  - тот же genre;
  - тот же mood;
  - близкий BPM;
  - высокий rating;
  - play count;
  - штраф за skip count.

### Действия

Поддерживаются только подтверждаемые действия:

- `create_playlist`;
- `queue_tracks_last`;
- `queue_tracks_next`;
- `play_track_now`.

### Action preview

- Preview перед выполнением.
- Список треков.
- Чекбоксы для исключения треков.
- Суммарная длительность.
- Причина выбора трека.
- Альтернативные кнопки:
  - Confirm;
  - Queue Last;
  - Queue Next;
  - Create Playlist;
  - Cancel.

### Safety

- Модель не получает прямой доступ к MusicBee API.
- Модель выбирает только из предоставленных `trackIds`.
- Все `trackIds` валидируются локально.
- Write actions требуют подтверждения.
- Destructive actions не поддерживаются.
- Локальные file paths не отправляются модели.

### JSON robustness

- Строгий JSON parser.
- JSON repair pass.
- Локальная минимальная попытка восстановления простых сломанных ответов.
- Базовый SQLite index foundation.
- Первичное фоновое индексирование библиотеки.
- Ручной rebuild index через меню Tools.
- Canonical-дедупликация треков и версий.
- Diversity-aware ranking для запросов с разными исполнителями/альбомами.
- Локальное предупреждение, если библиотека не может дать достаточно разных исполнителей.

## Частично готово

### Tool loop

Есть один read-only tool loop для сильных моделей:

- `get_now_playing`;
- `search_library`;
- `find_similar_tracks_basic`;
- `get_current_queue`.

Ограничения:

- только один дополнительный проход;
- нет полноценного многошагового planner/executor цикла.

### Запросы по длительности

Поддерживается базовое распознавание:

- `30 minutes`;
- `1 hour`;
- `30 мин`;
- `1 час`.

Ограничения:

- подбор приближается к длительности, но не решает задачу оптимально;
- нет точного knapsack-подбора.

### External lookup

Есть read-only internet tools:

- `lookup_listenbrainz_similar_artists`;
- `lookup_wikipedia`.

Ограничения:

- внешняя информация используется только по явному tool request модели;
- write actions все равно требуют подтверждения пользователя.

## Не готово

- SQLite индекс библиотеки.
- История чата в базе.
- История actions в базе.
- AI-owned playlist registry.
- Безопасное обновление только AI-owned плейлистов.
- Test connection button.
- Получение списка моделей через `/models`.
- Dockable MusicBee panel.
- Интернет-рекомендации.
- Внешние provider presets.
- Unit-тесты.
- Интеграционные тесты с MusicBee automation.
- Сборочный pipeline.
- README для релиза.

## Запрещено в MVP

Эти действия не должны появляться до отдельного safety-дизайна:

- delete file;
- delete playlist;
- clear queue;
- overwrite user playlist;
- bulk edit tags;
- commit tags to files;
- downloader;
- streaming service integration.
