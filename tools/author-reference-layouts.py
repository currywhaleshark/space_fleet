"""Reference-matched geometry contracts for the remaining hulls (art/reference/2026101*).
python tools/author-reference-layouts.py [ID ...]   -> data/model_layouts/<id>.json, then apply-model-layouts.py
Pieces are drawn by build-reference-models.py; pieces with "section" become hit sections (their bounding box).
Coordinates: +X starboard, +Y up, -Z forward, metres. Mirrored pieces are authored on the starboard (+X) side.
"""
import json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EARTH_SURFACE = dict(accent=[0.1, 0.14, 0.25], accentRatio=0.3, accentScale=5, wear=0.25, streaks=0.35)
MARS_SURFACE = dict(accent=[0.36, 0.09, 0.05], accentRatio=0.24, accentScale=4, wear=0.3, streaks=0.4)
SMALL_EARTH_SURFACE = dict(accent=[0.1, 0.14, 0.25], accentRatio=0.28, accentScale=4, wear=0.2, streaks=0.2)
SMALL_MARS_SURFACE = dict(accent=[0.36, 0.09, 0.05], accentRatio=0.26, accentScale=4, wear=0.2, streaks=0.2)
LAYOUTS = {}


def layout(key):
    def register(fn): LAYOUTS[key] = fn; return fn
    return register


def loft(name, rings, section=None, armor=None, **kw):
    p = dict(kind='loft', name=name, rings=rings, **kw)
    if section: p['section'] = section; p['armorFrom'] = armor or section
    return p


def box(name, center, size, section=None, armor=None, **kw):
    p = dict(kind='box', name=name, center=center, size=size, **kw)
    if section: p['section'] = section; p['armorFrom'] = armor or section
    return p


def prism(name, points, y=0, thickness=.5, section=None, armor=None, **kw):
    p = dict(kind='prism', name=name, points=points, y=y, thickness=thickness, **kw)
    if section: p['section'] = section; p['armorFrom'] = armor or section
    return p


def fin(name, root, tip, section=None, armor='fins', thickness=.24, **kw):
    p = dict(kind='fin', name=name, root=root, tip=tip, thickness=thickness, **kw)
    if section: p['section'] = section; p['armorFrom'] = armor
    return p


def truss(name, center, size, rod, section=None, armor='hull', **kw):
    p = dict(kind='truss', name=name, center=center, size=size, rod=rod, **kw)
    if section: p['section'] = section; p['armorFrom'] = armor
    return p


# ---------------------------------------------------------------- T5 cruisers / battleships
@layout('earth_air_defense_cruiser_t5')
def nimbus():
    """Narrow central keel, broad swept defence decks fore and aft with six PD, low bridge, thin aft radiators."""
    return dict(
        modules={'sensor': {'center': [0, 26, 52]},
                 'radiator-port': {'center': [-56, 0, 116], 'halfSize': [20, 0.9, 18]},
                 'radiator-starboard': {'center': [56, 0, 116], 'halfSize': [20, 0.9, 18]}},
        pdMounts=[[48, 8, 65], [-48, 8, 65], [0, -21, 45], [0, 23, -38], [48, 8, -65], [-48, 8, -65]],
        missileLaunch=[-18, 23, -104],
        blockout=dict(role='defense', reference='art/reference/20261017_t5_fleet/cruisers.png', palette='earth', surface=EARTH_SURFACE,
            sensorHead=False,
            pieces=[
                loft('Needle bow tip', [[-185, 4, 4, -2], [-150, 12, 9, -1]], 'bow-2'),
                loft('Needle bow', [[-150, 12, 9, -1], [-110, 20, 15, 0]], 'bow-1'),
                loft('Central keel hull', [[-110, 20, 15, 0], [-80, 26, 18, 0], [95, 26, 18, 0], [138, 22, 15, 0]], 'hull'),
                loft('Low bridge', [[30, 9, 7, 25], [38, 12, 10, 26], [72, 12, 10, 26], [80, 9, 7, 25]], 'bridge'),
                box('Bridge glazing', [0, 31, 37.6], [17, 3, .6], mat='glass', decor=True),
                box('Bridge roof array', [0, 36.4, 58], [10, .8, 16], mat='plate', decor=True),
                box('Forward battery base', [0, 22, -60], [20, 8, 20], 'turret-base-gun-1', 'hull'),
                box('Ventral battery base', [0, -22, -30], [20, 8, 20], 'turret-base-gun-2', 'hull'),
                prism('Swept defence deck', [[25, -130], [52, -84], [70, -40], [70, 52], [58, 82], [25, 92]], 0, 7, 'defense-deck', 'hull', mirror=True, core=False),
                prism('Deck navy panel', [[34, -96], [52, -70], [62, -40], [62, -10], [34, -10]], 3.7, .4, mirror=True, mat='plate', decor=True),
                prism('Deck navy panel aft', [[34, 28], [62, 28], [62, 52], [54, 74], [34, 78]], 3.7, .4, mirror=True, mat='plate', decor=True),
                box('Drone cradle', [70.6, 0, -20], [1.2, 5, 12], mirror=True, mat='edge', decor=True),
                box('Drone cradle aft', [70.6, 0, 30], [1.2, 5, 12], mirror=True, mat='edge', decor=True),
                box('Deck leading armour', [60, 0, -62], [3, 7.6, 34], mirror=True, mat='plate', decor=True),
                box('Antenna mast', [0, 40, 66], [2, 10, 2], mat='edge', decor=True),
            ],
            bands=[dict(piece='Central keel hull', z=[-20, -8]), dict(piece='Central keel hull', z=[100, 112]), dict(piece='Needle bow', z=[-130, -120])],
            hatches=[dict(start=[26.1, -5, -72], grid=[2, 4], pitch=[10, 6], size=[.6, 3.5, 4])],
            rcs=dict(stations=[-100, 125], size=1.5, plume=12)))


@layout('mars_gun_cruiser_t5')
def marineris():
    """Needle prow flanked by long tapering armour rails, open midship truss, tall aft sensor mast, vertical radiators."""
    return dict(
        modules={'radiator-port': {'center': [-34, 0, 83]}, 'radiator-starboard': {'center': [34, 0, 83]}, 'sensor': {'center': [0, 60, 75]}},
        pdMounts=[[0, -15.6, 33.6], [0, 18.72, -33.6]], missileLaunch=[-13, 19, -86],
        blockout=dict(role='gun', reference='art/reference/20261017_t5_fleet/cruisers.png', palette='mars-light', surface=MARS_SURFACE,
            pieces=[
                loft('Needle prow', [[-158, 1, 1.5, -2], [-125, 6, 8, -1]], 'bow-2'),
                loft('Prow shoulder', [[-125, 6, 8, -1], [-100, 14, 12.5, 0]], 'bow-1'),
                loft('Forward gun hull', [[-100, 14, 12.5, 0], [-80, 19.7, 14.8, 0], [9, 19.7, 14.8, 0]], 'hull'),
                loft('Midship spine', [[9, 10, 10, 0], [45, 10, 10, 0]], 'mid-spine', 'hull'),
                truss('Open midship truss', [16, 0, 27], [5, 24, 36], .7, mirror=True),
                loft('Aft engineering hull', [[45, 19.7, 14.8, 0], [100, 19.7, 14.8, 0], [118, 16, 12, 0]], 'aft-core', 'hull'),
                loft('Forward armour rail', [[-122, 1.4, 1.8, -5], [-96, 3.5, 3.5, -6], [-28, 3.5, 3.5, -6], [-20, 2.4, 2.4, -6]], 'forward-rail', 'hull', x=21.5, mirror=True),
                box('Rail strut', [20, -5, -70], [4, 3, 3], mirror=True, mat='edge', decor=True),
                box('Rail strut aft', [20, -5, -36], [4, 3, 3], mirror=True, mat='edge', decor=True),
                box('Forward battery base', [0, 17, -50.4], [14, 6, 14], 'turret-base-gun-1', 'hull'),
                box('Ventral battery base', [0, -17, -25.2], [14, 6, 14], 'turret-base-gun-2', 'hull'),
                box('Command block', [0, 22, 75], [18, 16, 26], 'bridge'),
                box('Sensor mast', [0, 44, 75], [6, 28, 8], 'mast-stem', 'bridge'),
                box('Sensor head', [0, 60, 75], [14, 18, 17], 'sensor-head', 'bridge', hidden=True),
                box('Mast dish support', [5.5, 50, 75], [8, 2, 2], mat='edge', decor=True),
                box('Mast dish', [9, 50, 75], [1, 9, 9], mat='edge', decor=True),
            ],
            bands=[dict(piece='Forward gun hull', z=[-62, -54]), dict(piece='Forward gun hull', z=[-14, -6]), dict(piece='Aft engineering hull', z=[58, 66]),
                   dict(piece='Prow shoulder', z=[-116, -108])],
            rcs=dict(stations=[-90, 108], size=1.3, plume=10)))


