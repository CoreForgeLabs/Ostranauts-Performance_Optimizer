## 🚀 v2.0 — Performance Optimizer + Save Accelerator

### What's New
- ⚡ **Save loading 55-60% faster** — parallel ship parsing, condition caching, visual skip during load *(72-82s → ~33s)*
- 🧊 **GC freeze eliminator** — eliminates periodic 0.5-1.5s stutters caused by garbage collector
- 🎯 **Quick Load** — `RUNSAVE.bat` auto-loads your last save, straight into gameplay

### Installation
Extract `v2/` contents into your game folder:
```
Ostranauts\
├── winhttp.dll
├── doorstop_config.ini
├── RUNSAVE.bat
└── BepInEx\
    ├── core\
    └── plugins\
        ├── SaveForce.dll
        ├── Run.dll
        └── OstronautsOptimizer.dll
```

Launch the game normally or use `RUNSAVE.bat` for instant quick-load.

---

### Что нового
- ⚡ **Загрузка сейвов на 55-60% быстрее** — параллельный парсинг кораблей, кеширование условий, пропуск визуала *(72-82с → ~33с)*
- 🧊 **Устранение GC-фризов** — убирает периодические зависания 0.5-1.5с, вызванные сборщиком мусора
- 🎯 **Быстрый запуск** — `RUNSAVE.bat` автозагрузка последнего сейва, сразу в игру

### Установка
Распакуйте содержимое `v2/` в папку игры и запустите обычным способом или через `RUNSAVE.bat`.
