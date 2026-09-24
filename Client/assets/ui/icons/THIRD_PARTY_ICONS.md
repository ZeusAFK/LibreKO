# Third-party UI icons

The files in this directory are a deliberately curated subset. Do not copy additional icons into the
game without adding their source, author and license here.

## Game-icons.net equipment silhouettes

License: [Creative Commons Attribution 3.0](https://creativecommons.org/licenses/by/3.0/)

Source collection: [game-icons/icons](https://github.com/game-icons/icons)

The original black background path was removed from each SVG so the silhouette can be tinted over the
game UI. The white foreground path was otherwise retained.

| Local file | Original icon | Author |
|---|---|---|
| `game/helmet.svg` | `lorc/visored-helm.svg` | Lorc |
| `game/chest.svg` | `delapouite/chest-armor.svg` | Delapouite |
| `game/shoulder.svg` | `delapouite/shoulder-armor.svg` | Delapouite |
| `game/gloves.svg` | `delapouite/winter-gloves.svg` | Delapouite |
| `game/boots.svg` | `delapouite/metal-boot.svg` | Delapouite |
| `game/legs.svg` | `lucasms/trousers.svg` | Lucas M. S. |
| `game/belt.svg` | `lucasms/belt.svg` | Lucas M. S. |
| `game/necklace.svg` | `delapouite/emerald-necklace.svg` | Delapouite |
| `game/earrings.svg` | `delapouite/crystal-earrings.svg` | Delapouite |
| `game/ring.svg` | `delapouite/ring.svg` | Delapouite |
| `game/main-hand.svg` | `lorc/broadsword.svg` | Lorc |
| `game/off-hand.svg` | `sbed/shield.svg` | sbed |

Icons made by Delapouite, Lorc, Lucas M. S. and sbed. Available on
[game-icons.net](https://game-icons.net/).

## Phosphor interface icons

Files: the `system/*.svg` files listed below, except `system/home.svg` and `system/bag.svg`

Source: [phosphor-icons/core](https://github.com/phosphor-icons/core), `assets`

License: MIT. Copyright © Phosphor Icons. The SVG `currentColor` fill was changed to white so Godot can
tint the imported texture consistently.

See `licenses/PHOSPHOR-MIT.txt` for the license text.

## Project-original icons

`system/home.svg` is an original house silhouette drawn for GKO on the Phosphor 256-unit grid and is
not derived from a third-party asset.

`system/bag.svg` is an original bag silhouette drawn for GKO on the same grid. It replaced Phosphor's
`backpack`, whose straps, buckle and pockets read as noise at HUD icon sizes.

`system/search.svg` is an original magnifier drawn for GKO on the Phosphor 256-unit grid so it sits
with the rest of the `system/` set; it is not copied from a third-party asset.

`system/gift.svg` is an original present silhouette drawn for GKO on the same grid -- lid, box and two
bow loops only. The ribbon channel every stock gift icon draws through the box vanishes at HUD icon
size, so it is left out.

`system/trophy.svg` is an original cup silhouette drawn for GKO on the same grid, for the achievement
window and its HUD button.

`system/envelope.svg` is an original envelope silhouette drawn for GKO on the same grid, for the mail
HUD button: a rounded body with the flap cut as a single V so it still reads at HUD icon size.

The character-sheet icon set is original, drawn for GKO on the same grid and tinted per stat at
runtime: `system/stat-str` (fist), `stat-hp` (heart), `stat-dex` (reticle), `stat-mp` (droplet),
`stat-int` (open book), `combat-attack` (upright sword), `combat-defence` (shield), and the six
resistances `res-fire`, `res-ice`, `res-lightning`, `res-magic`, `res-curse`, `res-poison`. Each is
drawn as a single filled silhouette so it survives being tinted and scaled to 20-22 px.

`system/level.svg` is an original bar chart drawn for GKO on the same grid, for the GM panel's Level
section: three rounded bars rising left to right.
