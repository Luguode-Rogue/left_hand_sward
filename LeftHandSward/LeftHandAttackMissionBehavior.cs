using LeftHandSward.Skills;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward
{
    /// <summary>
    /// Runtime support owned entirely by this extension mod.
    /// New_ZZZF only provides the skill framework and activation pipeline.
    /// </summary>
    public sealed class LeftHandAttackMissionBehavior : MissionBehavior
    {
        public LeftHandAttackMissionBehavior()
        {
            LeftHandAttackRuntime.Cleanup();
        }

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            LeftHandAttackRuntime.Tick(Mission);
        }

        public override void OnMeleeHit(
            Agent attacker,
            Agent victim,
            bool isCanceled,
            AttackCollisionData collisionData)
        {
            base.OnMeleeHit(attacker, victim, isCanceled, collisionData);
            LeftHandAttackRuntime.NotifyMeleeHit(attacker, victim, isCanceled, collisionData);
        }

        public override void OnAgentRemoved(
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState,
            KillingBlow killingBlow)
        {
            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, killingBlow);
            LeftHandAttackRuntime.RemoveVisualClone(affectedAgent);
        }
    }
}
