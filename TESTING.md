# NearbyCraft 1.8.0 acceptance checks

## 1.8.0 current acceptance status

Verified on 21 September 2026 against the installed V3.2 b10 game under Proton, after the user
closed their game. A fresh checksum-verified backup contains 1,072 files covering normal saves,
generated worlds, installed mods and persistent mod settings. Native checks ran only in the
separate `qa-userdata` root; normal worlds were not opened as test fixtures.

- Scheduler/forge planning: **4,977 assertions**, including 400 varied input layouts, two/three-lane
  balancing, mixed-material slot reservation, preservation of existing timers, upgraded-machine
  preference, proportional faster-machine allocation, faster available recipes, busy/full/unfueled
  machine avoidance, exact whole-yield output and resource conservation.
- Real collection receipts, no history before delivery, cancelled queues, repeat-safe completion,
  save/reload, and existing randomized scheduler cases are included in that suite.
- Machine transactions: **37 assertions**, including partial delivery callbacks and full-storage refusal.
- Persistence: **200 assertions**, including legacy migration, partial/capped delivery credit, no
  duplicate history, 60-entry retention, corrupt data refusal, failed-removal rollback and actual filesystem write failure.
- Native API metadata includes forge timers and the EffectManager tool-cache fields: **55 checks**.
- XML/localization/layout: **535 checks**; existing transfer/rule suite: **536,904 assertions**;
  backup tooling: **2 tests**.
- Normal and opt-in QA builds compile with zero warnings/errors against installed V3.2 b10.

Native run A (`gameplay-qa-180-a.log`, disposable world `NC_QA_180_20260921_A`) passed **102 checks**:

- A quality-six anvil changes the actual station-prepared recipe timing; the vanilla effect-tool
  cache is restored after each station-specific evaluation.
- Real forges receive split raw inputs and prepare cement concurrently with a mixer making sand.
  The one-click request returns exactly 24 concrete to connected storage and consumes real fuel.
- Completed history records 24 collected products; the finished request leaves active Jobs.
  Completed displays it and Repeat fills the quantity without silently creating another order.
- Changing the same product to KEEP STOCKED revives it without deleting history and replenishes
  withdrawn products. One-time requests do not replenish withdrawals.
- Native world save/unload/reload with smelting assignments pending completes the exact extra
  requested cement, with no duplicate assignments. Console upgrades/settings reload retain state.
- Native button handlers, quantity shortcuts, tab visibility and displayed label geometry pass.

Final run B (`gameplay-qa-180-b.log`, disposable world `NC_QA_180_20260921_B`) passed **103 checks**,
including an explicit completion-history check after native world/settings reload. Jobs, Machines,
Options and Completed screenshots were inspected at 1600x1000. Run A's Completed screenshot was
captured after the harness closed the window; a one-second capture wait in the test-only fixture
produced the correct run-B image. This is not a production rendering change. Images are under
`qa-userdata/production-180-{orders,machines,options,completed}.png`; none are packaged with the mod.
No NearbyCraft exceptions, missing workshop controls, or XML load errors occurred. The log still
contains the unrelated Discord registry warning and Xbox Live shutdown error seen in prior runs.

Post-fix run D (`gameplay-qa-180-d.log`, disposable world `NC_QA_180_20260921_D`) passed **111 checks**.
It adds native coverage for aggregated duplicate crafting requirements, exact player-plus-storage
payment, staged bulk deposit, loadout rollback after a chest changes during player apply, and the
backpack-or-toolbelt shift-click rule. The complete production, UI and save/reload fixture also passed.

Native UI run F (`gameplay-qa-180-f.log`, disposable world `NC_QA_180_20260921_F`) passed the same
**111 checks** after the production-window restyle. Jobs, Machines, Options and Completed were
inspected at 1600x1000 with the native two-panel layout. Every request caption had visible rendered
geometry, every tab and quantity control worked, no workshop control was missing, and no NearbyCraft
or XML error occurred.

