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
            Text = new TextObject("实验4-真实OffHand状态探针");
            Description = new TextObject("实机已验证可建立 valid OffHand。优先选择另一个武器槽中可单手使用的近战武器；只建立 native OffHand，不攻击。");
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
