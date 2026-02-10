<p align="center">
  <img src="https://img.shields.io/badge/Game-Ostranauts-blue?style=for-the-badge" alt="Ostranauts"/>
  <img src="https://img.shields.io/badge/Framework-BepInEx%205-green?style=for-the-badge" alt="BepInEx 5"/>
  <img src="https://img.shields.io/badge/Unity-5.6-lightgrey?style=for-the-badge" alt="Unity 5.6"/>
  <img src="https://img.shields.io/github/v/release/CoreForgeLabs/Ostranauts-Performance_Optimizer?style=for-the-badge&label=Version" alt="Version"/>
</p>

<h1 align="center">⚡ Ostranauts Performance Optimizer v2</h1>

<p align="center">
  <b>Save loading 2x faster | Eliminates microfreezes | Auto-load saves</b><br/>
  <b>Загрузка сейвов в 2 раза быстрее | Убирает микрофризы | Авто-загрузка</b>
</p>

<p align="center">
  <a href="#-support--поддержка">❤️ Support</a> •
  <a href="#-download--скачать">⬇️ Download</a> •
  <a href="#-installation--установка">📦 Install</a> •
  <a href="#-whats-included--что-входит">🔧 What's included</a>
</p>

---

## ❤️ Support / Поддержка

<p align="center">
  <b>Made with love by <a href="https://t.me/CoreForgeLabs">@CoreForgeLabs</a></b><br/>
  Telegram · Discord
</p>

This is one of my favorite games, and I truly want to grow our small community.
Your support motivates me to keep developing and improving the mod.

Это одна из моих любимых игр, и я хочу развивать наше сообщество.
Ваша поддержка — это мотивация продолжать работу над проектом.

| | |
|--------|---------|
| **Boosty** | [boosty.to/coreforgelabs](https://boosty.to/coreforgelabs) |
| **Tbank** | `2200 7013 8955 0366` |
| **BTC** | `bc1qjzw4nz6y0dl3pvy8v46j70yywsh4l78sg0eq3x` |
| **ETH / USDT / USDC (ERC-20)** | `0xc9B7c16ef301E6277BbEB28C9AfCEC7c107d244E` |

**Besides modding / Помимо модов:**
🤖 Telegram/Discord bots • ⚙️ Automation • 🔗 Integrations • 🌍 Game translations

**Feel free to reach out! / Пишите — отвечу всем! :)**

---

## 🇬🇧 English | 🇷🇺 [Русский](#-русский)

### What this mod does

A complete performance package for Ostranauts:

| Plugin | What it does |
|--------|-------------|
| **SaveForce** | Save loading **2x faster** (72s → 33s). 16 Harmony patches: parallel parsing, caching, skip visual updates during load |
| **OstronautsOptimizer** | Eliminates **0.5–1.5s freezes** during gameplay. Heap pre-expansion, sim loop throttling, allocation reduction |
| **Run** | Optional auto-load of the most recent save on game start |

### 📥 Download / Скачать

