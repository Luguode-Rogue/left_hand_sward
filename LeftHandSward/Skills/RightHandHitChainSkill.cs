using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class RightHandHitChainSkill : SkillBase
    {
        public const string Id = "LHTest_RightHandHitChainRelease";

        public RightHandHitChainSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("右手命中连击Release测试");
            Description = new TextObject(
                "纯原版右手动作测试：第一刀通过AttackRight正常进入原生攻击；"
                + "命中或攻击被格挡后，不等待后摇结束，下一Tick直接重启同一个原版ReleaseMelee。"
                + "每成功触发下一段，动作速度在上一段基础上提高10%。"
                + "一次挥击无论命中多少目标只触发一次下一段。"
                + "用于验证ReleaseMelee是否可以连续重新武装原生碰撞，而不重新经过AttackReady。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);

            bool ok = RightHandHitChainRuntime.Start(
                casterAgent,
                Id,
                out string result);

            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
