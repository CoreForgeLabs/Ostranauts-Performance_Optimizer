============================================================
  Ostranauts Performance Optimizer v2
  Made with love by @CoreForgeLabs (telegram)
============================================================

## What this mod does

Two major performance improvements:

  1. GC Freeze Eliminator
     Eliminates periodic 0.5-1.5 second freezes caused by
     Unity/Mono garbage collector during accelerated time (16x).

  2. Save Load Accelerator (SaveForce)
     Reduces save loading time by 55-60%.
     Example: 72-82 seconds -> ~33 seconds on large saves.

     Optimizations include:
     - Parallel ship JSON parsing (multi-threaded)
     - Condition & CondRule template caching with deep-clone
     - Skip visual updates during loading (faces, overlays)
     - Batched coroutine execution
     - Equation parse caching

  3. Quick Load (RUNSAVE.bat)
     Launch the game and auto-load your last save instantly.
     No menu waiting — straight into the game!

## Installation

1. Copy ALL contents of this folder into the game root:
   C:\...\steamapps\common\Ostranauts\

   The result should be:
     Ostranauts\winhttp.dll
     Ostranauts\doorstop_config.ini
     Ostranauts\RUNSAVE.bat
     Ostranauts\BepInEx\core\*.dll
     Ostranauts\BepInEx\plugins\SaveForce.dll
     Ostranauts\BepInEx\plugins\Run.dll
     Ostranauts\BepInEx\plugins\OstronautsOptimizer.dll

2. Launch the game normally or use RUNSAVE.bat for quick load.
   Done!

## Quick Load

RUNSAVE.bat launches the game and auto-loads your most recent
save. Just double-click it or create a desktop shortcut.
No need to wait through menus — you go straight into gameplay.

## Uninstallation

Delete the files from BepInEx\plugins\:
  SaveForce.dll, Run.dll, OstronautsOptimizer.dll

For complete BepInEx removal, also delete winhttp.dll,
doorstop_config.ini and the BepInEx\ folder.

## Configuration (optional)

After the first launch, config files appear in BepInEx\config\:

--- SaveForce (com.coreforgelabs.saveforce.cfg) ---

  ParallelShips = true
    Multi-threaded ship parsing during save load.

  ReduceYields = true
    Batch coroutine yields for faster initialization.

  YieldBatchSize = 10
    Number of items per batch (higher = faster but less smooth).

  ConditionCache = true
    Cache Condition objects to avoid redundant creation.

  KillDuplicates = true
    Auto-close duplicate game instances.

--- Optimizer (com.perf.ostranauts.optimizer.cfg) ---

  HeapExpansionMB — heap expansion size (MB)
    0    = disabled
    256  = moderate (GC every ~25s)
    512  = good (GC every ~50s)
    1024 = recommended (GC every ~100s)

  FrameBudgetMs = 12
    Frame time budget for simulation (ms).

  MaxSimStepsPerFrame = 50
    Max simulation steps per frame.

  MaxDeltaTime = 0.1
    Clamps deltaTime after GC freeze.

  OptFirstOrDefault = true
    Optimized list search.

  SuppressInteractionLog = true
    Caches missing interaction lookups.

## How it works

GC Freeze Eliminator:
  1. Heap Pre-Expansion: After loading a save, expands the Mono
     heap, creating a pool of free memory. GC triggers only when
     the pool is exhausted — once every 1-2 minutes instead of
     every 5 seconds.
  2. Sim Loop Optimization: Limits simulation load per frame.
  3. Allocation Reduction: Replaces allocating patterns
     (LINQ FirstOrDefault -> direct access, query caching).

Save Load Accelerator:
  1. Parallel Parsing: Ship JSON files are parsed on background
     threads while the main thread handles other tasks.
  2. Template Caching: Conditions and rules are cached with safe
     deep-cloning, eliminating redundant object creation.
  3. Visual Skip: Face rendering and item overlay visualization
     are skipped during loading (not needed until gameplay).
  4. Yield Batching: Coroutine yields are batched to reduce
     frame-switching overhead during initialization.

============================================================
  @CoreForgeLabs (telegram/Discord)
  https://boosty.to/coreforgelabs
============================================================

Support the development

This is one of my favorite games, and I truly want to grow our
small community. But without your support, there is a real risk
that mod development will slow down — my day job and daily tasks
gradually take up more and more time.

Your support is not just financial help. It's motivation to keep
working on the project and confidence that the mod actually
matters to someone.

──────────────────────────────────────────────────────────────────────────────

Besides modding, I also do software development:
  • Scripts and utilities for automation
  • Telegram / Discord bots
  • Integrations and data parsing
  • Game translations
  • And much more!

Feel free to reach out — I'll reply to everyone :)

Donations Crypto:
BTC
bc1qjzw4nz6y0dl3pvy8v46j70yywsh4l78sg0eq3x

ETH|USDT|USDC erc20
0xc9B7c16ef301E6277BbEB28C9AfCEC7c107d244E
