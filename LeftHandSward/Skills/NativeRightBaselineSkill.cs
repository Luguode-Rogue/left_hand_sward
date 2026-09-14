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
            Type = SPSkillType.MainActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("左手测试-普通动作基线");
            Description = new TextObject("按主主动技能键 E 独立播放普通单手右挥释放动作，不经过左键攻击，作为 SetActionChannel 对照组。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);
            if (!LeftHandAttackRuntime.TryGetActiveMeleeWeapon(casterAgent, out _, out string error))
            {
                LeftHandAttackRuntime.Report(Id + " 失败: " + error);
                return false;
            }

            bool ok = LeftHandAttackRuntime.PlayAction(
                casterAgent,
                "act_release_slashright_1h",
                (AnimFlags)0UL,
                out string result);

            if (ok)
                LeftHandAttackRuntime.BeginMeleeObservation(casterAgent, Id);

            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
