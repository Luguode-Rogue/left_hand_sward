using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class StickItemWeaponSweepProbeSkill : SkillBase
    {
        public const string Id = "LHTest_StickItemWeaponSweepProbe";

        public StickItemWeaponSweepProbeSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("左手真实武器碰撞探针");
            Description = new TextObject(
                "单变量测试：不复制模型、不AttachWeaponToBone、不建立真实OffHand、"
                + "不移动WeaponEntity、不改ActionSet、不做IK。"
                + "仍使用实验4已验证的左臂Release motion与当前1H/2H武器CombatParameter，"
                + "但移除use_left_hand_during_attack，仅给Clip加入stick_item_to_left_hand。"
                + "用于验证真实主手武器是否被原生动画系统带到左手，并以实际武器长度参与native melee碰撞。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);

            bool ok = LeftHandAttackRuntime.StartStickItemWeaponSweepProbe(
                casterAgent,
                Id,
                out string result);

            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
