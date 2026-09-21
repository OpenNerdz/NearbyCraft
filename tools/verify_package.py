#!/usr/bin/env python3
"""Read-only checks of the mod's XML against an installed V3.2 config tree."""
import copy
import csv
import re
import sys
from pathlib import Path
from lxml import etree

repo = Path(__file__).resolve().parents[1]
game = Path(sys.argv[1]) if len(sys.argv) > 1 else Path.home() / '.local/share/Steam/steamapps/common/7 Days To Die'
merged = {}
checks = 0

def check(condition, message):
    global checks
    assert condition, message
    checks += 1

for patch_path in sorted((repo / 'package/Config').rglob('*.xml')):
    relative = patch_path.relative_to(repo / 'package/Config')
    vanilla_path = game / 'Data/Config' / relative
    root = etree.parse(str(vanilla_path))
    patch = etree.parse(str(patch_path))
    for operation in patch.getroot():
        if not isinstance(operation.tag, str):
            continue
        check(operation.tag == 'append', f'Unsupported patch operation {operation.tag}')
        targets = root.xpath(operation.attrib['xpath'])
        check(bool(targets), f'Zero matches: {relative}: {operation.attrib["xpath"]}')
        for target in targets:
            if isinstance(target, etree._ElementUnicodeResult):
                parent = target.getparent()
                parent.set(target.attrname, str(target) + operation.text.strip())
            else:
                for child in operation:
                    target.append(copy.deepcopy(child))
    merged[str(relative)] = root

blocks = merged['blocks.xml']
items = merged['items.xml']
recipes = merged['recipes.xml']
base = 'nearbyCraftStorageTerminal'
locker = 'nearbyCraftLoadoutLocker'
names = [base] + [base + f'Tier{tier}' for tier in range(2, 5)]
check(len(blocks.xpath(f'/blocks/block[@name="{locker}"]')) == 1, 'Unique loadout locker block')
check(items.xpath(f'/items/item[@name="{locker}"]') == [], 'No loadout locker item/block collision')
check(blocks.xpath(f'/blocks/block[@name="{locker}"]/property[@name="Model"]/@value') == ['@:Entities/LootContainers/locker_ver1_lootPrefab.prefab'], 'Tall locker model selected')
check(blocks.xpath(f'/blocks/block[@name="{locker}"]/property[@name="MultiBlockDim"]/@value') == ['1,2,1'], 'Locker occupies a stable 1x2 footprint')
check(blocks.xpath(f'/blocks/block[@name="{locker}"]/property[@class="CompositeFeatures"]/property[@class="TEFeatureStorage"]/property[@name="LootList"]/@value') == ['nearbyCraftTerminalInternal'], 'Locker has no item-bearing internal inventory')
check(not blocks.xpath(f'/blocks/block[@name="{locker}"]/property[@class="UpgradeBlock"]'), 'Loadout locker has no misleading capacity upgrade')
locker_recipe = recipes.xpath(f'/recipes/recipe[@name="{locker}"]')[0]
check(locker_recipe.get('craft_area') == 'workbench', 'Loadout locker requires a workbench')
check([(i.get('name'), int(i.get('count'))) for i in locker_recipe] == [
    ('resourceForgedIron', 12), ('resourceMechanicalParts', 6), ('resourceElectricParts', 4),
    ('resourceSpring', 4), ('resourceDuctTape', 2)], 'Balanced loadout locker recipe')
for ingredient in locker_recipe:
    check(bool(items.xpath(f'/items/item[@name="{ingredient.get("name")}"]')), f'Locker ingredient exists: {ingredient.get("name")}')
for name in names:
    check(len(blocks.xpath(f'/blocks/block[@name="{name}"]')) == 1, f'Unique block {name}')
    check(items.xpath(f'/items/item[@name="{name}"]') == [], f'No item/block name collision: {name}')
for name in [base, names[-1]]:
    delay = blocks.xpath(f'/blocks/block[@name="{name}"]/property[@class="CompositeFeatures"]/property[@class="TEFeaturePickup"]/property[@name="TakeDelay"]/@value')
    check(delay == ['15'], f'Pickup enabled with 15-second delay: {name}')
for name in names[1:3]:
    check(blocks.xpath(f'/blocks/block[@name="{name}"]/property[@name="Extends"]/@value') == [base], f'{name} inherits pickup feature')
