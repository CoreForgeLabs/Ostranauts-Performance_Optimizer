
  Ostranauts Performance Optimizer v1
  Made with love by @CoreForgeLabs (telegram)
============================================================

## What this mod does

Eliminates periodic freezes (0.5-1.5 second stutters) caused
by Unity/Mono garbage collector (GC) during accelerated time
(16x speed).

## Versions

Two versions are included:

  512MB  — GC every ~50 seconds. RAM usage: ~4.0-4.3 GB.
           For PCs with 8 GB of RAM.

  1024MB — GC every ~100 seconds. RAM usage: ~4.8-5.0 GB.
           For PCs with 16+ GB RAM. (you can also try it on 8 GB)

## Installation

1. Choose the version you need (512MB or 1024MB).

2. Copy ALL contents of the version folder into the game root:
   C:\...\steamapps\common\Ostranauts\

   The result should be:
     Ostranauts\winhttp.dll
     Ostranauts\doorstop_config.ini
     Ostranauts\BepInEx\core\*.dll
     Ostranauts\BepInEx\plugins\OstronautsOptimizer.dll

3. Launch the game. Done!

   In the console (~) you will see:
   - "Mod Loaded"  — when the game starts
   - "Mod Working" — after loading a save

## Uninstallation

Delete BepInEx\plugins\OstronautsOptimizer.dll.
For complete BepInEx removal, also delete winhttp.dll,
doorstop_config.ini and the BepInEx\ folder.

## Configuration (optional)

After the first launch, a config file will appear at:
BepInEx\config\com.perf.ostranauts.optimizer.cfg

Key parameters:

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

1. Heap Pre-Expansion: After loading a save, the mod expands
   the Mono heap, creating a pool of free memory. GC only
   triggers when this pool is exhausted — instead of freezing
   every 5 seconds, it happens once every 1-2 minutes.

2. Sim Loop Optimization: Limits simulation load per frame,
   preventing stutters.

3. Allocation Reduction: Replaces allocating patterns
   (LINQ FirstOrDefault -> direct access, query caching).


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
