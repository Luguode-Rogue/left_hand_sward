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
            Text = new TextObject("右手完整原生连击测试");
            Description = new TextObject(
                "纯原版右手动作测试：每一刀都通过AttackRight重新进入Bannerlord原生近战状态机。"
                + "直接命中、盾挡、武器格挡/招架都会触发下一轮。"
                + "命中事件只安排连击；下一Tick先发送act_none结束上一轮攻击，确认已经离开Release/Blocked/Parried等攻击状态后，"
                + "再隔一Tick重新注入AttackRight，让引擎完整生成AttackReady→ReleaseMelee。"
                + "第二刀起，当前连击倍率会同时应用到Ready与Release动作，每轮在上一轮基础上提高10%。"
                + "一次挥击无论命中多少目标只触发一次下一段。"
                + "用于验证取消后摇后，完整原生攻击能否连续重新武装碰撞。"
            );
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