for tier in range(2, 5):
    kit = f'nearbyCraftTerminalUpgrade{tier}'
    check(len(items.xpath(f'/items/item[@name="{kit}"]')) == 1, f'Kit exists: {kit}')
    previous = names[tier - 2]
    upgrade = blocks.xpath(f'/blocks/block[@name="{previous}"]/property[@class="UpgradeBlock"]')[0]
    properties = {p.get('name'): p.get('value') for p in upgrade}
    check(properties['ToBlock'] == names[tier - 1] and properties['Item'] == kit, f'Correct tier chain {tier}')
    check(properties['ItemCount'] == '1', f'Exactly one kit per tier {tier}')
    recipe = recipes.xpath(f'/recipes/recipe[@name="{kit}"]')[0]
    check(recipe.get('craft_area') == 'workbench', f'Workbench required: {kit}')
    for ingredient in recipe:
        check(bool(items.xpath(f'/items/item[@name="{ingredient.get("name")}"]')), f'Ingredient exists: {ingredient.get("name")}')
    expected = [20, 10, 20, 20] if tier == 2 else [40, 20, 40, 40] if tier == 3 else [80, 40, 80, 80]
    check([int(i.get('count')) for i in recipe] == expected, f'Upgrade costs tier {tier}')
    allow_lists = items.xpath('/items/item/property/property[@name="Allowed_upgrade_items"]/@value')
    check(len(allow_lists) >= 3 and all(kit in entry.split(',') for entry in allow_lists), f'Repair tools support {kit}')
top = blocks.xpath(f'/blocks/block[@name="{names[-1]}"]')[0]
check(not top.xpath('property[@class="UpgradeBlock"] | property[@name="Extends"]'), 'Top tier cannot inherit another upgrade')
for filename in ['blocks.xml', 'items.xml']:
    for tint in etree.parse(str(repo / 'package/Config' / filename)).xpath('//property[@name="CustomIconTint"]/@value'):
        check(bool(re.fullmatch('[0-9a-fA-F]{6}', tint)), 'Icon tint must be hex')
for prop in ['Class', 'Model', 'Material', 'Shape', 'MaxDamage']:
    check(top.xpath(f'property[@name="{prop}"]/@value') == blocks.xpath(f'/blocks/block[@name="{base}"]/property[@name="{prop}"]/@value'), f'Top tier retains {prop}')

with (repo / 'package/Config/Localization.csv').open(newline='') as handle:
    rows = list(csv.DictReader(handle))
check(all(None not in row and all(value is not None for value in row.values()) for row in rows), 'Localization CSV column counts')
keys = [row['Key'] for row in rows]
check(len(keys) == len(set(keys)), 'Unique localization keys')
for name in [locker] + names + [f'nearbyCraftTerminalUpgrade{tier}' for tier in range(2, 5)]:
    check(name in keys and name + 'Desc' in keys, f'Localized name/description: {name}')

for relative, root in merged.items():
    if relative.endswith('windows.xml'):
        check(len(root.xpath('/windows/window[@name="nearbyCraftStorageTerminal"]')) == 1, 'Terminal window unique')
        check(len(root.xpath('/windows/window[@name="nearbyCraftLoadoutLocker"]')) == 1, 'Loadout locker window unique')
        for index in range(1, 5):
            check(len(root.xpath(f'//button[@name="nearbyCraftLoadoutSave{index}"]')) == 1, f'Loadout {index} save control')
            check(len(root.xpath(f'//button[@name="nearbyCraftLoadoutApply{index}"]')) == 1, f'Loadout {index} apply control')
        check(len(root.xpath('//button[@name="nearbyCraftTerminalReserve"]')) == 1, 'Reserve control exists')
        for name, caption in [('nearbyCraftTerminalDeposit', 'DEPOSIT ALL'), ('nearbyCraftTerminalDepositMatching', 'MATCHING ONLY'), ('nearbyCraftTerminalReserve', 'KEEP'), ('nearbyCraftTerminalClearReserve', 'CLEAR KEEP')]:
            buttons = root.xpath(f'//button[@name="{name}"]')
            check(len(buttons) == 1 and buttons[0].xpath('label/@text') == [caption], f'Visible labelled control: {caption}')
            check(int(buttons[0].get('width')) >= 100 and int(buttons[0].get('height')) >= 32, f'Usable click target: {caption}')