@layout('mars_missile_cruiser_t5')
def hellas():
    """Short spear bow, two wide parallel missile magazine hulls with dorsal VLS lids, low bridge, compact engines."""
    return dict(
        modules={'radiator-port': {'center': [-46.2, -12, 72]}, 'radiator-starboard': {'center': [46.2, -12, 72]}, 'sensor': {'center': [0, 26, 65]}},
        pdMounts=[[0, -15.6, 33.6], [0, 18.72, -33.6]], missileLaunch=[-33, 16.5, -10],
        blockout=dict(role='missile', reference='art/reference/20261017_t5_fleet/cruisers.png', palette='mars-dark', surface=MARS_SURFACE,
            pieces=[
                loft('Spear bow', [[-150, 1, 1.5, -2], [-118, 7, 8, -1]], 'bow-2'),
                loft('Spear shoulder', [[-118, 7, 8, -1], [-96, 14, 12, 0]], 'bow-1'),
                loft('Central hull', [[-96, 14, 12, 0], [-80, 19.7, 14.8, 0], [100, 19.7, 14.8, 0], [116, 16, 12, 0]], 'hull'),
                loft('Missile magazine hull', [[-60, 8, 10, 0], [-46, 12, 13, 0], [100, 12, 13, 0], [112, 10, 11, 0]], 'missile-bank', 'hull', x=33, mirror=True),
                box('Magazine hull root', [21.5, 0, -20], [6, 10, 20], mirror=True),
                box('Magazine hull root aft', [21.5, 0, 70], [6, 10, 24], mirror=True),
                box('Forward battery base', [0, 17, -50.4], [14, 6, 14], 'turret-base-gun-1', 'hull'),
                box('Ventral battery base', [0, -17, -25.2], [14, 6, 14], 'turret-base-gun-2', 'hull'),
                box('Low bridge', [0, 24, 65], [18, 20, 26], 'bridge'),
                box('Magazine nose cap', [33, 0, -60.5], [14, 16, 1], mirror=True, mat='plate', decor=True),
            ],
            bands=[dict(piece='Missile magazine hull', z=[-40, -30]), dict(piece='Missile magazine hull', z=[60, 70]), dict(piece='Central hull', z=[-70, -62]),
                   dict(piece='Spear shoulder', z=[-110, -102])],
            vls=[dict(center=[33, 13.25, 25], grid=[2, 8], pitch=[9, 16], cell=[7, 12]), dict(center=[-33, 13.25, 25], grid=[2, 8], pitch=[9, 16], cell=[7, 12])],
            rcs=dict(stations=[-86, 106], size=1.3, plume=10)))


@layout('battleship_t5')
def himalaya():
    """Monumental stepped armoured bow, thick lateral shoulders, raised gun deck, low citadel and command tower,
    broad aft cooling wings, 2x2 engine cluster."""
    return dict(
        modules={'sensor': {'center': [0, 136, 294]}},
        pdMounts=[[66, 62, -260], [-66, 62, -260], [66, 62, -100], [-66, 62, -100], [66, 62, 60], [-66, 62, 60], [66, 62, 220], [-66, 62, 220]],
        missileLaunch=[-55, 66, -180],
        blockout=dict(role='battleship', reference='art/reference/20261017_t5_fleet/battleships.png', palette='earth', surface=EARTH_SURFACE,
            bandGrow=1.2, plume=100,
            pieces=[
                loft('Stepped ram prow', [[-680, 40, 18, -10], [-596, 58, 27, -10]], 'bow-3'),
                loft('Stepped prow terrace', [[-596, 58, 27, -10], [-522, 74, 38, -7]], 'bow-2'),
                loft('Stepped prow shoulder', [[-522, 74, 38, -7], [-450, 87, 50, -3]], 'bow-1'),
                box('Prow upper terrace', [0, 26, -560], [96, 8, 70], mat='plate', decor=True),
                box('Prow upper terrace aft', [0, 41, -490], [124, 8, 70], mat='plate', decor=True),
                loft('Battleship hull', [[-450, 87, 52, 0], [-300, 90, 58, 0], [380, 90, 58, 0], [520, 80, 52, 0]], 'hull'),
                loft('Raised gun deck', [[-460, 34, 12, 70], [-400, 50, 16, 72], [380, 50, 16, 72]], 'deck'),
                loft('Armoured keel', [[-300, 44, 12, -72], [-260, 58, 15, -72], [360, 58, 15, -72], [380, 44, 12, -72]], 'keel'),
                loft('Lateral armour shoulder', [[-420, 10, 30, -2], [-380, 22, 48, -2], [300, 22, 48, -2], [340, 12, 30, -2]], 'armor-shoulder', 'hull', x=95, mirror=True),
                box('Low citadel', [0, 97, 305], [110, 26, 190], 'citadel', 'deck'),
                box('Command tower', [0, 125, 294], [44, 32, 64], 'bridge'),
                box('Sensor head', [0, 136, 294], [29, 31, 41], 'sensor-head', 'bridge', hidden=True),
                box('Forward battery barbette', [0, 83, -320], [60, 10, 57], 'turret-base-gun-1', 'hull'),
                box('Midship battery barbette', [0, 83, 160], [60, 10, 57], 'turret-base-gun-2', 'hull'),
                box('Ventral battery barbette', [0, -83, -120], [60, 10, 57], 'turret-base-gun-3', 'hull'),
                box('Engine cluster block', [0, 0, 550], [180, 130, 60], 'engine-block'),
                box('Drone cradle', [117.5, 10, 120], [3, 20, 50], mirror=True, mat='edge', decor=True),
                box('Drone cradle aft', [117.5, 10, 200], [3, 20, 50], mirror=True, mat='edge', decor=True),
            ],
            bands=[dict(piece='Battleship hull', z=[-380, -340]), dict(piece='Battleship hull', z=[-140, -100]), dict(piece='Battleship hull', z=[200, 240]),
                   dict(piece='Lateral armour shoulder', z=[-200, -150]), dict(piece='Lateral armour shoulder', z=[60, 110]),
                   dict(piece='Stepped prow shoulder', z=[-500, -476])],
            rcs=dict(stations=[-400, 470], size=5.4, plume=43)))


@layout('mars_battleship_t5')
def nergal():
    """Long spear bow, raised dorsal battery spine, open aft truss frame carrying engineering pods and
    exposed copper radiators, tall aft sensor mast."""
    return dict(
        modules={'sensor': {'center': [0, 153, 335]}},
        pdMounts=[[46.2, 49.6, -221], [-46.2, 49.6, -221], [61, 44, 370], [-61, 44, 370]], missileLaunch=[-35, 52, -160],
        bays={'hangar': {'center': [-41.65, 0, 160]}},
        blockout=dict(role='battleship', reference='art/reference/20261017_t5_fleet/battleships.png', palette='mars-dark', surface=MARS_SURFACE,
            bandGrow=1.2, plume=100,
            pieces=[
                loft('Spear point', [[-578, 6, 6, -8], [-506, 26, 21, -8]], 'bow-3'),
                loft('Spear bow', [[-506, 26, 21, -8], [-454, 38, 30, -5]], 'bow-2'),
                loft('Spear shoulder', [[-454, 38, 30, -5], [-382, 52, 40, -2]], 'bow-1'),
                loft('Battle hull', [[-382, 52, 40, -2], [-250, 59.5, 48, 0], [150, 59.5, 48, 0], [183, 52, 40, 0]], 'hull'),
                loft('Dorsal battery spine', [[-370, 18, 10, 57.6], [-330, 33.6, 12.8, 57.6], [110, 33.6, 12.8, 57.6]], 'deck'),
                loft('Ventral keel', [[-310, 26, 10, -57.6], [-270, 40.6, 12, -57.6], [150, 40.6, 12, -57.6]], 'keel'),
                box('Forward battery barbette', [0, 59.4, -272], [44, 24.2, 44], 'turret-base-gun-1', 'hull'),
                box('Ventral battery barbette', [0, -59.4, -102], [44, 24.2, 44], 'turret-base-gun-3', 'hull'),
                loft('Aft spine', [[171, 25, 27, 0], [441, 25, 27, 0]], 'aft-spine', 'hull'),
                loft('Engineering pod', [[213, 12, 18, 0], [230, 17, 25, 0], [310, 17, 25, 0], [327, 12, 18, 0]], 'engineering-pod', 'hull', x=47, mirror=True),
                truss('Aft truss frame', [61, 0, 330], [6, 84, 224], 2.2, 'aft-truss', 'hull', mirror=True),
                box('Engine drive block', [0, 0, 464], [148, 102, 58], 'engine-block'),
                box('Bridge foundation', [0, 37, 335], [46, 22, 60], 'bridge-foundation', 'bridge'),
                box('Command tower', [0, 86, 335], [48, 78, 66], 'bridge'),
                box('Sensor mast', [0, 132, 335], [10, 18, 14], 'mast-stem', 'bridge'),
                box('Sensor head', [0, 153, 335], [21.6, 26.4, 37.4], 'sensor-head', 'bridge', hidden=True),
                truss('Aft spine strut', [41.5, 0, 400], [33, 6, 10], 1.6, axis='x', mirror=True),
                box('PD gallery', [61, 42.5, 370], [8, 1.5, 30], mirror=True, mat='edge'),
            ],
            bands=[dict(piece='Battle hull', z=[-300, -270]), dict(piece='Battle hull', z=[0, 30]), dict(piece='Spear shoulder', z=[-430, -410]),
                   dict(piece='Engineering pod', z=[250, 270])],
            rcs=dict(stations=[-350, 455], size=5.4, plume=43)))


