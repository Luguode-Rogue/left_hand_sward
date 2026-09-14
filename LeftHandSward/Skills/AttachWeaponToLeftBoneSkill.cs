using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class AttachWeaponToLeftBoneSkill : SkillBase
    {
        public const string Id = "LHTest_AttachWeaponToLeftBone";

        public AttachWeaponToLeftBoneSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("实验3-原生左手挂点视觉基线");
            Description = new TextObject("实机已验证位置正确：用 Agent.AttachWeaponToBone + Identity frame 把当前合法 MissionWeapon 挂到 Monster.OffHandItemBoneIndex；只做视觉基线，不攻击。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);
            bool ok = LeftHandAttackRuntime.AttachCurrentWeaponToLeftItemBone(casterAgent, false, 4f, out string result);
            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
