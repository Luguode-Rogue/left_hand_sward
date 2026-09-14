using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class MetaMeshVisualCloneSkill : SkillBase
    {
        public const string Id = "LHTest_MetaMeshVisualClone";

        public MetaMeshVisualCloneSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("实验2-MetaMesh视觉复制基线");
            Description = new TextObject("只复制当前主手 WeaponEntity 的 MetaMesh 到左手骨骼，用来保留旧视觉方案作为对照；不触发攻击。");
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
