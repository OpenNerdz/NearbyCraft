# Nearby Craft

Nearby Craft lets 7 Days to Die use ingredients from inventories within 15 blocks when you craft. It also adds a craftable **Storage Network Console** that combines nearby player storage into one clean inventory screen. It targets game V3.2.

It includes accessible player storage, workstation output slots, collectors, vehicles, and drones. It excludes POI loot, land-claim internals, locked storage you cannot access, locked inventory slots, and containers another player is currently using.

The crafting panel has a **NEARBY: ON/OFF** control beside the recipe tabs. Its storage icon and label are green while active and grey while inactive. Clicking it refreshes recipe availability immediately and saves the choice to `config.json`.

## Storage Network Console

Craft the console at a workbench from 20 forged steel, 10 mechanical parts, 10 electrical parts, and 20 scrap polymers. Place it within 15 blocks of player chests or storage crates, then interact with its integrated control panel.

The terminal shows one virtual inventory backed by the real slots in every accessible player storage container in range. The header reports connected containers, used slots, and total items. You can:

- Search by localized or internal item name as you type.
- Cycle sorting between name, stack count, and item type; the selected mode is saved.
- Toggle whether the search box receives keyboard focus automatically whenever the console opens.
- Drag, swap, split, right-click, or shift-click items with the normal game controls.
- Deposit all unlocked backpack slots with the down-arrow button.
- Scroll through every matching item with the mouse wheel, draggable scrollbar, or row buttons, and manually rescan after placing or removing a chest.

The terminal does not copy items into itself. Every transfer validates and edits the original chest slots, respects container access and locked slots, and marks only changed tile entities for synchronization.

## Installation

1. Start 7 Days to Die with Easy Anti-Cheat disabled.
2. Extract `NearbyCraft-1.2.0-V3.2.zip` into the game's `Mods` folder.
3. The resulting path must be `Mods/NearbyCraft/ModInfo.xml`.
4. For multiplayer, install the same version on the server and every client.

Do not run this together with Beyond Storage, ProxiCraft, Craft From Containers, or another mod that changes crafting inventory lookup.

## Configuration

Edit `Mods/NearbyCraft/config.json` while the game is closed. `Range` controls nearby crafting and `TerminalRange` controls console connections; both are clamped to 1–30 blocks. `CacheMilliseconds` is clamped to 100–2000 ms. The nearby-crafting toggle, console sort mode, and search auto-focus preference are saved to this file when changed in the UI.

The default 250 ms snapshot is refreshed only when crafting code asks for it. There is no `Update` patch, background thread, coroutine, or whole-world scan. At the default range, only nearby loaded chunks are checked; source lists and item-count dictionaries are reused to limit garbage collection.

The terminal scans nearby loaded chunks once when opened and only scans again when you press its refresh button. Searching, sorting, and scrolling use its in-memory item index. The scroll view is virtualized, so it keeps only 54 item cells active even for very large networks. A transfer rebuilds the index from the already-connected containers; it does not search the world again.

## Building

The project references the DLLs from a local V3.2 installation. If your game is elsewhere, run:

```bash
dotnet build -c Release -p:GamePath="/path/to/7 Days To Die"
```

The installable archive is written to `dist/NearbyCraft-1.2.0-V3.2.zip`.

## Compatibility

Nearby Craft uses Harmony, so Easy Anti-Cheat must be disabled. Major 7 Days to Die updates can change method layouts; startup checks log an explicit error if the expected V3.2 crafting sites no longer match.

Concept and compatibility research referenced the MIT-licensed CraftFromContainers and ProxiCraft projects and the Apache-2.0-licensed Beyond Storage projects. This implementation is purpose-built for nearby crafting and storage-network management.
