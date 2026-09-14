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
            Text = new TextObject("左手测试-视觉+原生下刺");
            Description = new TextObject("按 LeftAlt：只复制当前武器模型到 l_hand，然后通过原生 MovementFlags 发起 AttackDown。不会创建第二个 ItemObject。");
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
                Agent.MovementControlFlag.AttackDown,
                out string attackResult);

            LeftHandAttackRuntime.Report(
                Id + " visual={" + visualResult + "} nativeAttack={" + attackResult + "}");
            return ok;
        }
    }
}