The release contains neither the fixture nor its startup hook. Native tests accelerate machine
timers only inside the guarded disposable world. Same-process world reload is not a power-loss
test or a full process-restart recovery guarantee. No font/shader or graphics workaround is shipped.

## Live production-UI iteration

Production-window XML can be checked in the real game without restarting it for every layout edit.
The helper validates the merged package first, keeps one restorable copy of the installed window
XML under ignored `dist/live-ui-backup`, and atomically syncs only the XUi window file:

```bash
python3 tools/live_ui.py install
```

With the disposable test world still running, open the F1 console and enter:

```text
xui reload nearbycraft_workshop
```

Reopen Production if needed. The game itself reparses and reconstructs the window group, so fonts,
sprites, bindings, hit areas and native scaling still use the real XUi implementation. Repeat the
sync and reload after each XML edit. `python3 tools/live_ui.py check` runs the same package checks
without installing, and `python3 tools/live_ui.py restore` restores the pre-session XML.

This loop covers XML layout, styling and existing bindings. A clean game launch remains required
after C# DLL or Harmony changes and for the final startup/load test. Before release, run the full
fresh disposable-world fixture once; it remains the accuracy gate for startup patching, native
transactions, world save/unload/reload and persisted settings.

Additional 1.8.0 manual checks: use both two-input modded forges and native three-input forges;
compare long-running throughput with different bellows/anvil/cooking-tool qualities; check history
and pagination at alternate UI scales; repeat, remove and pause requests using physical mouse
input. Estimates are bounded heuristics, not a claim of globally optimal scheduling. Native/mod
saves are still separate writes and are not crash-atomic.

## 1.7.0 historical acceptance status

Verified on 21 September 2026 against the installed V3.2 b10 game under Proton, after the user
closed their normal game. A fresh checksum-verified backup contains normal saves, generated worlds
and all installed mods. Native tests use only the separate `qa-userdata` root and test worlds;
the user's normal saves and persistent NearbyCraft settings are not test fixtures.

Standalone checks for 1.7.0:

- Production scheduler + transactions + actual JSON store with native API stubs: **2,709 assertions**.
  Includes 150 varied multi-forge examples, exact whole-yield output totals, material conservation,
  no duplicate reservations, farther preloaded forge preference, matching pending input preference,
  balanced assignment, competing-order fairness, missing-clay refusal, ordinary settings reload,
  removed-machine recovery, externally satisfied stock targets and save-failure handling.
  A single busy mixer can request cement from multiple forges while it makes sand.
- Pure-rule/transfer tests: **536,898 assertions**.
- Machine/collector transaction tests: **34 assertions**.
- Settings/persistence tests: **182 assertions**, including assignment migration and invalid-data rejection.
- Merged XML/localization: **493 assertions**; native patch/API metadata: **50 checks**.
- Normal and opt-in QA builds compile against V3.2 b10. Only normal Release builds package a ZIP.

Run the new standalone integration suite with:

```bash
DOTNET_ROLL_FORWARD=Major dotnet run --project tests/SchedulerTests.csproj -c Release
```

Native 1.7.0 acceptance:

- Three real forges prepare cement concurrently while the mixer makes sand. Starting from stone
  and wood, a one-click request produces exactly 24 concrete and returns it to connected storage.
- Actual native smelting inputs, forge/mixer queues, raw-material consumption and fuel are observed.
  Completed one-time requests do not replenish withdrawn items; stock targets do.
- All five workstation and three collector types are discovered. Dew collection supplies jars and
  exports water. Console upgrades retain orders, and settings reload retains assignments/pause state.
- The fixture saves and unloads its world with native smelting and matching assignments pending,
  reloads both world and settings, resumes automation, then verifies exactly the requested extra
  cement with no duplicate assigned production. This is a normal world reload in the same process,
  not a power-loss test or a full process-restart recovery guarantee.
