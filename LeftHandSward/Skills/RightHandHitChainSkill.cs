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
                + "直接命中、盾挡、武器格挡/招架都会触发下一轮。"
                + "命中事件只安排连击，不在碰撞回调里改动作；下一Tick先向当前动作通道发送act_none结束上一轮ReleaseMelee，"
                + "确认引擎已经离开ReleaseMelee后，再隔一Tick重启同一个原版Release。"
                + "每成功触发下一段，动作速度在上一段基础上提高10%；一次挥击无论命中多少目标只触发一次下一段。"
                + "用于验证显式结束上一轮后，ReleaseMelee是否可以连续重新武装原生碰撞，而不重新经过AttackReady。");
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
