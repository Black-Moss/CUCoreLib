# Changelog
I figured that this would be nice to have, as to easily take a look at everything that's been done and why your mod might not work with the new update (sorry!)

## v1.0.3

### New Stuff!
- Hotloading is a lot more mod-author friendly, so that you don't have to wrangle your code around it (as much)
- Check out the new docs for information! ^^
- Nightly/on-commit releases for CUCoreLib!
- Added itemsVisible and tagRestriction (tags only, no crafting quality)
- Added more verbose warnings
- Runtime debugger for variables was introduced, under the console command `debugwatch`

### Changes
- Locale recursive directory search was accidentally removed and now re-added
- Slimmed down batteryProperties, as I was overcomplicating it. Your mods will work still, but you may face some slight changes
- Custom structures now no longer spawn in the tutorial, and spawn up to only once in the debug world

### Fixes 
- Battery 0% condition issue...
- Fix keycode optimization (Thanks, @Jacbo1)!
- Crafting qualities now work and are added to the locale 
- `spawncategory` now can spawn all modded items
- Fixed lighting module pointLightOuterAngle/pointLightInnerAngle
- Fixed equippables teleporting when trying to equip on top on another, as well as console error logs whilst equipping for the first time
- Minor optimization changes
- Documentation is a neverending battle :)
- Fixed QoL image integration again


## v1.0.2

### New Stuff!
- Added a multi-block structure system via `StructureRegistry`, for custom structures. Will need to integrate still via the custom-structure-webapp side, though...
- Added expanded minigame helper support, subject to change
- Added keybind and keycode-related support
- Added XML documentation comments across the codebase! (this is the large change of the version)
- Added preloading for embedded images for optimization
- BuildingEntites how has a SpawnLayers field
- Added InventoryIconScale field to items
- Embedded locale files now work instead of needing to be bundled alongside the mod 

### Changes (fixes that might break your mod slightly)
- Expanded settings and locale UX support (locale category work, EN fallback behavior, and menu improvements)
- Updated moodle image defaults to use a `33.33f` pixels-per-unit baseline.
- Set `BuildingEntity`s to default to being Standard placement style instead of None.
- Having a light property now doesn't give your item a battery for some reason 

### Fixes (that are mostly safe)
- Sprite PPU behavior for is no longer fixed.
- Fixed several settings-page interaction issues, thanks @Black-Moss!
- Added console and utility support, thanks @Black-Moss!
- Reduced duplicate-warning noise.
- Added a dedicated multi-block structures docs page 
- Refreshed setup, settings, assets, utils, minigame, moodle, and status documentation.
- Documentation, documentation, documentation.
- Battery fixes once more
- Fix console errors with RegisterSpawnEntities
- Added a few enums for clarity

## v1.0.1 
- All update logs prior were lost in the great time catastrophe...

## v1.0
- Release!