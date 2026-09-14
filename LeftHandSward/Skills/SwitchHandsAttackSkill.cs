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
            Type = SPSkillType.MainActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("左手测试-换手Flags");
            Description = new TextObject("按主主动技能键 E 独立播放右挥释放动作，测试 switch-item-between-hands + use-left-hand，不经过左键攻击。");
        }

        public override bool Activate(Agent casterAgent)
        {
            if (!LeftHandAttackRuntime.TryGetActiveMeleeWeapon(casterAgent, out _, out string error))
            {
                LeftHandAttackRuntime.Report(Id + " 失败: " + error);
                return false;
            }

            AnimFlags flags = AnimFlags.anf_switch_item_between_hands
                            | AnimFlags.anf_use_left_hand_during_attack
                            | AnimFlags.anf_enable_left_hand_ik;

            bool ok = LeftHandAttackRuntime.PlayAction(
                casterAgent,
                "act_release_slashright_1h",
                flags,
                out string result);

            if (ok)
                LeftHandAttackRuntime.BeginMeleeObservation(casterAgent, Id);

            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
