using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class LeftHandFlagAttackSkill : SkillBase
    {
        public const string Id = "LHTest_LeftHandFlags";

        public LeftHandFlagAttackSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("左手测试-视觉+原生攻击");
            Description = new TextObject("按 LeftAlt：先复制主手武器到 l_hand，再通过原生 MovementFlags 发起攻击。已停用会造成异常的左手 AnimFlags。");
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

            bool attackOk = LeftHandAttackRuntime.QueueNativeAttack(
                casterAgent,
                Id,
                Agent.MovementControlFlag.AttackRight,
                out string attackResult);

            LeftHandAttackRuntime.Report(
                Id + " visual={" + visualResult + "} nativeAttack={" + attackResult + "}");
            return attackOk;
        }
    }
}
