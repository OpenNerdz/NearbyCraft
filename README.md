# Nearby Craft 1.4.0

A local-world quality-of-life mod for **7 Days to Die V3.2 b10**. Craft using nearby supplies, manage a chest network through a searchable Storage Console, and exchange complete activity loadouts through that same network. Easy Anti-Cheat must be off.

## Storage consoles and upgrades

The original console is now **Tier 1**. Existing chests and their contents are not moved or deleted. If a console has more chests nearby than its tier permits, the nearest eligible chests connect first; equal distances use stable position ordering. Extra chests remain accessible normally and are reported as out of capacity.

| Build / upgrade | Chest limit | Forged steel | Mechanical parts | Electrical parts | Scrap polymers |
| --- | ---: | ---: | ---: | ---: | ---: |
| Craft Tier 1 console | 8 | 20 | 10 | 10 | 20 |
| Tier 1 → 2 kit | 16 | 20 | 10 | 20 | 20 |
| Tier 2 → 3 kit | 32 | 40 | 20 | 40 | 40 |
| Tier 3 → 4 kit | 64 | 80 | 40 | 80 | 80 |

All recipes are available at the workbench. **Carry one kit**, close the console, fully repair it, then use the repair/upgrade action of a stone axe, claw hammer or nailgun. The game's normal upgrade action consumes one kit. Upgrade sequentially; no Tier 5 exists. Kits cannot be sold to traders.

To move a player-placed console, fully repair it inside your active land-claim area, hold **E**, and choose **Take**. Pickup takes 15 seconds and returns the same console tier; placing it again rebuilds its nearby chest connections at the new location.

Costs double with each capacity step. Higher tiers consolidate more chests into one interface; they do not grant larger chests, extra loot, extended crafting reach or a scan of distant chunks. Building several cheaper Tier 1 consoles remains a valid alternative, but their inventories are separate views.

All tiers have the same **15-block default range**, configurable between 1 and 30. A fully upgraded console costs 160 steel, 80 mechanical parts, 150 electrical parts and 160 polymers in total, including its original construction. Steel and workbench requirements keep this a mid-game convenience without requiring rare end-game loot.

## Storage Network Loadout Locker

The new tall metal locker is an **interface to the nearest accessible Storage Console**, not another container. It has no usable inventory and is explicitly excluded from console chest scans. Place it within the configured `TerminalRange` of a console; it then uses that console's nearest-chest selection, tier cap, range, ownership, container locks and slot locks.

It saves four per-world profiles containing all 12 equipment/clothing/badge slots, the full toolbelt, and only the backpack slots you have locked. Unlocked backpack loot is preserved exactly. A profile is a template: saving never moves or duplicates items. Applying it reuses suitable items already in any managed player slot, withdraws only the missing items, and deposits only the outgoing surplus.

Each exchange is planned against cloned chest inventories. It succeeds only if the whole requested loadout exists and every outgoing item fits in unlocked connected storage. A missing item, full network, changed chest, changed player inventory, incompatible slot or inaccessible lock rejects the operation before chest counts commit. Existing profile overwrites require a second click within five seconds. Profiles are stored in `loadouts.json`, separated by world and save name, with exact item quality, durability, modifications, ammunition and other metadata plus an item-name check to reject stale IDs safely.

The locker is crafted at a workbench in 45 seconds from **12 forged iron, 6 mechanical parts, 4 electrical parts, 4 springs and 2 duct tape**. This is intentionally cheaper than a Storage Console because the locker cannot function without one and adds no storage capacity. It has no upgrade tiers: upgrading the linked console already increases the network available to the locker from 8 to 64 chests. Inside an active land claim, a fully repaired locker can be picked up after 15 seconds.

## Controls and new supply features

