using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using UnityEngine;

namespace Rooftop.Editor
{
    [InitializeOnLoad]
    public static class RoofCheck
    {
        const string Active = "Rooftop.BatchCheck";
        static double started;
        static int stage;
        static RoofFish fish;
        static CatInventory bag;

        static RoofCheck()
        {
            if (SessionState.GetBool(Active,false)) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            const string path = "Assets/Rooftop/VentRoof.unity";
            var scene = EditorSceneManager.OpenScene(path);
            foreach (var name in new[]{"Fish","PracticeCat"})
            {
                string prefab = $"Assets/Rooftop/{name}.prefab";
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
                asset.GetComponent<NetworkObject>().SendMessage("OnValidate");
                PrefabUtility.SavePrefabAsset(asset);
            }
            foreach (var item in UnityEngine.Object.FindObjectsByType<RoofFish>(FindObjectsSortMode.None))
            {
                var net = item.GetComponent<NetworkObject>();
                net.SendMessage("OnValidate");
                PrefabUtility.RecordPrefabInstancePropertyModifications(net);
            }
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            SessionState.SetBool(Active,true);
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || !NetworkManager.Singleton) return;
            try
            {
                var manager = NetworkManager.Singleton;
                if (stage == 0)
                {
                    started = EditorApplication.timeSinceStartup;
                    if (!manager.StartHost()) throw new Exception("Host failed");
                    stage++;
                }
                if (EditorApplication.timeSinceStartup - started > 25) throw new TimeoutException("Stage " + stage);
                if (stage == 1 && manager.LocalClient.PlayerObject && EditorApplication.timeSinceStartup - started > 3)
                {
                    var fishes = UnityEngine.Object.FindObjectsByType<RoofFish>(FindObjectsSortMode.None);
                    if (fishes.Length != 12 || fishes.Any(f => !f.IsSpawned)) throw new Exception("Fish spawn failed");
                    fish = fishes[0];
                    bag = manager.LocalClient.PlayerObject.GetComponent<CatInventory>();
                    var controller = bag.GetComponent<CharacterController>();
                    if (!controller.isGrounded || Mathf.Abs(bag.transform.position.y - 1f) > .15f)
                        throw new Exception("Spawn did not settle on roof: " + bag.transform.position);
                    controller.enabled = false;
                    bag.transform.position = fish.transform.position + Vector3.up * .7f;
                    controller.enabled = true;
                    stage++;
                }
                if (stage == 2 && bag.HasFish)
                {
                    if (!bag.TryDeliver() || bag.TryDeliver()) throw new Exception("Delivery failed");
                    bag.GetComponent<CharacterController>().enabled = false;
                    bag.transform.position = Vector3.up + Vector3.left * 9;
                    bag.GetComponent<CharacterController>().enabled = true;
                    stage++;
                }
                if (stage == 3 && fish.GetComponent<Collider>().enabled)
                {
                    Debug.Log("ROOF CHECK PASS: host, player, 12 fish, pickup, single delivery, respawn");
                    Finish(0);
                }
            }
            catch (Exception e) { Debug.LogException(e); Finish(1); }
        }

        static void Finish(int result)
        {
            SessionState.SetBool(Active,false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(result);
        }
    }
}
