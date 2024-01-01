using BepInEx;
using BepInEx.Configuration;
using Dissonance;
using GameNetcodeStuff;
using HarmonyLib;
using LC_API.GameInterfaceAPI.Features;
using Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace Network
{
    [HarmonyPatch(typeof(Player))]
    public class PlayerPatch
    {
        private static readonly string BUNDLE_PATH = Path.Combine(Paths.PluginPath, "2018-LC_API", "Bundles", "networking");

        private const string PLAYER_NETWORKING_ASSET_LOCATION = "assets/lc_api/playernetworkingprefab.prefab";

        static GameObject networkPrefab;

        [HarmonyPatch(typeof(GameNetworkManager), "Start")]
        [HarmonyPostfix]
        [HarmonyPriority(1)]
        public static void Init()
        {
            
            GameObject Prefab = Traverse.Create(typeof(Player)).Property("PlayerNetworkPrefab").GetValue<GameObject>();
            Prefab.AddComponent<CustomNetworkHandler>();

        }

        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        // [HarmonyAfter("ModAPI")]
        public static void Init(ref Player __instance)
        {
/*            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                var networkHandlerHost = UnityEngine.Object.Instantiate(networkPrefab, UnityEngine.Vector3.zero, UnityEngine.Quaternion.identity);
                networkHandlerHost.GetComponent<NetworkObject>().Spawn(false);
            }*/
            
        }

    }

    public class CustomNetworkHandler : NetworkBehaviour
    {

        
        public static CustomNetworkHandler Instance { get; private set; }
        public override void OnNetworkSpawn()
        {
            Instance = this;
            base.OnNetworkSpawn();
        }

        [ClientRpc]
        public void SendRoundScreamStatsClientRpc(NetworkBehaviourReference[] controllers, int[] values)
        {
            for (int i = 0; i < controllers.Length; i++)
            {
                controllers[i].TryGet(out PlayerControllerB player);
                ScreamCounter.ScreamCounter.currentPerPlayerScreams[player] = values[i];
            }

        }

        [ServerRpc(RequireOwnership = false)]
        public void AddToScreamCounterServerRpc(NetworkBehaviourReference playerref)
        {
            playerref.TryGet(out PlayerControllerB player);

            if (ScreamCounter.ScreamCounter.currentPerPlayerScreams.ContainsKey(player))
                ScreamCounter.ScreamCounter.currentPerPlayerScreams[player] = ScreamCounter.ScreamCounter.currentPerPlayerScreams[player] + 1;
            else
                ScreamCounter.ScreamCounter.currentPerPlayerScreams[player] = 1;

            if (ScreamCounter.ScreamCounter.screamCooldown <= 0f)
            {
                ScreamCounter.ScreamCounter.screamCounter++;
                ScreamCounter.ScreamCounter.screamCooldown = 10f;
            }
            else
                ScreamCounter.ScreamCounter.screamCooldown -= Time.deltaTime;
                

            Debug.Log($"player {player.name} added to scream counter. His counter: {ScreamCounter.ScreamCounter.currentPerPlayerScreams[player]}, General: {ScreamCounter.ScreamCounter.screamCounter}");
        }



        
    }
}


namespace ScreamCounter
{
    [BepInPlugin(modGUID, modName, modVersion)]
    public class ScreamCounter : BaseUnityPlugin
    {

        private const string modGUID = "frsk.Scream_Counter";
        private const string modName = "ScreamCounter";
        private const string modVersion = "1.0.0";

        public static ConfigEntry<float> screamFromAveragePercentage;


        private Harmony harmony = new Harmony("frsk.Scream_Counter");

        public static ScreamCounter Instance;

        public static int screamCounter = 0;
        public static int averageCount = 0;
        public static float averageSpeechAmplitude;
        public static float totalSpeechAmplitude;
        public static int currentPlayerCheck = 0;
        public static float screamCooldown = 0f;
        public static float averageDetectCooldown = 0f;
        public static bool corRunning = false;

        public static Dictionary<PlayerControllerB, int> currentPerPlayerScreams = new Dictionary<PlayerControllerB, int>();


        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            screamFromAveragePercentage = Config.Bind("General", "ScreamFromAveragePercentage", 1.5f, "");

            int[] ints = new int[0];

            // Plugin startup logic
            Logger.LogInfo($"Plugin {modGUID} is loaded!");

            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }


