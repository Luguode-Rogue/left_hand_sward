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
            Text = new TextObject("实验3-AttachWeaponToBone左手挂点");
            Description = new TextObject("只测试 Agent.AttachWeaponToBone：把当前合法 MissionWeapon 以 Identity local frame 挂到 Monster.OffHandItemBoneIndex；不修改装备、不攻击。");
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
