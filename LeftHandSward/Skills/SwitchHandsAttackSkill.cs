using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class SwitchHandsAttackSkill : SkillBase
    {
        public const string Id = "LHTest_SwitchHandsFlags";

        public SwitchHandsAttackSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("左手测试-真实副手剑");
            Description = new TextObject("按 LeftAlt：复制当前主手武器为 Mission 临时副本，仅给副本添加 HeldInOffHand，然后通过原生装备 API 尝试建立真实 OffHand。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);

            bool ok = LeftHandAttackRuntime.EquipTemporaryOffhandClone(
                casterAgent,
                out string result);

            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
