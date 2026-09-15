using System;
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
            //
            // Do not PatchAll here. Early blanket Harmony patching of Agent methods can corrupt
            // human animation state ("human bullet/folded-man"). We only need two lifecycle
            // prefixes on New_ZZZF, so install exactly those methods and nothing Agent-related.
            _harmony = new Harmony(HarmonyId);
            InstallNewZZZFLifecyclePatches();
            LeftHandSwardLog.Info("Lifecycle", "Targeted Harmony lifecycle patches installed id=" + HarmonyId);
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

        private void InstallNewZZZFLifecyclePatches()
        {
            var newGameTarget =
                AccessTools.Method(
                    typeof(New_ZZZF.SubModule),
                    nameof(New_ZZZF.SubModule.OnNewGameCreated));
            var gameLoadedTarget =
                AccessTools.Method(
                    typeof(New_ZZZF.SubModule),
                    nameof(New_ZZZF.SubModule.OnGameLoaded));
            var beforeNewGame =
                AccessTools.Method(
                    typeof(NewZZZFSkillRegistrationPatch),
                    "BeforeNewGameCreated");
            var beforeGameLoaded =
                AccessTools.Method(
                    typeof(NewZZZFSkillRegistrationPatch),
                    "BeforeGameLoaded");

            if (newGameTarget == null ||
                gameLoadedTarget == null ||
                beforeNewGame == null ||
                beforeGameLoaded == null)
            {
                throw new MissingMethodException(
                    "Unable to resolve New_ZZZF lifecycle methods for targeted Harmony patching.");
            }

            _harmony.Patch(
                newGameTarget,
                prefix: new HarmonyMethod(beforeNewGame));
            _harmony.Patch(
                gameLoadedTarget,
                prefix: new HarmonyMethod(beforeGameLoaded));
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
            SkillFactory.RegisterSkill(MetaMeshVisualCloneSkill.Id, new MetaMeshVisualCloneSkill());
            SkillFactory.RegisterSkill(AttachWeaponToLeftBoneSkill.Id, new AttachWeaponToLeftBoneSkill());
            SkillFactory.RegisterSkill(NativeLeftHandAttackSkill.Id, new NativeLeftHandAttackSkill());
            SkillFactory.RegisterSkill(
                StickItemWeaponSweepProbeSkill.Id,
                new StickItemWeaponSweepProbeSkill());

            _skillsRegistered = true;
            LeftHandSwardLog.Info("SkillRegistry", "RegisterSkills SUCCESS count=5");
            Debug.Print("[LeftHandSward] Registered 5 New_ZZZF extension skills.");
        }
    }

    /// <summary>
    /// New_ZZZF loads before this module. Its campaign callbacks are therefore the ideal
    /// safe point: Bannerlord object data is ready, while New_ZZZF has not yet executed
    /// SkillToItemObject or parsed skill IDs for this callback.
    /// </summary>
    internal static class NewZZZFSkillRegistrationPatch
    {
        private static void BeforeNewGameCreated()
        {
            LeftHandSwardLog.Info("Lifecycle", "Prefix New_ZZZF.OnNewGameCreated");
            SubModule.RegisterSkills();
        }

        private static void BeforeGameLoaded()
        {
            LeftHandSwardLog.Info("Lifecycle", "Prefix New_ZZZF.OnGameLoaded");
            SubModule.RegisterSkills();
        }
    }
}
