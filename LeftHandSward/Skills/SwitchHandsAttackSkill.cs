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
            Text = new TextObject("左手测试-原生副手探针");
            Description = new TextObject("按 LeftAlt 调用游戏自身 WieldNextWeapon(OffHand)，记录真实副手槽/副手武器状态；不使用 switch_item_between_hands 动画 flag。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);

            bool ok = LeftHandAttackRuntime.ProbeOffhand(
                casterAgent,
                true,
                out string result);

            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
