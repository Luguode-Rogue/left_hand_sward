using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class NativeAlternativeAttackProbeSkill : SkillBase
    {
        public const string Id = "LHTest_NativeAlternativeAttackProbe";

        public NativeAlternativeAttackProbeSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("实验5-原生Kick/Bash探针");
            Description = new TextObject(
                "不改动作、不改碰撞、不建立临时OffHand，直接调用Agent.KickClear()。"
                + "用于观察Bannerlord原生Kick/WeaponBash/ShieldBash的AffectorWeaponSlot、"
                + "AttackBoneIndex和IsAlternativeAttack，作为真正OffHand攻击路径的对照。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);

            bool ok = LeftHandAttackRuntime.StartNativeAlternativeAttackProbe(
                casterAgent,
                Id,
                out string result);

            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
