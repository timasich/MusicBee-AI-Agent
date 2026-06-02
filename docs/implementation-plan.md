# План создания плагина

## Статус проекта

Текущий статус: рабочий MVP, требующий стабилизации на разных моделях и более сильного тестирования.

Проверено вручную:

- плагин подключается в MusicBee;
- настройки открываются;
- OpenAI-compatible endpoint LM Studio отвечает;
- текущий трек читается;
- плейлист создается после подтверждения;
- preview action работает.

Выявленная проблема:

- маленькие локальные модели могут ломать JSON при длинном prompt;
- для этого добавлен Small local model mode и JSON repair pass.

## Этап 1. Базовый MusicBee plugin

- [x] Собрать sample plugin.
- [x] Переименовать DLL в `MB_*.dll`.
- [x] Зафиксировать `x86`.
- [x] Подключить plugin в MusicBee.
- [x] Добавить пункты меню.
- [x] Добавить окно настроек.
- [x] Добавить окно чата.

## Этап 2. OpenAI-compatible provider

- [x] Base URL.
- [x] API Key.
- [x] Model.
- [x] Temperature.
- [x] Max tokens.
- [x] Timeout.
- [x] `POST /chat/completions`.
- [ ] Test connection button.
- [ ] `/models` discovery.
- [ ] Provider presets:
  - [ ] LM Studio;
  - [ ] Ollama;
  - [ ] Custom;
  - [ ] OpenAI;
  - [ ] OpenRouter или совместимый provider.

## Этап 3. MusicBee context

- [x] Current track.
- [x] Basic metadata.
- [x] Now Playing queue summary.
- [x] Basic library query.
- [ ] Recent listening summary.
- [ ] Top artists/genres summary.
- [ ] Playlist list.
- [ ] Playlist tracks.

## Этап 4. Agent safety

- [x] Structured JSON response.
- [x] Local validation of action type.
- [x] Local validation of trackIds.
- [x] Confirmation before write actions.
- [x] No destructive actions in MVP.
- [x] No file paths in MetadataOnly.
- [x] JSON repair pass.
- [ ] Stronger action schema validator.
- [ ] Action risk levels.
- [ ] AI-owned playlist registry.

## Этап 5. Local search and recommendations

- [x] Basic search by metadata.
- [x] Similarity scoring.
- [x] BPM/rating/play count/skip count signals.
- [x] Candidate limiting.
- [x] Duration-aware candidate selection.
- [ ] Better Russian/English query normalization.
- [ ] Exact duration optimizer.
- [ ] Duplicate detection.
- [ ] Exclude currently queued tracks.
- [ ] SQLite library index.
- [ ] Incremental reindex by MusicBee library events.

## Этап 6. Actions

- [x] Create playlist.
- [x] Queue last.
- [x] Queue next.
- [x] Play now.
- [ ] Append to existing AI-owned playlist.
- [ ] Replace only AI-owned playlist.
- [ ] Save action history.
- [ ] Undo for plugin-owned actions where possible.

## Этап 7. UI

- [x] Standalone chat window.
- [x] Settings window.
- [x] Action preview.
- [x] Track checkbox selection.
- [x] Alternative action buttons.
- [ ] Better layout and resizing.
- [ ] Error detail panel.
- [ ] Copy logs button.
- [ ] Dockable panel.
- [ ] Chat history persistence.
- [ ] Preview filtering/sorting.

## Этап 8. Local model stability

- [x] Small local model mode.
- [x] Shorter prompt in small mode.
- [x] Disable tool loop in small mode.
- [x] Lower candidate count in small mode.
- [x] JSON repair pass.
- [ ] Model-specific prompt presets.
- [ ] Output examples tuned for small models.
- [ ] Retry with stricter max tokens.
- [ ] Local fallback response when model JSON cannot be repaired.

## Этап 9. Testing

- [x] Manual MVP smoke test.
- [x] Manual playlist creation test.
- [ ] Unit tests for JSON parser.
- [ ] Unit tests for action validator.
- [ ] Unit tests for search scoring.
- [ ] Mock MusicBee API adapter tests.
- [ ] Integration test checklist.
- [ ] Test matrix by model:
  - [ ] Gemma small local;
  - [ ] Qwen local;
  - [ ] Llama local;
  - [ ] OpenAI-compatible online model.

## Этап 10. Packaging and release

- [x] Install script.
- [x] `.gitignore`.
- [ ] README.
- [ ] Release notes.
- [ ] Zip package.
- [ ] Build script.
- [ ] GitHub Actions workflow.

## Ближайшие задачи

1. Протестировать Small local model mode на LM Studio.
2. Проверить, что JSON repair pass убирает ошибки вида `Expected "."`.
3. Добавить fallback, если repair тоже не сработал.
4. Добавить `README.md`.
5. Добавить AI-owned playlist registry.
6. Добавить unit-тестируемый слой validator/search отдельно от MusicBee API.
