using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal static class LeftHandAttackRuntime
    {
        private sealed class VisualCloneState
        {
            public Agent Agent;
            public Skeleton Skeleton;
            public sbyte BoneIndex;
            public MetaMesh MetaMesh;
            public float ExpireAt;
        }

        private sealed class TemporaryOffhandState
        {
            public Agent Agent;
            public EquipmentIndex Slot;
            public ItemObject CloneItem;
        }

        private static readonly List<VisualCloneState> _visualClones = new List<VisualCloneState>();
        private static TemporaryOffhandState _temporaryOffhand;

        // A SkillBase.Activate call can happen outside the exact input collection point.
        // Queue the request and inject it from IPlayerInputEffector on the next collection.
        private static Agent _queuedNativeAttackAgent;
        private static string _queuedNativeAttackSkillId;
        private static Agent.MovementControlFlag _queuedNativeAttackFlag;

        private static Agent _pendingAttackAgent;
        private static string _pendingAttackSkillId;
        private static float _pendingAttackUntil;
        private static string _lastObservedAttackState;

        private static Agent _offhandProbeAgent;
        private static float _offhandProbeUntil;
        private static string _lastOffhandProbeState;

        public static bool TryGetActiveMeleeWeapon(Agent agent, out MissionWeapon weapon, out string error)
        {
            weapon = MissionWeapon.Invalid;
            error = null;

            if (agent == null || agent.State != AgentState.Active)
            {
                error = "Agent 不可用";
                LeftHandSwardLog.Warn("Weapon", error);
                return false;
            }

            weapon = agent.WieldedWeapon;
            if (weapon.IsEmpty || weapon.Item == null || weapon.CurrentUsageItem == null)
            {
                error = "当前没有有效主手武器";
                LeftHandSwardLog.Warn("Weapon", error);
                return false;
            }

            if (!weapon.CurrentUsageItem.IsMeleeWeapon)
            {
                error = "当前主手武器不是近战武器";
                LeftHandSwardLog.Warn("Weapon", error + " item=" + weapon.Item.StringId);
                return false;
            }

            LeftHandSwardLog.Info(
                "Weapon",
                "Active melee weapon item=" + weapon.Item.StringId
                + " usage=" + weapon.CurrentUsageItem.WeaponClass
                + " agent=" + SafeAgentName(agent)
                + " hands={" + DescribeHands(agent) + "}");
            return true;
        }

        public static bool QueueNativeAttack(
            Agent agent,
            string skillId,
            Agent.MovementControlFlag attackFlag,
            out string result)
        {
            if (!TryGetActiveMeleeWeapon(agent, out _, out string error))
            {
                result = error;
                return false;
            }

            if (agent.Mission == null || agent.Mission.MainAgent != agent)
            {
                result = "当前原生输入实验只支持 MainAgent";
                LeftHandSwardLog.Warn("NativeAttack", result + " skill=" + skillId);
                return false;
            }

            _queuedNativeAttackAgent = agent;
            _queuedNativeAttackSkillId = skillId;
            _queuedNativeAttackFlag = attackFlag;

            result = "已排队原生攻击输入: " + attackFlag;
            LeftHandSwardLog.Info(
                "NativeAttack",
                "QUEUED skill=" + skillId
                + " flag=" + attackFlag
                + " state={" + DescribeAgentAction(agent) + "}"
                + " hands={" + DescribeHands(agent) + "}");
            return true;
        }

        public static Agent.EventControlFlag CollectPlayerInput(Mission mission)
        {
            if (_queuedNativeAttackAgent == null || string.IsNullOrEmpty(_queuedNativeAttackSkillId))
                return Agent.EventControlFlag.None;

            Agent agent = _queuedNativeAttackAgent;
            string skillId = _queuedNativeAttackSkillId;
            Agent.MovementControlFlag attackFlag = _queuedNativeAttackFlag;

            _queuedNativeAttackAgent = null;
            _queuedNativeAttackSkillId = null;
            _queuedNativeAttackFlag = Agent.MovementControlFlag.None;

            if (mission == null || mission.MainAgent != agent || agent.State != AgentState.Active)
            {
                LeftHandSwardLog.Warn(
                    "NativeAttack",
                    "DROP queued input skill=" + skillId + " because MainAgent/Agent state is invalid");
                return Agent.EventControlFlag.None;
            }

            if (!TryGetActiveMeleeWeapon(agent, out _, out string error))
            {
                LeftHandSwardLog.Warn(
                    "NativeAttack",
                    "DROP queued input skill=" + skillId + " reason=" + error);
                return Agent.EventControlFlag.None;
            }

            LeftHandSwardLog.Info(
                "NativeAttack",
                "INJECT BEGIN skill=" + skillId
                + " flag=" + attackFlag
                + " beforeFlags=" + agent.MovementFlags
                + " state={" + DescribeAgentAction(agent) + "}"
                + " hands={" + DescribeHands(agent) + "}");

            // This is the same entry point that was already proven by
            // NativeMeleeCollisionTestBehavior: MissionMainAgentController clears
            // MovementFlags first, then gathers IPlayerInputEffector input.
            agent.MovementFlags |= attackFlag;

            LeftHandSwardLog.Info(
                "NativeAttack",
                "INJECT RETURN skill=" + skillId
                + " movementFlags=" + agent.MovementFlags);

            BeginMeleeObservation(agent, skillId, 2.5f);
            return Agent.EventControlFlag.None;
        }

        public static bool AddLeftHandVisualClone(Agent agent, float lifetimeSeconds, out string result)
        {
            LeftHandSwardLog.Info(
                "VisualClone",
                "BEGIN agent=" + SafeAgentName(agent) + " lifetime=" + lifetimeSeconds);

            if (!TryGetActiveMeleeWeapon(agent, out MissionWeapon weapon, out string error))
            {
                result = error;
                return false;
            }

            if (agent.AgentVisuals == null)
            {
                result = "AgentVisuals=null";
                LeftHandSwardLog.Warn("VisualClone", result);
                return false;
            }

            string meshName = weapon.Item.MultiMeshName;
            if (string.IsNullOrEmpty(meshName))
            {
                result = "当前物品没有 MultiMeshName";
                LeftHandSwardLog.Warn("VisualClone", result);
                return false;
            }

            Skeleton skeleton = agent.AgentVisuals.GetSkeleton();
            if (skeleton == null || !skeleton.IsValid)
            {
                result = "Skeleton 无效";
                LeftHandSwardLog.Warn("VisualClone", result);
                return false;
            }

            sbyte leftHandBone = FindBoneIndex(skeleton, "l_hand");
            if (leftHandBone < 0)
            {
                result = "找不到 l_hand 骨骼";
                LeftHandSwardLog.Warn("VisualClone", result);
                return false;
            }

            RemoveVisualClone(agent);

            LeftHandSwardLog.Info("VisualClone", "CALL MetaMesh.GetCopy BEGIN mesh=" + meshName);
            MetaMesh clone = MetaMesh.GetCopy(meshName, false, true);
            LeftHandSwardLog.Info(
                "VisualClone",
                "CALL MetaMesh.GetCopy RETURN clone=" + (clone == null ? "null" : "non-null")
                + " valid=" + (clone != null && clone.IsValid));

            if (clone == null || !clone.IsValid)
            {
                result = "无法复制武器 MetaMesh: " + meshName;
                return false;
            }

            LeftHandSwardLog.Info(
                "VisualClone",
                "CALL Skeleton.AddComponentToBone BEGIN bone=" + leftHandBone + " mesh=" + meshName);
            skeleton.AddComponentToBone(leftHandBone, clone);
            LeftHandSwardLog.Info(
                "VisualClone",
                "CALL Skeleton.AddComponentToBone RETURN bone=" + leftHandBone + " mesh=" + meshName);

            _visualClones.Add(new VisualCloneState
            {
                Agent = agent,
                Skeleton = skeleton,
                BoneIndex = leftHandBone,
                MetaMesh = clone,
                ExpireAt = (agent.Mission != null ? agent.Mission.CurrentTime : 0f)
                           + Math.Max(0.25f, lifetimeSeconds)
            });

            result = "已复制视觉模型到 l_hand: " + meshName + " bone=" + leftHandBone;
            LeftHandSwardLog.Info("VisualClone", "SUCCESS " + result);
            return true;
        }

        public static bool EquipTemporaryOffhandClone(Agent agent, out string result)
        {
            if (!TryGetActiveMeleeWeapon(agent, out MissionWeapon sourceWeapon, out string error))
            {
                result = error;
                return false;
            }

            RemoveTemporaryOffhand(agent);

            EquipmentIndex targetSlot = FindEmptyWeaponSlot(agent);
            if (targetSlot == EquipmentIndex.None)
            {
                result = "没有空武器槽可用于临时副手剑";
                LeftHandSwardLog.Warn("OffhandEquip", result);
                return false;
            }

            ItemObject sourceItem = sourceWeapon.Item;
            ItemObject cloneItem = new ItemObject(sourceItem)
            {
                // Keep the same native WeaponKind. The clone only exists in managed
                // mission state; it is not registered as a new campaign item.
                Id = sourceItem.Id,
                StringId = sourceItem.StringId + "_lhs_offhand_runtime"
            };

            cloneItem.Initialize();
            cloneItem.IsReady = true;
            cloneItem.SetItemFlagsForCosmetics(
                sourceItem.ItemFlags | ItemFlags.HeldInOffHand);

            MissionWeapon offhandWeapon = new MissionWeapon(
                cloneItem,
                sourceWeapon.ItemModifier,
                sourceWeapon.Banner);

            if (sourceWeapon.CurrentUsageIndex >= 0 &&
                sourceWeapon.CurrentUsageIndex < offhandWeapon.WeaponsCount)
            {
                offhandWeapon.CurrentUsageIndex = sourceWeapon.CurrentUsageIndex;
            }

            offhandWeapon.ReloadPhase = sourceWeapon.ReloadPhase;

            LeftHandSwardLog.Info(
                "OffhandEquip",
                "CALL EquipWeaponWithNewEntity BEGIN"
                + " slot=" + targetSlot
                + " sourceItem=" + sourceItem.StringId
                + " sourceFlags=" + sourceItem.ItemFlags
                + " cloneFlags=" + cloneItem.ItemFlags
                + " usage=" + offhandWeapon.CurrentUsageItem.WeaponClass
                + " before={" + DescribeHands(agent) + "}");

            agent.EquipWeaponWithNewEntity(targetSlot, ref offhandWeapon);

            LeftHandSwardLog.Info(
                "OffhandEquip",
                "CALL EquipWeaponWithNewEntity RETURN"
                + " slot=" + targetSlot
                + " state={" + DescribeHands(agent) + "}");

            LeftHandSwardLog.Info(
                "OffhandEquip",
                "CALL TryToWieldWeaponInSlot BEGIN slot=" + targetSlot);

            agent.TryToWieldWeaponInSlot(
                targetSlot,
                Agent.WeaponWieldActionType.Instant,
                false);

            LeftHandSwardLog.Info(
                "OffhandEquip",
                "CALL TryToWieldWeaponInSlot RETURN"
                + " slot=" + targetSlot
                + " state={" + DescribeHands(agent) + "}");

            _temporaryOffhand = new TemporaryOffhandState
            {
                Agent = agent,
                Slot = targetSlot,
                CloneItem = cloneItem
            };

            _offhandProbeAgent = agent;
            _offhandProbeUntil = agent.Mission.CurrentTime + 2f;
            _lastOffhandProbeState = null;

            result = "临时副手剑已装备到 " + targetSlot
                     + "；等待 native 确认 offhandSlot";
            return true;
        }

        public static void RemoveTemporaryOffhand(Agent agent)
        {
            TemporaryOffhandState state = _temporaryOffhand;
            if (state == null)
                return;

            if (agent != null && state.Agent != agent)
                return;

            _temporaryOffhand = null;

            Agent owner = state.Agent;
            if (owner == null || owner.State != AgentState.Active)
                return;

            try
            {
                MissionWeapon equipped = owner.Equipment[state.Slot];
                if (equipped.Item != state.CloneItem)
                    return;

                LeftHandSwardLog.Info(
                    "OffhandEquip",
                    "REMOVE BEGIN slot=" + state.Slot
                    + " state={" + DescribeHands(owner) + "}");

                if (owner.GetOffhandWieldedItemIndex() == state.Slot)
                {
                    owner.TryToSheathWeaponInHand(
                        Agent.HandIndex.OffHand,
                        Agent.WeaponWieldActionType.Instant);
                }

                owner.RemoveEquippedWeapon(state.Slot);

                LeftHandSwardLog.Info(
                    "OffhandEquip",
                    "REMOVE RETURN slot=" + state.Slot
                    + " state={" + DescribeHands(owner) + "}");
            }
            catch (Exception ex)
            {
                LeftHandSwardLog.Exception("OffhandEquip", ex);
            }
        }

        public static bool ProbeOffhand(Agent agent, bool cycleNativeOffhand, out string result)
        {
            if (agent == null || agent.State != AgentState.Active || agent.Mission == null)
            {
                result = "Agent 不可用";
                return false;
            }

            string before = DescribeHands(agent);
            LeftHandSwardLog.Info(
                "OffhandProbe",
                "BEGIN cycle=" + cycleNativeOffhand + " before={" + before + "}");

            if (cycleNativeOffhand)
            {
                // This is the exact native API used by MissionMainAgentController for
                // the game's "wield next offhand weapon" input. No custom action name,
                // animation flag, item flag mutation or unmanaged patch is involved.
                agent.WieldNextWeapon(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);
            }

            string after = DescribeHands(agent);
            LeftHandSwardLog.Info(
                "OffhandProbe",
                "CALL RETURN cycle=" + cycleNativeOffhand + " after={" + after + "}");

            _offhandProbeAgent = agent;
            _offhandProbeUntil = agent.Mission.CurrentTime + 1.5f;
            _lastOffhandProbeState = null;

            result = "副手探针 before={" + before + "} after={" + after + "}";
            return true;
        }

        public static void Tick(Mission mission)
        {
            if (mission == null)
                return;

            for (int i = _visualClones.Count - 1; i >= 0; i--)
            {
                VisualCloneState state = _visualClones[i];
                if (state.Agent == null ||
                    state.Agent.State != AgentState.Active ||
                    mission.CurrentTime >= state.ExpireAt)
                {
                    RemoveVisualCloneAt(i);
                }
            }

            if (_pendingAttackAgent != null && _pendingAttackSkillId != null)
            {
                if (_pendingAttackAgent.State != AgentState.Active)
                {
                    ClearMeleeObservation();
                }
                else
                {
                    string state = DescribeAttackObservationState(_pendingAttackAgent);
                    if (!string.Equals(state, _lastObservedAttackState, StringComparison.Ordinal))
                    {
                        _lastObservedAttackState = state;
                        LeftHandSwardLog.Info(
                            "NativeAttackState",
                            "skill=" + _pendingAttackSkillId + " {" + state + "}");
                    }

                    if (mission.CurrentTime > _pendingAttackUntil)
                    {
                        LeftHandSwardLog.Warn(
                            "MeleeObservation",
                            _pendingAttackSkillId + " timeout: no OnMeleeHit");
                        Report(_pendingAttackSkillId + " 观察窗口结束：未收到 OnMeleeHit");
                        ClearMeleeObservation();
                    }
                }
            }

            if (_offhandProbeAgent != null)
            {
                if (_offhandProbeAgent.State != AgentState.Active ||
                    mission.CurrentTime > _offhandProbeUntil)
                {
                    LeftHandSwardLog.Info(
                        "OffhandProbe",
                        "END state={" + DescribeHands(_offhandProbeAgent) + "}");
                    _offhandProbeAgent = null;
                    _offhandProbeUntil = 0f;
                    _lastOffhandProbeState = null;
                }
                else
                {
                    string state = DescribeHands(_offhandProbeAgent);
                    if (!string.Equals(state, _lastOffhandProbeState, StringComparison.Ordinal))
                    {
                        _lastOffhandProbeState = state;
                        LeftHandSwardLog.Info("OffhandProbe", "STATE {" + state + "}");
                    }
                }
            }
        }

        public static void Cleanup()
        {
            LeftHandSwardLog.Info(
                "Runtime",
                "Cleanup visualClones=" + _visualClones.Count
                + " queuedSkill=" + (_queuedNativeAttackSkillId ?? "null")
                + " pendingSkill=" + (_pendingAttackSkillId ?? "null"));

            for (int i = _visualClones.Count - 1; i >= 0; i--)
                RemoveVisualCloneAt(i);

            RemoveTemporaryOffhand(null);

            _queuedNativeAttackAgent = null;
            _queuedNativeAttackSkillId = null;
            _queuedNativeAttackFlag = Agent.MovementControlFlag.None;

            ClearMeleeObservation();

            _offhandProbeAgent = null;
            _offhandProbeUntil = 0f;
            _lastOffhandProbeState = null;
        }

        public static void RemoveVisualClone(Agent agent)
        {
            if (agent == null)
                return;

            for (int i = _visualClones.Count - 1; i >= 0; i--)
            {
                if (_visualClones[i].Agent == agent)
                    RemoveVisualCloneAt(i);
            }
        }

        public static void BeginMeleeObservation(Agent agent, string skillId, float seconds = 2f)
        {
            if (agent == null || agent.Mission == null || string.IsNullOrEmpty(skillId))
            {
                LeftHandSwardLog.Warn(
                    "MeleeObservation",
                    "Begin skipped skill=" + (skillId ?? "null"));
                return;
            }

            _pendingAttackAgent = agent;
            _pendingAttackSkillId = skillId;
            _pendingAttackUntil = agent.Mission.CurrentTime + Math.Max(0.25f, seconds);
            _lastObservedAttackState = null;

            LeftHandSwardLog.Info(
                "MeleeObservation",
                "Begin skill=" + skillId
                + " until=" + _pendingAttackUntil
                + " agent=" + SafeAgentName(agent));
        }

        public static void NotifyMeleeHit(
            Agent attacker,
            Agent victim,
            bool isCanceled,
            AttackCollisionData collisionData)
        {
            if (_pendingAttackAgent == null || _pendingAttackSkillId == null)
                return;

            if (attacker != _pendingAttackAgent)
                return;

            LeftHandSwardLog.Info(
                "MeleeHit",
                "skill=" + _pendingAttackSkillId
                + " canceled=" + isCanceled
                + " dir=" + collisionData.AttackDirection
                + " progress=" + collisionData.AttackProgress
                + " attacker=" + SafeAgentName(attacker)
                + " victim=" + SafeAgentName(victim)
                + " hands={" + DescribeHands(attacker) + "}");

            Report(
                _pendingAttackSkillId
                + " 命中 native melee"
                + " canceled=" + isCanceled
                + " dir=" + collisionData.AttackDirection
                + " progress=" + collisionData.AttackProgress
                + " victim=" + SafeAgentName(victim));

            ClearMeleeObservation();
        }

        public static void Report(string message)
        {
            LeftHandSwardLog.Info("Report", message);
            Debug.Print("[LeftHandSward] " + message);
            InformationManager.DisplayMessage(
                new InformationMessage("[左手攻击扩展] " + message));
        }

        public static void TraceSkillActivation(string skillId, Agent agent)
        {
            LeftHandSwardLog.Info(
                "SkillActivate",
                "skill=" + (skillId ?? "null")
                + " agent=" + SafeAgentName(agent)
                + " state={" + DescribeAgentAction(agent) + "}"
                + " hands={" + DescribeHands(agent) + "}");
        }

        private static void ClearMeleeObservation()
        {
            _pendingAttackAgent = null;
            _pendingAttackSkillId = null;
            _pendingAttackUntil = 0f;
            _lastObservedAttackState = null;
        }

        private static void RemoveVisualCloneAt(int index)
        {
            VisualCloneState state = _visualClones[index];

            try
            {
                if (state.Skeleton != null && state.MetaMesh != null && state.MetaMesh.IsValid)
                {
                    LeftHandSwardLog.Info(
                        "VisualClone",
                        "CALL Skeleton.RemoveBoneComponent BEGIN bone=" + state.BoneIndex);
                    state.Skeleton.RemoveBoneComponent(state.BoneIndex, state.MetaMesh);
                    LeftHandSwardLog.Info(
                        "VisualClone",
                        "CALL Skeleton.RemoveBoneComponent RETURN bone=" + state.BoneIndex);
                }
            }
            catch (Exception ex)
            {
                LeftHandSwardLog.Exception("VisualClone", ex);
            }

            _visualClones.RemoveAt(index);
            LeftHandSwardLog.Info("VisualClone", "Removed clone index=" + index);
        }

        private static string DescribeAttackObservationState(Agent agent)
        {
            if (agent == null)
                return "agent=null";

            try
            {
                return DescribeAgentAction(agent)
                       + " animFlags=" + agent.GetCurrentAnimationFlag(1)
                       + " movementFlags=" + agent.MovementFlags
                       + " hands={" + DescribeHands(agent) + "}";
            }
            catch (Exception ex)
            {
                return "observe-failed=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string DescribeAgentAction(Agent agent)
        {
            if (agent == null)
                return "agent=null";

            try
            {
                return "name=" + SafeAgentName(agent)
                       + " agentState=" + agent.State
                       + " actionType=" + agent.GetCurrentActionType(1)
                       + " actionStage=" + agent.GetCurrentActionStage(1)
                       + " actionDirection=" + agent.GetCurrentActionDirection(1)
                       + " actionProgress=" + agent.GetCurrentActionProgress(1)
                       + " attackDir=" + agent.AttackDirection;
            }
            catch (Exception ex)
            {
                return "describe-failed=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string DescribeHands(Agent agent)
        {
            if (agent == null)
                return "agent=null";

            try
            {
                EquipmentIndex primaryIndex = agent.GetPrimaryWieldedItemIndex();
                EquipmentIndex offhandIndex = agent.GetOffhandWieldedItemIndex();
                WeaponInfo mainInfo = agent.GetWieldedWeaponInfo(Agent.HandIndex.MainHand);
                WeaponInfo offInfo = agent.GetWieldedWeaponInfo(Agent.HandIndex.OffHand);

                return "primarySlot=" + primaryIndex
                       + " offhandSlot=" + offhandIndex
                       + " mainInfo=[valid=" + mainInfo.IsValid
                       + ",melee=" + mainInfo.IsMeleeWeapon
                       + ",ranged=" + mainInfo.IsRangedWeapon + "]"
                       + " offInfo=[valid=" + offInfo.IsValid
                       + ",melee=" + offInfo.IsMeleeWeapon
                       + ",ranged=" + offInfo.IsRangedWeapon + "]"
                       + " mainWeapon=" + DescribeMissionWeapon(agent.WieldedWeapon)
                       + " offWeapon=" + DescribeMissionWeapon(agent.WieldedOffhandWeapon);
            }
            catch (Exception ex)
            {
                return "hands-failed=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string DescribeMissionWeapon(MissionWeapon weapon)
        {
            if (weapon.IsEmpty || weapon.Item == null || weapon.CurrentUsageItem == null)
                return "<empty>";

            return weapon.Item.StringId + "/" + weapon.CurrentUsageItem.WeaponClass;
        }

        private static string SafeAgentName(Agent agent)
        {
            if (agent == null)
                return "null";

            try
            {
                return agent.Name == null ? "<unnamed>" : agent.Name.ToString();
            }
            catch
            {
                return "<name-error>";
            }
        }

        private static EquipmentIndex FindEmptyWeaponSlot(Agent agent)
        {
            if (agent == null || agent.Equipment == null)
                return EquipmentIndex.None;

            for (EquipmentIndex slot = EquipmentIndex.WeaponItemBeginSlot;
                 slot < EquipmentIndex.ExtraWeaponSlot;
                 slot++)
            {
                if (agent.Equipment[slot].IsEmpty)
                    return slot;
            }

            if (agent.Equipment[EquipmentIndex.ExtraWeaponSlot].IsEmpty)
                return EquipmentIndex.ExtraWeaponSlot;

            return EquipmentIndex.None;
        }

        private static sbyte FindBoneIndex(Skeleton skeleton, string boneName)
        {
            sbyte count = skeleton.GetBoneCount();
            for (sbyte i = 0; i < count; i++)
            {
                if (string.Equals(
                    skeleton.GetBoneName(i),
                    boneName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