session_source = (repo / 'src/StorageNetworkSession.cs').read_text()
check('LoadoutLockerManager.IsLocker' in session_source, 'Loadout locker is explicitly excluded from console storage scans')
controller_name = 'nearbyCraftWorkshopController'
controller = blocks.xpath(f'/blocks/block[@name="{controller_name}"]')
check(len(controller) == 1, 'Workshop controller block is unique')
check(not items.xpath(f'/items/item[@name="{controller_name}"]'), 'No workshop item/block ID collision')
check(controller[0].xpath('property[@name="Class"]/@value') == ['CompositeTileEntity'], 'Workshop uses native composite block class')
check(controller[0].xpath('property[@class="CompositeFeatures"]/property[@class="TEFeatureStorage"]/property[@name="LootList"]/@value') == ['nearbyCraftTerminalInternal'], 'Controller inventory is only an access node')
check(controller_name in keys and controller_name + 'Desc' in keys, 'Workshop block is localized')
workshop_recipe = recipes.xpath(f'/recipes/recipe[@name="{controller_name}"]')
check(len(workshop_recipe) == 1 and workshop_recipe[0].get('craft_area') == 'workbench', 'Controller is craftable at a workbench')
for ingredient in workshop_recipe[0]:
    check(bool(items.xpath(f'/items/item[@name="{ingredient.get("name")}"]')), f'Controller ingredient exists: {ingredient.get("name")}')
window = merged['XUi_InGame/windows.xml'].xpath('/windows/window[@name="nearbyCraftWorkshop"]')
check(len(window) == 1, 'Workshop window is unique')
group = merged['XUi_InGame/xui.xml'].xpath('/xui/window_group[@name="nearbycraft_workshop"]')
check(len(group) == 1 and group[0].get('controller') == 'NearbyCraft.XUiC_WorkshopWindowGroup, NearbyCraft', 'Workshop controller is registered')
check(group[0].xpath('window/@name') == ['nearbyCraftWorkshop'], 'Workshop group references its real window')
for name in ['workshopPrevious', 'workshopNext', 'workshopAdd', 'workshopRun', 'workshopLink',
             'workshopStorage', 'workshopOrders', 'workshopMachines', 'workshopSettings', 'workshopFuel', 'workshopHistory', 'workshopQty1',
             'workshopOnce', 'workshopStock', 'workshopClearDone', 'workshopQty10', 'workshopQty100', 'workshopQty1000',
             'workshopAutoCraft', 'workshopPagePrevious', 'workshopPageNext'] + [f'workshop{action}{i}' for i in range(6) for action in ['Result', 'Toggle', 'Remove']]:
    check(len(window[0].xpath(f'.//button[@name="{name}"]')) == 1, f'Workshop button exists: {name}')
for name in ['workshopSearch', 'workshopAmount']:
    check(len(window[0].xpath(f'.//textfield[@name="{name}"]')) == 1, f'Workshop input exists: {name}')
for i in range(6):
    for binding in ['name', 'goal', 'detail', 'toggle']:
        check(bool(window[0].xpath(f'.//label[@text="{{workshop_{binding}{i}}}"]')), f'Workshop row binding: {binding}{i}')
check('WorkshopManager.IsController' in session_source, 'Workshop access node excluded from chest network')

# XUiView.pos is Vector2i, not a floating point vector. This catches the exact
# fractional-label regression that only appeared when loading the in-world UI.
for patch_path in sorted((repo / 'package/Config').rglob('*.xml')):
    patch = etree.parse(str(patch_path))
    for element in patch.xpath('//*[@pos]'):
        position = element.get('pos')
        check(bool(re.fullmatch(r'-?\d+,-?\d+', position)),
              f'XUi position must be two integers: {patch_path.name}: {element.get("name", element.tag)} = {position}')

terminal = merged['XUi_InGame/windows.xml'].xpath('/windows/window[@name="nearbyCraftStorageTerminal"]')[0]
check(len(terminal.xpath('.//button[@name="nearbyCraftTerminalProduction"]')) == 1, 'Storage console exposes production from the same block')
check(not window[0].xpath('.//rect[@name="productionSearch"]/@visible'), 'Recipe picker remains available beside all views')
check(window[0].xpath('.//rect[@name="workshopOptions"]/@visible') == ['{workshop_settings_visible}'], 'Advanced controls are isolated in Options')
check(not window[0].xpath('.//button[starts-with(@name,"workshopLess") or starts-with(@name,"workshopMore")]'), 'No ambiguous per-row quantity controls')
for names_to_check in [
    ['workshopOrders', 'workshopMachines', 'workshopHistory', 'workshopSettings'],
    ['workshopAmount', 'workshopQty1', 'workshopQty10', 'workshopQty100', 'workshopQty1000']
]:
    controls = [window[0].xpath(f'.//*[@name="{name}"]')[0] for name in names_to_check]
    previous_right = -1
    for control in controls:
        left, top = map(int, control.get('pos').split(','))
        right = left + int(control.get('width'))
        check(left > previous_right and right <= 1024, f'No overlapping tab/quantity controls: {control.get("name")}')
        previous_right = right
