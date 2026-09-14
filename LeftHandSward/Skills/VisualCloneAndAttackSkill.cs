using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class VisualCloneAndAttackSkill : SkillBase
    {
        public const string Id = "LHTest_VisualCloneAndAttack";

        public VisualCloneAndAttackSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("左手测试-视觉+真实副手+原生攻击");
            Description = new TextObject("按 LeftAlt：建立真实 OffHand 临时副本，同时保留左手视觉副本，再通过 MovementFlags 发起原生攻击。");
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

            if (!LeftHandAttackRuntime.AddLeftHandVisualClone(
                    casterAgent,
                    2.5f,
                    out string visualResult))
            {
                LeftHandAttackRuntime.Report(Id + " 视觉阶段失败: " + visualResult);
                return false;
            }

            bool attackOk = LeftHandAttackRuntime.QueueNativeAttack(
                casterAgent,
                Id,
                Agent.MovementControlFlag.AttackRight,
                out string attackResult);

            LeftHandAttackRuntime.Report(
                Id
                + " offhand={" + offhandResult + "}"
                + " visual={" + visualResult + "}"
                + " nativeAttack={" + attackResult + "}");
            return attackOk;
        }
    }
}
