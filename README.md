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

All current experiments are `SPSkillType.SubActive`, so they use New_ZZZF's existing sub-active input path (LeftAlt by default) and do not overlap the original left-mouse melee attack.

- `LHTest_NativeRightBaseline` — right-hand native attack baseline
- `LHTest_MetaMeshVisualClone` — old MetaMesh visual-copy baseline
- `LHTest_AttachWeaponToLeftBone` — native AttachWeaponToBone with Identity frame
- `LHTest_LeftGripTransform` — transplant the live right-hand grip transform to the off-hand item bone
- `LHTest_OffHandStateProbe` — establish a real native OffHand from a second existing melee weapon slot, without attacking
- `LHTest_OffHandNativeAttack` — with a valid OffHand already present, inject native attack input and observe which hand/weapon owns the sweep

The experiment names and SkillIDs now describe the exact purpose of each test. The four older direction-only names were removed because AttackLeft/Right/Up/Down describe attack direction, not handedness.

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

Player equips an LHTest_* SubActive skill
    -> New_ZZZF AgentSkillComponent handles LeftAlt / cooldown / stamina
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
- queued/injected native MovementFlags attack input;
- action type/stage/direction transitions after native input;
- primary/offhand wielded slot and weapon info;
- real OffHand equip/wield/remove native-call boundaries;
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


### Runtime weapon visual cloning

Visual cloning does not construct or register a second `ItemObject`.

The source model is read from the actual runtime weapon entity:

```text
agent.GetPrimaryWieldedItemIndex()
    -> agent.GetWeaponEntityFromEquipmentSlot(slot)
    -> recursively enumerate root/child WeakGameEntity nodes
    -> GetMetaMesh(i)
    -> MetaMesh.CreateCopy()
    -> Skeleton.AddComponentToBone(l_hand, copy)
```

This is required for crafted/composite weapons whose `ItemObject.MultiMeshName`
can be empty even though the weapon is visibly rendered in-game.


### Visual weapon copy

Visual cloning does **not** depend on `ItemObject.MultiMeshName`.

Bannerlord's `Agent.EquipWeaponWithNewEntity` passes `WeaponData` to the native
`WeaponEquipped` callback, and the engine creates the actual equipped weapon entity.
Some valid weapons therefore have an empty `MultiMeshName` while still rendering normally.

LeftHandSward now copies the already-created live weapon entity:

```text
GetPrimaryWieldedItemIndex
    -> Agent.GetWeaponEntityFromEquipmentSlot
    -> recursively inspect root/child WeakGameEntity nodes
    -> GetMetaMesh(i)
    -> MetaMesh.CreateCopy()
    -> flatten child local transforms into MetaMesh.Frame
    -> Skeleton.AddComponentToBone(l_hand, copy)
```

This visual path does not require the weapon to be melee, so crafted weapons,
javelins and other currently wielded weapon entities can also be copied for visual tests.



## 2026-09-14 experiment reset

Real-game testing confirmed that the previous four visual+MovementFlags experiments still produced right-hand actions and right-hand melee hit detection. The cloned MetaMesh could render, but its bone-local transform was incorrect and the weapon floated away from the hand.

The new experiment order is deliberately staged:

1. confirm the normal right-hand native attack baseline;
2. keep the old MetaMesh clone only as a visual baseline;
3. test Bannerlord's native `Agent.AttachWeaponToBone` on `Monster.OffHandItemBoneIndex` with an identity frame;
4. reconstruct the live main-hand grip transform from the real WeaponEntity and reuse it on the off-hand item bone;
5. establish a real native OffHand using a second already-valid melee weapon slot, without attacking;
6. only after experiment 5 succeeds, inject native attack input and observe whether the native sweep follows MainHand or OffHand.

Experiment 5 intentionally requires a second melee weapon already present in another equipment slot. It does not construct a temporary ItemObject and does not call EquipWeaponWithNewEntity.
