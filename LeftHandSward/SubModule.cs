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

            LeftHandSwardLog.Initialize();
            LeftHandSwardLog.Info("Lifecycle", "OnSubModuleLoad BEGIN log=" + LeftHandSwardLog.LogPath);

            // Do NOT touch SkillFactory here. New_ZZZF.SkillFactory static initialization
            // constructs NullSkill, and Campaign object data is not ready during module load.
            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            LeftHandSwardLog.Info("Lifecycle", "Harmony patches installed id=" + HarmonyId);
        }

        protected override void OnSubModuleUnloaded()
        {
            LeftHandSwardLog.Info("Lifecycle", "OnSubModuleUnloaded");
            // Do not call Harmony.UnpatchSelf/UnpatchAll here.
            // Bannerlord distributions may expose different Harmony API versions,
            // and the process is shutting down when the module unloads anyway.
            _harmony = null;
            base.OnSubModuleUnloaded();
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            LeftHandSwardLog.Info("Lifecycle", "OnGameStart gameType=" + (game?.GameType == null ? "null" : game.GameType.GetType().FullName));

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
            LeftHandSwardLog.Info("Lifecycle", "OnMissionBehaviorInitialize mission=" + (mission == null ? "null" : mission.GetType().FullName));
            mission.AddMissionBehavior(new LeftHandAttackMissionBehavior());
        }

        internal static void RegisterSkills()
        {
            if (_skillsRegistered)
            {
                LeftHandSwardLog.Info("SkillRegistry", "RegisterSkills skipped: already registered");
                return;
            }

            LeftHandSwardLog.Info("SkillRegistry", "RegisterSkills BEGIN");

            // Set this only after SkillFactory has initialized successfully.
            SkillFactory.RegisterSkill(NativeRightBaselineSkill.Id, new NativeRightBaselineSkill());
            SkillFactory.RegisterSkill(LeftHandVisualCloneSkill.Id, new LeftHandVisualCloneSkill());
            SkillFactory.RegisterSkill(LeftHandFlagAttackSkill.Id, new LeftHandFlagAttackSkill());
            SkillFactory.RegisterSkill(SwitchHandsAttackSkill.Id, new SwitchHandsAttackSkill());
            SkillFactory.RegisterSkill(LeftStanceAttackSkill.Id, new LeftStanceAttackSkill());
            SkillFactory.RegisterSkill(VisualCloneAndAttackSkill.Id, new VisualCloneAndAttackSkill());

            _skillsRegistered = true;
            LeftHandSwardLog.Info("SkillRegistry", "RegisterSkills SUCCESS count=6");
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
            LeftHandSwardLog.Info("Lifecycle", "Prefix New_ZZZF.OnNewGameCreated");
            SubModule.RegisterSkills();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(New_ZZZF.SubModule), nameof(New_ZZZF.SubModule.OnGameLoaded))]
        private static void BeforeGameLoaded()
        {
            LeftHandSwardLog.Info("Lifecycle", "Prefix New_ZZZF.OnGameLoaded");
            SubModule.RegisterSkills();
        }
    }
}
