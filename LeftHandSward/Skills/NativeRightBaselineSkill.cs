using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class NativeRightBaselineSkill : SkillBase
    {
        public const string Id = "LHTest_NativeRightBaseline";

        public NativeRightBaselineSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("左手测试-原生攻击基线");
            Description = new TextObject("按副主动技能键 LeftAlt，通过 IPlayerInputEffector + MovementFlags 注入原生 AttackRight；不指定动作名、不修改动画 flags。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);
            LeftHandAttackRuntime.RemoveTemporaryOffhand(casterAgent);

            bool ok = LeftHandAttackRuntime.QueueNativeAttack(
                casterAgent,
                Id,
                Agent.MovementControlFlag.AttackRight,
                out string result);

            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
