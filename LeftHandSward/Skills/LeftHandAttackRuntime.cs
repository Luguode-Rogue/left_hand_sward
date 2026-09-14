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
            public List<MetaMesh> MetaMeshes;
            public float ExpireAt;
        }

        private sealed class NativeBoneAttachmentState
        {
            public Agent Agent;
            public int AttachedWeaponIndex;
            public sbyte BoneIndex;
            public float ExpireAt;
        }

        private sealed class CustomLeftHandAttackState
        {
            public Agent Agent;
            public MissionWeapon Weapon;
            public EquipmentIndex SourceSlot;
            public string SkillId;
            public float StartedAt;
            public float SweepStartAt;
            public float SweepEndAt;
            public bool HasPreviousTip;
            public Vec3 PreviousTip;
            public bool HitRegistered;
        }

        private static readonly List<VisualCloneState> _visualClones = new List<VisualCloneState>();
        private static readonly List<NativeBoneAttachmentState> _nativeBoneAttachments = new List<NativeBoneAttachmentState>();
        private static CustomLeftHandAttackState _customLeftHandAttack;

        private static Agent _offHandProbeAgent;
        private static EquipmentIndex _offHandProbePreviousIndex = EquipmentIndex.None;
        private static EquipmentIndex _offHandProbeCurrentIndex = EquipmentIndex.None;

        // A SkillBase.Activate call can happen outside the exact input collection point.
        // Queue the request and inject it from IPlayerInputEffector on the next collection.
        private static Agent _queuedNativeAttackAgent;
        private static string _queuedNativeAttackSkillId;
        private static Agent.MovementControlFlag _queuedNativeAttackFlag;

        private static Agent _pendingAttackAgent;
        private static string _pendingAttackSkillId;
        private static float _pendingAttackUntil;
        private static string _lastObservedAttackState;

        private static Agent _leftHandRewriteAgent;
        private static string _leftHandRewriteSkillId;
        private static EquipmentIndex _leftHandRewritePrimarySlot = EquipmentIndex.None;
        private static EquipmentIndex _leftHandRewriteOffHandSlot = EquipmentIndex.None;
        private static float _leftHandRewriteUntil;
        private static bool _leftHandRewriteApplied;

        private static bool _deferredOffHandRestorePending;
        private static float _deferredOffHandRestoreAt;
        private static string _deferredOffHandRestoreReason;


        public static bool TryGetActiveWeapon(Agent agent, out MissionWeapon weapon, out string error)
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

            LeftHandSwardLog.Info(
                "Weapon",
                "Active weapon item=" + weapon.Item.StringId
                + " usage=" + weapon.CurrentUsageItem.WeaponClass
                + " melee=" + weapon.CurrentUsageItem.IsMeleeWeapon
                + " ranged=" + weapon.CurrentUsageItem.IsRangedWeapon
                + " agent=" + SafeAgentName(agent)
                + " hands={" + DescribeHands(agent) + "}");
            return true;
        }

        public static bool TryGetActiveMeleeWeapon(Agent agent, out MissionWeapon weapon, out string error)
        {
            if (!TryGetActiveWeapon(agent, out weapon, out error))
                return false;

            if (!weapon.CurrentUsageItem.IsMeleeWeapon)
            {
                error = "当前主手武器不是近战武器";
                LeftHandSwardLog.Warn("Weapon", error + " item=" + weapon.Item.StringId);
                return false;
            }

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

            if (!TryGetActiveWeaponForVisual(agent, out MissionWeapon weapon, out EquipmentIndex weaponSlot, out string error))
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

            WeakGameEntity weaponEntity = agent.GetWeaponEntityFromEquipmentSlot(weaponSlot);
            if (!weaponEntity.IsValid)
            {
                result = "当前武器实体无效: slot=" + weaponSlot;
                LeftHandSwardLog.Warn("VisualClone", result);
                return false;
            }

            LeftHandSwardLog.Info(
                "VisualClone",
                "Source entity"
                + " slot=" + weaponSlot
                + " name=" + SafeEntityName(weaponEntity)
                + " rootMetaMeshes=" + weaponEntity.MultiMeshComponentCount
                + " children=" + weaponEntity.ChildCount
                + " item=" + (weapon.Item == null ? "null" : weapon.Item.StringId)
                + " usage=" + (weapon.CurrentUsageItem == null ? "null" : weapon.CurrentUsageItem.WeaponClass.ToString()));

            RemoveVisualClone(agent);

            List<MetaMesh> clones = new List<MetaMesh>();
            try
            {
                CollectWeaponMetaMeshCopies(
                    weaponEntity,
                    MatrixFrame.Identity,
                    clones,
                    "root");

                if (clones.Count == 0)
                {
                    result = "当前武器实体没有可复制的 MetaMesh";
                    LeftHandSwardLog.Warn("VisualClone", result);
                    return false;
                }

                for (int i = 0; i < clones.Count; i++)
                {
                    MetaMesh clone = clones[i];
                    LeftHandSwardLog.Info(
                        "VisualClone",
                        "CALL Skeleton.AddComponentToBone BEGIN"
                        + " bone=" + leftHandBone
                        + " index=" + i
                        + " mesh=" + SafeMetaMeshName(clone));

                    skeleton.AddComponentToBone(leftHandBone, clone);

                    LeftHandSwardLog.Info(
                        "VisualClone",
                        "CALL Skeleton.AddComponentToBone RETURN"
                        + " bone=" + leftHandBone
                        + " index=" + i);
                }
            }
            catch (Exception ex)
            {
                LeftHandSwardLog.Exception("VisualClone", ex);

                for (int i = 0; i < clones.Count; i++)
                {
                    try
                    {
                        MetaMesh clone = clones[i];
                        if (clone != null && clone.IsValid && skeleton.HasBoneComponent(leftHandBone, clone))
                            skeleton.RemoveBoneComponent(leftHandBone, clone);
                    }
                    catch
                    {
                    }
                }

                result = "复制当前武器实体失败: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }

            _visualClones.Add(new VisualCloneState
            {
                Agent = agent,
                Skeleton = skeleton,
                BoneIndex = leftHandBone,
                MetaMeshes = clones,
                ExpireAt = (agent.Mission != null ? agent.Mission.CurrentTime : 0f)
                           + Math.Max(0.25f, lifetimeSeconds)
            });

            result = "已从当前武器实体复制 " + clones.Count + " 个 MetaMesh 到 l_hand";
            LeftHandSwardLog.Info("VisualClone", "SUCCESS " + result);
            return true;
        }

        public static bool AttachCurrentWeaponToLeftItemBone(
            Agent agent,
            bool copyMainHandGrip,
            float lifetimeSeconds,
            out string result)
        {
            string mode = copyMainHandGrip ? "RightGripTransform" : "Identity";
            LeftHandSwardLog.Info(
                "NativeBoneAttach",
                "BEGIN mode=" + mode + " agent=" + SafeAgentName(agent));

            if (!TryGetActiveWeaponForVisual(
                    agent,
                    out MissionWeapon weapon,
                    out EquipmentIndex weaponSlot,
                    out string error))
            {
                result = error;
                return false;
            }

            if (agent.AgentVisuals == null || agent.Monster == null)
            {
                result = "AgentVisuals/Monster 不可用";
                LeftHandSwardLog.Warn("NativeBoneAttach", result);
                return false;
            }

            Skeleton skeleton = agent.AgentVisuals.GetSkeleton();
            if (skeleton == null || !skeleton.IsValid)
            {
                result = "Skeleton 无效";
                LeftHandSwardLog.Warn("NativeBoneAttach", result);
                return false;
            }

            sbyte leftItemBone = agent.Monster.OffHandItemBoneIndex;
            sbyte rightItemBone = agent.Monster.MainHandItemBoneIndex;
            if (leftItemBone < 0)
            {
                result = "Monster.OffHandItemBoneIndex 无效";
                LeftHandSwardLog.Warn("NativeBoneAttach", result);
                return false;
            }

            MatrixFrame attachLocalFrame = MatrixFrame.Identity;

            if (copyMainHandGrip)
            {
                WeakGameEntity weaponEntity = agent.GetWeaponEntityFromEquipmentSlot(weaponSlot);
                if (!weaponEntity.IsValid)
                {
                    result = "当前主手 WeaponEntity 无效";
                    LeftHandSwardLog.Warn("NativeBoneAttach", result);
                    return false;
                }

                if (rightItemBone < 0)
                {
                    result = "Monster.MainHandItemBoneIndex 无效";
                    LeftHandSwardLog.Warn("NativeBoneAttach", result);
                    return false;
                }

                skeleton.ForceUpdateBoneFrames();

                MatrixFrame visualsGlobal = agent.AgentVisuals.GetGlobalFrame();
                MatrixFrame rightBoneWorld =
                    visualsGlobal * skeleton.GetBoneEntitialFrameWithIndex(rightItemBone);
                MatrixFrame weaponWorld = weaponEntity.GetGlobalFrame();

                attachLocalFrame =
                    rightBoneWorld.TransformToLocalNonOrthogonal(weaponWorld);

                LeftHandSwardLog.Info(
                    "NativeBoneAttach",
                    "GripFrames"
                    + " rightBone=" + rightItemBone
                    + " leftBone=" + leftItemBone
                    + " rightBoneWorld={" + DescribeFrame(rightBoneWorld) + "}"
                    + " weaponWorld={" + DescribeFrame(weaponWorld) + "}"
                    + " attachLocal={" + DescribeFrame(attachLocalFrame) + "}");
            }

            RemoveNativeBoneAttachment(agent);

            int beforeCount = agent.GetAttachedWeaponsCount();
            LeftHandSwardLog.Info(
                "NativeBoneAttach",
                "CALL Agent.AttachWeaponToBone BEGIN"
                + " mode=" + mode
                + " beforeCount=" + beforeCount
                + " bone=" + leftItemBone
                + " item=" + (weapon.Item == null ? "null" : weapon.Item.StringId)
                + " local={" + DescribeFrame(attachLocalFrame) + "}");

            agent.AttachWeaponToBone(
                weapon,
                null,
                leftItemBone,
                ref attachLocalFrame);

            int afterCount = agent.GetAttachedWeaponsCount();
            LeftHandSwardLog.Info(
                "NativeBoneAttach",
                "CALL Agent.AttachWeaponToBone RETURN"
                + " mode=" + mode
                + " afterCount=" + afterCount
                + " hands={" + DescribeHands(agent) + "}");

            if (afterCount <= beforeCount)
            {
                result = "AttachWeaponToBone 返回后 attached weapon 数量未增加";
                LeftHandSwardLog.Warn("NativeBoneAttach", result);
                return false;
            }

            _nativeBoneAttachments.Add(new NativeBoneAttachmentState
            {
                Agent = agent,
                AttachedWeaponIndex = beforeCount,
                BoneIndex = leftItemBone,
                ExpireAt = (agent.Mission != null ? agent.Mission.CurrentTime : 0f)
                           + Math.Max(0.25f, lifetimeSeconds)
            });

            result = copyMainHandGrip
                ? "已用右手真实握持变换挂到 OffHandItemBone"
                : "已用 Identity frame 挂到 OffHandItemBone";
            LeftHandSwardLog.Info("NativeBoneAttach", "SUCCESS mode=" + mode + " " + result);
            return true;
        }

        public static bool EstablishOffHandStateFromExistingWeapon(
            Agent agent,
            out string result)
        {
            LeftHandSwardLog.Info(
                "OffHandProbe",
                "BEGIN agent=" + SafeAgentName(agent)
                + " hands={" + DescribeHands(agent) + "}");

            if (agent == null || agent.State != AgentState.Active)
            {
                result = "Agent 不可用";
                LeftHandSwardLog.Warn("OffHandProbe", result);
                return false;
            }

            if (agent.Mission == null || agent.Mission.MainAgent != agent)
            {
                result = "OffHand 状态实验只支持 MainAgent";
                LeftHandSwardLog.Warn("OffHandProbe", result);
                return false;
            }

            EquipmentIndex primaryIndex = agent.GetPrimaryWieldedItemIndex();
            if (primaryIndex == EquipmentIndex.None)
            {
                result = "当前没有主手武器";
                LeftHandSwardLog.Warn("OffHandProbe", result);
                return false;
            }

            EquipmentIndex currentOffHand = agent.GetOffhandWieldedItemIndex();
            if (currentOffHand != EquipmentIndex.None)
            {
                WeaponInfo existingInfo = agent.GetWieldedWeaponInfo(Agent.HandIndex.OffHand);
                if (_offHandProbeAgent == agent && existingInfo.IsValid)
                {
                    result = "OffHand probe 已建立: slot=" + currentOffHand;
                    LeftHandSwardLog.Info(
                        "OffHandProbe",
                        result + " hands={" + DescribeHands(agent) + "}");
                    return true;
                }

                result = "当前已经有原生 OffHand，本实验不会覆盖: slot=" + currentOffHand;
                LeftHandSwardLog.Warn("OffHandProbe", result);
                return false;
            }

            EquipmentIndex candidate = FindDistinctCurrentOneHandMeleeSlot(agent, primaryIndex);
            if (candidate == EquipmentIndex.None)
            {
                result = "需要在另一个武器槽装备第二把近战武器；本实验不会创建临时 ItemObject";
                LeftHandSwardLog.Warn("OffHandProbe", result);
                return false;
            }

            RestoreOffHandProbe();

            int mainUsageIndex = GetMainHandUsageIndex(agent);
            _offHandProbeAgent = agent;
            _offHandProbePreviousIndex = currentOffHand;
            _offHandProbeCurrentIndex = candidate;

            LeftHandSwardLog.Info(
                "OffHandProbe",
                "CALL SetWieldedItemIndexAsClient BEGIN"
                + " hand=OffHand"
                + " candidate=" + candidate
                + " mainUsageIndex=" + mainUsageIndex
                + " candidateWeapon=" + DescribeMissionWeapon(agent.Equipment[candidate]));

            agent.SetWieldedItemIndexAsClient(
                Agent.HandIndex.OffHand,
                candidate,
                true,
                false,
                mainUsageIndex);

            LeftHandSwardLog.Info(
                "OffHandProbe",
                "CALL SetWieldedItemIndexAsClient RETURN"
                + " hands={" + DescribeHands(agent) + "}");

            EquipmentIndex actualOffHand = agent.GetOffhandWieldedItemIndex();
            WeaponInfo offInfo = agent.GetWieldedWeaponInfo(Agent.HandIndex.OffHand);
            if (actualOffHand != candidate || !offInfo.IsValid)
            {
                result = "native 调用返回，但 OffHand 未建立"
                         + " requested=" + candidate
                         + " actual=" + actualOffHand
                         + " valid=" + offInfo.IsValid;
                LeftHandSwardLog.Warn("OffHandProbe", result);
                _offHandProbeAgent = null;
                _offHandProbePreviousIndex = EquipmentIndex.None;
                _offHandProbeCurrentIndex = EquipmentIndex.None;
                return false;
            }

            result = "OffHand 已建立: slot=" + actualOffHand
                     + " weapon=" + DescribeMissionWeapon(agent.WieldedOffhandWeapon);
            LeftHandSwardLog.Info(
                "OffHandProbe",
                "SUCCESS " + result + " hands={" + DescribeHands(agent) + "}");
            return true;
        }

        // Compatibility shim for stale local copies of the retired
        // OffHandNativeAttackSkill.cs. The old skill is no longer registered, but
        // keeping this method lets existing working trees compile cleanly until the
        // deleted source file is removed by a fresh pull/clean.
        [Obsolete("Use QueueNativeLeftHandAttack instead.")]
        public static bool QueueOffHandNativeAttack(
            Agent agent,
            string skillId,
            Agent.MovementControlFlag attackFlag,
            out string result)
        {
            LeftHandSwardLog.Warn(
                "Compatibility",
                "QueueOffHandNativeAttack called by stale source; forwarding to QueueNativeLeftHandAttack");
            return QueueNativeLeftHandAttack(
                agent,
                skillId,
                attackFlag,
                out result);
        }

        public static bool StartCustomLeftHandAttack(
            Agent agent,
            string skillId,
            out string result)
        {
            if (!TryGetActiveMeleeWeapon(agent, out MissionWeapon weapon, out string error))
            {
                result = error;
                return false;
            }

            if (agent.Mission == null || agent.Mission.MainAgent != agent)
            {
                result = "左手攻击目前只支持 MainAgent";
                LeftHandSwardLog.Warn("LeftHandCustom", result);
                return false;
            }

            if (_customLeftHandAttack != null)
            {
                result = "上一轮左手攻击尚未结束";
                LeftHandSwardLog.Warn("LeftHandCustom", result);
                return false;
            }

            // The native OffHand route is retired. Do not let a stale experimental
            // OffHand state leak into the custom attack.
            if (_leftHandRewriteAgent != null || _deferredOffHandRestorePending)
            {
                result = "旧 native 左手实验仍在恢复中，请稍后再试";
                LeftHandSwardLog.Warn("LeftHandCustom", result);
                return false;
            }

            if (_offHandProbeAgent != null)
                RestoreOffHandProbe();

            EquipmentIndex sourceSlot = agent.GetPrimaryWieldedItemIndex();
            if (sourceSlot == EquipmentIndex.None)
            {
                result = "当前没有主手武器槽";
                LeftHandSwardLog.Warn("LeftHandCustom", result);
                return false;
            }

            // Reuse the only visual path that has already been proven in-game:
            // MissionWeapon + AttachWeaponToBone + OffHandItemBone + Identity.
            if (!AttachCurrentWeaponToLeftItemBone(
                    agent,
                    false,
                    0.9f,
                    out string visualResult))
            {
                result = "左手视觉建立失败: " + visualResult;
                LeftHandSwardLog.Warn("LeftHandCustom", result);
                return false;
            }

            float now = agent.Mission.CurrentTime;

            // Use a harmless left-arm action only for presentation. This no longer
            // starts Bannerlord's melee state machine, so the right hand does not attack.
            LeftHandSwardLog.Info(
                "LeftHandCustom",
                "ACTION BEGIN skill=" + skillId
                + " visual={" + visualResult + "}"
                + " weapon=" + DescribeMissionWeapon(weapon));

            bool actionAccepted = agent.SetActionChannel(
                1,
                ActionIndexCache.act_greeting_left_1,
                true,
                (AnimFlags)0UL,
                0f,
                2.0f,
                0.05f,
                0.2f,
                0f,
                false,
                0.15f,
                0,
                false);

            LeftHandSwardLog.Info(
                "LeftHandCustom",
                "ACTION RETURN accepted=" + actionAccepted
                + " action=" + agent.GetCurrentAction(1).GetName());

            _customLeftHandAttack = new CustomLeftHandAttackState
            {
                Agent = agent,
                Weapon = weapon,
                SourceSlot = sourceSlot,
                SkillId = skillId,
                StartedAt = now,
                SweepStartAt = now + 0.08f,
                SweepEndAt = now + 0.46f,
                HasPreviousTip = false,
                PreviousTip = Vec3.Zero,
                HitRegistered = false
            };

            LeftHandSwardLog.Info(
                "LeftHandCustom",
                "SWEEP ARMED"
                + " start=" + _customLeftHandAttack.SweepStartAt
                + " end=" + _customLeftHandAttack.SweepEndAt
                + " weaponLength=" + weapon.CurrentUsageItem.WeaponLength);

            result = "左手武器已挂载；已启动自定义左手 sweep";
            return true;
        }

        public static bool QueueNativeLeftHandAttack(
            Agent agent,
            string skillId,
            Agent.MovementControlFlag attackFlag,
            out string result)
        {
            if (agent == null || agent.State != AgentState.Active)
            {
                result = "Agent 不可用";
                return false;
            }

            if (agent.Mission == null || agent.Mission.MainAgent != agent)
            {
                result = "左手原生攻击实验只支持 MainAgent";
                LeftHandSwardLog.Warn("LeftHandNative", result);
                return false;
            }

            EquipmentIndex primaryBefore = agent.GetPrimaryWieldedItemIndex();
            if (primaryBefore == EquipmentIndex.None)
            {
                result = "当前没有主手武器";
                LeftHandSwardLog.Warn("LeftHandNative", result);
                return false;
            }

            MissionWeapon mainWeapon = agent.Equipment[primaryBefore];
            if (mainWeapon.IsEmpty ||
                mainWeapon.CurrentUsageItem == null ||
                !mainWeapon.CurrentUsageItem.IsMeleeWeapon)
            {
                result = "当前主手不是有效近战武器";
                LeftHandSwardLog.Warn("LeftHandNative", result);
                return false;
            }

            if (_leftHandRewriteAgent != null ||
                _deferredOffHandRestorePending)
            {
                result = "上一轮左手攻击仍在执行/恢复中，请等待动作结束";
                LeftHandSwardLog.Warn(
                    "LeftHandNative",
                    result + " hands={" + DescribeHands(agent) + "}");
                return false;
            }

            // A stale probe can only remain here if no rewrite/restore is active.
            // Never clear it while a ReleaseMelee callback chain is in progress.
            if (_offHandProbeAgent != null)
                RestoreOffHandProbe();

            EquipmentIndex existingOffHand = agent.GetOffhandWieldedItemIndex();
            EquipmentIndex offHandSlot = EquipmentIndex.None;
            bool createdOffHand = false;

            if (existingOffHand != EquipmentIndex.None &&
                existingOffHand != primaryBefore &&
                IsCurrentUsageOneHandMelee(agent.Equipment[existingOffHand]))
            {
                offHandSlot = existingOffHand;
                LeftHandSwardLog.Info(
                    "LeftHandNative",
                    "Using existing safe OffHand slot=" + offHandSlot
                    + " weapon=" + DescribeMissionWeapon(agent.Equipment[offHandSlot]));
            }
            else
            {
                offHandSlot = FindDistinctCurrentOneHandMeleeSlot(agent, primaryBefore);
                if (offHandSlot == EquipmentIndex.None)
                {
                    result = "需要在另一个装备槽放一把当前 usage 就是单手近战的武器（建议普通单手剑/斧/锤）";
                    LeftHandSwardLog.Warn(
                        "LeftHandNative",
                        result + " hands={" + DescribeHands(agent) + "}");
                    return false;
                }

                int mainUsageIndex = GetMainHandUsageIndex(agent);
                _offHandProbeAgent = agent;
                _offHandProbePreviousIndex = existingOffHand;
                _offHandProbeCurrentIndex = offHandSlot;

                LeftHandSwardLog.Info(
                    "LeftHandNative",
                    "CALL SetWieldedItemIndexAsClient BEGIN"
                    + " primary=" + primaryBefore
                    + " offHandCandidate=" + offHandSlot
                    + " mainUsageIndex=" + mainUsageIndex
                    + " candidate=" + DescribeMissionWeapon(agent.Equipment[offHandSlot]));

                agent.SetWieldedItemIndexAsClient(
                    Agent.HandIndex.OffHand,
                    offHandSlot,
                    true,
                    false,
                    mainUsageIndex);

                LeftHandSwardLog.Info(
                    "LeftHandNative",
                    "CALL SetWieldedItemIndexAsClient RETURN"
                    + " hands={" + DescribeHands(agent) + "}");

                createdOffHand = true;
            }

            EquipmentIndex primaryAfter = agent.GetPrimaryWieldedItemIndex();
            EquipmentIndex offHandAfter = agent.GetOffhandWieldedItemIndex();
            WeaponInfo offInfo = agent.GetWieldedWeaponInfo(Agent.HandIndex.OffHand);
            MissionWeapon offWeapon = agent.WieldedOffhandWeapon;

            if (primaryAfter != primaryBefore ||
                offHandAfter == EquipmentIndex.None ||
                offHandAfter == primaryAfter ||
                offHandAfter != offHandSlot ||
                !offInfo.IsValid ||
                !offInfo.IsMeleeWeapon ||
                offWeapon.IsEmpty ||
                offWeapon.CurrentUsageItem == null ||
                !IsCurrentUsageOneHandMelee(offWeapon))
            {
                result = "OffHand 建立后状态不安全，已立即回滚"
                         + " primaryBefore=" + primaryBefore
                         + " primaryAfter=" + primaryAfter
                         + " requestedOffHand=" + offHandSlot
                         + " actualOffHand=" + offHandAfter
                         + " offInfoValid=" + offInfo.IsValid
                         + " offInfoMelee=" + offInfo.IsMeleeWeapon;

                LeftHandSwardLog.Warn(
                    "LeftHandNative",
                    result + " hands={" + DescribeHands(agent) + "}");

                if (createdOffHand)
                    RestoreOffHandProbe();
                return false;
            }

            LeftHandSwardLog.Info(
                "LeftHandNative",
                "SAFE OFFHAND READY"
                + " primary=" + primaryAfter
                + " offHand=" + offHandAfter
                + " mainWeapon=" + DescribeMissionWeapon(agent.WieldedWeapon)
                + " offWeapon=" + DescribeMissionWeapon(offWeapon));

            bool queued = QueueNativeAttack(
                agent,
                skillId,
                attackFlag,
                out string queueResult);

            if (!queued)
            {
                if (createdOffHand)
                    RestoreOffHandProbe();
                result = queueResult;
                return false;
            }

            _leftHandRewriteAgent = agent;
            _leftHandRewriteSkillId = skillId;
            _leftHandRewritePrimarySlot = primaryAfter;
            _leftHandRewriteOffHandSlot = offHandAfter;
            _leftHandRewriteUntil = agent.Mission.CurrentTime + 2.0f;
            _leftHandRewriteApplied = false;

            LeftHandSwardLog.Info(
                "LeftHandNative",
                "ARMED release rewrite"
                + " skill=" + skillId
                + " primary=" + _leftHandRewritePrimarySlot
                + " offHand=" + _leftHandRewriteOffHandSlot
                + " flag=anf_use_left_hand_during_attack");

            result = "已建立安全 OffHand 并排队原生攻击；等待 ReleaseMelee 后追加左手攻击 flag";
            return true;
        }

        public static void Tick(Mission mission)
        {
            if (mission == null)
                return;

            TickDeferredOffHandRestore(mission);
            TickNativeLeftHandRewrite(mission);
            TickCustomLeftHandAttack(mission);

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

            for (int i = _nativeBoneAttachments.Count - 1; i >= 0; i--)
            {
                NativeBoneAttachmentState state = _nativeBoneAttachments[i];
                if (state.Agent == null ||
                    state.Agent.State != AgentState.Active ||
                    mission.CurrentTime >= state.ExpireAt)
                {
                    RemoveNativeBoneAttachmentAt(i);
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


        }

        public static void Cleanup()
        {
            LeftHandSwardLog.Info(
                "Runtime",
                "Cleanup visualClones=" + _visualClones.Count
                + " nativeBoneAttachments=" + _nativeBoneAttachments.Count
                + " offHandProbe=" + (_offHandProbeAgent == null ? "null" : SafeAgentName(_offHandProbeAgent))
                + " queuedSkill=" + (_queuedNativeAttackSkillId ?? "null")
                + " pendingSkill=" + (_pendingAttackSkillId ?? "null"));

            for (int i = _visualClones.Count - 1; i >= 0; i--)
                RemoveVisualCloneAt(i);

            for (int i = _nativeBoneAttachments.Count - 1; i >= 0; i--)
                RemoveNativeBoneAttachmentAt(i);

            _queuedNativeAttackAgent = null;
            _queuedNativeAttackSkillId = null;
            _queuedNativeAttackFlag = Agent.MovementControlFlag.None;

            _customLeftHandAttack = null;
            ClearMeleeObservation();

            _deferredOffHandRestorePending = false;
            _deferredOffHandRestoreReason = null;
            RestoreOffHandProbe();
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

        public static void RemoveNativeBoneAttachment(Agent agent)
        {
            if (agent == null)
                return;

            for (int i = _nativeBoneAttachments.Count - 1; i >= 0; i--)
            {
                if (_nativeBoneAttachments[i].Agent == agent)
                    RemoveNativeBoneAttachmentAt(i);
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

        private static EquipmentIndex FindDistinctCurrentOneHandMeleeSlot(
            Agent agent,
            EquipmentIndex primaryIndex)
        {
            if (agent == null || agent.Equipment == null)
                return EquipmentIndex.None;

            for (EquipmentIndex slot = EquipmentIndex.WeaponItemBeginSlot;
                 slot < EquipmentIndex.ExtraWeaponSlot;
                 slot++)
            {
                if (slot == primaryIndex)
                    continue;

                MissionWeapon weapon = agent.Equipment[slot];
                if (!IsCurrentUsageOneHandMelee(weapon))
                    continue;

                LeftHandSwardLog.Info(
                    "LeftHandNative",
                    "Selected strict OffHand candidate"
                    + " slot=" + slot
                    + " weapon=" + DescribeMissionWeapon(weapon));
                return slot;
            }

            return EquipmentIndex.None;
        }

        private static bool IsCurrentUsageOneHandMelee(MissionWeapon weapon)
        {
            if (weapon.IsEmpty ||
                weapon.Item == null ||
                weapon.CurrentUsageItem == null ||
                !weapon.CurrentUsageItem.IsMeleeWeapon)
            {
                return false;
            }

            return !weapon.CurrentUsageItem.WeaponFlags.HasAnyFlag(
                WeaponFlags.NotUsableWithOneHand);
        }

        private static void TickNativeLeftHandRewrite(Mission mission)
        {
            Agent agent = _leftHandRewriteAgent;
            if (agent == null || string.IsNullOrEmpty(_leftHandRewriteSkillId))
                return;

            if (agent.State != AgentState.Active || mission.MainAgent != agent)
            {
                AbortNativeLeftHandRewrite("Agent/MainAgent 已失效", true);
                return;
            }

            EquipmentIndex primary = agent.GetPrimaryWieldedItemIndex();
            EquipmentIndex offHand = agent.GetOffhandWieldedItemIndex();

            if (primary != _leftHandRewritePrimarySlot ||
                offHand != _leftHandRewriteOffHandSlot ||
                primary == EquipmentIndex.None ||
                offHand == EquipmentIndex.None ||
                primary == offHand)
            {
                AbortNativeLeftHandRewrite(
                    "持武器槽在攻击过程中发生变化"
                    + " expectedPrimary=" + _leftHandRewritePrimarySlot
                    + " actualPrimary=" + primary
                    + " expectedOffHand=" + _leftHandRewriteOffHandSlot
                    + " actualOffHand=" + offHand,
                    true);
                return;
            }

            if (!_leftHandRewriteApplied &&
                agent.GetCurrentActionType(1) == Agent.ActionCodeType.ReleaseMelee)
            {
                ActionIndexCache currentAction = agent.GetCurrentAction(1);
                float progress = agent.GetCurrentActionProgress(1);
                AnimFlags beforeFlags = agent.GetCurrentAnimationFlag(1);

                LeftHandSwardLog.Info(
                    "LeftHandNative",
                    "CALL SetActionChannel BEGIN"
                    + " actionIndex=" + currentAction.Index
                    + " progress=" + progress
                    + " beforeFlags=" + beforeFlags
                    + " addFlag=anf_use_left_hand_during_attack"
                    + " hands={" + DescribeHands(agent) + "}");

                bool accepted = agent.SetActionChannel(
                    1,
                    currentAction,
                    true,
                    AnimFlags.anf_use_left_hand_during_attack,
                    0f,
                    1f,
                    0f,
                    0.4f,
                    progress,
                    false,
                    -0.2f,
                    0,
                    false);

                _leftHandRewriteApplied = true;

                LeftHandSwardLog.Info(
                    "LeftHandNative",
                    "CALL SetActionChannel RETURN"
                    + " accepted=" + accepted
                    + " afterType=" + agent.GetCurrentActionType(1)
                    + " afterStage=" + agent.GetCurrentActionStage(1)
                    + " afterFlags=" + agent.GetCurrentAnimationFlag(1)
                    + " hands={" + DescribeHands(agent) + "}");

                if (!accepted)
                {
                    AbortNativeLeftHandRewrite(
                        "native ReleaseMelee 左手 flag 被拒绝",
                        true);
                    return;
                }
            }

            if (mission.CurrentTime > _leftHandRewriteUntil)
            {
                AbortNativeLeftHandRewrite(
                    _leftHandRewriteApplied
                        ? "左手 ReleaseMelee rewrite 已执行但观察窗口结束"
                        : "native 未进入 ReleaseMelee，未执行左手 rewrite",
                    true);
            }
        }

        private static void AbortNativeLeftHandRewrite(
            string reason,
            bool restoreOffHand)
        {
            if (_leftHandRewriteAgent != null)
            {
                LeftHandSwardLog.Warn(
                    "LeftHandNative",
                    "END " + reason
                    + " applied=" + _leftHandRewriteApplied
                    + " hands={" + DescribeHands(_leftHandRewriteAgent) + "}");
            }

            _leftHandRewriteAgent = null;
            _leftHandRewriteSkillId = null;
            _leftHandRewritePrimarySlot = EquipmentIndex.None;
            _leftHandRewriteOffHandSlot = EquipmentIndex.None;
            _leftHandRewriteUntil = 0f;
            _leftHandRewriteApplied = false;

            if (restoreOffHand)
                ScheduleOffHandRestore(reason);
        }

        private static void ScheduleOffHandRestore(string reason)
        {
            if (_offHandProbeAgent == null)
                return;

            Mission mission = _offHandProbeAgent.Mission;
            _deferredOffHandRestorePending = true;
            _deferredOffHandRestoreAt = (mission == null ? 0f : mission.CurrentTime) + 0.12f;
            _deferredOffHandRestoreReason = reason;

            LeftHandSwardLog.Info(
                "LeftHandNative",
                "Deferred OffHand restore scheduled"
                + " at=" + _deferredOffHandRestoreAt
                + " reason=" + reason);
        }

        private static void TickDeferredOffHandRestore(Mission mission)
        {
            if (!_deferredOffHandRestorePending)
                return;

            Agent agent = _offHandProbeAgent;
            if (agent == null || agent.State != AgentState.Active)
            {
                _deferredOffHandRestorePending = false;
                _deferredOffHandRestoreReason = null;
                return;
            }

            if (mission.CurrentTime < _deferredOffHandRestoreAt)
                return;

            Agent.ActionCodeType actionType = agent.GetCurrentActionType(1);
            if (actionType == Agent.ActionCodeType.ReleaseMelee)
                return;

            string reason = _deferredOffHandRestoreReason;
            _deferredOffHandRestorePending = false;
            _deferredOffHandRestoreReason = null;

            LeftHandSwardLog.Info(
                "LeftHandNative",
                "Deferred OffHand restore executing"
                + " actionType=" + actionType
                + " reason=" + (reason ?? "null"));

            RestoreOffHandProbe();
        }

        private static void TickCustomLeftHandAttack(Mission mission)
        {
            CustomLeftHandAttackState state = _customLeftHandAttack;
            if (state == null)
                return;

            Agent agent = state.Agent;
            if (agent == null ||
                agent.State != AgentState.Active ||
                mission.MainAgent != agent)
            {
                LeftHandSwardLog.Warn(
                    "LeftHandCustom",
                    "END invalid agent/main-agent");
                _customLeftHandAttack = null;
                return;
            }

            float now = mission.CurrentTime;
            if (now < state.SweepStartAt)
                return;

            if (now > state.SweepEndAt)
            {
                LeftHandSwardLog.Info(
                    "LeftHandCustom",
                    "END hit=" + state.HitRegistered);
                _customLeftHandAttack = null;
                return;
            }

            float duration = Math.Max(0.01f, state.SweepEndAt - state.SweepStartAt);
            float progress = MathF.Clamp(
                (now - state.SweepStartAt) / duration,
                0f,
                1f);

            WeaponComponentData usage = state.Weapon.CurrentUsageItem;
            if (usage == null)
            {
                LeftHandSwardLog.Warn("LeftHandCustom", "END weapon usage became null");
                _customLeftHandAttack = null;
                return;
            }

            Vec3 forward = agent.LookDirection;
            forward.z = 0f;
            if (forward.LengthSquared < 0.0001f)
                forward = new Vec3(0f, 1f, 0f, -1f);
            forward.Normalize();

            Vec3 right = Vec3.CrossProduct(forward, Vec3.Up);
            if (right.LengthSquared < 0.0001f)
                right = new Vec3(1f, 0f, 0f, -1f);
            right.Normalize();

            Vec3 center = agent.GetEyeGlobalPosition();
            center.z -= 0.55f;
            center += right * -0.18f;

            float weaponLength = MathF.Clamp(
                usage.WeaponLength * 0.01f,
                0.55f,
                1.8f);
            float reach = 0.42f + weaponLength;

            // Left-to-right horizontal arc in front of the player.
            float angle = (-75f + 150f * progress) * (MathF.PI / 180f);
            Vec3 sweepDirection =
                forward * MathF.Cos(angle)
                + right * MathF.Sin(angle);
            sweepDirection.Normalize();

            Vec3 tip = center + sweepDirection * reach;

            if (!state.HitRegistered)
            {
                Agent victim = null;
                sbyte boneIndex = 0;
                Vec3 hitPoint = tip;

                // First test the blade itself at this frame.
                if (TryRayCastLeftHandVictim(
                        mission,
                        agent,
                        center,
                        tip,
                        out Agent bladeVictim,
                        out sbyte bladeBone,
                        out Vec3 bladeHit))
                {
                    victim = bladeVictim;
                    boneIndex = bladeBone;
                    hitPoint = bladeHit;
                }
                // Then test tip movement between frames so low frame rates do not
                // create holes in the sweep.
                else if (state.HasPreviousTip &&
                         TryRayCastLeftHandVictim(
                             mission,
                             agent,
                             state.PreviousTip,
                             tip,
                             out Agent arcVictim,
                             out sbyte arcBone,
                             out Vec3 arcHit))
                {
                    victim = arcVictim;
                    boneIndex = arcBone;
                    hitPoint = arcHit;
                }

                if (victim != null)
                {
                    state.HitRegistered = RegisterCustomLeftHandBlow(
                        state,
                        victim,
                        boneIndex,
                        hitPoint,
                        sweepDirection,
                        progress);
                }
            }

            state.PreviousTip = tip;
            state.HasPreviousTip = true;
        }

        private static bool TryRayCastLeftHandVictim(
            Mission mission,
            Agent attacker,
            Vec3 source,
            Vec3 target,
            out Agent victim,
            out sbyte boneIndex,
            out Vec3 hitPoint)
        {
            victim = null;
            boneIndex = 0;
            hitPoint = target;

            float collisionDistance;
            sbyte collisionBone;
            Agent candidate = mission.RayCastForClosestAgentsLimbs(
                source,
                target,
                attacker.Index,
                0.22f,
                out collisionDistance,
                out collisionBone);

            if (candidate == null ||
                candidate.State != AgentState.Active ||
                candidate == attacker ||
                attacker.IsFriendOf(candidate))
            {
                return false;
            }

            Vec3 delta = target - source;
            float length = delta.Length;
            if (length > 0.0001f)
            {
                float t = MathF.Clamp(collisionDistance / length, 0f, 1f);
                hitPoint = source + delta * t;
            }

            victim = candidate;
            boneIndex = collisionBone;
            return true;
        }

        private static bool RegisterCustomLeftHandBlow(
            CustomLeftHandAttackState state,
            Agent victim,
            sbyte boneIndex,
            Vec3 hitPoint,
            Vec3 sweepDirection,
            float attackProgress)
        {
            Agent attacker = state.Agent;
            MissionWeapon weapon = state.Weapon;
            WeaponComponentData usage = weapon.CurrentUsageItem;

            if (attacker == null ||
                victim == null ||
                usage == null ||
                victim.State != AgentState.Active)
            {
                return false;
            }

            // Prototype damage: use the weapon's own swing stat, but reduce the raw
            // number because this direct Blow path does not run the full native melee
            // armor/momentum calculation yet.
            int rawSwingDamage = Math.Max(1, usage.SwingDamage);
            int damage = Math.Max(6, Math.Min(70, (int)MathF.Round(rawSwingDamage * 0.55f)));

            Vec3 blowDirection = victim.Position - attacker.Position;
            blowDirection.z = 0f;
            if (blowDirection.LengthSquared < 0.0001f)
                blowDirection = sweepDirection;
            blowDirection.Normalize();

            Blow blow = new Blow(attacker.Index);
            blow.DamageType = usage.SwingDamageType;
            blow.StrikeType = StrikeType.Swing;
            blow.BoneIndex = boneIndex;
            blow.VictimBodyPart = BoneBodyPartType.Chest;
            blow.BaseMagnitude = damage;
            blow.GlobalPosition = hitPoint;
            blow.DamagedPercentage = 1f;
            blow.SwingDirection = sweepDirection;
            blow.Direction = blowDirection;
            blow.InflictedDamage = damage;
            blow.DamageCalculated = true;

            sbyte attachBone = attacker.Monster == null
                ? (sbyte)-1
                : attacker.Monster.OffHandItemBoneIndex;

            blow.WeaponRecord.FillAsMeleeBlow(
                weapon.Item,
                usage,
                (int)state.SourceSlot,
                attachBone);

            AttackCollisionData collisionData =
                AttackCollisionData.GetAttackCollisionDataForDebugPurpose(
                    false,
                    false,
                    false,
                    true,
                    false,
                    false,
                    false,
                    false,
                    false,
                    false,
                    false,
                    false,
                    CombatCollisionResult.StrikeAgent,
                    (int)state.SourceSlot,
                    (int)StrikeType.Swing,
                    (int)usage.SwingDamageType,
                    boneIndex,
                    BoneBodyPartType.Chest,
                    attachBone,
                    Agent.UsageDirection.AttackLeft,
                    -1,
                    CombatHitResultFlags.NormalHit,
                    attackProgress,
                    0.8f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    Vec3.Up,
                    blowDirection,
                    hitPoint,
                    Vec3.Zero,
                    Vec3.Zero,
                    victim.Velocity,
                    Vec3.Up);

            collisionData.BaseMagnitude = damage;
            collisionData.InflictedDamage = damage;

            float healthBefore = victim.Health;

            LeftHandSwardLog.Info(
                "LeftHandCustom",
                "HIT BEGIN"
                + " victim=" + SafeAgentName(victim)
                + " bone=" + boneIndex
                + " damage=" + damage
                + " healthBefore=" + healthBefore);

            victim.RegisterBlow(blow, collisionData);

            LeftHandSwardLog.Info(
                "LeftHandCustom",
                "HIT RETURN"
                + " victim=" + SafeAgentName(victim)
                + " healthAfter=" + victim.Health);

            Report(
                state.SkillId
                + " 左手命中 "
                + SafeAgentName(victim)
                + " damage=" + damage);

            return true;
        }

        private static int GetMainHandUsageIndex(Agent agent)
        {
            if (agent == null)
                return 0;

            EquipmentIndex primary = agent.GetPrimaryWieldedItemIndex();
            if (primary == EquipmentIndex.None)
                return 0;

            MissionWeapon weapon = agent.Equipment[primary];
            return weapon.IsEmpty ? 0 : weapon.CurrentUsageIndex;
        }

        private static void RestoreOffHandProbe()
        {
            _deferredOffHandRestorePending = false;
            _deferredOffHandRestoreReason = null;

            Agent agent = _offHandProbeAgent;
            EquipmentIndex previous = _offHandProbePreviousIndex;
            EquipmentIndex current = _offHandProbeCurrentIndex;

            _offHandProbeAgent = null;
            _offHandProbePreviousIndex = EquipmentIndex.None;
            _offHandProbeCurrentIndex = EquipmentIndex.None;

            if (agent == null || agent.State != AgentState.Active)
                return;

            int mainUsageIndex = GetMainHandUsageIndex(agent);
            LeftHandSwardLog.Info(
                "OffHandProbe",
                "CALL restore SetWieldedItemIndexAsClient BEGIN"
                + " current=" + current
                + " previous=" + previous
                + " mainUsageIndex=" + mainUsageIndex);

            agent.SetWieldedItemIndexAsClient(
                Agent.HandIndex.OffHand,
                previous,
                true,
                false,
                mainUsageIndex);

            LeftHandSwardLog.Info(
                "OffHandProbe",
                "CALL restore SetWieldedItemIndexAsClient RETURN"
                + " hands={" + DescribeHands(agent) + "}");
        }

        private static void RemoveNativeBoneAttachmentAt(int index)
        {
            NativeBoneAttachmentState state = _nativeBoneAttachments[index];
            try
            {
                Agent agent = state.Agent;
                if (agent != null && agent.State == AgentState.Active)
                {
                    int count = agent.GetAttachedWeaponsCount();
                    if (state.AttachedWeaponIndex >= 0 &&
                        state.AttachedWeaponIndex < count &&
                        agent.GetAttachedWeaponBoneIndex(state.AttachedWeaponIndex) == state.BoneIndex)
                    {
                        LeftHandSwardLog.Info(
                            "NativeBoneAttach",
                            "CALL Agent.DeleteAttachedWeapon BEGIN"
                            + " index=" + state.AttachedWeaponIndex
                            + " bone=" + state.BoneIndex);

                        agent.DeleteAttachedWeapon(state.AttachedWeaponIndex);

                        LeftHandSwardLog.Info(
                            "NativeBoneAttach",
                            "CALL Agent.DeleteAttachedWeapon RETURN"
                            + " remaining=" + agent.GetAttachedWeaponsCount());
                    }
                    else
                    {
                        LeftHandSwardLog.Warn(
                            "NativeBoneAttach",
                            "Skip delete because attached weapon index/bone changed"
                            + " index=" + state.AttachedWeaponIndex
                            + " count=" + count
                            + " expectedBone=" + state.BoneIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                LeftHandSwardLog.Exception("NativeBoneAttach", ex);
            }

            _nativeBoneAttachments.RemoveAt(index);
        }

        private static string DescribeFrame(MatrixFrame frame)
        {
            return "origin=("
                   + frame.origin.x + ","
                   + frame.origin.y + ","
                   + frame.origin.z + ")"
                   + " s=("
                   + frame.rotation.s.x + ","
                   + frame.rotation.s.y + ","
                   + frame.rotation.s.z + ")"
                   + " f=("
                   + frame.rotation.f.x + ","
                   + frame.rotation.f.y + ","
                   + frame.rotation.f.z + ")"
                   + " u=("
                   + frame.rotation.u.x + ","
                   + frame.rotation.u.y + ","
                   + frame.rotation.u.z + ")";
        }

        private static void ClearMeleeObservation()
        {
            AbortNativeLeftHandRewrite("melee observation finished", true);

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
                if (state.Skeleton != null && state.MetaMeshes != null)
                {
                    for (int i = state.MetaMeshes.Count - 1; i >= 0; i--)
                    {
                        MetaMesh metaMesh = state.MetaMeshes[i];
                        if (metaMesh == null || !metaMesh.IsValid)
                            continue;

                        LeftHandSwardLog.Info(
                            "VisualClone",
                            "CALL Skeleton.RemoveBoneComponent BEGIN"
                            + " bone=" + state.BoneIndex
                            + " index=" + i
                            + " mesh=" + SafeMetaMeshName(metaMesh));

                        if (state.Skeleton.HasBoneComponent(state.BoneIndex, metaMesh))
                            state.Skeleton.RemoveBoneComponent(state.BoneIndex, metaMesh);

                        LeftHandSwardLog.Info(
                            "VisualClone",
                            "CALL Skeleton.RemoveBoneComponent RETURN"
                            + " bone=" + state.BoneIndex
                            + " index=" + i);
                    }
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
                // Deliberately omit actionProgress and MovementFlags here.
                // Including them caused one log line almost every frame.
                return "name=" + SafeAgentName(agent)
                       + " actionType=" + agent.GetCurrentActionType(1)
                       + " actionStage=" + agent.GetCurrentActionStage(1)
                       + " actionDirection=" + agent.GetCurrentActionDirection(1)
                       + " animFlags=" + agent.GetCurrentAnimationFlag(1)
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

        private static bool TryGetActiveWeaponForVisual(
            Agent agent,
            out MissionWeapon weapon,
            out EquipmentIndex weaponSlot,
            out string error)
        {
            weapon = MissionWeapon.Invalid;
            weaponSlot = EquipmentIndex.None;
            error = null;

            if (agent == null || agent.State != AgentState.Active)
            {
                error = "Agent 不可用";
                LeftHandSwardLog.Warn("VisualClone", error);
                return false;
            }

            weaponSlot = agent.GetPrimaryWieldedItemIndex();
            if (weaponSlot == EquipmentIndex.None)
            {
                error = "当前没有主手武器槽";
                LeftHandSwardLog.Warn("VisualClone", error);
                return false;
            }

            weapon = agent.Equipment[weaponSlot];
            if (weapon.IsEmpty || weapon.Item == null)
            {
                error = "当前没有有效主手武器";
                LeftHandSwardLog.Warn("VisualClone", error);
                return false;
            }

            return true;
        }

        private static void CollectWeaponMetaMeshCopies(
            WeakGameEntity entity,
            MatrixFrame relativeFrame,
            List<MetaMesh> destination,
            string path)
        {
            if (!entity.IsValid)
                return;

            int metaMeshCount = entity.MultiMeshComponentCount;
            LeftHandSwardLog.Info(
                "VisualClone",
                "Scan entity"
                + " path=" + path
                + " name=" + SafeEntityName(entity)
                + " metaMeshes=" + metaMeshCount
                + " children=" + entity.ChildCount);

            for (int i = 0; i < metaMeshCount; i++)
            {
                MetaMesh source = entity.GetMetaMesh(i);
                if (source == null || !source.IsValid)
                    continue;

                LeftHandSwardLog.Info(
                    "VisualClone",
                    "CALL MetaMesh.CreateCopy BEGIN"
                    + " path=" + path
                    + " index=" + i
                    + " source=" + SafeMetaMeshName(source));

                MetaMesh clone = source.CreateCopy();

                LeftHandSwardLog.Info(
                    "VisualClone",
                    "CALL MetaMesh.CreateCopy RETURN"
                    + " path=" + path
                    + " index=" + i
                    + " valid=" + (clone != null && clone.IsValid));

                if (clone == null || !clone.IsValid)
                    continue;

                // Entity child transforms are separate from the MetaMesh component frame.
                // Flatten the entity hierarchy into the copied component before attaching
                // every component directly to the l_hand bone.
                clone.Frame = relativeFrame * source.Frame;
                destination.Add(clone);
            }

            int childCount = entity.ChildCount;
            for (int i = 0; i < childCount; i++)
            {
                WeakGameEntity child = entity.GetChild(i);
                if (!child.IsValid)
                    continue;

                MatrixFrame childRelativeFrame = relativeFrame * child.GetLocalFrame();
                CollectWeaponMetaMeshCopies(
                    child,
                    childRelativeFrame,
                    destination,
                    path + "/" + i);
            }
        }

        private static string SafeEntityName(WeakGameEntity entity)
        {
            if (!entity.IsValid)
                return "<invalid>";

            try
            {
                return entity.Name ?? "<unnamed>";
            }
            catch
            {
                return "<name-error>";
            }
        }

        private static string SafeMetaMeshName(MetaMesh metaMesh)
        {
            if (metaMesh == null || !metaMesh.IsValid)
                return "<invalid>";

            try
            {
                return metaMesh.GetName() ?? "<unnamed>";
            }
            catch
            {
                return "<name-error>";
            }
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
