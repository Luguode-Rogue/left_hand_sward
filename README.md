# Left Hand Sward

Independent skill-extension module for **New_ZZZF**.

This repository is based on the `bannerlord-newmod` project structure, but the stale template `bin/` and `obj/` build artifacts are intentionally not copied.

## Purpose

This module proves that New_ZZZF skills can be extended from a separate Bannerlord module without modifying the New_ZZZF source code.

The extension:

- references the installed `New_ZZZF.dll`;
- derives skills directly from `New_ZZZF.SkillBase`;
- registers them through `SkillFactory.RegisterSkill` during `OnSubModuleLoad`;
- declares `New_ZZZF` as a module dependency so it loads first;
- owns its own `MissionBehavior` for left-hand visual lifetime and native melee-hit observation;
- does not patch or modify New_ZZZF.

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
LeftHandSward.SubModule.OnGameStart
    -> SkillFactory.RegisterSkill(...)

# Important: do not touch SkillFactory during OnSubModuleLoad.
# New_ZZZF's static SkillFactory initialization constructs NullSkill,
# and that constructor reads Game.Current, which is still null during submodule load.

New_ZZZF game initialization
    -> SkillFactory.SkillToItemObject()
    -> SkillCatalog reads SkillFactory._skillRegistry

Player equips an LHTest_* MainActive skill
    -> New_ZZZF AgentSkillComponent handles E / cooldown / stamina
    -> external SkillBase.Activate() runs

LeftHandSward MissionBehavior
    -> visual clone cleanup
    -> OnMeleeHit observation
```

## Build

Set `BANNERLORD_GAME_DIR` to the Bannerlord installation directory.

The project expects New_ZZZF to already be installed at:

```text
Modules/New_ZZZF/bin/<game binary folder>/New_ZZZF.dll
```

The reference uses `Private=False`, so New_ZZZF.dll is not copied into this module's output.

## Dependency

`SubModule.xml` explicitly depends on `New_ZZZF` and orders it before `LeftHandSward`.
