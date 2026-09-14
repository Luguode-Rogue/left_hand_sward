using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class LeftGripTransformSkill : SkillBase
    {
        public const string Id = "LHTest_LeftGripTransform";

        public LeftGripTransformSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("实验4-右手握持变换迁移到左手");
            Description = new TextObject("读取真实主手 WeaponEntity 相对 MainHandItemBone 的局部握持变换，再用 Agent.AttachWeaponToBone 将同一变换挂到 OffHandItemBone；不攻击。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);
            bool ok = LeftHandAttackRuntime.AttachCurrentWeaponToLeftItemBone(casterAgent, true, 4f, out string result);
            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