- Left-drag a normal stack from the console. Right-drag takes half (one item for a single-item stack). Shift-click moves a stack using the normal player inventory rules.
- Dragged items stay attached to the cursor while crossing other cells. Full/partial transfers use the amount actually transferred, and unlike-item swaps are all-or-nothing.
- Search by localized or internal name; sort by name, count or type. Sorting and search auto-focus preferences are saved.
- **DEPOSIT ALL** (green button below the chest grid): left-click to deposit eligible backpack supplies into connected chests, **including supplies in locked backpack slots**. Your toolbelt and personal reserves stay untouched. The slot lock settings themselves are not changed; chest access/slot locks still apply.
- **MATCHING ONLY:** left-click to deposit only supplies already present in an unlocked connected chest slot. This mode still skips locked backpack slots. It fills matching stacks before empty slots. Ordinary stackable supplies match even when their hidden creation seeds differ.
- **KEEP:** carry a stack on the cursor and left-click KEEP to reserve that amount of its item type. With the same item held, left-click **CLEAR KEEP** to clear its reserve (right-clicking KEEP also works). The held stack is not consumed. Return it to your backpack afterward.
- Each deposit reports its moved item count in the footer for six seconds and in the game log. If nothing moves, the footer directs you to check space, locks and reserves.
- Reserves are totals across eligible backpack slots, not per-stack limits. DEPOSIT ALL counts both locked and unlocked backpack slots toward the reserve; MATCHING ONLY counts unlocked slots. Neither includes the toolbelt. Manual drag/shift-click transfers and crafting remain intentional actions and do not apply bulk-deposit reserves.
- Refresh manually with the computer icon, or let the console refresh every two seconds while idle. Automatic refresh pauses during a mouse gesture or while carrying a stack.
- Closing the console returns a carried stack using the game's normal inventory handling; if there is no room, vanilla may drop it at your feet.
- Move an item to your backpack before using it. Use actions are disabled on the console's virtual cells so food, drink, medicine, books and bundles always update a real inventory slot.

The console excludes land-claim maintenance storage, all console internal inventories, inaccessible locked containers, and containers in use. It closes if its block disappears, the player moves more than eight blocks away, or a remote player joins.

Nearby crafting still includes accessible player storage, workstation outputs, collectors, vehicles and drones within its own range. Console chest caps do **not** limit nearby crafting.

## Safety and compatibility

**Storage operations are solo/local-world only in this release.** The old client-side multiplayer transfers lacked server-authoritative coordination. On a remote server, or while another client is connected to your hosted game, nearby crafting falls back to vanilla and the console cannot transfer items. Installing the mod on every client does not make it multiplayer-safe.

Terminal operations plan on cloned inventories, validate the source contents and slot locks, then commit only changed slots. Bulk deposit marks each affected chest once. Item metadata is retained rather than merging different variants. This is a synchronous local-world transaction, not a network transaction protocol.

Loadout swaps also refuse to run while an item is held on the cursor or the held tool/weapon is performing an action. Do not remove the mod or downgrade a save while its custom blocks are placed. As with any inventory mod, test the loadout workflow in a disposable world before trusting it with valuable equipment.

The mod checks the expected V3.2 crafting patch sites at startup. A mismatch removes all of this mod's Harmony patches and disables nearby crafting for that session. Review the game log before using it after a game update.

Do not combine with Beyond Storage, ProxiCraft, Craft From Containers or another mod that changes the same crafting/storage behavior. Broader remote repair/refuel and workstation automation are not part of this storage-focused release.

## Installation and updating

1. Close the game and back up saves, generated worlds and the old mod.
2. Start the game with Easy Anti-Cheat disabled.
3. Extract `NearbyCraft-1.4.0-V3.2.zip` into `Mods` so the manifest is `Mods/NearbyCraft/ModInfo.xml`.
4. Preserve your existing `config.json` when updating. Missing settings use defaults.
5. Test the controls and upgrades in a disposable world before using valuable supplies.

The terminal's original block name is retained, so existing consoles do not require replacement. The localization file is now correctly named `Localization.csv`; remove the obsolete `Localization.txt` when replacing an old installation.

## Configuration

Edit `Mods/NearbyCraft/config.json` while the game is closed.

