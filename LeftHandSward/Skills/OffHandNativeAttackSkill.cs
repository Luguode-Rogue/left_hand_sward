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
            Text = new TextObject("实验6-OffHand原生攻击探针");
            Description = new TextObject("仅在真实 OffHand 已经有效时注入原生 AttackRight，观察 Bannerlord 最终选择哪只手、哪把武器和哪条 melee sweep。不会自行创建 OffHand。");
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
