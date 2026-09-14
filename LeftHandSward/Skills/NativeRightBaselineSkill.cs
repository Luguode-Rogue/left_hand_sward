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
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("实验1-右手原生攻击基线");
            Description = new TextObject("基线实验：按 LeftAlt 注入原生 AttackRight，确认 MovementFlags -> 右手原生动作 -> 右手 OnMeleeHit 链路正常。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);

            bool ok = LeftHandAttackRuntime.QueueNativeAttack(
                casterAgent,
                Id,
                Agent.MovementControlFlag.AttackRight,
                out string result);

            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