# ---------------------------------------------------------------- T5 small craft
def small_common(cockpit, nozzle, stations, **kw):
    return dict(cockpit=cockpit, nozzleRadius=nozzle, rcs=dict(stations=stations, size=.26, plume=4), **kw)


@layout('earth_recon_t5')
def polaris():
    """Long straight transverse sensor wing with big pods at the tips, dorsal six-probe cassette, low canopy,
    compact lower engine pods and tall twin fins."""
    return dict(
        modules={'engine-0': {'center': [-3.4, -0.5, 9.6]}, 'engine-1': {'center': [3.4, -0.5, 9.6]},
                 'cooling-port': {'center': [-3.4, -0.5, 7.2]}, 'cooling-starboard': {'center': [3.4, -0.5, 7.2]},
                 'sensor': {'center': [0, 0.5, -11]}, 'gun': {'center': [0, -0.6, -12.3]}, 'am-containment': {'center': [0, -1.5, -3.3]}},
        gunMuzzle=[0, -0.6, -16.7], missileLaunch=[-2.65, -0.65, -3.8], amLaunch=[0, -1.5, -5.28],
        pdMounts=[[0, -1.75, 7], [0, 1.75, 8.9]],
        probeLaunchPoints=[[x, 2.25, 3.2] for x in (-1.65, -0.99, -0.33, 0.33, 0.99, 1.65)],
        blockout=dict(role='recon', reference='art/reference/20261017_t5_fleet/small.png', palette='earth', surface=SMALL_EARTH_SURFACE,
            **small_common(dict(z=-5.6, w=.8, y=1.25), .85, [-12.5, 9.5]),
            pieces=[
                loft('Tapered prow', [[-15.2, .3, .35, -.35], [-12.5, .75, .65, -.2], [-9, 1.1, .9, -.05], [-6, 1.35, 1.05, 0]], 'nose', 'nose'),
                loft('Main pressure hull', [[-6, 1.35, 1.05, 0], [-3, 1.9, 1.4, .05], [5, 1.9, 1.45, 0], [11.2, 1.45, 1.2, 0]], 'hull', 'hull'),
                box('Probe cassette', [0, 1.85, 4.6], [4.1, .9, 2.6], 'probe-carrier', 'hull'),
                box('AM keel', [0, -1.5, -3.3], [1.9, 2, 3.6], 'am-keel', 'hull', hidden=True),
                prism('Transverse sensor wing', [[1.7, -1.7], [11.6, -1.3], [11.6, .3], [1.7, .7]], 0, .5, 'wing-0', 'wing-back', mirror=True, core=False),
                box('Wing tip sensor pod', [12.4, 0, -0.6], [1.8, 1.7, 3.4], 'sensor-ear', 'nose', mirror=True),
                box('Pod sensor aperture', [12.4, 0, -2.33], [1.4, 1.2, .08], mirror=True, mat='glass', decor=True),
                loft('Engine pod', [[5.0, .75, .75, -.5], [6.0, 1.0, 1.0, -.5], [11.0, 1.0, 1.0, -.5], [11.5, .85, .85, -.5]], 'engine', 'engine-block', x=3.4, mirror=True),
                prism('Tail plane', [[1.6, 7.6], [4.4, 8.6], [4.4, 10.4], [1.6, 10.6]], -.3, .35, 'wing-1', 'wing-back', mirror=True, core=False),
                fin('Attached fin', [[3.4, .4, 7.2], [3.4, .4, 11.0]], [[3.55, 4.3, 9.6], [3.55, 4.3, 11.2]], 'fin', mirror=True, core=False, mat='plate'),
                prism('Wing navy panel', [[4, -1.55], [10.8, -1.25], [10.8, .2], [4, .55]], 0, .56, mirror=True, mat='plate', decor=True),
            ],
            bands=[dict(piece='Tapered prow', z=[-9.6, -8.8]), dict(piece='Main pressure hull', z=[1.2, 2.2])]))


@layout('mars_interceptor_t5')
def schiaparelli():
    """Very narrow dart fuselage, close twin engines, steeply swept thin wings, forward canards, low outward canted fins."""
    return dict(
        modules={'engine-0': {'center': [-3.15, 0, 7.9]}, 'engine-1': {'center': [3.15, 0, 7.9]},
                 'cooling-port': {'center': [-3.15, 0, 5.8]}, 'cooling-starboard': {'center': [3.15, 0, 5.8]}},
        gunMuzzle=[0, -0.48, -15.1], missileLaunch=[-1.8, -0.65, -3.8], amLaunch=[0, -1.2, -4.58],
        pdMounts=[[0, 1.5, 7.2]], probeLaunchPoints=[[-1.9, 0.8, -0.2], [1.9, 0.8, -0.2]],
        blockout=dict(role='agile', reference='art/reference/20261017_t5_fleet/small.png', palette='mars-light', surface=SMALL_MARS_SURFACE,
            **small_common(dict(z=-5.2, w=.62, y=1.12), .72, [-11, 8]),
            pieces=[
                loft('Dart prow', [[-13.6, .2, .3, -.3], [-10.5, .55, .6, -.15], [-7, .85, .85, 0], [-4.8, .95, .9, 0]], 'nose', 'nose'),
                loft('Dart fuselage', [[-4.8, .95, .9, 0], [-2, 1.55, 1.2, 0], [6, 1.65, 1.25, 0], [9.4, 1.15, 1.05, 0]], 'hull', 'hull'),
                loft('Engine nacelle', [[3.4, .7, .7, 0], [4.4, 1.0, 1.0, 0], [9.6, 1.0, 1.0, 0], [10.1, .85, .85, 0]], 'engine', 'engine-block', x=3.15, mirror=True),
                box('AM keel', [0, -1.2, -2.8], [1.6, 1.7, 3.2], 'am-keel', 'hull', hidden=True),
                prism('Steep swept wing', [[1.5, -3.5], [7.4, 7.0], [7.0, 8.0], [1.5, 6.0]], 0, .35, 'wing-1', 'wing-back', mirror=True, core=False),
                prism('Canard', [[.85, -8.6], [2.9, -7.1], [2.9, -6.5], [.9, -6.3]], .1, .28, 'wing-0', 'wing-front', mirror=True, core=False),
                fin('Attached fin', [[3.7, .65, 6.2], [3.7, .65, 9.4]], [[5.3, 2.1, 8.4], [5.3, 2.1, 9.7]], 'fin', mirror=True, core=False, mat='plate'),
                prism('Wing rust panel', [[3.2, 1.6], [6.6, 6.9], [6.3, 7.6], [3.2, 5.7]], 0, .4, mirror=True, mat='plate', decor=True),
            ],
            bands=[dict(piece='Dart prow', z=[-8.4, -7.6]), dict(piece='Dart fuselage', z=[2.6, 3.4])]))


@layout('mars_assault_t5')
def enyo():
    """Thick keel with a six-cell torpedo block, heavy clipped angular wings, deep outboard nacelles, tall fins."""
    return dict(
        modules={'am-containment': {'center': [0, -1.9, -2.2]}},
        gunMuzzle=[0, -0.48, -15.2], missileLaunch=[-2.45, -0.65, -3.8], amLaunch=[0, -1.9, -5.65],
        pdMounts=[[0, 1.85, 6.5]],
        blockout=dict(role='assault', reference='art/reference/20261017_t5_fleet/small.png', palette='mars-light', surface=SMALL_MARS_SURFACE,
            keelPiece=True, **small_common(dict(z=-4.6, w=.85, y=1.55), 1.05, [-10.5, 6.9]),
            pieces=[
                loft('Armoured prow', [[-13.4, .4, .5, -.25], [-9, 1.1, .95, -.1], [-4, 1.45, 1.15, 0]], 'nose', 'nose'),
                loft('Armoured fuselage', [[-4, 1.45, 1.15, 0], [-2, 2.35, 1.6, 0], [6, 2.35, 1.6, 0], [10, 1.6, 1.3, 0]], 'hull', 'hull'),
                box('Torpedo storage keel', [0, -1.9, -2.2], [2.8, 1.6, 6.6], 'am-keel', 'hull'),
                loft('Deep engine nacelle', [[3.0, 1.0, 1.2, 0], [4.0, 1.35, 1.55, 0], [10.0, 1.35, 1.55, 0], [10.6, 1.15, 1.35, 0]], 'engine', 'engine-block', x=5.2, mirror=True),
                prism('Clipped heavy wing', [[1.8, -3.5], [6.6, -0.5], [6.6, 7.2], [1.8, 7.6]], 0, 1.0, 'wing-0', 'wing-back', mirror=True, core=False),
                fin('Attached fin', [[5.2, 1.0, 5.2], [5.2, 1.0, 9.6]], [[5.45, 4.6, 8.0], [5.45, 4.6, 10.0]], 'fin', mirror=True, core=False, mat='plate'),
                box('Wing armour band', [4.2, 0, 2.0], [4.6, 1.06, 1.6], mirror=True, mat='plate', decor=True),
                box('Torpedo cell cap', [-.7, -2.25, -5.53], [.6, .5, .1], mat='edge', decor=True),
                box('Torpedo cell cap 3', [.7, -2.25, -5.53], [.6, .5, .1], mat='edge', decor=True),
                box('Torpedo cell cap 4', [-.7, -1.55, -5.53], [.6, .5, .1], mat='edge', decor=True),
                box('Torpedo cell cap 5', [.7, -1.55, -5.53], [.6, .5, .1], mat='edge', decor=True),
            ],
            bands=[dict(piece='Armoured prow', z=[-7.6, -6.8]), dict(piece='Armoured fuselage', z=[3, 4]), dict(piece='Deep engine nacelle', z=[5.2, 6.0])]))


