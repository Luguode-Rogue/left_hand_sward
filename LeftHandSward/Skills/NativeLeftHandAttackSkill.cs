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
            Text = new TextObject("实验4-原生左手近战攻击");
            Description = new TextObject("要求另一个装备槽当前就是可单手使用的近战武器。先建立安全 native OffHand，再用正常 MovementFlags 建立攻击上下文；仅在 native 进入 ReleaseMelee 后给当前动作追加 anf_use_left_hand_during_attack。结束后自动恢复 OffHand。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);
            bool ok = LeftHandAttackRuntime.QueueNativeLeftHandAttack(
                casterAgent,
                Id,
                Agent.MovementControlFlag.AttackRight,
                out string result);
            LeftHandAttackRuntime.Report(Id + " " + result);
            return ok;
        }
    }
}
