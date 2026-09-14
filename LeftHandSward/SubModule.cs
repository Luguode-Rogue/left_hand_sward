using System.Reflection;
using HarmonyLib;
using LeftHandSward.Skills;
using New_ZZZF;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward
{
    public sealed class SubModule : MBSubModuleBase
    {
        private const string HarmonyId = "LeftHandSward.NewZZZF.SkillExtension";
        private static bool _skillsRegistered;
        private Harmony _harmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            // Do NOT touch SkillFactory here. New_ZZZF.SkillFactory static initialization
            // constructs NullSkill, and Campaign object data is not ready during module load.
            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        protected override void OnSubModuleUnloaded()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            base.OnSubModuleUnloaded();
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);

            // Campaign initialization is still too early here: DefaultItemCategories can be null.
            // Campaign skills are registered by Harmony prefixes immediately before New_ZZZF
            // handles OnNewGameCreated / OnGameLoaded.
            //
            // Non-campaign game types do not enter NullSkill's Campaign-only ItemObject path,
            // so registration is safe here.
            if (!(game?.GameType is Campaign))
                RegisterSkills();
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            mission.AddMissionBehavior(new LeftHandAttackMissionBehavior());
        }

        internal static void RegisterSkills()
        {
            if (_skillsRegistered)
                return;

            // Set this only after SkillFactory has initialized successfully.
            SkillFactory.RegisterSkill(NativeRightBaselineSkill.Id, new NativeRightBaselineSkill());
            SkillFactory.RegisterSkill(LeftHandVisualCloneSkill.Id, new LeftHandVisualCloneSkill());
            SkillFactory.RegisterSkill(LeftHandFlagAttackSkill.Id, new LeftHandFlagAttackSkill());
            SkillFactory.RegisterSkill(SwitchHandsAttackSkill.Id, new SwitchHandsAttackSkill());
            SkillFactory.RegisterSkill(LeftStanceAttackSkill.Id, new LeftStanceAttackSkill());
            SkillFactory.RegisterSkill(VisualCloneAndAttackSkill.Id, new VisualCloneAndAttackSkill());

            _skillsRegistered = true;
            Debug.Print("[LeftHandSward] Registered 6 New_ZZZF extension skills.");
        }
    }

    /// <summary>
    /// New_ZZZF loads before this module. Its campaign callbacks are therefore the ideal
    /// safe point: Bannerlord object data is ready, while New_ZZZF has not yet executed
    /// SkillToItemObject or parsed skill IDs for this callback.
    /// </summary>
    [HarmonyPatch]
    internal static class NewZZZFSkillRegistrationPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(New_ZZZF.SubModule), nameof(New_ZZZF.SubModule.OnNewGameCreated))]
        private static void BeforeNewGameCreated()
        {
            SubModule.RegisterSkills();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(New_ZZZF.SubModule), nameof(New_ZZZF.SubModule.OnGameLoaded))]
        private static void BeforeGameLoaded()
        {
            SubModule.RegisterSkills();
        }
    }
}
