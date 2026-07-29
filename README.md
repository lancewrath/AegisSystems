Aegis Systems Deflector Shield is a server side script that adds deflector shields for large ships.

It is a software layer over the jump drive and utilizes its gravitational power to charge the ships hull and disperse kinetic force back into space via electric charge.

The way it works is once a jump drive is detected on a ship, the Aegis system will add the deflector capabilities while the jump drive is on. Once the jump drive is fully charged the deflector shields will activate and will protect the ship from damage. However damage will decrease the jump drives charge, when it is depleted the deflector shield will fail and the ships integrity will no longer be protected. Multiple jump drives can be added to increase shield strength, (it is even possible to keep one off until the shields fall and turn it on as a backup, provided it wasn't damaged).

This is still in beta and i am trying to make a server side only shield mod so Console compatible servers can have an option for shielding large grids. I am considering in the future making an option for small ships, however I am trying to keep this mod relatively simple and not add additional blocks to the game.

This mod can be added and removed safely it does not change any blocks in the game or set any weird back-end variables that can conflict with other mods, it simply acts as a software layer over handling damage systems. For Xbox it must be run on a dedicated server.

There are indicators when shields go up and when they fail as well as some visuals.

Update 1.1.2

Performance and notification spam reductions (chat/notifications were reported to lag servers during combat):
- Added a rate-limited notification queue: messages are drained slowly (~0.75s apart) instead of firing instantly on every shield event.
- Same message type for the same jump drive/grid cannot re-queue until a cooldown expires (~5s by default).
- Critical-low and fully-drained warnings now fire once per jump drive until it recharges (not every hit while low).
- "Shields Are Down" also limited to once until shields recover.
- New config options in ShieldConfig.xml: EnableNotifications, NotificationCooldownTicks, NotificationSendInterval, MaxQueuedNotifications.
- Replaced linear List.Find grid lookups with a Dictionary for O(1) access on every damage event.
- Stopped resetting hit sound/FX buffers every update (that was defeating throttling during sustained fire).
- LCD [SHIELD] panels only update on the periodic shield tick — no longer rewritten on every few damage hits (large network savings).
- Removed init chat message; firmware install notifications are cooldown-gated per grid.
- Optimized LCD bar string building.

Update 1.1.1

Null-safety and crash-prevention fixes to improve server stability when grids break apart or jump drives are destroyed:
- Validate grid and block references (null and closed checks) before use across the mod.
- Prune invalid or closed grids from the shielded list during updates instead of modifying the list while iterating.
- Guard all jump-drive and panel iteration with null/closed checks and try/catch so destroyed or detached blocks cannot cause null reference exceptions.
- Fix division-by-zero when a grid has no jump drives (e.g. after all are removed or destroyed).
- Harden event handlers (OnClose, OnBlockRemoved, OnBlockAdded, RemoveTheGrid, ShieldHandler) so grid splits and block removal cannot crash the server.
- Initialize lists in MyGridShieldInfo so panels/jumpDrives are never null; filter out closed blocks in GetJumpDrives and GetPanels.

Update 1.1.0

Added some code that hopefully fixes an index out of range error that happens on Torch servers.
Update 1.0.9

Cleaned up code considerably
Made optimizations and changes to help reduce network and visual lag
Update 1.0.8

Due to some visual artifacts at distances with highlight effect, Shields fx only show initially when turned on, and then again will pulse briefly when hit. The outline effect of the shield highlight will continue to shrink until shields are depleted.
Added an option in the generated config file to turn off the highlight effect entirely if needed.
Update 1.0.7

Bugfixes and Optimizations
Removed Jump Drive damaged message, may be causing server lag and crashes when grinding.
Update 1.0.6

Removed the on hit effect to reduce lag. Added a Shieldbuffer entry to config, this is just a number of hits it will take until it plays the hit sound fx again, buffer does reset after a second.
Update 1.0.5

Removed the shield on/off effect due to causing lag, instead shields now use the built-in highlight effect with the color of your faction.
Added tag [NOSHIELD] can be added to Custom Data on jump drives that will be excluded from shields.
Config File now generated in Save game Storage folder, allows you to change the amount shield energy damage will reduce, lower number creates stronger shield.
e.g. <YOURSAVEFOLDER>\Storage\CommonLanguageRuntimeLibrary\ShieldConfig.xml (You must first run the server for the config to generate)
Updated LCD Display, now always shows 0-100% regardless of number of jump drives used for shields.
Update 1.0.4

Now you can display shield strength on LCD screens! Simply set screen to display Text, then put [SHIELD] in custom data, and it should start displaying after a moment.
Tweaked the FX to work a little better
Update 1.0.3

Add shield particle buffer between emissions and reduced particle FXs
Removed warp FX from shields up
Update 1.0.2

New Warfare Update added some nice functions to get blocks of a specific type, this allowed me to ******** the code and squash some of the bugs listed below.

Current Bugs to Fix

Shield doesn't go down immediately when jump drives are turned off, requires grid to be shot
Grinding and Welding doesn't work until shields are down and then you have to grind a block slightly to weld it back up
Particle effects are performance heavy and missing a texture.
