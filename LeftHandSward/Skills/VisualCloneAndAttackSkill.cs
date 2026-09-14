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
            Type = SPSkillType.MainActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("左手测试-视觉+左手攻击");
            Description = new TextObject("按主主动技能键 E：先把右手武器视觉复制到左手，再用 left_stance + 左手相关 flags 独立发动攻击。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);
            if (!LeftHandAttackRuntime.AddLeftHandVisualClone(casterAgent, 2.5f, out string visualResult))
            {
                LeftHandAttackRuntime.Report(Id + " 视觉阶段失败: " + visualResult);
                return false;
            }

            AnimFlags flags = AnimFlags.anf_switch_item_between_hands
                            | AnimFlags.anf_stick_item_to_left_hand
                            | AnimFlags.anf_use_left_hand_during_attack
                            | AnimFlags.anf_enable_left_hand_ik;

            bool attackOk = LeftHandAttackRuntime.PlayAction(
                casterAgent,
                "act_release_slashright_1h_left_stance",
                flags,
                out string attackResult);

            if (attackOk)
                LeftHandAttackRuntime.BeginMeleeObservation(casterAgent, Id);

            LeftHandAttackRuntime.Report(
                Id + " visual={" + visualResult + "} attack={" + attackResult + "}");
            return attackOk;
        }
    }
}
