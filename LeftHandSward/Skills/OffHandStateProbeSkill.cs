using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class OffHandStateProbeSkill : SkillBase
    {
        public const string Id = "LHTest_OffHandStateProbe";

        public OffHandStateProbeSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("实验5-真实OffHand状态探针");
            Description = new TextObject("要求另一个武器槽中已有第二把合法近战武器。只调用 SetWieldedItemIndexAsClient(OffHand, slot) 建立原生 OffHand 状态；不创建临时 ItemObject，不攻击。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);
            bool ok = LeftHandAttackRuntime.EstablishOffHandStateFromExistingWeapon(casterAgent, out string result);
            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
