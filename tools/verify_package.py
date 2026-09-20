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
print(f'PASS: {checks} XML/localization assertions across {len(merged)} patched vanilla files.')
