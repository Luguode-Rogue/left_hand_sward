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
            Text = new TextObject("实验4-原生左手Collider攻击");
            Description = new TextObject("建立真实OffHand；攻击仍由实验1的MovementFlags进入原生melee状态机。进入ReleaseMelee后切到自定义actt_release_melee action，其AnimationClip带use_left_hand_during_attack。当前阶段只验证左手collider与原生Blow/weapon slot，不再把left_stance当作左手机制。");
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
