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
            Text = new TextObject("左手测试-视觉+原生上挥");
            Description = new TextObject("按 LeftAlt：只复制当前武器模型到 l_hand，然后通过原生 MovementFlags 发起 AttackUp。用于观察左手骨骼在不同原生攻击方向下的运动。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);

            if (!LeftHandAttackRuntime.AddLeftHandVisualClone(
                    casterAgent,
                    2.5f,
                    out string visualResult))
            {
                LeftHandAttackRuntime.Report(Id + " 视觉阶段失败: " + visualResult);
                return false;
            }

            bool ok = LeftHandAttackRuntime.QueueNativeAttack(
                casterAgent,
                Id,
                Agent.MovementControlFlag.AttackUp,
                out string attackResult);

            LeftHandAttackRuntime.Report(
                Id + " visual={" + visualResult + "} nativeAttack={" + attackResult + "}");
            return ok;
        }
    }
}
