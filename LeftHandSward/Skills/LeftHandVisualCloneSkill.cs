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
            Type = SPSkillType.MainActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("左手测试-视觉复制");
            Description = new TextObject("按主主动技能键 E，把当前右手武器 MetaMesh 复制到 l_hand 骨骼；只测试双持视觉，不触发原版左键攻击。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);
            bool ok = LeftHandAttackRuntime.AddLeftHandVisualClone(casterAgent, 4f, out string result);
            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
