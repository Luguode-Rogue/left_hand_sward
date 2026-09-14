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
            Text = new TextObject("实验4-程序IK左手挥砍");
            Description = new TextObject("要求另一个装备槽有单手近战武器。左手第二把武器只负责视觉、IK和碰撞范围；最终伤害始终使用激活瞬间右手武器，并交给Bannerlord原版StrikeMagnitude/AgentApplyDamage模型计算。");
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