            harmony.PatchAll();
        }


        [HarmonyPatch(typeof(GameNetworkManager))]
        internal class GameNetworkManagerPatch
        {
            [HarmonyPatch("SaveGameValues")]
            [HarmonyPostfix]
            static void SaveGameValuesPatch(ref GameNetworkManager __instance)
            {
                if (!__instance.isHostingGame || !StartOfRound.Instance.inShipPhase)
                {
                    return;
                }

                ES3.Save<int>("Stats_ScreamCounter", screamCounter, __instance.currentSaveFileName);
                Debug.Log("Scream Counter Successfully saved. Current value is: ");
            }


            [HarmonyPatch("ResetSaveGameValues")]
            static void ResetSaveGameValuesPatch(ref GameNetworkManager _instance) 
            {

                if (!_instance.isHostingGame)
                {
                    return;
                }

                screamCounter = 0;
                ES3.Save<int>("Stats_ScreamCounter", 0, _instance.currentSaveFileName);
            }

        }


        [HarmonyPatch(typeof(StartOfRound))]
        internal class StartOfRoundPatch
        {

            [HarmonyPatch("SetTimeAndPlanetToSavedSettings")]
            [HarmonyPostfix]
            static void SetTimeAndPlanetToSavedSettingsPatchServerRpc(ref StartOfRound __instance)
            {
                string curSaveFileName = GameNetworkManager.Instance.currentSaveFileName;
                if (ES3.KeyExists("Stats_ScreamCounter"))
                {
                    screamCounter = ES3.Load("Stats_ScreamCounter", curSaveFileName, 0);
                    Debug.Log("Scream Counter Successfully loaded. Current value is: " + screamCounter);
                }

            }


            [HarmonyPatch("DetectVoiceChatAmplitude")]
            [HarmonyPostfix]
            static void DetectVoiceChatAmplitudePatch(ref StartOfRound __instance)
            {
                var pc = GameNetworkManager.Instance.localPlayerController;

                if (pc == null)
                    throw new Exception("pc is null");

                var cd = Traverse.Create(__instance).Field<float>("voiceChatNoiseCooldown").Value;

                var vs = __instance.voiceChatModule.FindPlayer(__instance.voiceChatModule.LocalPlayerName);

                var dontAppendAverage = false;

                float num = vs.Amplitude / Mathf.Clamp(__instance.averageVoiceAmplitude, 0.008f, 0.5f);
                if (!(vs.IsSpeaking && averageDetectCooldown <= 0f && num > 3f))
                    dontAppendAverage = true;
                    
                
                if (averageCount > 5 && !pc.isPlayerDead && __instance.shipHasLanded && screamCooldown <= 0.0f)
                {
                    if (vs.Amplitude / averageSpeechAmplitude > screamFromAveragePercentage.Value)
                    {
                        CustomNetworkHandler.Instance.AddToScreamCounterServerRpc(pc);
                        screamCooldown = 10f;
                        dontAppendAverage = true;
                    }
                        
                }
                else if (screamCooldown > 0.0f)
                {
                    screamCooldown -= Time.deltaTime;
                }

                if (!dontAppendAverage)
                {
                    averageCount++;
                    totalSpeechAmplitude += vs.Amplitude;
                    averageSpeechAmplitude = totalSpeechAmplitude / averageCount;
                    Debug.Log("Added amplitude to average. Current average: " + averageSpeechAmplitude);
                    averageDetectCooldown = 2f;
                }
                averageDetectCooldown -= Time.deltaTime;




            }

            [HarmonyPatch("WritePlayerNotes")]
            [HarmonyPostfix]
            static void WritePlayerNotesPatch(ref StartOfRound __instance)
            {
                var sortedDict = from entry in currentPerPlayerScreams orderby entry.Value descending select entry;

                var targetPlayer = sortedDict.FirstOrDefault().Key;

                if (sortedDict.FirstOrDefault().Value > 2)
                    __instance.gameStats.allPlayerStats[targetPlayer.playerClientId].playerNotes.Add("Screamed the most");

            }


            [HarmonyPatch("EndGameServerRpc")]
            [HarmonyPostfix]
            static void EndGameServerRpc(ref StartOfRound __instance)
            {
                NetworkBehaviourReference[] controllers = new NetworkBehaviourReference[currentPerPlayerScreams.Count];
                for (int i = 0; i< controllers.Length; i++) 
                {
                    controllers[i] = new NetworkBehaviourReference(currentPerPlayerScreams.Keys.ToArray()[i]);
                }

                int[] values = currentPerPlayerScreams.Values.ToArray();

                CustomNetworkHandler.Instance.SendRoundScreamStatsClientRpc(controllers, values);
            }

            [HarmonyPatch("FirePlayersAfterDeadlineClientRpc")]
            [HarmonyPostfix]
            static void FirePlayersAfterDeadlineClientRpcPatch(ref StartOfRound __instance, int[] endGameStats)
            {
                HUDManager.Instance.EndOfRunStatsText.text = string.Format("Days on the job: {0}\n", endGameStats[0]) + string.Format("Scrap value collected: {0}\n", endGameStats[1]) + string.Format("Deaths: {0}\n", endGameStats[2]) + string.Format("Steps taken: {0}", endGameStats[3] + string.Format("\nScreams: {0}", endGameStats[4]));
                screamCounter = 0;
            }

            [HarmonyPatch("ResetStats")]
            [HarmonyPostfix]
            static void ResetStatsPatch()
            {
                currentPerPlayerScreams.Clear();
            }

            [HarmonyPatch("Start")]
            [HarmonyPrefix]
            static void StartResetCounter(ref StartOfRound __instance)
            {
                if (__instance.IsServer)
                    screamCounter = 0;
            }

            [HarmonyPatch("GetEndgameStatsInOrder")]
            [HarmonyPrefix]
            static bool GetEndgameStatsInOrderPatch(ref StartOfRound __instance, ref int[] __result)
            {
                __result = [__instance.gameStats.daysSpent, __instance.gameStats.scrapValueCollected, __instance.gameStats.deaths, __instance.gameStats.allStepsTaken, screamCounter];
                return false;
            }
        }
    }
}