**[⬇️ Download Latest Release](https://github.com/CoreForgeLabs/Ostranauts-Performance_Optimizer/releases/latest)**

Or clone and use the `v2/` folder directly.

### 📦 Installation / Установка

1. Download or clone this repository
2. Copy the **contents of `v2/`** into your game folder:
   ```
   C:\...\steamapps\common\Ostranauts\
   ```
3. Your game folder should look like:
   ```
   Ostranauts\
   ├── winhttp.dll              ← new
   ├── doorstop_config.ini      ← new
   ├── RUNSAVE.bat              ← new (optional quick-load)
   ├── BepInEx\
   │   ├── core\*.dll           ← new (framework)
   │   └── plugins\
   │       ├── SaveForce.dll    ← save load optimization
   │       ├── OstronautsOptimizer.dll  ← runtime optimization
   │       └── Run.dll          ← auto-load (optional)
   ├── Ostranauts.exe
   └── Ostranauts_Data\
   ```
4. **Launch the game.** Done!

### 🚀 Quick-Load (optional)

Create a shortcut to `RUNSAVE.bat` on your desktop.
Double-click it to instantly launch the game and load your most recent save — no menus!

### ❌ Uninstallation

Delete `BepInEx\plugins\SaveForce.dll`, `Run.dll`, and `OstronautsOptimizer.dll`.

For complete BepInEx removal, also delete `winhttp.dll`, `doorstop_config.ini` and the `BepInEx\` folder.

### ⚙️ Configuration (optional)

After first launch, config files appear in `BepInEx\config\`:

**SaveForce** (`com.coreforgelabs.saveforce.cfg`):

| Parameter | Default | Description |
|-----------|---------|-------------|
| `ParallelShips` | true | Parse ship JSONs in parallel threads |
| `ReduceYields` | true | Batch multiple yield operations into one |
| `YieldBatchSize` | 50 | Items processed per batch |
| `ConditionCache` | true | Cache condition parsing results |

**OstronautsOptimizer** (`com.perf.ostranauts.optimizer.cfg`):

| Parameter | Default | Description |
|-----------|---------|-------------|
| `HeapExpansionMB` | 1024 | Mono heap pre-expansion (0=off, 512=good, 1024=best) |
| `FrameBudgetMs` | 12 | Frame time budget for simulation (ms) |
| `MaxSimStepsPerFrame` | 50 | Hard cap on sim steps per frame |
| `MaxDeltaTime` | 0.1 | Clamps deltaTime after GC freeze |

**Run** (`com.coreforgelabs.run.cfg`):

| Parameter | Default | Description |
|-----------|---------|-------------|
| `AutoLoadMostRecentSave` | false | Auto-load on every launch (true) or only via RUNSAVE.bat (false) |

### 🔧 What's included / Что входит

#### SaveForce — Save Load Optimization

16 Harmony patches that reduce save loading from ~72–82s to ~33s (56–60% faster):

- **Parallel JSON parsing** — ship data parsed in worker threads
- **Condition caching** — 460K+ cache hits, 99.8% hit rate
- **ParseCondEquation cache** — 403K hits, 97% hit rate
- **CondRule cache** — 28K hits, 98.4% hit rate
- **Skip UpdateFaces during load** — 507K calls skipped
- **Skip VisualizeOverlays during load** — 78K calls skipped
- **Batched yield operations** — reduces Unity coroutine overhead
- **Inter-batch GC** — garbage collection between load batches
- **Profiling** — detailed timing logs in BepInEx console

#### OstronautsOptimizer — Runtime Optimization

- **Heap Pre-Expansion** — expands Mono heap post-load, GC triggers every ~100s instead of ~5s
- **Sim Loop Throttling** — prevents simulation from consuming entire frame budget
- **Allocation Reduction** — LINQ → direct access, query caching

#### Run — Auto-Load

- Flag-file based auto-load via `RUNSAVE.bat`
- Or config-based auto-load on every launch

### 🛠️ Building from Source

Source code and build scripts are in the `BUILD/` folder.

```powershell
cd BUILD
# Edit game_path.cfg with your game path (auto-detects via Steam registry)
.\run.ps1              # Build + launch
.\pack.ps1             # Build + create distribution ZIP
.\BUILD_AND_RUN.bat    # Same as run.ps1, but as .bat
```

Requirements: .NET Framework 4+ (csc.exe), BepInEx 5.4.23.2 installed in game folder.

---

## 🇷🇺 Русский

### Что делает мод

Комплексный пакет оптимизации для Ostranauts:

| Плагин | Что делает |
|--------|-----------|
| **SaveForce** | Загрузка сейвов **в 2 раза быстрее** (72с → 33с). 16 патчей: параллельный парсинг, кеширование, пропуск визуалов при загрузке |
| **OstronautsOptimizer** | Устраняет **фризы 0.5–1.5с** во время игры. Расширение кучи, троттлинг симуляции, снижение аллокаций |
| **Run** | Опциональная авто-загрузка последнего сейва при запуске |

### 📥 Скачать

**[⬇️ Скачать последнюю версию](https://github.com/CoreForgeLabs/Ostranauts-Performance_Optimizer/releases/latest)**

Или склонируйте репозиторий и используйте папку `v2/`.

### 📦 Установка

1. Скачайте или склонируйте этот репозиторий
2. Скопируйте **содержимое папки `v2/`** в папку игры:
   ```
   C:\...\steamapps\common\Ostranauts\
   ```
3. В папке с игрой должно быть:
   ```
   Ostranauts\
   ├── winhttp.dll              ← новое
   ├── doorstop_config.ini      ← новое
   ├── RUNSAVE.bat              ← новое (быстрая загрузка)
   ├── BepInEx\
   │   ├── core\*.dll           ← новое (фреймворк)
   │   └── plugins\
   │       ├── SaveForce.dll    ← оптимизация загрузки
   │       ├── OstronautsOptimizer.dll  ← рантайм оптимизация
   │       └── Run.dll          ← авто-загрузка (опцион.)
   ├── Ostranauts.exe
   └── Ostranauts_Data\
   ```
4. **Запустите игру.** Готово!

### 🚀 Быстрая загрузка (опционально)

Создайте ярлык на `RUNSAVE.bat` на рабочем столе.
Двойной клик — мгновенный запуск игры с загрузкой последнего сейва, без меню!

### ❌ Удаление

Удалите `BepInEx\plugins\SaveForce.dll`, `Run.dll` и `OstronautsOptimizer.dll`.

Для полного удаления BepInEx также удалите `winhttp.dll`, `doorstop_config.ini` и папку `BepInEx\`.

### ⚙️ Настройка (опционально)

После первого запуска в `BepInEx\config\` появятся конфиг-файлы:

**SaveForce** (`com.coreforgelabs.saveforce.cfg`):

| Параметр | По умолч. | Описание |
|----------|-----------|----------|
| `ParallelShips` | true | Параллельный парсинг JSON кораблей |
| `ReduceYields` | true | Пакетная обработка yield-операций |
| `YieldBatchSize` | 50 | Объектов за один батч |
| `ConditionCache` | true | Кеширование парсинга условий |

**OstronautsOptimizer** (`com.perf.ostranauts.optimizer.cfg`):

| Параметр | По умолч. | Описание |
|----------|-----------|----------|
| `HeapExpansionMB` | 1024 | Расширение кучи (0=выкл, 512=хорошо, 1024=рекомендуется) |
| `FrameBudgetMs` | 12 | Бюджет фрейма для симуляции (мс) |
| `MaxSimStepsPerFrame` | 50 | Макс шагов симуляции за фрейм |
| `MaxDeltaTime` | 0.1 | Ограничение deltaTime после GC |

**Run** (`com.coreforgelabs.run.cfg`):

| Параметр | По умолч. | Описание |
|----------|-----------|----------|
| `AutoLoadMostRecentSave` | false | Авто-загрузка при каждом запуске (true) или только через RUNSAVE.bat (false) |

### 🛠️ Сборка из исходников

Исходный код и скрипты сборки в папке `BUILD/`.

```powershell
cd BUILD
# Отредактируйте game_path.cfg (или автодетект через реестр Steam)
.\run.ps1              # Собрать + запустить
.\pack.ps1             # Собрать + создать ZIP для раздачи
.\BUILD_AND_RUN.bat    # То же что run.ps1, но .bat
```

---

<p align="center">
  <sub>© 2026 CoreForgeLabs</sub>
</p>
