<p align="center">
  <img src="https://img.shields.io/badge/Game-Ostranauts-blue?style=for-the-badge" alt="Ostranauts"/>
  <img src="https://img.shields.io/badge/Framework-BepInEx%205-green?style=for-the-badge" alt="BepInEx 5"/>
  <img src="https://img.shields.io/badge/Unity-5.6-lightgrey?style=for-the-badge" alt="Unity 5.6"/>
  <img src="https://img.shields.io/github/v/release/CoreForgeLabs/Ostranauts-Performance_Optimizer?style=for-the-badge&label=Version" alt="Version"/>
</p>

<h1 align="center">⚡ Ostranauts Performance Optimizer</h1>

<p align="center">
  <b>Eliminates freezes at 16x speed | Убирает фризы на 16x скорости</b>
</p>

<p align="center">
  <a href="#-download--скачать">⬇️ Download</a> •
  <a href="#-installation--установка">📦 Install</a> •
  <a href="#-how-it-works--как-работает">🔧 How it works</a> •
  <a href="#-support--поддержка">❤️ Support</a>
</p>

---

## 🇬🇧 English | 🇷🇺 [Русский](#-русский)

### What this mod does

Eliminates periodic **0.5–1.5 second freezes** caused by Unity/Mono garbage collector (GC) during accelerated time (**16x speed**).

**Without mod:** GC triggers every ~5 seconds → constant stuttering  
**With mod:** GC triggers every ~50–100 seconds → smooth gameplay

### 📥 Download / Скачать

