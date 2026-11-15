#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace Rooftop
{
    // Development build scenario. Ordinary launches always use keyboard input.
    public sealed class PracticeProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Environment.GetCommandLineArgs().Contains("-roofProbe"))
                new GameObject("Practice verification").AddComponent<PracticeProbe>();
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            string[] args = Environment.GetCommandLineArgs();
            bool host = args.Contains("-roofHost");
            var session = FindFirstObjectByType<RoofConnection>();
            session.largeRoof = args.Contains("-sixty");
            RoofConnection.FullUpdates = args.Contains("-fullFish");
            PracticeCat.Prediction = !args.Contains("-baseline");
            ushort port = ushort.Parse(args[Array.IndexOf(args,"-roofPort")+1]);
            if (!session.StartSession(host,"127.0.0.1",port)) { Application.Quit(2); yield break; }
            var manager = NetworkManager.Singleton;
            yield return new WaitUntil(() => manager.IsConnectedClient && manager.LocalClient.PlayerObject);
            var cat = manager.LocalClient.PlayerObject.GetComponent<PracticeCat>();
            cat.ProbeInput = true;
            if (host)
            {
                Debug.Log($"HOST_READY id={cat.OwnerClientId} x={cat.transform.position.x:F2}");
                if (args.Contains("-fishTrial")) yield return FishTrial(manager);
                yield break;
            }
            yield return new WaitForSecondsRealtime(1.5f);
            int count = FindObjectsByType<RoofFish>(FindObjectsSortMode.None).Length;
            Debug.Log($"CLIENT_READY id={cat.OwnerClientId} x={cat.transform.position.x:F2} fish={count} players={FindObjectsByType<PracticeCat>(FindObjectsSortMode.None).Length}");
            if (args.Contains("-fishTrial"))
            {
                int visible = FindObjectsByType<RoofFish>(FindObjectsSortMode.None).Count(f => f.GetComponent<Renderer>().enabled);
                Debug.Log($"FISH_CLIENT visible={visible} id={manager.LocalClientId}");
                if (count != 60 || visible != 59) throw new Exception("Late or initial fish state differs");
                yield return new WaitUntil(() => FindObjectsByType<RoofFish>(FindObjectsSortMode.None)
                    .All(f => f.GetComponent<Renderer>().enabled));
                Debug.Log("FISH_CLIENT restored=60");
                yield break;
            }
            cat.ProbeDirection = Vector2.left;
            yield return new WaitForSecondsRealtime(.6f);
            cat.ProbeDirection = Vector2.zero;
            yield return new WaitForSecondsRealtime(.8f);
            cat.RequestJump();
            yield return new WaitForSecondsRealtime(.12f);
            Capture(args, "jump");
            yield return new WaitForSecondsRealtime(1.25f);
            cat.RequestJump(true);
            yield return new WaitForSecondsRealtime(.7f);
            Capture(args, "correction");
            yield return new WaitForSecondsRealtime(.8f);
            cat.ProbeDirection = Vector2.left;
            yield return new WaitForSecondsRealtime(2);
            float atVent = cat.transform.position.x;
            if (atVent < .8f || atVent > 1.3f) throw new Exception("Vent did not block ground movement: " + atVent);
            cat.RequestJump();
            yield return new WaitForSecondsRealtime(1.25f);
            cat.ProbeDirection = Vector2.zero;
            yield return new WaitForSecondsRealtime(1.25f);
            if (cat.transform.position.x > -1 || Mathf.Abs(cat.transform.position.y-1.08f) > .15f)
                throw new Exception("Jump did not clear vent: " + cat.transform.position);
            Debug.Log($"VENT_OK from={atVent:F2} to={cat.transform.position.x:F2}");
            Debug.Log($"PROBE_END x={cat.transform.position.x:F2} y={cat.transform.position.y:F2} fish={count} bag={cat.GetComponent<CatInventory>().HasFish}");
        }

        static IEnumerator FishTrial(NetworkManager manager)
        {
            yield return new WaitUntil(() => manager.ConnectedClients.Count == 2 &&
                manager.ConnectedClientsList.All(client => client.PlayerObject));
            var fish = FindObjectsByType<RoofFish>(FindObjectsSortMode.None)
                .OrderBy(f => f.transform.position.x).ThenBy(f => f.transform.position.z).First();
            var bags = manager.ConnectedClientsList.Select(client => client.PlayerObject.GetComponent<CatInventory>()).ToArray();
            for (int i=0; i<2; i++)
                Place(bags[i],fish.transform.position + Vector3.up * .85f + Vector3.right * (i==0 ? -.52f : .52f));
            yield return new WaitForSecondsRealtime(.5f);
            if (bags.Count(b => b.HasFish) != 1) throw new Exception("Simultaneous pickup failed");
            Debug.Log("FISH_HELD single winner");
            yield return new WaitUntil(() => manager.ConnectedClients.Count == 3);
            yield return new WaitForSecondsRealtime(4);
            var winner = bags.Single(b => b.HasFish);
            if (!winner.TryDeliver() || winner.TryDeliver()) throw new Exception("Duplicate delivery");
            for (int i=0;i<2;i++) Place(bags[i],new Vector3(-9+18*i,1.1f,-3));
            yield return new WaitForSecondsRealtime(5.5f);
            if (!fish.GetComponent<Collider>().enabled) throw new Exception("Respawn failed");
            Debug.Log("FISH_FINISHED delivery, duplicate rejection, respawn");
            // Identical item workload for the following 60-second traffic sample.
            for (int round=0; round<6; round++)
            {
                yield return new WaitForSecondsRealtime(3);
                Place(winner,fish.transform.position+Vector3.up*.85f);
                yield return new WaitForSecondsRealtime(.5f);
                if (!winner.TryDeliver()) throw new Exception("Repeated pickup failed");
                Place(winner,new Vector3(-9,1.1f,-3));
                yield return new WaitForSecondsRealtime(6);
            }
            Debug.Log("FISH_SAMPLE six deliveries");
        }

        static void Place(CatInventory bag,Vector3 position)
        {
            var controller = bag.GetComponent<CharacterController>();
            controller.enabled = false;
            bag.transform.position = position;
            controller.enabled = true;
            Physics.SyncTransforms();
        }

        static void Capture(string[] args, string name)
        {
            int index = Array.IndexOf(args,"-roofCapture");
            if (index >= 0) ScreenCapture.CaptureScreenshot(args[index+1]+"-"+name+".png");
        }
    }
}
#endif
