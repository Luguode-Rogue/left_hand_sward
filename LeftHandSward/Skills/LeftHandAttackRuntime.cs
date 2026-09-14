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

        private static readonly List<VisualCloneState> _visualClones = new List<VisualCloneState>();

        private static Agent _pendingAttackAgent;
        private static string _pendingAttackSkillId;
        private static float _pendingAttackUntil;

        public static bool TryGetActiveMeleeWeapon(Agent agent, out MissionWeapon weapon, out string error)
        {
            weapon = MissionWeapon.Invalid;
            error = null;

            if (agent == null || agent.State != AgentState.Active)
            {
                error = "Agent 不可用";
                return false;
            }

            weapon = agent.WieldedWeapon;
            if (weapon.IsEmpty || weapon.Item == null || weapon.CurrentUsageItem == null)
            {
                error = "当前没有有效武器";
                return false;
            }

            if (!weapon.CurrentUsageItem.IsMeleeWeapon)
            {
                error = "当前武器不是近战武器";
                return false;
            }

            return true;
        }

        public static bool PlayAction(Agent agent, string actionName, AnimFlags flags, out string result)
        {
            if (agent == null)
            {
                result = "Agent=null";
                return false;
            }

            ActionIndexCache action = ActionIndexCache.Create(actionName);
            if (action.Index < 0)
            {
                result = "动作不存在: " + actionName;
                return false;
            }

            bool accepted = agent.SetActionChannel(
                1,
                action,
                true,
                flags,
                0f,
                1f,
                -0.1f,
                0.2f,
                0f,
                false,
                -0.1f,
                0,
                true);

            result = "SetActionChannel=" + accepted
                     + " action=" + actionName
                     + " type=" + agent.GetCurrentActionType(1)
                     + " stage=" + agent.GetCurrentActionStage(1)
                     + " attackDir=" + agent.AttackDirection;
            return accepted;
        }

        public static bool AddLeftHandVisualClone(Agent agent, float lifetimeSeconds, out string result)
        {
            if (!TryGetActiveMeleeWeapon(agent, out MissionWeapon weapon, out string error))
            {
                result = error;
                return false;
            }

            if (agent.AgentVisuals == null)
            {
                result = "AgentVisuals=null";
                return false;
            }

            string meshName = weapon.Item.MultiMeshName;
            if (string.IsNullOrEmpty(meshName))
            {
                result = "当前物品没有 MultiMeshName";
                return false;
            }

            Skeleton skeleton = agent.AgentVisuals.GetSkeleton();
            if (skeleton == null || !skeleton.IsValid)
            {
                result = "Skeleton 无效";
                return false;
            }

            sbyte leftHandBone = FindBoneIndex(skeleton, "l_hand");
            if (leftHandBone < 0)
            {
                result = "找不到 l_hand 骨骼";
                return false;
            }

            RemoveVisualClone(agent);

            MetaMesh clone = MetaMesh.GetCopy(meshName, false, true);
            if (clone == null || !clone.IsValid)
            {
                result = "无法复制武器 MetaMesh: " + meshName;
                return false;
            }

            skeleton.AddComponentToBone(leftHandBone, clone);
            _visualClones.Add(new VisualCloneState
            {
                Agent = agent,
                Skeleton = skeleton,
                BoneIndex = leftHandBone,
                MetaMesh = clone,
                ExpireAt = (agent.Mission != null ? agent.Mission.CurrentTime : 0f) + Math.Max(0.25f, lifetimeSeconds)
            });

            result = "已复制视觉模型到 l_hand: " + meshName + " bone=" + leftHandBone;
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

            if (_pendingAttackSkillId != null && mission.CurrentTime > _pendingAttackUntil)
            {
                Report(_pendingAttackSkillId + " 观察窗口结束：未收到 OnMeleeHit");
                _pendingAttackAgent = null;
                _pendingAttackSkillId = null;
                _pendingAttackUntil = 0f;
            }
        }

        public static void Cleanup()
        {
            for (int i = _visualClones.Count - 1; i >= 0; i--)
                RemoveVisualCloneAt(i);

            _pendingAttackAgent = null;
            _pendingAttackSkillId = null;
            _pendingAttackUntil = 0f;
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
                return;

            _pendingAttackAgent = agent;
            _pendingAttackSkillId = skillId;
            _pendingAttackUntil = agent.Mission.CurrentTime + Math.Max(0.25f, seconds);
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

            Report(
                _pendingAttackSkillId
                + " 命中 native melee"
                + " canceled=" + isCanceled
                + " dir=" + collisionData.AttackDirection
                + " progress=" + collisionData.AttackProgress
                + " victim=" + (victim == null ? "null" : victim.Name.ToString()));

            _pendingAttackAgent = null;
            _pendingAttackSkillId = null;
            _pendingAttackUntil = 0f;
        }

        public static void Report(string message)
        {
            Debug.Print("[LeftHandSward] " + message);
            InformationManager.DisplayMessage(new InformationMessage("[左手攻击扩展] " + message));
        }

        private static void RemoveVisualCloneAt(int index)
        {
            VisualCloneState state = _visualClones[index];
            try
            {
                if (state.Skeleton != null && state.MetaMesh != null && state.MetaMesh.IsValid)
                    state.Skeleton.RemoveBoneComponent(state.BoneIndex, state.MetaMesh);
            }
            catch (Exception ex)
            {
                Debug.Print("[LeftHandSward] RemoveVisualClone failed: " + ex.Message);
            }

            _visualClones.RemoveAt(index);
        }

        private static sbyte FindBoneIndex(Skeleton skeleton, string boneName)
        {
            sbyte count = skeleton.GetBoneCount();
            for (sbyte i = 0; i < count; i++)
            {
                if (string.Equals(skeleton.GetBoneName(i), boneName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }
    }
}
