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
            Text = new TextObject("左手测试-视觉+副手状态+原生攻击");
            Description = new TextObject("按 LeftAlt：复制武器到左手、调用原生副手切换，再通过 MovementFlags 发起真实原生攻击。全程不手动指定 release 动作。");
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

            LeftHandAttackRuntime.ProbeOffhand(
                casterAgent,
                true,
                out string probeResult);

            bool attackOk = LeftHandAttackRuntime.QueueNativeAttack(
                casterAgent,
                Id,
                Agent.MovementControlFlag.AttackRight,
                out string attackResult);

            LeftHandAttackRuntime.Report(
                Id
                + " visual={" + visualResult + "}"
                + " offhand={" + probeResult + "}"
                + " nativeAttack={" + attackResult + "}");
            return attackOk;
        }
    }
}