**[⬇️ Download Latest Release](https://github.com/CoreForgeLabs/Ostranauts-Performance_Optimizer/releases/latest)**

Two versions included / В архиве две версии:

| Version | RAM Usage | GC Interval | Best For |
|---------|-----------|-------------|----------|
| **1024 MB** | ~4.8–5.0 GB | every ~100s | 🖥️ 16+ GB RAM (recommended) |
| **512 MB** | ~4.0–4.3 GB | every ~50s | 💻 8 GB RAM |

> 💡 **Not sure which one?** If you have 16 GB RAM or more — take **1024 MB**.  
> If you have 8 GB RAM — take **512 MB** (можно попробовать и 1024 на 8-ми).

### 📦 Installation / Установка

1. Download the archive from [Releases](https://github.com/CoreForgeLabs/Ostranauts-Performance_Optimizer/releases/latest)
2. Choose your version inside: `512MB` or `1024MB`
3. **Extract** the version folder contents into your game folder:
   ```
   C:\...\steamapps\common\Ostranauts\
   ```
4. Your game folder should look like:
   ```
   Ostranauts\
   ├── winhttp.dll          ← new
   ├── doorstop_config.ini  ← new
   ├── BepInEx\
   │   ├── core\*.dll       ← new
   │   └── plugins\
   │       └── OstronautsOptimizer.dll  ← the mod
   ├── Ostranauts.exe
   └── Ostranauts_Data\
   ```
5. **Launch the game.** Done!

In the in-game console (`~`) you will see:
- **"Mod Loaded"** — when the game starts
- **"Mod Working"** — after loading a save

### ❌ Uninstallation

Delete `BepInEx\plugins\OstronautsOptimizer.dll`.

For complete BepInEx removal, also delete `winhttp.dll`, `doorstop_config.ini` and the `BepInEx\` folder.

### ⚙️ Configuration (optional)

After the first launch, a config file appears at:  
`BepInEx\config\com.perf.ostranauts.optimizer.cfg`

| Parameter | Default | Description |
|-----------|---------|-------------|
| `HeapExpansionMB` | 1024 | Heap pre-expansion size. `0` = off, `256` = moderate, `512` = good, `1024` = recommended |
| `FrameBudgetMs` | 12 | Frame time budget for simulation (ms) |
| `MaxSimStepsPerFrame` | 50 | Hard cap on simulation steps per frame |
| `MaxDeltaTime` | 0.1 | Clamps deltaTime after GC freeze |
| `OptFirstOrDefault` | true | Optimized list search (LINQ → direct access) |
| `SuppressInteractionLog` | true | Caches missing interaction lookups |

### 🔧 How it works / Как работает

1. **Heap Pre-Expansion** — After loading a save, the mod expands the Mono heap by 512–1024 MB, creating a pool of free memory. GC only triggers when this pool is exhausted — instead of freezing every 5 seconds, it happens once every 1–2 minutes.

2. **Sim Loop Optimization** — Limits simulation load per frame, preventing lag spikes.

3. **Allocation Reduction** — Replaces allocating patterns (LINQ `FirstOrDefault` → direct access, query caching).

---

## 🇷🇺 Русский

### Что делает мод

Устраняет периодические **фризы 0.5–1.5 секунды**, вызванные сборщиком мусора (GC) Unity/Mono при ускоренном времени (**16x скорость**).

**Без мода:** GC срабатывает каждые ~5 секунд → постоянные подвисания  
**С модом:** GC срабатывает каждые ~50–100 секунд → плавный геймплей

### 📥 Скачать

**[⬇️ Скачать последнюю версию](https://github.com/CoreForgeLabs/Ostranauts-Performance_Optimizer/releases/latest)**

В архиве две версии:

| Версия | Расход RAM | Интервал GC | Для кого |
|--------|------------|-------------|----------|
| **1024 MB** | ~4.8–5.0 GB | каждые ~100с | 🖥️ 16+ GB RAM (рекомендуется) |
| **512 MB** | ~4.0–4.3 GB | каждые ~50с | 💻 8 GB RAM |

> 💡 **Не знаете какую выбрать?** Если у вас 16 ГБ RAM и выше — берите **1024 MB**.  
> Если 8 ГБ — берите **512 MB** (можно попробовать и 1024 на 8-ми).

### 📦 Установка

1. Скачайте архив из [Releases](https://github.com/CoreForgeLabs/Ostranauts-Performance_Optimizer/releases/latest)
2. Выберите нужную версию внутри: `512MB` или `1024MB`
3. **Распакуйте** содержимое папки версии в папку игры:
   ```
   C:\...\steamapps\common\Ostranauts\
   ```
4. В папке игры должно быть:
   ```
   Ostranauts\
   ├── winhttp.dll          ← новое
   ├── doorstop_config.ini  ← новое
   ├── BepInEx\
   │   ├── core\*.dll       ← новое
   │   └── plugins\
   │       └── OstronautsOptimizer.dll  ← мод
   ├── Ostranauts.exe
   └── Ostranauts_Data\
   ```
5. **Запустите игру.** Готово!

В консоли (`~`) появятся сообщения:
- **"Mod Loaded"** — при запуске игры
- **"Mod Working"** — после загрузки сейва

### ❌ Удаление

Удалите `BepInEx\plugins\OstronautsOptimizer.dll`.

Для полного удаления BepInEx также удалите `winhttp.dll`, `doorstop_config.ini` и папку `BepInEx\`.

### ⚙️ Настройка (опционально)

После первого запуска появится файл конфигурации:  
`BepInEx\config\com.perf.ostranauts.optimizer.cfg`

| Параметр | По умолчанию | Описание |
|----------|-------------|----------|
| `HeapExpansionMB` | 1024 | Размер расширения кучи. `0` = выкл, `256` = умеренно, `512` = хорошо, `1024` = рекомендуется |
| `FrameBudgetMs` | 12 | Бюджет фрейма для симуляции (мс) |
| `MaxSimStepsPerFrame` | 50 | Макс шагов симуляции за фрейм |
| `MaxDeltaTime` | 0.1 | Ограничение deltaTime после фриза GC |
| `OptFirstOrDefault` | true | Оптимизация поиска в списках |
| `SuppressInteractionLog` | true | Кеширование отсутствующих взаимодействий |

### 🔧 Как работает

1. **Heap Pre-Expansion** — После загрузки сейва мод расширяет Mono heap на 512–1024 МБ, создавая запас свободной памяти. GC запускается только когда запас исчерпан — вместо каждых 5 секунд фриз происходит раз в 1–2 минуты.

2. **Sim Loop Optimization** — Ограничивает нагрузку симуляции на каждый фрейм, предотвращая рывки.

3. **Allocation Reduction** — Заменяет аллоцирующие паттерны (LINQ `FirstOrDefault` → прямой доступ, кеширование запросов).

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

---

**Besides modding, I also do / Помимо модов, я занимаюсь:**
- 🤖 Telegram / Discord bots
- ⚙️ Scripts & automation utilities
- 🔗 Integrations & data parsing
- 🌍 Game translations
- And more / И многое другое!

**Feel free to reach out — I'll reply to everyone!**  
**Пишите — отвечу всем! :)**

---

### 💰 Donations / Донаты

| Method | Details |
|--------|---------|
| **Boosty** | [boosty.to/coreforgelabs](https://boosty.to/coreforgelabs) |
| **Tbank** | `2200 7013 8955 0366` |
| **BTC** | `bc1qjzw4nz6y0dl3pvy8v46j70yywsh4l78sg0eq3x` |
| **ETH / USDT / USDC (ERC-20)** | `0xc9B7c16ef301E6277BbEB28C9AfCEC7c107d244E` |

---

<p align="center">
  <sub>© 2025 CoreForgeLabs</sub>
</p>