- Jobs/Machines/Options screenshots are inspected at 1600x1000; tests exercise native control
  handlers and verify rendered page text, panel visibility and hidden unused rows.

Evidence: `gameplay-qa-170-f.log` and `gameplay-qa-170-h.log` each finish with **88 assertions**.
The final `gameplay-qa-170-i.log` finishes with **89 assertions**, including one temporary
rendering-diagnostic preference-restoration check. That diagnostic has been removed from the
fixture. The user confirmed the loaded UI looks good. Some captured frames intermittently omit
glyphs/icons despite populated native label geometry; the cause was not established. No graphics,
font or shader workaround is shipped, and persistent graphics settings were left unchanged.

The opt-in fixture accelerates only its native machine timers and disables structural collapse
only in its guarded disposable world. Its isolated settings path is selected before world creation.
The normal Release DLL contains neither the fixture nor its startup hook; QA builds cannot package
a release ZIP. Logs/screenshots and disposable worlds are not release-package contents.

Additional manual checks:

1. Search by display/internal name, choose results on different pages, clear a search and try no
   matches. Verify item icons, selection highlight, readable long names and selected-recipe tooltip.
2. Request exact quantities and use all three shortcuts. CRAFT must start automatically; KEEP STOCKED
   must refill to its target. Switching mode replaces the unfinished plan but leaves native work alone.
3. Check zero/one/six/seven/24 job layouts. Unused rows stay hidden. Clear finished jobs must preserve
   incomplete, native-pending and persistent stock jobs. Test failed settings writes during a request.
4. Read Jobs, Machines and Options at normal UI scale and alternate resolutions. Verify useful state
   badges, no overlapping labels, no hidden controls intercepting clicks, and a persistent recipe picker.
5. Use several forges with different preloaded materials and differing tools, plus multiple orders.
   Verify preference for usable preloaded materials, fair first opportunities, and no duplicate feed.
6. Remove/pause/exclude a forge or order while smelting; keep native contents intact. Restore an
   excluded forge, add missing raw resources/wood, and verify progress resumes without re-requesting.
7. Fully save/quit/relaunch while assignments are pending. Confirm the native world and matching
   workshops.json recover together. Separate writes still do not provide crash-atomic recovery.

## 1.6.0 live baseline, verified on 21 September 2026


- Release build against the installed V3.2 b10 assemblies: zero warnings/errors.
- Pure-rule/transfer suite: **536,894 assertions**.
- Production machine/forge/collector transactions against API stubs: **34 assertions**.
- Workshop settings, migration and failure handling: **173 assertions**.
- Native patch/API metadata: **50 checks**; XML/package verification: **434 checks**.
- Save-backup tests: **2 tests**.
- Actual V3.2 b10 game under Proton: **39 assertions** in an isolated Navezgane save.
  The native console opened production; all five workstation types and three collector types
  were discovered. A MAKE ONCE order produced exactly ten concrete from stone and wood via
  native mixer queues, forge input/smelting and cement crafting, then returned output to storage.
  Tests also confirmed wood/input consumption, dew-collector jars/output, no one-time restocking,
  KEEP STOCK replenishment, console-upgrade preservation and settings-file reload.
- Orders and Machines views were inspected at 1600x1000. Tests checked the actual rendered
  machine-page label and panel visibility, catching and fixing a stale UI-binding refresh bug.

The opt-in `tests/GameplayQA.cs` fixture is compiled only with `-p:GameplayQA=true`, needs the
`-NearbyCraftGameplayQA` launch flag, and refuses to run outside its disposable user-data save.
It accelerates native machine tick methods and disables structural collapse only in that fixture.
The normal release excludes the fixture and its startup hook; QA builds cannot create release ZIPs.
Final run evidence is `gameplay-qa-e.log` and `qa-userdata/orders-ui.png` / `machines-ui.png` in
the source workspace. Those files and the disposable worlds are not release-package contents.

