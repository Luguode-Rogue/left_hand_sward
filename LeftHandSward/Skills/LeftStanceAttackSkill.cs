using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class LeftStanceAttackSkill : SkillBase
    {
        public const string Id = "LHTest_LeftStanceFlags";

        public LeftStanceAttackSkill()
        {
            SkillID = Id;
            Type = SPSkillType.MainActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("左手测试-镜像LeftStance动作");
            Description = new TextObject("按主主动技能键 E 独立使用原版 slashleft_1h_left_stance 释放动作，并叠加换手/左手攻击 flags，测试左右镜像方向。");
        }

        public override bool Activate(Agent casterAgent)
        {
            if (!LeftHandAttackRuntime.TryGetActiveMeleeWeapon(casterAgent, out _, out string error))
            {
                LeftHandAttackRuntime.Report(Id + " 失败: " + error);
                return false;
            }

            AnimFlags flags = AnimFlags.anf_switch_item_between_hands
                            | AnimFlags.anf_stick_item_to_left_hand
                            | AnimFlags.anf_use_left_hand_during_attack
                            | AnimFlags.anf_enable_left_hand_ik;

            bool ok = LeftHandAttackRuntime.PlayAction(
                casterAgent,
                "act_release_slashleft_1h_left_stance",
                flags,
                out string result);

            if (ok)
                LeftHandAttackRuntime.BeginMeleeObservation(casterAgent, Id);

            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
