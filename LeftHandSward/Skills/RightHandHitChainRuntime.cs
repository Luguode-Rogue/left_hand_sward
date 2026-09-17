using System;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    /// <summary>
    /// Isolated native-right-hand combo probe.
    ///
    /// First attack: normal MovementFlags.AttackRight -> vanilla AttackReady ->
    /// vanilla ReleaseMelee.
    ///
    /// Any melee contact from the active release (normal hit, shield block,
    /// weapon block/parry/canceled contact) arms exactly one next segment.
    /// The collision callback never changes actions directly.
    ///
    /// Chain transition is deliberately split across mission ticks:
    ///   CONTACT
    ///     -> next tick: send act_none to end the current ReleaseMelee
    ///     -> later tick: confirm channel 1 is no longer ReleaseMelee
    ///     -> following tick: restart the captured vanilla ReleaseMelee at progress 0
    ///
    /// Each successfully started next segment multiplies action speed by 1.10.
    /// </summary>
    internal static class RightHandHitChainRuntime
    {
        private const float SpeedMultiplierPerTrigger = 1.10f;
        private const float InitialTimeoutSeconds = 3.0f;
        private const float ChainTimeoutSeconds = 2.0f;
        private const float FinishTimeoutSeconds = 0.50f;

        private static Agent _agent;
        private static string _skillId;
        private static ActionIndexCache _releaseAction = ActionIndexCache.act_none;
        private static bool _releaseCaptured;

        private static bool _finishPending;
        private static bool _waitingForFinish;
        private static bool _restartPending;
        private static float _finishDeadline;

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
                + " normalHitContinues=true"
                + " blockedContinues=true"
                + " parryContinues=true"
                + " transition=contact-end-confirm-restart"
                + " speedStep=1.10x");

            result =
                "已启动右手原生命中连击测试：第一刀正常 AttackRight；"
                + "直接命中、盾挡、武器格挡/招架都会触发下一轮；"
                + "每轮先结束当前 ReleaseMelee，确认退出后再重启同一个原版 Release；"
                + "每成功触发下一段，动作速度累计 +10%";
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

            if (!_releaseCaptured &&
                _agent.GetCurrentActionType(1) == Agent.ActionCodeType.ReleaseMelee)
            {
                CaptureCurrentRelease("tick");
            }

            // Phase 1: after a hit/block/parry, explicitly end the current release.
            // Never do this from inside OnMeleeHit; wait until mission tick.
            if (_finishPending)
            {
                EndCurrentReleaseOnTick(mission);
                return;
            }

            // Phase 2: require an observable engine state where channel 1 has left
            // ReleaseMelee. Even if act_none switches immediately, we still return
            // and wait for another mission tick before starting the next release.
            if (_waitingForFinish)
            {
                ObserveReleaseFinishedOnTick(mission);
                return;
            }

            // Phase 3: only after the previous release was observed finished do we
            // start the next vanilla ReleaseMelee, on a separate mission tick.
            if (_restartPending)
            {
                RestartReleaseOnTick(mission);
                return;
            }

            if (mission.CurrentTime > _expireAt)
            {
                End(
                    _releaseCaptured
                        ? "连击等待超时：本段没有命中/格挡/招架"
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
            // the first vanilla release but the collision callback has, capture it.
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

            // One physical release can contact multiple agents/colliders. Any first
            // contact counts, including canceled/blocked/parried contacts, but only
            // one next segment may be armed for the current release sequence.
            if (_finishPending ||
                _waitingForFinish ||
                _restartPending ||
                _triggeredSequence == _releaseSequence)
            {
                LeftHandSwardLog.Info(
                    "RightChain",
                    "CONTACT duplicate ignored"
                    + " sequence=" + _releaseSequence
                    + " canceled=" + isCanceled
                    + " blockedWithShield=" + collisionData.AttackBlockedWithShield
                    + " result=" + collisionData.CollisionResult
                    + " victim=" + SafeAgentName(victim));
                return;
            }

            _triggeredSequence = _releaseSequence;
            _pendingSpeed = _currentSpeed * SpeedMultiplierPerTrigger;
            _finishPending = true;
            _expireAt = attacker.Mission == null
                ? 0f
                : attacker.Mission.CurrentTime + ChainTimeoutSeconds;

            // Intentionally NO filtering by isCanceled, shield block, collision
            // result, or victim state. The experiment requires both a clean strike
            // and any defended melee contact to continue the chain.
            LeftHandSwardLog.Info(
                "RightChain",
                "CONTACT -> FINISH PENDING"
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

        private static void EndCurrentReleaseOnTick(Mission mission)
        {
            if (_agent == null || !_releaseCaptured)
            {
                End("无法结束上一轮：Release action 未捕获");
                return;
            }

            string beforeAction = _agent.GetCurrentAction(1).GetName();
            Agent.ActionCodeType beforeType = _agent.GetCurrentActionType(1);
            var beforeStage = _agent.GetCurrentActionStage(1);

            LeftHandSwardLog.Info(
                "RightChain",
                "FINISH BEGIN"
                + " sequence=" + _releaseSequence
                + " action=" + beforeAction
                + " type=" + beforeType
                + " stage=" + beforeStage);

            // act_none is the explicit channel-end signal. We do not immediately
            // start the next attack in this call or this tick.
            bool accepted = _agent.SetActionChannel(
                1,
                ActionIndexCache.act_none,
                true,
                (AnimFlags)0UL,
                0f,
                1f,
                0f,
                0f,
                0f,
                false,
                0f,
                0,
                false);

            _finishPending = false;
            _waitingForFinish = true;
            _finishDeadline = mission.CurrentTime + FinishTimeoutSeconds;

            LeftHandSwardLog.Info(
                "RightChain",
                "FINISH SIGNAL RETURN"
                + " accepted=" + accepted
                + " afterAction=" + _agent.GetCurrentAction(1).GetName()
                + " afterType=" + _agent.GetCurrentActionType(1)
                + " afterStage=" + _agent.GetCurrentActionStage(1)
                + " nextSpeed=" + _pendingSpeed);

            if (!accepted)
            {
                End("上一轮 ReleaseMelee 结束信号被引擎拒绝");
            }
        }

        private static void ObserveReleaseFinishedOnTick(Mission mission)
        {
            if (_agent == null)
                return;

            Agent.ActionCodeType currentType = _agent.GetCurrentActionType(1);
            string currentAction = _agent.GetCurrentAction(1).GetName();

            if (currentType != Agent.ActionCodeType.ReleaseMelee)
            {
                _waitingForFinish = false;
                _restartPending = true;

                LeftHandSwardLog.Info(
                    "RightChain",
                    "FINISH CONFIRMED"
                    + " sequence=" + _releaseSequence
                    + " currentAction=" + currentAction
                    + " currentType=" + currentType
                    + " nextSequence=" + (_releaseSequence + 1)
                    + " nextSpeed=" + _pendingSpeed);

                // Deliberately return to caller. RestartReleaseOnTick is not invoked
                // until the following mission tick.
                return;
            }

            if (mission.CurrentTime > _finishDeadline)
            {
                End(
                    "等待上一轮 ReleaseMelee 结束超时"
                    + " action=" + currentAction
                    + " type=" + currentType);
            }
        }

        private static void RestartReleaseOnTick(Mission mission)
        {
            if (_agent == null || !_releaseCaptured || _releaseAction.Index < 0)
            {
                End("无法重启：Release action 未捕获");
                return;
            }

            // Insurance check: never start a new release while the old one is still
            // reported as ReleaseMelee by the engine.
            if (_agent.GetCurrentActionType(1) == Agent.ActionCodeType.ReleaseMelee)
            {
                _waitingForFinish = true;
                _restartPending = false;
                _finishDeadline = mission.CurrentTime + FinishTimeoutSeconds;
                LeftHandSwardLog.Warn(
                    "RightChain",
                    "RESTART deferred because previous ReleaseMelee is still active");
                return;
            }

            string beforeAction = _agent.GetCurrentAction(1).GetName();
            Agent.ActionCodeType beforeType = _agent.GetCurrentActionType(1);
            var beforeStage = _agent.GetCurrentActionStage(1);

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
                (AnimFlags)0UL,
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
                    + " finishPending=" + _finishPending
                    + " waitingForFinish=" + _waitingForFinish
                    + " restartPending=" + _restartPending);
            }

            ResetState();
        }

        private static void ResetState()
        {
            _agent = null;
            _skillId = null;
            _releaseAction = ActionIndexCache.act_none;
            _releaseCaptured = false;

            _finishPending = false;
            _waitingForFinish = false;
            _restartPending = false;
            _finishDeadline = 0f;

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