These checks do **not** establish multiplayer support, full mouse/drag regression coverage,
all display scales, long-running large-base performance, or every third-party machine integration.
The 1.6.0 baseline tested settings reload only; 1.7.0 adds the native world save/unload/reload check
described above. Crash-atomic coordination between `workshops.json` and native saves is not promised.
The cases below remain a broader manual regression checklist, not a claim that every case passed.

## Earlier releases

1.5.4 adds an allocation-free export preflight and a container-discovery-only scan mode. The
pure-rule suite passes **532,876 assertions**, including 10,000 randomized preflight comparisons
and **zero allocated managed bytes across 10,000 warmed reserved-output checks**. This is a
targeted algorithm test, not a measurement of the whole Unity frame or a promised FPS increase.
The normal console/catalog and workshop tests remain enabled. See DeepBore/TESTING.md for native
bridge, open-inventory and rendering regression results.

1.5.3 raises the output bridge's miner-to-console distance limit to 200 blocks. The public range
capability is checked by DeepBore before binding, and package checks enforce matching 200-block
limits. Console scan range, capacity and access checks are unchanged.

The 1.5.2 bridge adds 10,000 randomized ore-output transfer scenarios to the existing suite.
Total: 522,870 assertions, including partial output, type filtering, source/destination locks,
metadata preservation and conservation. Run the net8 test project on this net10-only host with
`DOTNET_ROLL_FORWARD=Major dotnet run --project tests/TransferTests.csproj -c Release`.

For the optional DeepBore bridge, additionally test two-step Link selection, box/console ownership,
busy/full/unloaded destinations, source exclusion, hopper fuel retention and links across upgrades.
See DeepBore/TESTING.md for the isolated in-engine integration run.

The standalone suites cover the production transaction planner, tier/reserve rules, XML patch targets, recipe references, upgrade tool allow-lists, localization, patch-site metadata and closed-game backups. They do not launch Unity or simulate mouse input; the separate native game fixtures are described above.

Workshop planner checks additionally cover pending-output accounting, bounded batches, multi-output target limits, missing ingredients, protected slots, hypothetical capacity reservations that must never mint products, partial collection, metadata retention and 2,000 randomized target/collection scenarios. Broader station progression, physical UI interaction and full process-restart recovery remain on the manual checklist below.

Use a disposable local world with EAC disabled. Do not use your main save as the first test.

