using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class LeftStanceAttackSkill : SkillBase
    {
        public const string Id = "LHTest_LeftStanceFlags";

        public LeftStanceAttackSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("左手测试-真实副手+原生攻击");
            Description = new TextObject("按 LeftAlt：先建立 HeldInOffHand 的真实临时副手剑，再通过 IPlayerInputEffector + MovementFlags 发起原生攻击。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);

            if (!LeftHandAttackRuntime.EquipTemporaryOffhandClone(
                    casterAgent,
                    out string offhandResult))
            {
                LeftHandAttackRuntime.Report(Id + " 副手装备失败: " + offhandResult);
                return false;
            }

            bool attackOk = LeftHandAttackRuntime.QueueNativeAttack(
                casterAgent,
                Id,
                Agent.MovementControlFlag.AttackRight,
                out string attackResult);

            LeftHandAttackRuntime.Report(
                Id + " offhand={" + offhandResult + "} nativeAttack={" + attackResult + "}");
            return attackOk;
        }
    }
}
