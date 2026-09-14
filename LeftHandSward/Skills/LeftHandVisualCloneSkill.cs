using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class LeftHandVisualCloneSkill : SkillBase
    {
        public const string Id = "LHTest_VisualClone";

        public LeftHandVisualCloneSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("左手测试-纯视觉复制");
            Description = new TextObject("按副主动技能键 LeftAlt，把当前主手武器 MetaMesh 复制到 l_hand；只测试视觉附着，不触发攻击。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);
            bool ok = LeftHandAttackRuntime.AddLeftHandVisualClone(
                casterAgent,
                4f,
                out string result);
            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
