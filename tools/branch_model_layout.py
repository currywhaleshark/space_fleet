"""Apply authored geometry after tier statistics, without changing weapon/HP values."""
import copy
import json


def apply_layout(root, definition):
    path = root / 'data/model_layouts' / (definition['id'] + '.json')
    if not path.exists():
        return definition
    layout = json.loads(path.read_text(encoding='utf-8'))
    result = copy.deepcopy(definition)
    result['modelId'] = definition['id']
    # Section armor is copied from the corresponding base section, not rebalanced here.
    original = {s['id']: s for s in definition['hullSections']}
    result['hullSections'] = []
    for section in layout['sections']:
        source = original[section['armorFrom']]
        result['hullSections'].append({
            k: copy.deepcopy(v) for k, v in source.items() if k not in ('id', 'name', 'center', 'halfSize')})
        result['hullSections'][-1].update({k: v for k, v in section.items() if k != 'armorFrom'})
    for module in result['modules']:
        module.update(layout['modules'].get(module['id'], {}))
    for bay in result['bays']:
        bay.update(layout['bays'].get(bay['id'], {}))
    if 'gunMuzzle' in layout: result['railgun']['muzzle'] = layout['gunMuzzle']
    if 'missileLaunch' in layout: result['missiles']['launchPoint'] = layout['missileLaunch']
    if result.get('antimatter') and 'amLaunch' in layout: result['antimatter']['flight']['launchPoint'] = layout['amLaunch']
    if 'pdMounts' in layout: result['pointDefense']['mounts'] = layout['pdMounts']
    if result.get('reconProbes') and 'probeLaunchPoints' in layout:
        result['reconProbes']['launchPoints'] = layout['probeLaunchPoints']
    return result


def without_authored_layout(root, definition, source_id):
    """A higher tier inherits branch statistics, not another hull's authored geometry."""
    path = root / 'data/model_layouts' / (source_id + '.json')
    if not path.exists(): return definition
    original = json.loads(path.read_text(encoding='utf-8')).get('inheritedGeometry')
    if original is None: return definition
    result = copy.deepcopy(definition)
    result['modelId'] = original['modelId']
    current_sections = {s['id']: s for s in result['hullSections']}
    result['hullSections'] = copy.deepcopy(original['hullSections'])
    for section in result['hullSections']:
        # Restore shape only: a later branch armor adjustment must still reach higher tiers.
        current = current_sections[section['id']]
        for field in ('armor', 'faces'):
            if field in current: section[field] = copy.deepcopy(current[field])
            else: section.pop(field, None)
    for module in result['modules']: module.update(original['modules'][module['id']])
    for bay in result['bays']: bay.update(original['bays'][bay['id']])
    result['pointDefense']['mounts'] = copy.deepcopy(original['pdMounts'])
    return result


def merge(base, patch):
    result = copy.deepcopy(base)
    for key, value in patch.items():
        if value is None:
            result.pop(key, None)
        elif isinstance(value, dict) and isinstance(result.get(key), dict):
            result[key] = merge(result[key], value)
        else:
            result[key] = copy.deepcopy(value)
    return result


def resolve(root, key):
    source = root / 'data/ships' / (key + '.json')
    if source.exists():
        return json.loads(source.read_text(encoding='utf-8-sig'))
    variant = json.loads((root / 'data/variants' / (key + '.json')).read_text(encoding='utf-8'))
    return merge(resolve(root, variant['baseHullId']), variant['patch'])
