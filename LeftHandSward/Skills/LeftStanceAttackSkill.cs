using New_ZZZF;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    internal sealed class LeftStanceAttackSkill : SkillBase
    {
        public const string Id = "LHTest_LeftStanceFlags";

        public LeftStanceAttackSkill()
        {
            SkillID = Id;
            Type = SPSkillType.SubActive;
            Cooldown = 0f;
            ResourceCost = 0f;
            Text = new TextObject("左手测试-副手状态+原生攻击");
            Description = new TextObject("按 LeftAlt：先让原生系统尝试切换副手，再从 IPlayerInputEffector 注入原生攻击；观察副手状态是否影响 native melee。");
        }

        public override bool Activate(Agent casterAgent)
        {
            LeftHandAttackRuntime.TraceSkillActivation(Id, casterAgent);

            if (!LeftHandAttackRuntime.ProbeOffhand(
                    casterAgent,
                    true,
                    out string probeResult))
            {
                LeftHandAttackRuntime.Report(Id + " 副手探针失败: " + probeResult);
                return false;
            }

            bool attackOk = LeftHandAttackRuntime.QueueNativeAttack(
                casterAgent,
                Id,
                Agent.MovementControlFlag.AttackRight,
                out string attackResult);

            LeftHandAttackRuntime.Report(
                Id + " offhand={" + probeResult + "} nativeAttack={" + attackResult + "}");
            return attackOk;
        }
    }
}
