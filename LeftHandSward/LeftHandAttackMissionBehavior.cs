using LeftHandSward.Skills;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward
{
    /// <summary>
    /// Runtime support owned entirely by this extension mod.
    /// New_ZZZF only provides the skill framework and activation pipeline.
    ///
    /// Native melee attacks are injected through IPlayerInputEffector, matching the
    /// already-validated NativeMeleeCollisionTest path from New_ZZZF.
    /// </summary>
    public sealed class LeftHandAttackMissionBehavior : MissionBehavior, IPlayerInputEffector
    {
        public LeftHandAttackMissionBehavior()
        {
            LeftHandSwardLog.Info("Mission", "LeftHandAttackMissionBehavior ctor");
            LeftHandAttackRuntime.Cleanup();
            RightHandHitChainRuntime.Cleanup();
        }

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public Agent.EventControlFlag OnCollectPlayerEventControlFlags()
        {
            return LeftHandAttackRuntime.CollectPlayerInput(Mission);
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            LeftHandAttackRuntime.Tick(Mission);
            RightHandHitChainRuntime.Tick(Mission);
        }

        public override void OnMeleeHit(
            Agent attacker,
            Agent victim,
            bool isCanceled,
            AttackCollisionData collisionData)
        {
            base.OnMeleeHit(attacker, victim, isCanceled, collisionData);

            if (attacker == Mission?.MainAgent)
            {
                LeftHandSwardLog.Info(
                    "Mission",
                    "OnMeleeHit player"
                    + " canceled=" + isCanceled
                    + " blockedWithShield=" + collisionData.AttackBlockedWithShield
                    + " result=" + collisionData.CollisionResult
                    + " dir=" + collisionData.AttackDirection
                    + " progress=" + collisionData.AttackProgress);
            }

            // The right-hand chain probe intentionally sees the raw melee contact
            // before the generic observation is cleared. It accepts blocked/canceled
            // contacts as valid combo triggers.
            RightHandHitChainRuntime.NotifyMeleeHit(
                attacker,
                victim,
                isCanceled,
                collisionData);

            LeftHandAttackRuntime.NotifyMeleeHit(
                attacker,
                victim,
                isCanceled,
                collisionData);
        }

        public override void OnRegisterBlow(
            Agent attacker,
            Agent victim,
            WeakGameEntity realHitEntity,
            Blow blow,
            ref AttackCollisionData collisionData,
            in MissionWeapon attackerWeapon)
        {
            base.OnRegisterBlow(
                attacker,
                victim,
                realHitEntity,
                blow,
                ref collisionData,
                in attackerWeapon);

            LeftHandAttackRuntime.NotifyRegisterBlow(
                attacker,
                victim,
                blow,
                collisionData,
                in attackerWeapon);
        }

        public override void OnAgentRemoved(
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState,
            KillingBlow killingBlow)
        {
            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, killingBlow);

            // Avoid the previous end-of-battle log flood. Only the experiment agent matters.
            if (affectedAgent == Mission?.MainAgent)
            {
                LeftHandSwardLog.Info("Mission", "MainAgent removed state=" + agentState);
            }

            RightHandHitChainRuntime.OnAgentRemoved(affectedAgent);
            LeftHandAttackRuntime.RemoveVisualClone(affectedAgent);
        }

        protected override void OnEndMission()
        {
            LeftHandSwardLog.Info("Mission", "OnEndMission cleanup");
            RightHandHitChainRuntime.Cleanup();
            LeftHandAttackRuntime.Cleanup();
            base.OnEndMission();
        }

        public override void OnRemoveBehavior()
        {
            LeftHandSwardLog.Info("Mission", "OnRemoveBehavior cleanup");
            RightHandHitChainRuntime.Cleanup();
            LeftHandAttackRuntime.Cleanup();
            base.OnRemoveBehavior();
        }
    }
}