# ---------------------------------------------------------------- T4 (art/reference/20261017_t4_*)
MARS_ORANGE = {'base': 'mars-dark', 'plate': [0.5, 0.17, 0.05]}
MARS_ORANGE_SURFACE = dict(accent=[0.55, 0.2, 0.06], accentRatio=0.22, accentScale=4, wear=0.25, streaks=0.35)


def earth_cruiser_core(prow, hull_rings, aft_rings):
    """Shared Earth cruiser hull: given prow pieces, a main hull loft and the aft engineering loft."""
    return prow + [loft('Main hull', hull_rings, 'hull'), loft('Aft engineering hull', aft_rings, 'engine-block', 'hull'),
                   box('Forward battery base', [0, 22, -60], [20, 8, 20], 'turret-base-gun-1', 'hull'),
                   box('Ventral battery base', [0, -22, -30], [20, 8, 20], 'turret-base-gun-2', 'hull')]


@layout('earth_gun_cruiser_t4')
def nile():
    """Split twin armoured prow lobes around a central slot, raised mid citadel, bulky aft, vertical navy radiators."""
    return dict(
        modules={'radiator-port': {'center': [-40, 6, 100], 'halfSize': [0.9, 14, 22]}, 'radiator-starboard': {'center': [40, 6, 100], 'halfSize': [0.9, 14, 22]},
                 'sensor': {'center': [0, 35, 70]}},
        pdMounts=[[20, 24, 58], [-20, 24, 58], [0, -20, 40], [0, 24, -40]], missileLaunch=[0, 30, -20],
        blockout=dict(role='gun', reference='art/reference/20261017_t4_cruisers/t4-cruisers.png', palette='earth', surface=EARTH_SURFACE,
            pieces=earth_cruiser_core(
                [loft('Twin prow lobe', [[-185, 6, 9, -2], [-150, 11, 14, -1], [-115, 13, 16, 0]], 'bow-1', x=12, mirror=True),
                 box('Prow centre slot', [0, 2, -150], [6, 18, 72], mat='dark', decor=True)],
                [[-115, 25, 17, 0], [-90, 27, 18.5, 0], [50, 27, 18.5, 0]],
                [[50, 30, 21, 0], [60, 31, 22, 0], [132, 31, 22, 0], [138, 27, 19, 0]]) + [
                box('Mid citadel', [0, 24, 22], [34, 11, 60], 'citadel', 'hull'),
                box('Command block', [0, 34, 70], [24, 24, 30], 'bridge'),
                box('Citadel navy plate', [17.2, 24, 22], [.5, 8, 20], mirror=True, mat='plate', decor=True)],
            bands=[dict(piece='Twin prow lobe', z=[-140, -132]), dict(piece='Main hull', z=[-30, -22]), dict(piece='Aft engineering hull', z=[80, 90])],
            hatches=[dict(start=[27.1, -5, -72], grid=[2, 4], pitch=[10, 6], size=[.6, 3.5, 4])],
            rcs=dict(stations=[-100, 120], size=1.5, plume=12)))


@layout('earth_air_defense_cruiser_t4')
def stratus():
    """Trimaran: twin-lobed central hull, two outrigger hulls on cross beams carrying PD, raised bridge bar."""
    return dict(
        modules={'radiator-port': {'center': [-40, 6, 100], 'halfSize': [0.9, 14, 22]}, 'radiator-starboard': {'center': [40, 6, 100], 'halfSize': [0.9, 14, 22]},
                 'sensor': {'center': [0, 37, 30]}},
        pdMounts=[[48, 12, 27], [-48, 12, 27], [0, -21, 45], [0, 23, -38], [48, 12, -75], [-48, 12, -75]], missileLaunch=[0, 30, -20],
        blockout=dict(role='defense', reference='art/reference/20261017_t4_cruisers/t4-cruisers.png', palette='earth', surface=EARTH_SURFACE,
            pieces=earth_cruiser_core(
                [loft('Twin prow lobe', [[-185, 6, 9, -2], [-150, 11, 14, -1], [-115, 13, 16, 0]], 'bow-1', x=12, mirror=True),
                 box('Prow centre slot', [0, 2, -150], [6, 18, 72], mat='dark', decor=True)],
                [[-115, 25, 17, 0], [-90, 27, 18.5, 0], [50, 27, 18.5, 0]],
                [[50, 30, 21, 0], [60, 31, 22, 0], [132, 31, 22, 0], [138, 27, 19, 0]]) + [
                loft('Outrigger hull', [[-125, 5, 7, 0], [-105, 9, 10, 0], [60, 9, 10, 0], [75, 7, 8, 0]], 'outrigger', 'hull', x=48, mirror=True, core=False),
                box('Cross beam', [33, 0, -50], [26, 6, 10], mirror=True),
                box('Cross beam aft', [33, 0, 40], [26, 6, 10], mirror=True),
                box('Command block', [0, 33, 30], [20, 30, 24], 'bridge'),
                box('Bridge bar', [0, 45.5, 42], [62, 5, 8], mat='edge', decor=True),
                box('Bridge bar glazing', [0, 45.5, 37.9], [56, 2, .4], mat='glass', decor=True),
                box('Outrigger navy panel', [57.1, 0, -100], [.5, 12, 22], mirror=True, mat='plate', decor=True),
                box('Drone cradle', [57.4, 0, -20], [1, 5, 12], mirror=True, mat='edge', decor=True)],
            bands=[dict(piece='Twin prow lobe', z=[-140, -132]), dict(piece='Outrigger hull', z=[-20, -12]), dict(piece='Aft engineering hull', z=[80, 90])],
            rcs=dict(stations=[-100, 120], size=1.5, plume=12)))


@layout('mars_gun_cruiser_t4')
def melas():
    """Slender spear bow with an exposed truss frame along the forward hull, bulky midships citadel and mast,
    tall aft vertical radiators."""
    return dict(
        modules={'radiator-port': {'center': [-33, -6, 68]}, 'radiator-starboard': {'center': [33, -6, 68]}, 'sensor': {'center': [0, 58, 8]}},
        pdMounts=[[0, -15.6, 33.6], [0, 18.72, -33.6]], missileLaunch=[0, 23.4, -16.8],
        blockout=dict(role='gun', reference='art/reference/20261017_t4_cruisers/t4-cruisers.png', palette=MARS_ORANGE, surface=MARS_ORANGE_SURFACE,
            pieces=[
                loft('Spear point', [[-158, 1, 1.5, -2], [-120, 6, 7, -1]], 'bow-2'),
                loft('Spear bow', [[-120, 6, 7, -1], [-95, 13, 11, 0]], 'bow-1'),
                loft('Forward hull', [[-95, 15, 12.5, 0], [-80, 19.7, 14.8, 0], [116, 19.7, 14.8, 0], [122, 16, 12, 0]], 'hull'),
                truss('Forward truss frame', [22, 0, -60], [5, 22, 72], .7, mirror=True, decor=True),
                box('Midships citadel', [0, 4, 25], [46, 36, 70], 'citadel', 'hull'),
                box('Forward battery base', [0, 17, -50.4], [14, 6, 14], 'turret-base-gun-1', 'hull'),
                box('Ventral battery base', [0, -17, -25.2], [14, 6, 14], 'turret-base-gun-2', 'hull'),
                box('Command block', [0, 26, 8], [16, 8, 20], 'bridge'),
                box('Sensor mast', [0, 36, 8], [6, 28, 8], 'mast-stem', 'bridge'),
                box('Sensor head', [0, 58, 8], [13, 17, 16], 'sensor-head', 'bridge', hidden=True),
                box('Citadel rust plate', [23.2, 4, 25], [.5, 20, 30], mirror=True, mat='plate', decor=True)],
            bands=[dict(piece='Forward hull', z=[-70, -60]), dict(piece='Forward hull', z=[80, 90]), dict(piece='Spear bow', z=[-112, -104])],
            rcs=dict(stations=[-88, 108], size=1.3, plume=10)))


