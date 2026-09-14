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
            Text = new TextObject("实验4-自定义左手攻击原型");
            Description = new TextObject("不再使用 native OffHand，也不触发右手攻击。复用已验证的左手挂点显示当前武器，用左手前方自定义 sweep 检测并通过 Blow 造成伤害。");
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
