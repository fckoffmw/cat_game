using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Rooftop.Editor
{
    public static class RoofScene
    {
        const string Root = "Assets/Rooftop/";
        static Material Paint(string name, Color color)
        {
            var material = new Material(Shader.Find("Standard")) { color = color };
            AssetDatabase.CreateAsset(material, Root + name + ".mat");
            return material;
        }

        static GameObject Shape(string name, Vector3 position, Vector3 scale, Material material,
            PrimitiveType type = PrimitiveType.Cube)
        {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.position = position;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            return obj;
        }

        public static void Create()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);
            var roof = Paint("Roof", new Color(.38f,.46f,.49f));
            var metal = Paint("Metal", new Color(.62f,.72f,.73f));
            Shape("Roof", new(0,-.3f,0), new(24,.6f,16), roof);
            foreach (int sign in new[]{-1,1})
            {
                Shape("Parapet X", new(sign*12,.5f,0), new(.3f,1,16), metal);
                Shape("Parapet Z", new(0,.5f,sign*8), new(24,1,.3f), metal);
                Shape("Box " + sign, new(sign*10,.25f,0), new(1.5f,.5f,1.5f),
                    Paint("Box" + sign, sign < 0 ? Color.cyan : Color.magenta));
            }
            Shape("Vent", new(0,.35f,0), new(1,.7f,10), metal);
            var fish = Shape("Fish", Vector3.zero, new(.75f,.28f,.38f), Paint("Scales",new Color(1,.66f,.2f)), PrimitiveType.Sphere);
            fish.GetComponent<Collider>().isTrigger = true;
            fish.AddComponent<Rigidbody>().isKinematic = true;
            fish.AddComponent<NetworkObject>();
            fish.AddComponent<RoofFish>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(fish, Root + "Fish.prefab");
            Object.DestroyImmediate(fish);
            for (int section = 0; section < 6; section++)
            {
                var group = new GameObject("Section " + (section+1));
                for (int i=0; i<2; i++)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                    instance.transform.position = new((section%2==0?-1:1)*(2+5*i), .24f, (section/2-1)*5);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                }
            }
            var camera = Camera.main;
            camera.transform.position = new(0,24,-13);
            camera.transform.LookAt(Vector3.zero);
            camera.orthographic = true;
            camera.orthographicSize = 11;
            var network = new GameObject("Session");
            var manager = network.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig { NetworkTransport = network.AddComponent<UnityTransport>() };
            var cat = Shape("PracticeCat", Vector3.zero, Vector3.one, Paint("Fur", new Color(.8f,.42f,.22f)), PrimitiveType.Capsule);
            Object.DestroyImmediate(cat.GetComponent<CapsuleCollider>());
            cat.AddComponent<CharacterController>().stepOffset = .2f;
            cat.AddComponent<NetworkObject>();
            cat.AddComponent<Unity.Netcode.Components.NetworkTransform>();
            cat.AddComponent<CatInventory>();
            cat.AddComponent<PracticeCat>();
            manager.NetworkConfig.PlayerPrefab = PrefabUtility.SaveAsPrefabAsset(cat, Root + "PracticeCat.prefab");
            Object.DestroyImmediate(cat);
            network.AddComponent<RoofConnection>();
            const string path = Root + "VentRoof.unity";
            EditorSceneManager.SaveScene(scene,path);
            EditorBuildSettings.scenes = new[]{new EditorBuildSettingsScene(path,true)};
            AssetDatabase.SaveAssets();
        }
    }
}