- `Range` and `TerminalRange`: 1–30 blocks; default 15.
- `CacheMilliseconds`: 100–2000 ms; default 250.
- `RespectLockedSlots`: protects chest slot locks when enabled (default true). Independently, MATCHING ONLY protects backpack locks, while DEPOSIT ALL includes their contents.
- `PersonalReserves`: internal item names mapped to quantities, e.g. `{"ammo9mmBulletBall": 150}`. The KEEP control sets these without editing JSON.
- Existing source toggles, sorting and autofocus preferences are preserved.
- Loadout profiles are kept separately in `loadouts.json`; its `.bak` file is the previous saved revision. Keep both files when updating or moving the mod.

UI preference changes use a temporary file and retain the previous configuration as `config.json.bak`. Nearby crafting scans only loaded chunks when needed; ingredient removal reuses a batch snapshot instead of rescanning for every ingredient.

## Automatic backups on this Linux/Proton machine

The companion `tools/backup_saves.py` runs independently of the game mod. The installed user timer checks every five minutes while logged in and backs up changed data **only while the game is closed**. It includes Saves, GeneratedWorlds and Mods, verifies SHA-256 hashes, and keeps the latest eight archives. It does not copy running saves or restore anything automatically.

- Configuration: `~/.config/NearbyCraft/backup.json`
- Backups: `~/.local/share/NearbyCraft/backups/`
- Status: `systemctl --user status nearbycraft-backup.timer`
- Run now: `systemctl --user start nearbycraft-backup.service`
- Disable: `systemctl --user disable --now nearbycraft-backup.timer`

Verify an archive with `python3 tools/backup_saves.py --verify /path/to/archive.zip`. For recovery, close the game, verify the archive, extract it to a separate folder, then copy the required save/world and matching mod files back to the paths recorded in `manifest.json`. Keep the current files separately first. An archive's `data/` folder corresponds to the configured game user-data root.

These are local backups, not protection from drive failure. The timer is configured for the currently used Proton save folder; update it if you switch to native Linux or move the game data.

## Build and verification

```bash
dotnet build -c Release -p:GamePath="/path/to/7 Days To Die"
dotnet run --project tests/TransferTests.csproj -c Release
dotnet run --project tests/PatchSites.csproj -- "/path/to/7 Days To Die"
python3 tools/verify_package.py "/path/to/7 Days To Die"
python3 -m unittest discover -s tests -p 'test_*.py' -v
```

The XML checker uses Python's lxml package. Planner tests compile the production transfer/rules code against minimal item stubs, including 3,000 randomized conservation cases. Patch-site tests inspect the installed game DLL without executing it. Backup tests include restore-to-temp, retention and refusal during gameplay.

**These checks do not replace an in-game UI/playtest.** The mouse drag/drop experience, visual layout and native upgrade animation still need live confirmation. See `TESTING.md` for the short acceptance checklist.

### 1.3.1 deposit hotfix

V3.2 routes left-click through `OnPress` with ID -1 and right-click through `OnRightPress` with ID -2. Version 1.3.0 subscribed only to the left-click event and incorrectly tested for mouse button 1, so its advertised right-click deposit-all action never ran. It also compared hidden item creation seeds when matching ordinary stackable supplies. The labelled left-click buttons and seed-aware supply matching fix those defects; snapshot validation still compares exact values to reject stale transfers. Regression tests cover both mistakes.

### 1.3.2 Deposit All backpack-lock behavior

DEPOSIT ALL now includes locked backpack slots as requested. MATCHING ONLY retains its lock protection. Personal reserves, toolbelt contents, chest access and chest slot-lock handling are unchanged. Regression tests check both modes, reserve accounting and item conservation.

### 1.3.3 terminal use-action fix

Use actions are disabled on the console's virtual item cells. Move food, drink, medicine, books or bundles to the backpack before using them. This prevents vanilla from applying an effect while decrementing only the terminal's read-only display instead of the real chest stack.

### 1.4.0 Storage Network Loadout Locker

Adds four loss-safe equipment, toolbelt and locked-backpack supply profiles through a dedicated tall locker access node. The locker holds no gear and cannot become an extra network chest. Whole swaps share the nearest Storage Console's actual tier-limited chest set and fail without committing when requested items or destination space are unavailable.

Research referenced the MIT-licensed CraftFromContainers and ProxiCraft projects and Apache-2.0-licensed Beyond Storage projects; this implementation is purpose-built.
