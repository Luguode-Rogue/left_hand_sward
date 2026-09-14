using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class OffHandNativeAttackSkill : SkillBase
    {
        public const string Id = "LHTest_OffHandNativeAttack";

        public OffHandNativeAttackSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("实验5-OffHand状态+原生攻击");
            Description = new TextObject("同一次激活先自动建立已验证的 native OffHand，再通过 MovementFlags 注入原生 AttackRight；观察动作和 OnMeleeHit 最终使用主手还是副手。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);
            bool ok = LeftHandAttackRuntime.QueueOffHandNativeAttack(casterAgent, Id, Agent.MovementControlFlag.AttackRight, out string result);
            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
