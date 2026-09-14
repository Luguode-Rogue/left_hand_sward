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
            Text = new TextObject("实验4-原生LeftStance左手攻击");
            Description = new TextObject("建立真实OffHand并强制Bannerlord原生LeftStance；攻击仍由实验1的MovementFlags进入原生melee状态机。Release阶段切到同为actt_release_melee的左手action，其AnimationClip由构建工具从原版left_stance挥砍clip克隆，并烧入use_left_hand_during_attack。命中、格挡、Blow和伤害全部由Bannerlord原生combat system生成。");
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
