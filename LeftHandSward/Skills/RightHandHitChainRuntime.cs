using System;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    /// <summary>
    /// Isolated native-right-hand combo probe.
    ///
    /// The first attack enters Bannerlord's normal melee state machine through
    /// MovementFlags.AttackRight. Once the first vanilla ReleaseMelee action is
    /// observed, that exact ActionIndexCache is retained. A melee contact schedules
    /// (never performs inside the collision callback) a restart of the same vanilla
    /// release on the next mission tick. Each successful restart multiplies the
    /// action speed by 1.10.
    ///
    /// Important: canceled/blocked contacts are intentionally accepted. The purpose
    /// of this probe is to test whether a fresh ReleaseMelee can re-arm native melee
    /// collision without going through another AttackReady state.
    /// </summary>
    internal static class RightHandHitChainRuntime
    {
        private const float SpeedMultiplierPerTrigger = 1.10f;
        private const float InitialTimeoutSeconds = 3.0f;
        private const float ChainTimeoutSeconds = 2.0f;

        private static Agent _agent;
        private static string _skillId;
        private static ActionIndexCache _releaseAction = ActionIndexCache.act_none;
        private static bool _releaseCaptured;
        private static bool _restartPending;
        private static int _releaseSequence;
        private static int _triggeredSequence;
        private static float _currentSpeed = 1.0f;
        private static float _pendingSpeed = 1.0f;
        private static float _expireAt;

        public static bool Start(
            Agent agent,
            string skillId,
            out string result)
        {
            if (agent == null || agent.State != AgentState.Active)
            {
                result = "Agent 不可用";
                return false;
            }

            if (agent.Mission == null || agent.Mission.MainAgent != agent)
            {
                result = "右手连击 Release 测试只支持 MainAgent";
                LeftHandSwardLog.Warn("RightChain", result);
                return false;
            }

            if (!LeftHandAttackRuntime.TryGetActiveMeleeWeapon(
                    agent,
                    out MissionWeapon weapon,
                    out string weaponError))
            {
                result = weaponError;
                return false;
            }

            if (_agent != null)
            {
                result = "上一轮右手命中连击测试仍在执行";
                LeftHandSwardLog.Warn("RightChain", result);
                return false;
            }

            ResetState();

            _agent = agent;
            _skillId = skillId;
            _expireAt = agent.Mission.CurrentTime + InitialTimeoutSeconds;

            bool queued = LeftHandAttackRuntime.QueueNativeAttack(
                agent,
                skillId,
                Agent.MovementControlFlag.AttackRight,
                out string queueResult);

            if (!queued)
            {
                ResetState();
                result = queueResult;
                return false;
            }

            LeftHandSwardLog.Info(
                "RightChain",
                "ARMED"
                + " skill=" + skillId
                + " weapon=" + DescribeWeapon(weapon)
                + " firstAttack=native-AttackRight"
                + " nextRelease=same-vanilla-action"
                + " blockedContinues=true"
                + " speedStep=1.10x");

            result =
                "已启动右手原生命中连击测试：第一刀正常 AttackRight；"
                + "命中或被格挡后，下一 Tick 直接重启同一个原版 ReleaseMelee；"
                + "每次连击速度累计 +10%";
            return true;
        }

        public static void Tick(Mission mission)
        {
            if (_agent == null)
                return;

            if (mission == null ||
                mission.MainAgent != _agent ||
                _agent.State != AgentState.Active)
            {
                End("Agent/MainAgent 已失效");
                return;
            }

            if (_restartPending)
            {
                RestartReleaseOnTick(mission);
                return;
            }

            if (!_releaseCaptured &&
                _agent.GetCurrentActionType(1) == Agent.ActionCodeType.ReleaseMelee)
            {
                CaptureCurrentRelease("tick");
            }

            if (mission.CurrentTime > _expireAt)
            {
                End(
                    _releaseCaptured
                        ? "连击等待超时：本段没有命中/格挡"
                        : "首次攻击未观察到 ReleaseMelee");
            }
        }

        public static void NotifyMeleeHit(
            Agent attacker,
            Agent victim,
            bool isCanceled,
            AttackCollisionData collisionData)
        {
            if (_agent == null || attacker != _agent)
                return;

            // Be robust to callback ordering. If the mission tick has not yet seen
            // the first ReleaseMelee but the collision callback has, capture it here.
            if (!_releaseCaptured &&
                attacker.GetCurrentActionType(1) == Agent.ActionCodeType.ReleaseMelee)
            {
                CaptureCurrentRelease("hit-callback");
            }

            if (!_releaseCaptured)
            {
                LeftHandSwardLog.Warn(
                    "RightChain",
                    "CONTACT ignored because no vanilla ReleaseMelee has been captured"
                    + " canceled=" + isCanceled
                    + " blockedWithShield=" + collisionData.AttackBlockedWithShield
                    + " result=" + collisionData.CollisionResult);
                return;
            }

            // One physical release may touch more than one agent. Only the first
            // contact is allowed to arm the next combo segment.
            if (_restartPending || _triggeredSequence == _releaseSequence)
            {
                LeftHandSwardLog.Info(
                    "RightChain",
                    "CONTACT duplicate ignored"
                    + " sequence=" + _releaseSequence
                    + " victim=" + SafeAgentName(victim));
                return;
            }

            _triggeredSequence = _releaseSequence;
            _pendingSpeed = _currentSpeed * SpeedMultiplierPerTrigger;
            _restartPending = true;
            _expireAt = attacker.Mission == null
                ? 0f
                : attacker.Mission.CurrentTime + ChainTimeoutSeconds;

            // Do NOT reject isCanceled. Bannerlord uses canceled/blocked states for
            // some defended contacts, and this experiment explicitly requires a
            // block/parry to continue the chain as well.
            LeftHandSwardLog.Info(
                "RightChain",
                "CONTACT -> RESTART PENDING"
                + " sequence=" + _releaseSequence
                + " canceled=" + isCanceled
                + " blockedWithShield=" + collisionData.AttackBlockedWithShield
                + " result=" + collisionData.CollisionResult
                + " dir=" + collisionData.AttackDirection
                + " progress=" + collisionData.AttackProgress
                + " victim=" + SafeAgentName(victim)
                + " currentSpeed=" + _currentSpeed
                + " nextSpeed=" + _pendingSpeed);
        }

        public static void OnAgentRemoved(Agent agent)
        {
            if (agent != null && agent == _agent)
                End("测试 Agent 被移除");
        }

        public static void Cleanup()
        {
            if (_agent != null)
                LeftHandSwardLog.Info("RightChain", "Cleanup active chain");
            ResetState();
        }

        private static void CaptureCurrentRelease(string source)
        {
            if (_agent == null)
                return;

            ActionIndexCache current = _agent.GetCurrentAction(1);
            if (current.Index < 0 ||
                _agent.GetCurrentActionType(1) != Agent.ActionCodeType.ReleaseMelee)
            {
                return;
            }

            _releaseAction = current;
            _releaseCaptured = true;
            _releaseSequence = 1;
            _triggeredSequence = 0;
            _currentSpeed = 1.0f;

            LeftHandSwardLog.Info(
                "RightChain",
                "CAPTURE VANILLA RELEASE"
                + " source=" + source
                + " action=" + current.GetName()
                + " index=" + current.Index
                + " sequence=" + _releaseSequence
                + " speed=" + _currentSpeed);
        }

        private static void RestartReleaseOnTick(Mission mission)
        {
            if (_agent == null || !_releaseCaptured || _releaseAction.Index < 0)
            {
                End("无法重启：Release action 未捕获");
                return;
            }

            string beforeAction = _agent.GetCurrentAction(1).GetName();
            Agent.ActionCodeType beforeType = _agent.GetCurrentActionType(1);
            Agent.ActionStage beforeStage = _agent.GetCurrentActionStage(1);

            LeftHandSwardLog.Info(
                "RightChain",
                "RESTART BEGIN"
                + " fromAction=" + beforeAction
                + " fromType=" + beforeType
                + " fromStage=" + beforeStage
                + " target=" + _releaseAction.GetName()
                + " nextSequence=" + (_releaseSequence + 1)
                + " actionSpeed=" + _pendingSpeed);

            bool accepted = _agent.SetActionChannel(
                1,
                _releaseAction,
                true,
                AnimFlags.None,
                0f,
                _pendingSpeed,
                0f,
                0.4f,
                0f,
                false,
                -0.2f,
                0,
                false);

            Agent.ActionCodeType afterType = _agent.GetCurrentActionType(1);
            string afterAction = _agent.GetCurrentAction(1).GetName();

            LeftHandSwardLog.Info(
                "RightChain",
                "RESTART RETURN"
                + " accepted=" + accepted
                + " afterAction=" + afterAction
                + " afterType=" + afterType
                + " afterStage=" + _agent.GetCurrentActionStage(1)
                + " actionSpeed=" + _pendingSpeed);

            if (!accepted || afterType != Agent.ActionCodeType.ReleaseMelee)
            {
                End(
                    "ReleaseMelee 重启被引擎拒绝"
                    + " accepted=" + accepted
                    + " afterType=" + afterType);
                return;
            }

            _currentSpeed = _pendingSpeed;
            _restartPending = false;
            _releaseSequence++;
            _expireAt = mission.CurrentTime + ChainTimeoutSeconds;

            LeftHandSwardLog.Info(
                "RightChain",
                "SEGMENT ACTIVE"
                + " sequence=" + _releaseSequence
                + " speed=" + _currentSpeed
                + " action=" + afterAction);
        }

        private static void End(string reason)
        {
            if (_agent != null)
            {
                LeftHandSwardLog.Warn(
                    "RightChain",
                    "END"
                    + " reason=" + reason
                    + " sequence=" + _releaseSequence
                    + " speed=" + _currentSpeed
                    + " pending=" + _restartPending);
            }

            ResetState();
        }

        private static void ResetState()
        {
            _agent = null;
            _skillId = null;
            _releaseAction = ActionIndexCache.act_none;
            _releaseCaptured = false;
            _restartPending = false;
            _releaseSequence = 0;
            _triggeredSequence = 0;
            _currentSpeed = 1.0f;
            _pendingSpeed = 1.0f;
            _expireAt = 0f;
        }

        private static string SafeAgentName(Agent agent)
        {
            try
            {
                return agent == null || agent.Name == null
                    ? "null"
                    : agent.Name.ToString();
            }
            catch
            {
                return "<name-error>";
            }
        }

        private static string DescribeWeapon(MissionWeapon weapon)
        {
            if (weapon.IsEmpty || weapon.Item == null)
                return "<empty>";

            string usage = weapon.CurrentUsageItem == null
                ? "null"
                : weapon.CurrentUsageItem.WeaponClass.ToString();
            return weapon.Item.StringId + "/" + usage;
        }
    }
}
