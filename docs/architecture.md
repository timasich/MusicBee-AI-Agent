# Архитектура MusicBee AI Agent

## Цель

Плагин добавляет AI-ассистента в MusicBee. Пользователь пишет запрос на естественном языке, плагин собирает ограниченный локальный контекст MusicBee, отправляет его в OpenAI-compatible endpoint и получает текстовый ответ или структурированное действие.

Модель не управляет MusicBee напрямую. Она возвращает JSON-интенты. Плагин валидирует их, показывает preview и выполняет только после подтверждения пользователя.

## Основные слои

### Plugin entrypoint

Файл: `Plugin.cs`

Отвечает за:

- инициализацию MusicBee API;
- загрузку настроек;
- регистрацию пунктов меню;
- открытие окна чата и окна настроек;
- корректное закрытие формы при завершении MusicBee.

### UI

Файлы:

- `ChatForm.cs`
- `SettingsForm.cs`

`ChatForm` показывает историю сообщений, поле ввода и action preview. Preview содержит список треков с чекбоксами, суммарную длительность и быстрые действия:

- подтвердить действие модели;
- добавить в конец очереди;
- добавить следующим;
- создать плейлист;
- отменить.

`SettingsForm` управляет provider-настройками:

- Base URL;
- API Key;
- Model;
- Temperature;
- Max tokens;
- Timeout;
- Privacy mode;
- Small local model mode.

### Agent

Файл: `AgentController.cs`

Отвечает за:

- построение intent из запроса пользователя;
- выбор candidate tracks;
- построение prompt;
- вызов AI provider;
- JSON parse;
- JSON repair pass;
- read-only tool loop для сильных моделей;
- валидацию action;
- исполнение подтвержденных действий.

### AI Provider

Файл: `OpenAiCompatibleProvider.cs`

Текущая реализация поддерживает OpenAI-compatible Chat Completions API:

- `POST /chat/completions`;
- `model`;
- `temperature`;
- `max_tokens`;
- `messages`;
- optional Bearer API key.

Подходит для LM Studio, Ollama-compatible gateway и онлайн-провайдеров с OpenAI-compatible endpoint.

### MusicBee Integration

Файл: `MusicBeeApiAdapter.cs`

Оборачивает MusicBee API:

- текущий трек;
- очередь Now Playing;
- поиск по библиотеке;
- базовый similarity scoring;
- создание плейлиста;
- queue next / queue last;
- play now.

Запрещенные операции не вынесены в агентный слой: удаление файлов, удаление плейлистов, bulk edit tags, commit tags.

### Data / Settings

Файлы:

- `PluginSettings.cs`
- `Models.cs`

Настройки сохраняются в XML в persistent storage path MusicBee.

Пока нет SQLite. История чата и actions пишутся только в простой log-файл.

## Agent workflow

1. Пользователь пишет сообщение.
2. Плагин читает текущий трек.
3. Плагин строит локальный intent.
4. Плагин ищет candidate tracks.
5. Плагин отправляет короткий prompt в модель.
6. Модель возвращает JSON.
7. Плагин пытается распарсить JSON.
8. Если JSON сломан, выполняется repair pass.
9. Если есть action, плагин валидирует:
   - action type разрешен;
   - requiresConfirmation=true;
   - все trackIds есть в candidate set;
   - play_track_now содержит ровно один трек.
10. UI показывает preview.
11. Пользователь подтверждает или меняет выбранные треки.
12. Плагин выполняет действие через MusicBee API.

## Small local model mode

Режим предназначен для маленьких локальных моделей вроде Gemma 2B/4B.

В этом режиме:

- candidate tracks ограничены сильнее;
- prompt короче;
- tool loop отключен;
- модель получает меньше metadata;
- схема JSON проще;
- repair pass остается включенным.

Цель режима: стабильный JSON важнее сложного агентного поведения.
