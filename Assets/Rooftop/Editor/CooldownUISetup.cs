using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Rooftop.Editor
{
    public static class CooldownUISetup
    {
        const string ScenePath = "Assets/Rooftop/VentRoof.unity";
        const string CanvasName = "JumpCooldownCanvas";

        [MenuItem("Tools/Rooftop/Add Jump Cooldown UI")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            var existing = scene.GetRootGameObjects().FirstOrDefault(root => root.name == CanvasName);
            if (existing)
            {
                if (!existing.GetComponentInChildren<CooldownUI>(true))
                    throw new InvalidOperationException($"{CanvasName} exists without CooldownUI; nothing changed.");
                Selection.activeGameObject = existing;
                return;
            }

            var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            if (!sprite) throw new InvalidOperationException("Built-in circular UI sprite not found.");

            var canvasObject = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;

            var iconObject = new GameObject("JumpCooldown", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconObject.transform.SetParent(canvasObject.transform, false);
            var rect = iconObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = Vector2.right;
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(-64, 64);
            rect.sizeDelta = new Vector2(64, 64);

            var image = iconObject.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Top;
            image.fillClockwise = true;
            image.fillAmount = 1;
            image.color = new Color(.25f, .85f, 1f, .95f);
            image.raycastTarget = false;
            image.enabled = false;
            iconObject.AddComponent<CooldownUI>();

            Undo.RegisterCreatedObjectUndo(canvasObject, "Add jump cooldown UI");
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = iconObject;
            Debug.Log("Jump cooldown UI added to VentRoof. Save the scene with Ctrl+S.");
        }

        [MenuItem("Tools/Rooftop/Add Jump Cooldown UI", true)]
        static bool CanInstall() => !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