@layout('mars_missile_cruiser_t4')
def argyre():
    """Central hull ringed by four long missile pods with side VLS hatches, tall bridge mast, aft vertical radiators."""
    return dict(
        modules={'radiator-port': {'center': [-31, -2, 104]}, 'radiator-starboard': {'center': [31, -2, 104]}, 'sensor': {'center': [0, 48, 62]}},
        pdMounts=[[0, -15.6, 33.6], [0, 18.72, -33.6]], missileLaunch=[0, 23.4, -16.8],
        blockout=dict(role='missile', reference='art/reference/20261017_t4_cruisers/t4-cruisers.png', palette=MARS_ORANGE, surface=MARS_ORANGE_SURFACE,
            pieces=[
                loft('Spear point', [[-150, 1, 1.5, -2], [-118, 7, 8, -1]], 'bow-2'),
                loft('Spear bow', [[-118, 7, 8, -1], [-96, 14, 12, 0]], 'bow-1'),
                loft('Central hull', [[-96, 14, 12, 0], [-80, 19.7, 14.8, 0], [116, 19.7, 14.8, 0], [122, 16, 12, 0]], 'hull'),
                loft('Upper missile pod', [[-90, 6, 6, 12], [-80, 9, 9, 12], [70, 9, 9, 12], [80, 7, 7, 12]], 'missile-bank-upper', 'hull', x=30, mirror=True),
                loft('Lower missile pod', [[-90, 6, 6, -11], [-80, 9, 9, -11], [70, 9, 9, -11], [80, 7, 7, -11]], 'missile-bank-lower', 'hull', x=30, mirror=True),
                box('Pod strut', [21, -1, -40], [6, 32, 8], mirror=True),
                box('Pod strut aft', [21, -1, 40], [6, 32, 8], mirror=True),
                box('Forward battery base', [0, 17, -50.4], [14, 6, 14], 'turret-base-gun-1', 'hull'),
                box('Ventral battery base', [0, -17, -25.2], [14, 6, 14], 'turret-base-gun-2', 'hull'),
                box('Command block', [0, 23.5, 62], [18, 18, 24], 'bridge'),
                box('Sensor mast', [0, 36.5, 62], [6, 8, 8], 'mast-stem', 'bridge'),
                box('Sensor head', [0, 48, 62], [13, 17, 16], 'sensor-head', 'bridge', hidden=True)],
            bands=[dict(piece='Upper missile pod', z=[-70, -50]), dict(piece='Lower missile pod', z=[-70, -50]), dict(piece='Upper missile pod', z=[40, 52]),
                   dict(piece='Spear bow', z=[-110, -102])],
            hatches=[dict(start=[39.05, 9.5, -40], grid=[2, 6], pitch=[5, 14], size=[.6, 4, 10]), dict(start=[39.05, -13.5, -40], grid=[2, 6], pitch=[5, 14], size=[.6, 4, 10])],
            rcs=dict(stations=[-88, 108], size=1.3, plume=10)))


@layout('earth_recon_t4')
def canopus():
    """Chunky fuselage, straight wings with tip sensor pods, big boxy aft engine pods, dorsal five-probe cassette."""
    return dict(
        modules={}, gunMuzzle=[0, -0.6, -16.4], missileLaunch=[-2.6, -0.65, -3.8], amLaunch=[0, -1.5, -5.28],
        pdMounts=[[0, -1.75, 7.5], [0, 1.75, 9]], probeLaunchPoints=[[x, 2.3, 4.0] for x in (-1.4, -0.7, 0, 0.7, 1.4)],
        blockout=dict(role='recon', reference='art/reference/20261017_t4_small/t4-small.png', palette='earth', surface=SMALL_EARTH_SURFACE,
            **small_common(dict(z=-5.5, w=.95, y=1.45), 1.15, [-12.5, 9.5]),
            pieces=[
                loft('Blunt prow', [[-15, .5, .5, -.3], [-12.5, 1.1, .9, -.1], [-9, 1.4, 1.1, 0], [-6, 1.6, 1.25, 0]], 'nose', 'nose'),
                loft('Chunky fuselage', [[-6, 1.6, 1.25, 0], [-3, 2.2, 1.7, .05], [6, 2.2, 1.7, 0], [11, 1.7, 1.4, 0]], 'hull', 'hull'),
                box('Probe cassette', [0, 1.95, 5.4], [3.6, .9, 2.6], 'probe-carrier', 'hull'),
                box('AM keel', [0, -1.5, -3.3], [1.9, 2, 3.6], 'am-keel', 'hull', hidden=True),
                loft('Boxy engine pod', [[5.5, 1.1, 1.1, 0], [6.5, 1.5, 1.5, 0], [11.5, 1.5, 1.5, 0], [12, 1.3, 1.3, 0]], 'engine', 'engine-block', x=4.8, mirror=True),
                box('Engine pylon', [2.75, 0, 8.6], [1.8, .8, 3]),
                box('Engine pylon port', [-2.75, 0, 8.6], [1.8, .8, 3]),
                prism('Straight wing', [[2.0, -2.5], [8.6, 1.2], [8.6, 2.6], [2.0, 3.0]], 0, .45, 'wing-0', 'wing-back', mirror=True, core=False),
                box('Wing tip sensor pod', [9.1, 0, 1.6], [1.0, .9, 2.2], 'sensor-ear', 'nose', mirror=True),
                box('Wing tip aperture', [9.1, 0, .48], [.7, .6, .06], mirror=True, mat='glass', decor=True),
                fin('Attached fin', [[4.8, 1.5, 9.0], [4.8, 1.5, 11.3]], [[4.9, 2.7, 10.4], [4.9, 2.7, 11.5]], 'fin', mirror=True, core=False, mat='plate'),
                box('Engine pod navy band', [4.8, 0, 7.5], [3.1, 3.1, .8], mirror=True, mat='plate', decor=True)],
            bands=[dict(piece='Blunt prow', z=[-9.6, -8.8]), dict(piece='Chunky fuselage', z=[1.0, 2.0])]))


@layout('interceptor_t4')
def saker():
    """Forward-swept wings, side intakes, outboard engine pods on pylons carrying tall fins."""
    return dict(
        modules={}, gunMuzzle=[0, -0.6, -16.7], missileLaunch=[-2.55, -0.65, -3.8], amLaunch=[0, -1.5, -5.08],
        pdMounts=[[0, -1.85, 7], [0, 1.85, 8.5]], probeLaunchPoints=[[-2.7, 0.5, 0], [2.7, 0.5, 0]],
        blockout=dict(role='defense', reference='art/reference/20261017_t4_small/t4-small.png', palette='earth', surface=SMALL_EARTH_SURFACE,
            **small_common(dict(z=-6.5, w=.8, y=1.3), .95, [-12.5, 9.5]),
            pieces=[
                loft('Tapered prow', [[-15.2, .3, .35, -.35], [-12.5, .75, .6, -.2], [-9, 1.05, .9, -.05], [-6, 1.25, 1.0, 0]], 'nose', 'nose'),
                loft('Main pressure hull', [[-6, 1.25, 1.0, 0], [-3, 1.9, 1.4, .05], [6, 1.9, 1.45, 0], [11, 1.5, 1.2, 0]], 'hull', 'hull'),
                box('AM keel', [0, -1.5, -3.1], [1.9, 2, 3.6], 'am-keel', 'hull', hidden=True),
                box('Side intake', [1.95, .15, -3], [1.2, 1.6, 4.5], mirror=True),
                box('Intake mouth', [1.95, .15, -5.27], [.9, 1.2, .06], mirror=True, mat='dark', decor=True),
                box('Probe sponson', [2.3, .45, 0], [.9, .6, 1.4], mirror=True),
                loft('Engine pod', [[6.0, .9, .9, 0], [7.0, 1.2, 1.2, 0], [11.6, 1.2, 1.2, 0], [12.1, 1.0, 1.0, 0]], 'engine', 'engine-block', x=5.4, mirror=True),
                box('Engine pylon', [3.6, 0, 8], [3.8, .6, 2.5], mirror=True),
                prism('Forward swept wing', [[1.9, -1.0], [9.6, -4.2], [9.6, -2.6], [1.9, 5.5]], 0, .4, 'wing-0', 'wing-back', mirror=True, core=False),
                prism('Wing navy edge', [[6.0, -2.6], [9.4, -4.0], [9.4, -3.2], [6.0, -1.4]], 0, .46, mirror=True, mat='plate', decor=True),
                fin('Attached fin', [[5.4, 1.1, 8.0], [5.4, 1.1, 11.4]], [[5.55, 4.2, 10.4], [5.55, 4.2, 11.8]], 'fin', mirror=True, core=False, mat='plate')],
            bands=[dict(piece='Tapered prow', z=[-9.6, -8.8]), dict(piece='Main pressure hull', z=[2.0, 3.0])]))


@layout('mars_assault_t4')
def phobos():
    """Broad notched delta wing, armoured torpedo keel, nacelles with inboard twin fins, dorsal turret."""
    return dict(
        modules={'am-containment': {'center': [0, -1.4, -2.6]}}, gunMuzzle=[0, -0.48, -15.2], missileLaunch=[-2.2, -0.65, -3.8], amLaunch=[0, -1.4, -5.05],
        pdMounts=[[0, 1.72, 7.4]],
        blockout=dict(role='assault', reference='art/reference/20261017_t4_small/t4-small.png', palette=MARS_ORANGE, surface=MARS_ORANGE_SURFACE,
            keelPiece=True, **small_common(dict(z=-4.6, w=.8, y=1.4), .95, [-10.5, 8.1]),
            pieces=[
                loft('Armoured prow', [[-13.5, .35, .45, -.25], [-9, 1.0, .9, -.1], [-4, 1.25, 1.05, 0]], 'nose', 'nose'),
                loft('Armoured fuselage', [[-4, 1.25, 1.05, 0], [-2, 2.1, 1.5, 0], [6, 2.1, 1.5, 0], [9.5, 1.5, 1.2, 0]], 'hull', 'hull'),
                box('Torpedo keel', [0, -1.45, -2.4], [2.2, 1.5, 5.0], 'am-keel', 'hull'),
                loft('Engine nacelle', [[3.0, .9, 1.0, 0], [4.0, 1.15, 1.3, 0], [9.4, 1.15, 1.3, 0], [9.9, 1.0, 1.1, 0]], 'engine', 'engine-block', x=4.4, mirror=True),
                prism('Broad delta wing', [[1.0, -6.5], [9.8, 5.2], [9.6, 6.6], [5.6, 7.4], [1.6, 8.0]], 0, .6, 'wing-0', 'wing-back', mirror=True, core=False),
                prism('Wing orange panel', [[5.0, 1.8], [8.0, 4.4], [8.0, 5.8], [5.0, 5.8]], 0, .66, mirror=True, mat='plate', decor=True),
                fin('Attached fin', [[4.4, 1.0, 5.6], [4.4, 1.0, 9.2]], [[4.75, 3.9, 8.0], [4.75, 3.9, 9.6]], 'fin', mirror=True, core=False, mat='plate')],
            bands=[dict(piece='Armoured prow', z=[-7.6, -6.8]), dict(piece='Engine nacelle', z=[5.0, 5.8])]))


