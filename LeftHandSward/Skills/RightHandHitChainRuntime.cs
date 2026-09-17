using System;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    /// <summary>
    /// Isolated native-right-hand combo probe.
    ///
    /// Every segment is a complete Bannerlord-native melee attack. The first and all
    /// chained attacks enter through MovementFlags.AttackRight; we never restart a
    /// ReleaseMelee action directly.
    ///
    /// Any melee contact from the active attack (normal strike, shield block,
    /// weapon block/parry/canceled contact) arms exactly one next segment.
    /// Transition:
    ///   CONTACT
    ///     -> next mission tick: send act_none as an explicit end signal
    ///     -> later tick: confirm the old attack has left attack/reaction state
    ///     -> following tick: queue a fresh native AttackRight input
    ///     -> Bannerlord builds a complete AttackReady -> ReleaseMelee again
    ///
    /// Starting from segment 2, both the ready and release actions are accelerated
    /// by the accumulated 1.10x speed multiplier.
    /// </summary>
    internal static class RightHandHitChainRuntime
    {
        private const float SpeedMultiplierPerTrigger = 1.10f;
        private const float InitialTimeoutSeconds = 3.0f;
        private const float ChainTimeoutSeconds = 2.5f;
        private const float FinishTimeoutSeconds = 0.50f;

        private static Agent _agent;
        private static string _skillId;

        private static bool _finishPending;
        private static bool _waitingForFinish;
        private static bool _queueNextAttackPending;
        private static bool _awaitingNativeAttackStart;
        private static float _finishDeadline;

        private static int _attackSequence;
        private static int _triggeredSequence;
        private static float _currentSpeed = 1.0f;
        private static float _pendingSpeed = 1.0f;
        private static float _expireAt;

        private static string _lastSpeedAppliedAction;
        private static Agent.ActionCodeType _lastSpeedAppliedType;
        private static bool _releaseObservedForSequence;

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
                result = "右手连击测试只支持 MainAgent";
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
            _attackSequence = 1;
            _currentSpeed = 1.0f;
            _pendingSpeed = 1.0f;
            _awaitingNativeAttackStart = true;
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
                + " sequence=1"
                + " attackEntry=native-AttackRight"
                + " everySegment=full-native-attack"
                + " normalHitContinues=true"
                + " blockedContinues=true"
                + " parryContinues=true"
                + " transition=contact-end-confirm-native-rearm"
                + " speedStep=1.10x");

            result =
                "已启动右手完整原生连击测试：每一刀都通过 AttackRight 重新进入原生攻击；"
                + "直接命中、盾挡、武器格挡/招架都会触发下一轮；"
                + "每轮先显式结束上一刀并确认退出，再启动完整 AttackReady→ReleaseMelee；"
                + "第二刀起 Ready/Release 动作速度累计 +10%";
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

            if (_finishPending)
            {
                EndCurrentAttackOnTick(mission);
                return;
            }

            if (_waitingForFinish)
            {
                ObserveAttackFinishedOnTick(mission);
                return;
            }

            if (_queueNextAttackPending)
            {
                QueueNextNativeAttackOnTick(mission);
                return;
            }

            ObserveAndAccelerateNativeAttack(mission);

            if (mission.CurrentTime > _expireAt)
            {
                End(
                    _releaseObservedForSequence
                        ? "连击等待超时：本段没有命中/格挡/招架"
                        : "本段原生攻击未进入 ReleaseMelee");
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

            // One physical attack may touch multiple agents/colliders. The first
            // melee contact of this sequence arms exactly one next native attack.
            // Intentionally do NOT filter canceled/blocked/parried contacts.
            if (_finishPending ||
                _waitingForFinish ||
                _queueNextAttackPending ||
                _triggeredSequence == _attackSequence)
            {
                LeftHandSwardLog.Info(
                    "RightChain",
                    "CONTACT duplicate ignored"
                    + " sequence=" + _attackSequence
                    + " canceled=" + isCanceled
                    + " blockedWithShield=" + collisionData.AttackBlockedWithShield
                    + " result=" + collisionData.CollisionResult
                    + " victim=" + SafeAgentName(victim));
                return;
            }

            _triggeredSequence = _attackSequence;
            _pendingSpeed = _currentSpeed * SpeedMultiplierPerTrigger;
            _finishPending = true;
            _expireAt = attacker.Mission == null
                ? 0f
                : attacker.Mission.CurrentTime + ChainTimeoutSeconds;

            LeftHandSwardLog.Info(
                "RightChain",
                "CONTACT -> END PENDING"
                + " sequence=" + _attackSequence
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

        private static void ObserveAndAccelerateNativeAttack(Mission mission)
        {
            if (_agent == null)
                return;

            Agent.ActionCodeType type = _agent.GetCurrentActionType(1);
            string action = _agent.GetCurrentAction(1).GetName();
            var stage = _agent.GetCurrentActionStage(1);

            bool isReady =
                type == Agent.ActionCodeType.AttackMeleeAllBegin ||
                type == Agent.ActionCodeType.ReadyMelee;
            bool isRelease = type == Agent.ActionCodeType.ReleaseMelee;

            if (isReady || isRelease)
            {
                if (_currentSpeed > 1.0001f &&
                    (!string.Equals(
                         action,
                         _lastSpeedAppliedAction,
                         StringComparison.Ordinal) ||
                     type != _lastSpeedAppliedType))
                {
                    _agent.SetCurrentActionSpeed(1, _currentSpeed);
                    _lastSpeedAppliedAction = action;
                    _lastSpeedAppliedType = type;

                    LeftHandSwardLog.Info(
                        "RightChain",
                        "SPEED APPLY"
                        + " sequence=" + _attackSequence
                        + " action=" + action
                        + " type=" + type
                        + " stage=" + stage
                        + " speed=" + _currentSpeed);
                }

                if (_awaitingNativeAttackStart)
                {
                    _awaitingNativeAttackStart = false;
                    LeftHandSwardLog.Info(
                        "RightChain",
                        "NATIVE ATTACK STARTED"
                        + " sequence=" + _attackSequence
                        + " action=" + action
                        + " type=" + type
                        + " stage=" + stage
                        + " speed=" + _currentSpeed);
                }
            }

            if (isRelease && !_releaseObservedForSequence)
            {
                _releaseObservedForSequence = true;
                _expireAt = mission.CurrentTime + ChainTimeoutSeconds;

                LeftHandSwardLog.Info(
                    "RightChain",
                    "FULL RELEASE OBSERVED"
                    + " sequence=" + _attackSequence
                    + " action=" + action
                    + " stage=" + stage
                    + " speed=" + _currentSpeed);
            }
        }

        private static void EndCurrentAttackOnTick(Mission mission)
        {
            if (_agent == null)
                return;

            string beforeAction = _agent.GetCurrentAction(1).GetName();
            Agent.ActionCodeType beforeType = _agent.GetCurrentActionType(1);
            var beforeStage = _agent.GetCurrentActionStage(1);

            LeftHandSwardLog.Info(
                "RightChain",
                "END CURRENT ATTACK BEGIN"
                + " sequence=" + _attackSequence
                + " action=" + beforeAction
                + " type=" + beforeType
                + " stage=" + beforeStage);

            // Explicit end signal. The next attack is NOT started in this tick.
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
                "END CURRENT ATTACK RETURN"
                + " accepted=" + accepted
                + " afterAction=" + _agent.GetCurrentAction(1).GetName()
                + " afterType=" + _agent.GetCurrentActionType(1)
                + " afterStage=" + _agent.GetCurrentActionStage(1)
                + " nextSpeed=" + _pendingSpeed);

            if (!accepted)
                End("上一轮攻击结束信号被引擎拒绝");
        }

        private static void ObserveAttackFinishedOnTick(Mission mission)
        {
            if (_agent == null)
                return;

            Agent.ActionCodeType currentType = _agent.GetCurrentActionType(1);
            string currentAction = _agent.GetCurrentAction(1).GetName();

            if (!IsAttackExecutionState(currentType))
            {
                _waitingForFinish = false;
                _queueNextAttackPending = true;

                LeftHandSwardLog.Info(
                    "RightChain",
                    "END CONFIRMED"
                    + " sequence=" + _attackSequence
                    + " currentAction=" + currentAction
                    + " currentType=" + currentType
                    + " nextSequence=" + (_attackSequence + 1)
                    + " nextSpeed=" + _pendingSpeed);

                // QueueNextNativeAttackOnTick runs on a later mission tick.
                return;
            }

            if (mission.CurrentTime > _finishDeadline)
            {
                End(
                    "等待上一轮攻击结束超时"
                    + " action=" + currentAction
                    + " type=" + currentType);
            }
        }

        private static void QueueNextNativeAttackOnTick(Mission mission)
        {
            if (_agent == null)
                return;

            if (IsAttackExecutionState(_agent.GetCurrentActionType(1)))
            {
                _waitingForFinish = true;
                _queueNextAttackPending = false;
                _finishDeadline = mission.CurrentTime + FinishTimeoutSeconds;
                LeftHandSwardLog.Warn(
                    "RightChain",
                    "NATIVE REARM deferred because previous attack state returned"
                    + " type=" + _agent.GetCurrentActionType(1)
                    + " action=" + _agent.GetCurrentAction(1).GetName());
                return;
            }

            bool queued = LeftHandAttackRuntime.QueueNativeAttack(
                _agent,
                _skillId,
                Agent.MovementControlFlag.AttackRight,
                out string queueResult);

            LeftHandSwardLog.Info(
                "RightChain",
                "NATIVE REARM QUEUE"
                + " accepted=" + queued
                + " fromAction=" + _agent.GetCurrentAction(1).GetName()
                + " fromType=" + _agent.GetCurrentActionType(1)
                + " nextSequence=" + (_attackSequence + 1)
                + " nextSpeed=" + _pendingSpeed
                + " result=" + queueResult);

            if (!queued)
            {
                End("下一轮原生 AttackRight 排队失败: " + queueResult);
                return;
            }

            _currentSpeed = _pendingSpeed;
            _attackSequence++;
            _triggeredSequence = 0;
            _queueNextAttackPending = false;
            _awaitingNativeAttackStart = true;
            _releaseObservedForSequence = false;
            _lastSpeedAppliedAction = null;
            _lastSpeedAppliedType = Agent.ActionCodeType.Other;
            _expireAt = mission.CurrentTime + InitialTimeoutSeconds;

            LeftHandSwardLog.Info(
                "RightChain",
                "SEGMENT REARMED"
                + " sequence=" + _attackSequence
                + " speed=" + _currentSpeed
                + " entry=native-AttackRight");
        }

        private static bool IsAttackExecutionState(Agent.ActionCodeType type)
        {
            return type == Agent.ActionCodeType.AttackMeleeAllBegin ||
                   type == Agent.ActionCodeType.ReadyMelee ||
                   type == Agent.ActionCodeType.ReleaseMelee ||
                   type == Agent.ActionCodeType.BlockedMelee ||
                   type == Agent.ActionCodeType.ParriedMelee;
        }

        private static void End(string reason)
        {
            if (_agent != null)
            {
                LeftHandSwardLog.Warn(
                    "RightChain",
                    "END"
                    + " reason=" + reason
                    + " sequence=" + _attackSequence
                    + " speed=" + _currentSpeed
                    + " finishPending=" + _finishPending
                    + " waitingForFinish=" + _waitingForFinish
                    + " queueNextAttackPending=" + _queueNextAttackPending
                    + " awaitingNativeAttackStart=" + _awaitingNativeAttackStart);
            }

            ResetState();
        }

        private static void ResetState()
        {
            _agent = null;
            _skillId = null;
            _finishPending = false;
            _waitingForFinish = false;
            _queueNextAttackPending = false;
            _awaitingNativeAttackStart = false;
            _finishDeadline = 0f;
            _attackSequence = 0;
            _triggeredSequence = 0;
            _currentSpeed = 1.0f;
            _pendingSpeed = 1.0f;
            _expireAt = 0f;
            _lastSpeedAppliedAction = null;
            _lastSpeedAppliedType = Agent.ActionCodeType.Other;
            _releaseObservedForSequence = false;
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
