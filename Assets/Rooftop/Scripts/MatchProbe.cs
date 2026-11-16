#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace Rooftop
{
    public sealed class MatchProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Environment.GetCommandLineArgs().Contains("-roofMatchProbe"))
                new GameObject("Match verification").AddComponent<MatchProbe>();
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            var args = Environment.GetCommandLineArgs();
            bool host = args.Contains("-roofHost");
            bool full = args.Contains("-matchBaseline");
            bool checkRematch = !full;
            var session = FindFirstObjectByType<RoofConnection>();
            session.largeRoof = true;
            RoofConnection.FullUpdates = full;
            RoofMatch.BroadcastFullState = full;
            PracticeCat.Prediction = true;
            ushort port = ushort.Parse(args[Array.IndexOf(args, "-roofPort") + 1]);
            if (!session.StartSession(host, "127.0.0.1", port)) throw new Exception("Session failed");
            var manager = NetworkManager.Singleton;
            yield return new WaitUntil(() => manager.IsConnectedClient && manager.LocalClient.PlayerObject && RoofMatch.Instance);
            var match = RoofMatch.Instance;
            var cat = manager.LocalClient.PlayerObject.GetComponent<PracticeCat>();
            cat.ProbeInput = true;
            int fishCount = FindObjectsByType<RoofFish>(FindObjectsSortMode.None).Length;
            Require(fishCount == 60, "60 fish synchronized");
            Debug.Log(host ? "MATCH_HOST_READY" : "MATCH_CLIENT_READY");
            if (host) yield return HostTrial(manager, match, checkRematch);
            else yield return ClientTrial(manager, match, cat, checkRematch);
        }

        static IEnumerator HostTrial(NetworkManager manager, RoofMatch match, bool checkRematch)
        {
            Require(!match.TryStartRound(), "single-player start refused");
            yield return new WaitUntil(() => match.PlayerCount == 3);
            var cats = manager.ConnectedClientsList.Select(c => c.PlayerObject.GetComponent<PracticeCat>())
                .OrderBy(c => c.SpawnSlot).ToArray();
            var bags = cats.Select(c => c.GetComponent<CatInventory>()).ToArray();
            var boxes = FindObjectsByType<DeliveryBox>(FindObjectsSortMode.None).OrderBy(b => b.Slot).ToArray();
            var fish = FindObjectsByType<RoofFish>(FindObjectsSortMode.None)
                .OrderBy(f => f.transform.position.x).ThenBy(f => f.transform.position.z).First();
            var pickup = fish.transform.position + Vector3.up * .85f;
            Require(boxes.Select(box => box.Slot).SequenceEqual(new[] { 0, 1, 2 }), "three delivery zones");

            Place(cats[0], pickup);
            yield return new WaitForSecondsRealtime(.25f);
            Require(!bags[0].HasFish, "lobby pickup refused");
            Require(match.TryStartRound(), "start with three cats");
            var traffic = MatchTrafficMetrics.TryAttach();
            Debug.Log("MATCH_SAMPLE_BEGIN");
            double roundStart = match.Round.EndsAt - RoofMatch.RoundSeconds;
            int[] route = { 0, 1, 2, 0, 1, 0 };
            for (int delivery = 0; delivery < route.Length; delivery++)
            {
                double scheduled = roundStart + 4 + 9 * delivery;
                yield return new WaitUntil(() => manager.ServerTime.Time >= scheduled);
                int slot = route[delivery];
                var atHome = cats[slot].transform.position;
                var box = boxes[slot].transform.position;
                Place(cats[slot], pickup);
                yield return new WaitForSecondsRealtime(.35f);
                Require(bags[slot].HasFish, "pickup on contact");

                if (delivery == 0)
                {
                    var another = FindObjectsByType<RoofFish>(FindObjectsSortMode.None)
                        .First(f => f != fish && f.GetComponent<Collider>().enabled);
                    Place(cats[slot], another.transform.position + Vector3.up * .85f);
                    yield return new WaitForSecondsRealtime(.25f);
                    Require(another.GetComponent<Collider>().enabled, "second fish refused");
                    Place(cats[slot], boxes[1].transform.position + new Vector3(0, .8f, 1.12f));
                    yield return new WaitForSecondsRealtime(.25f);
                    Require(bags[slot].HasFish && Score(match, cats[slot].OwnerClientId) == 0, "wrong box refused");
                }

                int before = Score(match, cats[slot].OwnerClientId);
                Place(cats[slot], box + new Vector3(0, .8f, slot == 2 ? 1.12f : -1.12f));
                yield return new WaitForSecondsRealtime(.45f);
                Require(!bags[slot].HasFish && Score(match, cats[slot].OwnerClientId) == before + 1, "one point per delivery");
                Require(!fish.GetComponent<Collider>().enabled, "fish waits before respawn");
                Place(cats[slot], atHome);
                yield return new WaitForSecondsRealtime(5);
                Require(fish.GetComponent<Collider>().enabled, "fish respawned at pickup");
                Debug.Log($"MATCH_DELIVERY slot={slot} score={before + 1}");
            }

            Place(cats[0], pickup);
            yield return new WaitForSecondsRealtime(.35f);
            Require(bags[0].HasFish, "carry across timeout");
            yield return new WaitUntil(() => match.Round.Phase == RoofMatch.Phase.Finished);
            Debug.Log("MATCH_SAMPLE_END");
            traffic?.Finish();
            Require(match.Round.WinnerSlot == 0 && match.TimeRemaining == 0, "server winner and timeout");
            Place(cats[0], boxes[0].transform.position + new Vector3(0, .8f, -1.12f));
            yield return new WaitForSecondsRealtime(1.5f);
            Require(bags[0].HasFish && Score(match, cats[0].OwnerClientId) == 3, "delivery after timeout refused");
            Debug.Log("MATCH_HOST_WINNER");
            if (!checkRematch) yield break;

            Require(match.TryStartRound(), "rematch starts");
            Require(bags.All(bag => !bag.HasFish) && fish.GetComponent<Collider>().enabled, "rematch resets fish");
            Require(cats.All(cat => Score(match, cat.OwnerClientId) == 0), "rematch resets points");
            Require(cats.Select(cat => cat.transform.position).Distinct().Count() == 3 &&
                    cats.All(cat => Mathf.Approximately(Mathf.Abs(cat.transform.position.x), 9)), "rematch resets positions");
            Debug.Log("MATCH_HOST_REMATCH");
            yield return new WaitUntil(() => match.Round.Phase == RoofMatch.Phase.Finished);
            Require(match.Round.WinnerSlot == -1, "server draw");
            Debug.Log("MATCH_HOST_DRAW");
        }

        static IEnumerator ClientTrial(NetworkManager manager, RoofMatch match, PracticeCat cat, bool checkRematch)
        {
            yield return new WaitUntil(() => match.IsRoundActive);
            Require(match.TryGetStanding(manager.LocalClientId, out var standing) && standing.Slot is 1 or 2,
                "client has its own box");
            Require(match.TimeRemaining > 50 && match.TimeRemaining <= 61, "deadline synchronized");
            // Same client movement and jump in both traffic profiles, away from the fish route.
            cat.ProbeDirection = Vector2.up;
            yield return new WaitForSecondsRealtime(.45f);
            cat.RequestJump();
            yield return new WaitForSecondsRealtime(.4f);
            cat.ProbeDirection = Vector2.zero;

            yield return new WaitUntil(() => match.Round.Phase == RoofMatch.Phase.Finished);
            Require(match.Round.WinnerSlot == 0, "client sees server winner");
            yield return new WaitUntil(() => ScoresMatch(match, 3, 2, 1));
            Debug.Log("MATCH_CLIENT_WINNER");
            if (!checkRematch) yield break;
            yield return new WaitUntil(() => match.IsRoundActive && ScoresMatch(match, 0, 0, 0));
            Require(!cat.GetComponent<CatInventory>().HasFish, "client bag reset");
            Debug.Log("MATCH_CLIENT_REMATCH");
            yield return new WaitUntil(() => match.Round.Phase == RoofMatch.Phase.Finished);
            Require(match.Round.WinnerSlot == -1, "client sees draw");
            Debug.Log("MATCH_CLIENT_DRAW");
        }

        static bool ScoresMatch(RoofMatch match, params int[] expected)
        {
            if (match.PlayerCount != expected.Length) return false;
            for (int i = 0; i < match.PlayerCount; i++)
            {
                var standing = match.GetStanding(i);
                if (standing.Score != expected[standing.Slot]) return false;
            }
            return true;
        }

        static int Score(RoofMatch match, ulong clientId) =>
            match.TryGetStanding(clientId, out var standing) ? standing.Score : -1;

        static void Require(bool condition, string description)
        {
            if (!condition) throw new Exception("Match check failed: " + description);
        }

        static void Place(PracticeCat cat, Vector3 position)
        {
            var controller = cat.GetComponent<CharacterController>();
            controller.enabled = false;
            cat.transform.position = position;
            controller.enabled = true;
            Physics.SyncTransforms();
        }
    }
}
#endif