@layout('mars_interceptor_t4')
def huygens():
    """Narrow fuselage with canards and slim swept wings, outboard engine pods on stub pylons, twin tall fins."""
    return dict(
        modules={}, gunMuzzle=[0, -0.48, -15.1], missileLaunch=[-1.85, -0.65, -3.8], amLaunch=[0, -1.2, -4.58],
        pdMounts=[[0, 1.5, 7.5]], probeLaunchPoints=[[-1.95, 0.5, -0.2], [1.95, 0.5, -0.2]],
        blockout=dict(role='agile', reference='art/reference/20261017_t4_small/t4-small.png', palette=MARS_ORANGE, surface=MARS_ORANGE_SURFACE,
            **small_common(dict(z=-5.2, w=.62, y=1.05), .8, [-11, 7.5]),
            pieces=[
                loft('Dart prow', [[-13.6, .2, .3, -.3], [-10.5, .55, .55, -.15], [-7, .85, .8, 0], [-4.8, .95, .85, 0]], 'nose', 'nose'),
                loft('Narrow fuselage', [[-4.8, .95, .85, 0], [-2, 1.55, 1.15, 0], [6, 1.55, 1.15, 0], [9.4, 1.1, .95, 0]], 'hull', 'hull'),
                box('AM keel', [0, -1.2, -2.8], [1.6, 1.7, 3.2], 'am-keel', 'hull', hidden=True),
                loft('Engine pod', [[2.8, .75, .8, 0], [3.6, 1.0, 1.05, 0], [9.0, 1.0, 1.05, 0], [9.5, .85, .9, 0]], 'engine', 'engine-block', x=3.75, mirror=True),
                box('Stub pylon', [2.55, 0, 6], [2.0, .4, 2]),
                box('Stub pylon port', [-2.55, 0, 6], [2.0, .4, 2]),
                prism('Slim swept wing', [[1.5, -1.5], [6.8, 4.6], [6.6, 5.5], [1.5, 3.2]], 0, .35, 'wing-1', 'wing-back', mirror=True, core=False),
                prism('Canard', [[.8, -8.5], [2.7, -7.2], [2.7, -6.6], [.85, -6.4]], .1, .28, 'wing-0', 'wing-front', mirror=True, core=False),
                fin('Attached fin', [[3.75, .9, 5.2], [3.75, .9, 8.6]], [[4.1, 3.6, 7.6], [4.1, 3.6, 9.0]], 'fin', mirror=True, core=False, mat='plate'),
                box('Side missile tube', [1.6, -.4, 2.5], [.5, .5, 3.0], mirror=True, mat='white', decor=True)],
            bands=[dict(piece='Dart prow', z=[-8.4, -7.6]), dict(piece='Engine pod', z=[4.6, 5.4])]))


# ---------------------------------------------------------------- T3 (art/reference/20261017_t3_*, 20261015_small_branches)
EARTH_DARK = {'base': 'earth', 'armor': [0.3, 0.31, 0.33], 'plate': [0.08, 0.12, 0.24]}


def sensor_tower(sensor, half, bridge_center, bridge_size, mast=None):
    """Command block + optional mast + hidden sensor-head section around the sensor module."""
    out = [box('Command block', bridge_center, bridge_size, 'bridge')]
    if mast: out.append(box('Sensor mast', mast[0], mast[1], 'mast-stem', 'bridge'))
    out.append(box('Sensor head', sensor, [h * 2 + 1 for h in half], 'sensor-head', 'bridge', hidden=True))
    return out


@layout('earth_gun_cruiser')
def danube():
    """Broad blocky hull with a chamfered chisel bow, raised gun deck, mid sensor tower, stepped aft engineering."""
    return dict(
        modules={'sensor': {'center': [0, 44, 40]}}, pdMounts=[[18, 22, 60], [-18, 22, 60], [0, -20, 40], [0, 24, -40]], missileLaunch=[0, 30, -20],
        blockout=dict(role='gun', reference='art/reference/20261017_t3_cruisers/earth_gun_cruiser.png', palette=EARTH_DARK, surface=EARTH_SURFACE,
            pieces=earth_cruiser_core(
                [loft('Chisel bow', [[-185, 14, 8, -3], [-160, 20, 13, -2], [-130, 26, 17, -1]], 'bow-1'),
                 box('Bow armour plate', [0, -3, -185.4], [22, 12, .8], mat='plate', decor=True)],
                [[-130, 26, 17, -1], [-110, 27, 18.5, 0], [50, 27, 18.5, 0]],
                [[50, 29, 20, 0], [60, 30, 21, 0], [132, 30, 21, 0], [138, 26, 18, 0]]) + [
                box('Raised gun deck', [0, 21, -5], [34, 6, 80], 'deck', 'hull'),
                *sensor_tower([0, 44, 40], [8, 10, 9], [0, 30, 40], [22, 14, 30])],
            bands=[dict(piece='Chisel bow', z=[-150, -142]), dict(piece='Main hull', z=[-60, -52]), dict(piece='Aft engineering hull', z=[90, 100])],
            hatches=[dict(start=[27.1, -5, -72], grid=[2, 4], pitch=[10, 6], size=[.6, 3.5, 4])],
            rcs=dict(stations=[-100, 120], size=1.5, plume=12)))


@layout('earth_air_defense_cruiser')
def cirrus():
    """Central hull with separated broad fore and aft defence deck lobes carrying PD, mid sensor tower."""
    return dict(
        modules={'sensor': {'center': [0, 44, 40]},  # radiators stay at the base escort position (T3 module contract)
                 'radiator-port': {'center': [-44, 0, 70], 'halfSize': [17, 0.9, 22]}, 'radiator-starboard': {'center': [44, 0, 70], 'halfSize': [17, 0.9, 22]}},
        pdMounts=[[39, 12, 35], [-39, 12, 35], [0, -20, 40], [0, 24, -40], [39, 12, -70], [-39, 12, -70]], missileLaunch=[0, 30, -20],
        blockout=dict(role='defense', reference='art/reference/20261017_t3_cruisers/earth_air_defense_cruiser.png', palette=EARTH_DARK, surface=EARTH_SURFACE,
            pieces=earth_cruiser_core(
                [loft('Chisel bow', [[-185, 12, 8, -3], [-160, 18, 13, -2], [-130, 26, 17, -1]], 'bow-1')],
                [[-130, 26, 17, -1], [-110, 27, 18.5, 0], [50, 27, 18.5, 0]],
                [[50, 29, 20, 0], [60, 30, 21, 0], [132, 30, 21, 0], [138, 26, 18, 0]]) + [
                prism('Fore defence deck', [[26, -112], [44, -98], [50, -74], [50, -46], [40, -35], [26, -35]], 0, 8, 'deck-fore', 'hull', mirror=True, core=False),
                prism('Aft defence deck', [[26, 4], [42, 8], [50, 22], [50, 38], [42, 46], [26, 46]], 0, 8, 'deck-aft', 'hull', mirror=True, core=False),
                prism('Deck navy panel', [[32, -95], [44, -85], [46, -60], [32, -55]], 4.2, .4, mirror=True, mat='plate', decor=True),
                box('Drone cradle', [50.6, 0, 30], [1.2, 5, 12], mirror=True, mat='edge', decor=True),
                *sensor_tower([0, 44, 40], [8, 10, 9], [0, 27.75, 40], [22, 18.5, 30])],
            bands=[dict(piece='Chisel bow', z=[-150, -142]), dict(piece='Main hull', z=[-20, -12]), dict(piece='Aft engineering hull', z=[90, 100])],
            rcs=dict(stations=[-100, 120], size=1.5, plume=12)))


def mars_cruiser_core(sensor, bridge_center, bridge_size, mast):
    return [loft('Spear point', [[-158, 1, 1.5, -2], [-120, 6, 7, -1]], 'bow-2'),
            loft('Spear bow', [[-120, 6, 7, -1], [-95, 13, 11, 0]], 'bow-1'),
            loft('Long hull', [[-95, 15, 12.5, 0], [-80, 19.7, 14.8, 0], [116, 19.7, 14.8, 0], [122, 16, 12, 0]], 'hull'),
            box('Forward battery base', [0, 17, -50.4], [14, 6, 14], 'turret-base-gun-1', 'hull'),
            box('Ventral battery base', [0, -17, -25.2], [14, 6, 14], 'turret-base-gun-2', 'hull'),
            *sensor_tower(sensor, [5.84, 7.8, 7.56], bridge_center, bridge_size, mast)]