1. Open a Tier 1 console near nine chests. Confirm 8/8 connected and one out of capacity. Move/remove a connected chest; the next nearest should connect on idle refresh. Contents of the excluded chest must remain accessible directly.
2. Drag a stack across several terminal cells to the backpack. Only the initially picked stack should move. Repeat with a half-stack, a single non-stackable item and right-click placement. Close with an item held; confirm the item returns, or drops normally if inventory is full.
3. With a full network, try depositing and swapping. Confirm actual counts on both sides, no displayed phantom items, and no change for an impossible swap. Test backpack/toolbelt shift-click, restricted items and a locked chest slot.
4. Confirm the labelled DEPOSIT ALL and MATCHING ONLY buttons appear beneath the chest grid. Left-click DEPOSIT ALL: eligible backpack supplies, including locked backpack slots, should enter the chests and a moved count should appear in the footer and log. The backpack lock toggles themselves must remain set. Put one supply type in a chest, then collect another stack of that supply separately (different creation seed). MATCHING ONLY should recognize it and leave unrelated supplies and locked backpack slots behind. Both buttons must preserve the toolbelt and saved reserves; locked destination chest slots must remain protected. Repeat with full chests and check the zero-moved feedback.
5. Hold 150 ammunition and click KEEP. Put it back. With 250 in unlocked backpack slots, bulk deposit should leave 150. Hold ammunition and left-click CLEAR KEEP to clear the reserve, then test again. Also test the right-click KEEP shortcut. Reopen the game to confirm the preference persists.
6. Carry one appropriate kit and use a repair tool on the fully repaired console. Test each step: 8 → 16 → 32 → 64. Exactly one kit should be consumed per step, ownership/lock behavior retained, and no further upgrade offered at Tier 4. Verify a damaged console repairs before upgrading. At each tier, place the console inside an active land claim, hold E, choose Take, and confirm the 15-second pickup returns the same tier. Re-place it and confirm its nearby chest connections rebuild. Confirm Take is unavailable outside the active land claim and while the console is damaged.
7. Place a land-claim block nearby. Its maintenance supplies must not enter the console network or nearby-crafting counts. Existing supplies are not deleted by exclusion.
8. Craft several ingredient types from player inventory plus nearby storage. Confirm the recipe queue and actual ingredient consumption. Toggle nearby crafting off and repeat using vanilla inventory only.
9. Select food, drink, medicine, a readable item or a bundle in the console. Its Use action must be disabled and clicking it must direct you to move the item to the backpack. Move it first, then use it normally and confirm the real stack decreases once.
10. Join a remote game, or have a second client join a host. Confirm remote storage actions are blocked and ordinary crafting still works. Do not use multiplayer until this check is confirmed.
11. Inspect the new game log for NearbyCraft patch failures, XML errors, color-parse warnings and exceptions. Close the game and verify a backup archive (the timer is optional and is not installed by the mod).
12. Craft a Storage Network Loadout Locker at a workbench and confirm the recipe consumes 12 forged iron, 6 mechanical parts, 4 electrical parts, 4 springs and 2 duct tape. Place it within 15 blocks of an accessible console. Confirm it opens the four-profile loadout UI but never appears as a connected chest and never exposes its empty internal activation slot.
13. Equip armor/clothing/badges and fill the toolbelt. Lock several backpack slots and put ammunition, food and tools in them; leave unrelated loot in unlocked slots. Save Loadout 1. Change every managed area, then equip Loadout 1. Confirm the exact equipment, toolbelt order, locked-slot layout, stack counts, durability, modifications and loaded ammunition return; outgoing gear should be in the console's connected chests and unlocked backpack loot must be unchanged.
14. Test a pure toolbelt reorder while all relevant items are already on the player. It should succeed without needing duplicate items in storage. Then remove one required item from the network and retry: the entire player and chest state must remain unchanged and the missing item must be reported. Repeat with every connected unlocked chest slot full; outgoing items must not disappear and the swap must be rejected.
15. Lock a required chest slot, lock the console from the player, move the nearest console outside link range, exceed the console's tier chest cap, and place a second farther console. Confirm the locker uses the nearest accessible in-range console and only the exact chests that console exposes. Upgrade that console and confirm the locker gains the higher chest cap without any locker upgrade or replacement.
16. Attempt to save over an existing profile. Confirm the first click asks for an overwrite and expires after five seconds. Save again, restart the game and confirm the correct world/save profiles persist. Hold an item on the cursor and start a held-item action; both should block exchanges. Pick up a fully repaired locker inside a land claim and confirm the 15-second recovery returns the block.

## Production acceptance checks (1.6.0; disposable solo world)

17. Place only a console, connected chest and workstations. Press E, then PRODUCTION. Verify self-link,
    initially paused/empty orders, storage/orders/machines navigation, active tab colors and readable
    controls. Repeat from an existing separate Workshop Controller and its linked console.
18. Queue a MAKE ONCE order with existing product stock present. The entered amount must be *new*
    production. Add more of the same item to increase remaining quantity. Test whole-yield rounding,
    invalid amounts, all 24 entries, six-row pagination, removal and individual pause/resume.
19. Use KEEP STOCK and verify stored + native queued + finished output prevent duplicate work.
    Take some stock and verify replenishment. PAUSE/removal must leave paid native queues intact.
    Cancelling a submitted MAKE ONCE batch in the workstation must not reissue it automatically.