check(window[0].xpath('.//button[@name="workshopAdd"]/@enabled') == ['{workshop_add_ready}'], 'Invalid or locked requests disable Craft')
check(window[0].xpath('.//button[@name="workshopClearDone"]/@pos') == ['844,-600'], 'Archive control is outside job rows')
check(len(window[0].xpath('./rect/headerbg')) == 2, 'Production uses native game panel headers')
check(window[0].xpath('./rect[@name="productionHeader"]/@width') == ['366'], 'Request flow has a distinct crafting-list pane')
check(window[0].xpath('./rect[@name="workshopHeader"]/@width') == ['666'], 'Status views have a distinct workstation pane')
check(window[0].xpath('.//label[@text="1  FIND AN ITEM"]') and window[0].xpath('.//label[@text="2  SET REQUEST"]'),
      'Request flow is visibly ordered')
check(window[0].xpath('.//button[@name="workshopAdd"]/@defaultcolor') == ['45,85,45,255'], 'Primary production action uses native green')
check(not window[0].xpath('.//*[@name="workshopBackdrop"]'), 'Legacy dashboard backdrop was removed')
for name, binding in [('workshopPrevious', 'workshop_previous_ready'), ('workshopNext', 'workshop_next_ready'),
                      ('workshopPagePrevious', 'workshop_page_previous_ready'), ('workshopPageNext', 'workshop_page_next_ready')]:
    check(window[0].xpath(f'.//button[@name="{name}"]/@enabled') == ['{' + binding + '}'], 'End-of-list arrows are disabled')
check(window[0].xpath('.//rect[@name="machineOverview"]/@visible') == ['{workshop_machines_visible}'], 'Machine legend appears on machine page')
for index in range(6):
    for action in ['Remove']:
        check(window[0].xpath(f'.//button[@name="workshop{action}{index}"]/@visible') == ['{workshop_orders_visible}'], 'Order-only controls cannot obscure machine actions')
    check(window[0].xpath(f'.//rect[@name="workshopRow{index}"]/@visible') == [f'{{workshop_row_visible{index}}}'], 'Unused job slots are hidden')
    check(window[0].xpath(f'.//button[@name="workshopResult{index}"]/@visible') == [f'{{workshop_result_visible{index}}}'], 'Empty recipe results are hidden')
    check(bool(window[0].xpath(f'.//label[@text="{{workshop_phase{index}}}"]')), 'Every job has a clear state badge')
for name in ['workbench', 'cementMixer', 'forge', 'campfire', 'chemistryStation', 'cntDewCollector', 'cntApiary', 'cntChickenCoop']:
    check(len(blocks.xpath(f'/blocks/block[@name="{name}"]')) == 1, f'Supported machine exists: {name}')
for name in ['resourceWood', 'resourceScrapIron', 'resourceScrapBrass', 'resourceScrapLead', 'resourceCrushedSand', 'resourceRockSmall', 'resourceClayLump',
             'drinkJarEmpty', 'resourceChickenFeed', 'resourceCropChrysanthemumPlant']:
    check(len(items.xpath(f'/items/item[@name="{name}"]')) == 1, f'Automatic machine input exists: {name}')
manifest = etree.parse(str(repo / 'package/ModInfo.xml'))
check(manifest.xpath('/xml/Version/@value') == ['1.8.0'], 'Release manifest matches new production version')
check('and \'$(GameplayQA)\' != \'true\'' in (repo / 'NearbyCraft.csproj').read_text(), 'Gameplay QA builds cannot package a release')
for name, caption in [('nearbyCraftTerminalSort', 'NAME A-Z'), ('nearbyCraftTerminalSortCount', 'MOST ITEMS'), ('nearbyCraftTerminalSortType', 'ITEM TYPE')]:
    button = terminal.xpath(f'.//button[@name="{name}"]')
    check(len(button) == 1 and button[0].xpath('label/@text') == [caption], f'One-click visible quick sort: {caption}')
    check(int(button[0].get('width')) >= 150 and int(button[0].get('height')) >= 32, f'Usable quick-sort hit target: {caption}')
check(terminal.xpath('.//label[@name="resultCount"]/@text') == ['{terminal_result_count} ENTRIES'], 'Header counts grouped entries, not physical stacks')
check('catalog.Add(stack)' in session_source and 'entry.TransferStack()' in session_source, 'Session uses the tested grouped catalog and capped transfers')
check('totals[i] = entry.Count' in session_source, 'Grid receives the full aggregate separately from its physical transfer stack')
print(f'PASS: {checks} XML/localization assertions across {len(merged)} patched vanilla files.')