@layout('mars_gun_cruiser')
def candor():
    """Very long slender hull with exposed X-braced truss panels, tall midships mast, aft vertical radiators."""
    return dict(
        modules={'sensor': {'center': [0, 63.9, 33.6]}}, pdMounts=[[0, -15.6, 33.6], [0, 18.72, -33.6]], missileLaunch=[0, 23.4, -16.8],
        blockout=dict(role='gun', reference='art/reference/20261017_t3_cruisers/mars_gun_cruiser.png', palette=MARS_ORANGE, surface=MARS_ORANGE_SURFACE,
            pieces=mars_cruiser_core([0, 63.9, 33.6], [0, 20, 33.6], [16, 12, 22], ([0, 41, 33.6], [5, 30, 6])) + [
                truss('Side truss panel', [21, 0, -45], [3, 22, 50], .6, mirror=True, decor=True),
                truss('Side truss panel aft', [21, 0, 10], [3, 22, 40], .6, mirror=True, decor=True)],
            bands=[dict(piece='Long hull', z=[-80, -72]), dict(piece='Long hull', z=[45, 53]), dict(piece='Spear bow', z=[-112, -104])],
            rcs=dict(stations=[-88, 108], size=1.3, plume=10)))


@layout('mars_missile_cruiser')
def chryse():
    """Central hull flanked by long segmented missile cassettes with side hatches, midships mast."""
    return dict(
        modules={'sensor': {'center': [0, 63.9, 33.6]},  # radiators stay at the base position (T3 module contract)
                 'radiator-port': {'center': [-22.5, 0, 59], 'halfSize': [0.4275, 12.825, 21.849999999999998]},
                 'radiator-starboard': {'center': [22.5, 0, 59], 'halfSize': [0.4275, 12.825, 21.849999999999998]}},
        pdMounts=[[0, -15.6, 33.6], [0, 18.72, -33.6]], missileLaunch=[0, 23.4, -16.8],
        blockout=dict(role='missile', reference='art/reference/20261017_t3_cruisers/mars_missile_cruiser.png', palette=MARS_ORANGE, surface=MARS_ORANGE_SURFACE,
            pieces=mars_cruiser_core([0, 63.9, 33.6], [0, 20, 33.6], [16, 12, 22], ([0, 41, 33.6], [5, 30, 6])) + [
                loft('Missile cassette', [[-82, 5, 9, 0], [-78, 7, 11, 0], [88, 7, 11, 0], [92, 5, 9, 0]], 'missile-cassette', 'hull', x=31, mirror=True),
                box('Cassette strut', [22, 0, -40], [5, 6, 8], mirror=True),
                box('Cassette strut aft', [22, 0, 88], [5, 6, 6], mirror=True),
                *[box(f'Cassette collar {i}', [31, 0, z], [14.6, 22.6, 2], mirror=True, mat='plate', decor=True) for i, z in enumerate((-50, -15, 20, 55))]],
            bands=[dict(piece='Long hull', z=[-80, -72]), dict(piece='Spear bow', z=[-112, -104])],
            hatches=[dict(start=[38.05, -5, -70], grid=[2, 10], pitch=[10, 15.5], size=[.6, 4, 11])],
            rcs=dict(stations=[-88, 108], size=1.3, plume=10)))


@layout('interceptor_t3')
def falke():
    """Wide-shouldered fuselage, swept wings carrying engine pods with forward intakes, small twin fins."""
    return dict(
        modules={}, gunMuzzle=[0, -0.6, -16.3], missileLaunch=[-2.3, -0.65, -4.1], amLaunch=[0, -1.55, -3.9],
        pdMounts=[[0, -1.8, 6], [0, 1.85, 8.2]], probeLaunchPoints=[[-2.5, 0.5, -1.3], [2.5, 0.5, -1.3]],
        blockout=dict(role='defense', reference='art/reference/20261017_t3_interceptors/falke-herschel.png', palette='earth', surface=SMALL_EARTH_SURFACE,
            **small_common(dict(z=-6.2, w=.85, y=1.35), 1.0, [-12, 10]),
            pieces=[
                loft('Tapered prow', [[-15.4, .3, .35, -.35], [-12.5, .8, .65, -.2], [-9, 1.15, .95, -.05], [-6, 1.35, 1.05, 0]], 'nose', 'nose'),
                loft('Wide fuselage', [[-6, 1.35, 1.05, 0], [-3, 1.9, 1.4, .05], [6, 1.9, 1.45, 0], [11, 1.5, 1.2, 0]], 'hull', 'hull'),
                box('AM keel', [0, -1.55, -2], [1.9, 2, 4.2], 'am-keel', 'hull', hidden=True),
                loft('Engine pod', [[6.0, .85, .85, 0], [7.0, 1.15, 1.15, 0], [11.2, 1.15, 1.15, 0], [11.7, .95, .95, 0]], 'engine', 'engine-block', x=4.9, mirror=True),
                box('Pod intake', [4.9, 0, 5.97], [1.3, 1.3, .06], mirror=True, mat='dark', decor=True),
                prism('Swept wing', [[1.9, -3.0], [8.0, 6.2], [7.8, 7.6], [1.9, 7.0]], 0, .4, 'wing-0', 'wing-back', mirror=True, core=False),
                prism('Wing navy panel', [[3.5, 1.4], [6.8, 5.6], [6.7, 6.6], [3.5, 5.9]], 0, .46, mirror=True, mat='plate', decor=True),
                fin('Attached fin', [[4.9, 1.0, 8.5], [4.9, 1.0, 11.2]], [[5.0, 2.4, 10.2], [5.0, 2.4, 11.4]], 'fin', mirror=True, core=False, mat='plate')],
            bands=[dict(piece='Tapered prow', z=[-9.6, -8.8]), dict(piece='Wide fuselage', z=[2.0, 3.0])]))


@layout('mars_interceptor_t3')
def herschel():
    """Thin central fuselage, canards, swept wings with mid-span engine nacelles and small fins."""
    return dict(
        modules={}, gunMuzzle=[0, -0.48, -14.3], missileLaunch=[-1.8, -0.65, -3.8], amLaunch=[0, -1.25, -4.1],
        pdMounts=[[0, 1.52, 7.5]], probeLaunchPoints=[[-1.75, 0.45, -1.3], [1.75, 0.45, -1.3]],
        blockout=dict(role='agile', reference='art/reference/20261017_t3_interceptors/falke-herschel.png', palette=MARS_ORANGE, surface=MARS_ORANGE_SURFACE,
            **small_common(dict(z=-5.0, w=.6, y=1.0), .8, [-10.5, 8.8]),
            pieces=[
                loft('Thin prow', [[-13.6, .2, .3, -.3], [-10.5, .5, .55, -.15], [-7, .75, .75, 0], [-4.8, .85, .8, 0]], 'nose', 'nose'),
                loft('Thin fuselage', [[-4.8, .85, .8, 0], [-2, 1.4, 1.1, 0], [6, 1.4, 1.1, 0], [9.4, 1.0, .9, 0]], 'hull', 'hull'),
                box('AM keel', [0, -1.25, -2.4], [1.2, 1.5, 3.4], 'am-keel', 'hull', hidden=True),
                loft('Wing nacelle', [[4.0, .7, .75, 0], [5.0, .95, 1.0, 0], [9.4, .95, 1.0, 0], [9.9, .8, .85, 0]], 'engine', 'engine-block', x=3.65, mirror=True),
                prism('Swept wing', [[1.3, -2.5], [6.6, 5.0], [6.3, 6.0], [1.3, 4.0]], 0, .34, 'wing-1', 'wing-back', mirror=True, core=False),
                prism('Canard', [[.75, -8.2], [2.4, -6.9], [2.4, -6.4], [.8, -6.2]], .1, .26, 'wing-0', 'wing-front', mirror=True, core=False),
                fin('Attached fin', [[3.65, .8, 6.8], [3.65, .8, 9.4]], [[3.9, 2.6, 8.6], [3.9, 2.6, 9.7]], 'fin', mirror=True, core=False, mat='plate')],
            bands=[dict(piece='Thin prow', z=[-8.2, -7.4]), dict(piece='Wing nacelle', z=[6.0, 6.8])]))


