using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class NativeLeftHandAttackSkill : SkillBase
    {
        public const string Id = "LHTest_NativeLeftHandAttack";

        public NativeLeftHandAttackSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("实验4-左臂原生命中");
            Description = new TextObject("项目主线实验：不建立真实OffHand，也不要求第二把武器或单手武器。当前主手只要能进入原生melee状态机即可；左手武器视觉直接复用实验3已验证的Agent.AttachWeaponToBone + OffHandItemBoneIndex + Identity挂载，ReleaseMelee使用左臂motion并按当前1H/2H武器CombatParameter计算伤害。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);
            bool ok = LeftHandAttackRuntime.StartCustomLeftHandAttack(
                casterAgent,
                Id,
                out string result);
            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