20. With only stone and wood in storage, order concrete. Verify sand is made in the mixer, stone is
    inserted into the forge, native smelting produces units, cement is queued using those units,
    and concrete returns through the mixer to storage. Turn CRAFT INPUTS off and verify missing
    intermediates are reported rather than created. Test dependency cycles in a modded recipe set.
21. Test wood fueling, ignition, continued refill of long native queues, fuel-buffer bounds and idle
    shutdown. Turn AUTO FUEL off and verify manual fueling/lighting is required. Verify installed
    tools and recipe unlocks still matter. Try a submerged station. Valuable combustible items must
    never be selected as automatic fuel.
22. Test iron + clay forge recipes and required crucible/anvil tools. Existing smelted units and
    raw material already smelting must prevent duplicate feed. Preserve typed zero-count unit slots.
    Repeat with the game's smelter-disabled recipe variant. Quality equipment remains unsupported.
23. Verify dew collectors receive jars, apiaries receive accepted flowers, coops receive feed, and
    output returns to storage. Install bees/chickens/upgrades manually; test sky/water obstruction,
    missing catalysts, full output/storage, an in-progress slot and AUTO FUEL off.
24. Exclude a machine through MACHINES and confirm no new input/fuel/collection touches it while
    its existing native work continues. Repeat while another device/chest is open, while storage or
    the loadout screen is open, while holding a cursor stack, when dead and when a remote client joins.
25. Test full/partially-full storage and machine outputs, source slot locks, changed/removed machines,
    duplicate controllers and overlapping networks. No rejected transaction may consume inputs or fuel.
    Upgrade the console in place and verify orders survive. Pickup/destruction should forget the manager.
    Save/restart, check world isolation and corrupt-file preservation. Test a failed progress write:
    automation must stop without rolling back the already committed native batch.
26. Normal game saves and workshops.json are separate. Cancelled batches, power loss and restoring
    only a world or only workshop settings need explicit review of one-time remaining quantities.
    Test a matching full backup restore; this release does not promise crash-atomic order bookkeeping.

The pre-update archive and old installed-mod directory should remain available until these checks pass. Do not downgrade a world containing custom consoles, lockers, workshop controllers or upgrade kits without first restoring its matching pre-update save.

## Grouped console / quick-sort checks (1.5.1)

26. Fully restart, enter a disposable solo world and open the Workshop Controller. Confirm all six rows' minus/plus/on-off/remove labels are positioned correctly and the game log has no Vector2i/decimal-position errors. Check at the normal UI scale, not only the main menu.
27. Split the same standard ammunition over several stacks in multiple connected chests, including ammo gathered at different times. The console should show one entry with the exact combined total. Different ammunition types, meaningful metadata/paint variants and individual equipment must retain separate entries. Lock one contributing slot and verify its quantity disappears from the displayed total when RespectLockedSlots is enabled.
28. Click NAME A-Z, MOST ITEMS and ITEM TYPE. Verify each mode, active-button highlight, first-row reset and persistence after closing/reopening. Quantity order must reflect the combined stock, not normal stack limits. Search by item name and internal name in each mode. Sorting and searching must not rearrange or change the physical chest slots. Repeat while holding an item: quick sorting should wait until the held item is put down.
29. From a grouped total larger than a native stack, left-drag, right-drag, Shift-click, deposit one and swap unlike items. Cursor/backpack stacks must never exceed the normal limit. Each successful transfer must change the network total by exactly the transferred amount. Check a single-item remainder, full backpack, full chests, locked slots and metadata variants. Repeat after scrolling/searching/sorting, and check that depleted entries disappear and no stale totals remain.
30. Check large count labels fit on one line; hover shows the exact total. Quality/durability badges must retain their vanilla display. Test an empty network and many distinct entries requiring scrolling. Close the console while holding an item and verify normal item return/drop handling.
