using LeftHandSward.Skills;
using New_ZZZF;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward
{
    public sealed class SubModule : MBSubModuleBase
    {
        private static bool _skillsRegistered;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            RegisterSkills();
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            mission.AddMissionBehavior(new LeftHandAttackMissionBehavior());
        }

        private static void RegisterSkills()
        {
            if (_skillsRegistered)
                return;

            _skillsRegistered = true;

            SkillFactory.RegisterSkill(NativeRightBaselineSkill.Id, new NativeRightBaselineSkill());
            SkillFactory.RegisterSkill(LeftHandVisualCloneSkill.Id, new LeftHandVisualCloneSkill());
            SkillFactory.RegisterSkill(LeftHandFlagAttackSkill.Id, new LeftHandFlagAttackSkill());
            SkillFactory.RegisterSkill(SwitchHandsAttackSkill.Id, new SwitchHandsAttackSkill());
            SkillFactory.RegisterSkill(LeftStanceAttackSkill.Id, new LeftStanceAttackSkill());
            SkillFactory.RegisterSkill(VisualCloneAndAttackSkill.Id, new VisualCloneAndAttackSkill());

            Debug.Print("[LeftHandSward] Registered 6 New_ZZZF extension skills.");
        }
    }
}
