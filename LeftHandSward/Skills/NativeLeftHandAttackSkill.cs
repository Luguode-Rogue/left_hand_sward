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
            Text = new TextObject("实验4-原生MainHand左移攻击");
            Description = new TextObject("把AnimationSystemData中的MainHand/OffHand骨骼角色交换，使真实主手武器和原生近战系统指向左手；用SkeletonPostIntegrateCallback把原版右手挥砍骨骼结果镜像到左臂并冻结右臂。攻击输入、碰撞、格挡、Blow和伤害仍全部走实验1的Bannerlord原生melee pipeline。");
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
