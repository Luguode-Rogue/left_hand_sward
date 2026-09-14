# Left Hand Sward

Independent skill-extension module for **New_ZZZF**.

This repository is based on the `bannerlord-newmod` project structure, but the stale template `bin/` and `obj/` build artifacts are intentionally not copied.

## Purpose

This module proves that New_ZZZF skills can be extended from a separate Bannerlord module without modifying the New_ZZZF source code.

The extension:

- references the installed `New_ZZZF.dll`;
- derives skills directly from `New_ZZZF.SkillBase`;
- registers them through `SkillFactory.RegisterSkill` from Harmony prefixes on New_ZZZF's campaign initialization callbacks;
- declares `New_ZZZF` as a module dependency so it loads first;
- owns its own `MissionBehavior` for left-hand visual lifetime and native melee-hit observation;
- does not modify New_ZZZF source or binaries; it only patches the two New_ZZZF lifecycle callbacks needed to register external skills at the safe initialization point.

## Registered skills

All current experiments are `SPSkillType.MainActive`, so they use New_ZZZF's existing main-active input path (E by default) and do not overlap the original left-mouse melee attack.

- `LHTest_NativeRightBaseline`
- `LHTest_VisualClone`
- `LHTest_LeftHandFlags`
- `LHTest_SwitchHandsFlags`
- `LHTest_LeftStanceFlags`
- `LHTest_VisualCloneAndAttack`

The IDs intentionally remain the same as the earlier in-core experiments so existing skill configuration data can resolve them when this extension module is installed.

## Extension flow

```text
LeftHandSward.OnSubModuleLoad
    -> install Harmony lifecycle prefixes
    -> DO NOT touch SkillFactory yet

Campaign starts loading
    -> OnGameStart
    -> still too early for DefaultItemCategories
    -> no Campaign SkillFactory access

New_ZZZF.OnNewGameCreated / OnGameLoaded is about to run
    -> LeftHandSward Harmony Prefix
    -> SkillFactory.RegisterSkill(...) for all 6 external skills
    -> New_ZZZF original callback continues
       -> CompositeSpellRegistry.LoadAndRegisterAll()
       -> SkillFactory.SkillToItemObject()
          (includes LeftHandSward skills)
       -> troop skill XML parsing sees the external skill IDs

Player equips an LHTest_* MainActive skill
    -> New_ZZZF AgentSkillComponent handles E / cooldown / stamina
    -> external SkillBase.Activate() runs

LeftHandSward MissionBehavior
    -> visual clone cleanup
    -> OnMeleeHit observation
```

### Why registration is not done earlier

New_ZZZF's static `SkillFactory` constructs `NullSkill`. In Campaign:

- during `OnSubModuleLoad`, `Game.Current` is still null;
- during `OnGameStart`, `Game.Current` exists but `DefaultItemCategories.Unassigned` is not initialized yet;
- at `New_ZZZF.OnNewGameCreated / OnGameLoaded`, the object system is ready because New_ZZZF itself performs its skill ItemObject initialization there.

The Harmony Prefix therefore registers external skills at the same safe lifecycle point, immediately before New_ZZZF processes its registry.


## Diagnostic log

Every important left-hand experiment event is written immediately to disk:

```text
Modules/LeftHandSward/Logs/LeftHandSward.log
```

The previous session is rotated to:

```text
Modules/LeftHandSward/Logs/LeftHandSward.previous.log
```

If the module directory cannot be written, the fallback path is:

```text
Documents/Mount and Blade II Bannerlord/LeftHandSwardLogs/LeftHandSward.log
```

The log is intentionally event-based, not per-frame. It records:

- skill activation ID;
- active weapon and usage class;
- action name, action index and raw AnimFlags;
- action state immediately before the native call;
- `CALL SetActionChannel BEGIN`;
- `CALL SetActionChannel RETURN`;
- MetaMesh copy and skeleton attach/remove native-call boundaries;
- native `OnMeleeHit`;
- observation timeout;
- module / mission lifecycle.

If the game raises `AccessViolationException`, send `LeftHandSward.log` from that same run without restarting the game first. The final line is especially important.

## Build

Set `BANNERLORD_GAME_DIR` to the Bannerlord installation directory.

The project expects New_ZZZF to already be installed at:

```text
Modules/New_ZZZF/bin/<game binary folder>/New_ZZZF.dll
```

The reference uses `Private=False`, so New_ZZZF.dll is not copied into this module's output.

## Dependency

`SubModule.xml` explicitly depends on `New_ZZZF` and orders it before `LeftHandSward`.