@layout('earth_recon_t3')
def altair():
    """Long sensor-nosed fuselage, side observation pods with forward probe ports, outboard engine pods, aft radiator fins."""
    return dict(
        modules={}, gunMuzzle=[0, 0.6, -15.8], missileLaunch=[-2.0, -0.45, -6.5], amLaunch=[0, -1.9, -3.1],
        pdMounts=[[0, -1.7, 7], [0, 1.7, 8.5]], probeLaunchPoints=[[-3.95, 0.45, 0], [3.95, 0.45, 0], [-3.95, -0.45, 0], [3.95, -0.45, 0]],
        blockout=dict(role='recon', reference='art/reference/20261015_small_branches/earth_recon_t3_v2.png', palette=EARTH_DARK, surface=SMALL_EARTH_SURFACE,
            keelPiece=True, **small_common(dict(z=-6.8, w=.8, y=1.35), 1.2, [-12.5, 10]),
            pieces=[
                loft('Sensor prow', [[-15.0, .45, .6, -.35], [-13.5, 1.0, .95, -.25], [-10, 1.3, 1.1, 0], [-8, 1.45, 1.15, 0]], 'nose', 'nose'),
                loft('Long fuselage', [[-8, 1.45, 1.15, 0], [-5, 2.2, 1.5, 0], [6, 2.2, 1.5, 0], [10.5, 1.6, 1.2, 0]], 'hull', 'hull'),
                box('Containment keel', [0, -1.4, -.95], [1.8, 1.9, 4.2], 'am-keel', 'hull'),
                box('Observation pod', [3.95, 0, 1.3], [1.3, 1.8, 2.6], 'sensor-ear', 'nose', mirror=True),
                box('Observation pylon', [2.65, 0, 1.3], [1.5, .6, 1.8], mirror=True),
                loft('Engine pod', [[8.5, .8, .8, 0], [9.3, 1.05, 1.05, 0], [12.6, 1.05, 1.05, 0], [13.0, .9, .9, 0]], 'engine', 'engine-block', x=4, mirror=True),
                box('Engine pylon', [3.0, 0, 10.5], [2.2, .5, 2], mirror=True),
                prism('Stub wing', [[1.9, 2.5], [5.5, 7.0], [5.5, 8.0], [1.9, 7.5]], 0, .35, 'wing-0', 'wing-back', mirror=True, core=False),
                fin('Attached fin', [[1.6, 1.2, 7.5], [1.6, 1.2, 10.0]], [[2.2, 3.0, 9.0], [2.2, 3.0, 10.4]], 'fin', mirror=True, core=False, mat='plate')],
            bands=[dict(piece='Sensor prow', z=[-11, -10.2]), dict(piece='Long fuselage', z=[3.0, 4.0])]))


@layout('mars_assault_t3')
def deimos():
    """Narrow prow, reinforced wide central section, big aft boost engines on pylons, twin fins, containment keel."""
    return dict(
        modules={}, gunMuzzle=[0, -0.3, -13.8], missileLaunch=[-2.0, -0.45, -5.1], amLaunch=[0, -2.3, -2.8],
        pdMounts=[[0, 1.85, 7]],
        blockout=dict(role='assault', reference='art/reference/20261015_small_branches/mars_assault_t3_v2.png', palette=MARS_ORANGE, surface=MARS_ORANGE_SURFACE,
            keelPiece=True, **small_common(dict(z=-5.5, w=.8, y=1.5), 1.1, [-11, 9.5]),
            pieces=[
                loft('Narrow prow', [[-13.0, .3, .4, -.25], [-10, .8, .8, -.1], [-6, 1.0, .95, 0]], 'nose', 'nose'),
                loft('Reinforced centre', [[-6, 1.0, .95, 0], [-3, 2.3, 1.6, 0], [6, 2.3, 1.6, 0], [10, 1.7, 1.3, 0]], 'hull', 'hull'),
                box('Containment keel', [0, -1.55, -.8], [2.6, 2.5, 3.9], 'am-keel', 'hull'),
                loft('Boost engine', [[6.5, 1.0, 1.0, 0], [7.5, 1.3, 1.2, 0], [11.2, 1.3, 1.2, 0], [11.8, 1.1, 1.0, 0]], 'engine', 'engine-block', x=4.1, mirror=True),
                box('Engine pylon', [2.55, 0, 9], [1.2, .8, 2.5], mirror=True),
                prism('Short swept wing', [[2.2, -1.0], [6.0, 5.5], [6.0, 7.0], [2.2, 6.5]], 0, .45, 'wing-0', 'wing-back', mirror=True, core=False),
                fin('Attached fin', [[4.1, 1.1, 8.0], [4.1, 1.1, 11.0]], [[4.4, 3.4, 10.0], [4.4, 3.4, 11.3]], 'fin', mirror=True, core=False, mat='plate')],
            bands=[dict(piece='Narrow prow', z=[-9, -8.2]), dict(piece='Reinforced centre', z=[2.5, 3.5]), dict(piece='Boost engine', z=[8.0, 8.8])]))


# ---------------------------------------------------------------- T1 (art/reference/20261017_t1_starters)
@layout('earth_starter_escort')
def zephyr():
    """Blunt armoured bow, low bridge, single dorsal battery, horizontal radiators."""
    return dict(
        modules={'sensor': {'center': [0, 28, 41]}}, pdMounts=[[16.8, 17.6, 65], [-16.8, 17.6, 65], [0, -16, 32], [0, 19.2, -32]], missileLaunch=[0, 24, -16],
        blockout=dict(role='starter', reference='art/reference/20261017_t1_starters/t1-starters.png', palette='earth', surface=EARTH_SURFACE,
            pieces=[
                loft('Blunt armoured bow', [[-148, 12, 7, -2], [-128, 17, 11, -1], [-100, 21.6, 15.2, 0]], 'bow-1'),
                box('Bow armour plate', [0, -2, -148.4], [18, 10, .8], mat='plate', decor=True),
                loft('Escort hull', [[-100, 21.6, 15.2, 0], [100, 21.6, 15.2, 0], [112, 19, 13, 0]], 'hull'),
                box('Forward battery base', [0, 17.5, -48], [16, 6.6, 16], 'turret-base-gun-1', 'hull'),
                *sensor_tower([0, 28, 41], [6.4, 8, 7.2], [0, 19, 41], [18, 8, 22])],
            bands=[dict(piece='Blunt armoured bow', z=[-120, -112]), dict(piece='Escort hull', z=[-10, -2]), dict(piece='Escort hull', z=[80, 88])],
            rcs=dict(stations=[-80, 100], size=1.2, plume=10)))


@layout('mars_starter_escort')
def lunae():
    """Thin bow, mast-mounted external sensor, exposed aft engineering frame with service blocks, vertical radiators."""
    return dict(
        modules={'sensor': {'center': [0, 40, 20]}}, pdMounts=[[0, -12.48, 26.88], [0, 14.976, -26.88]], missileLaunch=[0, 18.72, -13.44],
        blockout=dict(role='starter', reference='art/reference/20261017_t1_starters/t1-starters.png', palette='mars-light', surface=MARS_SURFACE,
            pieces=[
                loft('Thin bow point', [[-124.3, 1, 1.5, -2], [-107.5, 7, 6.2, -1.9]], 'bow-2'),
                loft('Thin bow', [[-107.5, 7, 6.2, -1.9], [-73.9, 12.8, 9.4, -.6]], 'bow-1'),
                loft('Forward hull', [[-73.9, 14, 10.5, 0], [-60, 15.77, 11.86, 0], [38, 15.77, 11.86, 0]], 'hull'),
                box('Engineering keel', [0, -7, 57.7], [10, 8, 40.4], 'engineering-keel', 'hull'),
                box('Service generator block', [-7.59, 0, 47.04], [7.6, 15.6, 20.8], 'service-generator-port', 'hull'),
                box('Service cooling block', [11.68, 0, 53.76], [6.44, 13.1, 16.8], 'service-cooling', 'hull', mirror=True),
                box('Starboard feed', [11.68, 0, 41.8], [5.84, 6, 8], 'starboard-feed', 'hull'),
                truss('Exposed aft frame', [15.4, 0, 57.7], [2, 20, 40.4], .8, mirror=True, decor=True),
                loft('Aft engine block', [[77, 15.77, 11.86, 0], [90, 15.77, 11.86, 0], [92.7, 14, 10.5, 0]], 'aft-block', 'hull'),
                box('Forward battery base', [0, 13.4, -40.32], [11.2, 6.2, 11.2], 'turret-base-gun-1', 'hull'),
                *sensor_tower([0, 40, 20], [4.67, 6.24, 6.05], [0, 17.9, 20], [16, 12.2, 24], ([0, 29.5, 20], [5.6, 11, 7.6]))],
            bands=[dict(piece='Forward hull', z=[-50, -44]), dict(piece='Aft engine block', z=[82, 86]), dict(piece='Thin bow', z=[-95, -89])],
            rcs=dict(stations=[-60, 85], size=1.1, plume=9)))


def radiator_sections(key, overrides):
    """Radiator modules keep a thin plate section of their own; thin axis padded 0.1 m, others 1 m."""
    sys.path.insert(0, str(ROOT / 'tools')); from branch_model_layout import resolve
    out = []
    for m in resolve(ROOT, key)['modules']:
        if not m['id'].startswith('radiator-'): continue
        m = {**m, **overrides.get(m['id'], {})}; thin = min(range(3), key=lambda a: m['halfSize'][a])
        half = [h + (.1 if a == thin else 1) for a, h in enumerate(m['halfSize'])]
        out.append(dict(id=m['id'], name=m['id'], center=m['center'], halfSize=half, armorFrom=m['id']))
    return out


def write(key):
    data = LAYOUTS[key](); data['id'] = key; data.setdefault('bays', {}); data.setdefault('sections', [])
    fixed = data['blockout'].setdefault('fixedSections', [])
    fixed += [s for s in radiator_sections(key, data.get('modules', {})) if s['id'] not in {f['id'] for f in fixed}]
    path = ROOT / f'data/model_layouts/{key}.json'
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8'); print('Authored', key)


if __name__ == '__main__':
    for key in sys.argv[1:] or LAYOUTS: write(key)
