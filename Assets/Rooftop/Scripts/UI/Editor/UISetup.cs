using System;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Rooftop.UI.Editor
{
    public static class UISetup
    {
        const string ScenePath = "Assets/Rooftop/VentRoof.unity";
        const string CanvasName = "GameUICanvas";
        const string FontPath = "Assets/Rooftop/Scripts/UI/RooftopUI SDF.asset";
        static TMP_FontAsset font;

        [MenuItem("Tools/Rooftop/Add Game UI")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var existing = scene.GetRootGameObjects().FirstOrDefault(root => root.name == CanvasName);
            if (existing)
            {
                if (!existing.GetComponent<MainMenuUI>())
                    throw new InvalidOperationException("GameUICanvas already exists without MainMenuUI; nothing changed.");
                Selection.activeGameObject = existing;
                return;
            }

            font = PrepareFont();
            var root = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, scene);
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<Canvas>().sortingOrder = 20;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var main = root.AddComponent<MainMenuUI>();

            // Scoreboard is below all modal panels and away from the existing top-left IMGUI.
            var board = Panel("Scoreboard", root.transform, false);
            var boardRect = (RectTransform)board.transform;
            boardRect.anchorMin = boardRect.anchorMax = new Vector2(0, 0);
            boardRect.pivot = Vector2.zero;
            boardRect.anchoredPosition = new Vector2(24, 100);
            boardRect.sizeDelta = new Vector2(310, 230);
            var boardLayout = Layout(board, 18);
            boardLayout.spacing = 12;
            Label("Счёт", board.transform, 30, 40);
            var scores = Label("Ожидание игроков…", board.transform, 23, 140);
            scores.alignment = TextAlignmentOptions.TopLeft;
            var scoreboard = board.AddComponent<ScoreboardUI>();
            Bind(scoreboard, "scores", scores);

            var mainPanel = Panel("MainMenu", root.transform, true);
            var menuCard = Card(mainPanel.transform, new Vector2(500, 520));
            Label("Коты на крыше", menuCard.transform, 42, 80);
            var host = Button("Создать игру", menuCard.transform);
            var join = Button("Подключиться", menuCard.transform);
            var openSettings = Button("Настройки", menuCard.transform);
            var quit = Button("Выход", menuCard.transform);
            var status = Label("", menuCard.transform, 20, 60);
            status.color = new Color(1, .65f, .55f);

            var settingsPanel = Panel("Settings", root.transform, true);
            var settingsCard = Card(settingsPanel.transform, new Vector2(500, 340));
            Label("Настройки", settingsCard.transform, 40, 70);
            Label("Громкость", settingsCard.transform, 26, 40);
            var slider = VolumeSlider(settingsCard.transform);
            var back = Button("Назад", settingsCard.transform);
            var settings = settingsPanel.AddComponent<SettingsUI>();
            Bind(settings, "volume", slider);
            Bind(settings, "mainMenu", main);

            var victoryPanel = Panel("Victory", root.transform, true);
            var victoryCard = Card(victoryPanel.transform, new Vector2(600, 300));
            var result = Label("Ничья", victoryCard.transform, 40, 130);
            result.richText = false;
            var returnButton = Button("В главное меню", victoryCard.transform);
            var victory = victoryPanel.AddComponent<VictoryUI>();
            Bind(victory, "result", result);
            Bind(victory, "mainMenu", main);

            Bind(main, "panel", mainPanel);
            Bind(main, "settings", settings);
            Bind(main, "victory", victory);
            Bind(main, "scoreboard", board);
            Bind(main, "status", status);
            UnityEventTools.AddPersistentListener(host.onClick, main.CreateGame);
            UnityEventTools.AddPersistentListener(join.onClick, main.JoinGame);
            UnityEventTools.AddPersistentListener(openSettings.onClick, main.OpenSettings);
            UnityEventTools.AddPersistentListener(quit.onClick, main.QuitGame);
            UnityEventTools.AddPersistentListener(back.onClick, settings.Back);
            UnityEventTools.AddPersistentListener(slider.onValueChanged, settings.SetVolume);
            UnityEventTools.AddPersistentListener(returnButton.onClick, victory.ReturnToMainMenu);
            settingsPanel.SetActive(false);
            victoryPanel.SetActive(false);
            board.SetActive(false);

            // Existing input modules and EventSystems are left intact.
            if (!UnityEngine.Object.FindFirstObjectByType<EventSystem>())
            {
                var events = new GameObject("GameUIEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                SceneManager.MoveGameObjectToScene(events, scene);
                events.GetComponent<EventSystem>().firstSelectedGameObject = host.gameObject;
                Undo.RegisterCreatedObjectUndo(events, "Add game UI EventSystem");
            }
            Undo.RegisterCreatedObjectUndo(root, "Add game UI");
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Failed to save VentRoof.");
            Selection.activeGameObject = root;
            Debug.Log("Game UI created and saved in VentRoof. Scoreboard is a placeholder; call VictoryUI.ShowResult from a future public match adapter.");
        }

        static TMP_FontAsset PrepareFont()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (existing) return existing;
            const string sourcePath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
            var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if (!source || !Resources.Load<TMP_Settings>("TMP Settings"))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
                if (package == null) throw new InvalidOperationException("TextMeshPro package not found.");
                AssetDatabase.ImportPackage(System.IO.Path.Combine(package.resolvedPath,
                    "Package Resources/TMP Essential Resources.unitypackage"), false);
                source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            }
            if (!source) throw new InvalidOperationException("TMP Essential Resources font not found.");
            var asset = TMP_FontAsset.CreateFontAsset(source, 40, 5, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, false);
            if (!asset) throw new InvalidOperationException("Failed to create UI font.");
            asset.name = "RooftopUI SDF";
            var characters = new StringBuilder();
            for (int code = 32; code <= 126; code++) characters.Append((char)code);
            characters.Append("АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя—…");
            if (!asset.TryAddCharacters(characters.ToString(), out string missing))
                throw new InvalidOperationException("Missing UI font characters: " + missing);
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(asset, FontPath);
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            foreach (var texture in asset.atlasTextures) AssetDatabase.AddObjectToAsset(texture, asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static GameObject Panel(string name, Transform parent, bool modal)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            Stretch((RectTransform)obj.transform);
            var image = obj.GetComponent<Image>();
            image.color = new Color(.035f, .05f, .085f, modal ? .96f : .85f);
            image.raycastTarget = modal;
            return obj;
        }

        static GameObject Card(Transform parent, Vector2 size)
        {
            var obj = new GameObject("Content", typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            Layout(obj, 24);
            return obj;
        }

        static VerticalLayoutGroup Layout(GameObject obj, int padding)
        {
            var layout = obj.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        static TextMeshProUGUI Label(string caption, Transform parent, float size, float height)
        {
            var obj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<LayoutElement>().preferredHeight = height;
            var text = obj.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.text = caption;
            text.color = new Color(.93f, .96f, 1f);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        static Button Button(string caption, Transform parent)
        {
            var obj = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<LayoutElement>().preferredHeight = 54;
            var image = obj.GetComponent<Image>();
            image.color = new Color(.16f, .28f, .37f);
            var button = obj.GetComponent<Button>();
            button.targetGraphic = image;
            var label = Label(caption, obj.transform, 27, 54);
            Stretch(label.rectTransform);
            return button;
        }

        static Slider VolumeSlider(Transform parent)
        {
            var obj = new GameObject("Volume", typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<LayoutElement>().preferredHeight = 44;
            var background = Bar("Background", obj.transform, new Color(.18f, .23f, .3f));
            background.anchorMin = new Vector2(0, .35f);
            background.anchorMax = new Vector2(1, .65f);
            var fill = Bar("Fill", obj.transform, new Color(.3f, .8f, .9f));
            fill.anchorMin = new Vector2(0, .35f);
            fill.anchorMax = new Vector2(1, .65f);
            var handle = Bar("Handle", obj.transform, Color.white);
            handle.anchorMin = handle.anchorMax = new Vector2(1, .5f);
            handle.sizeDelta = new Vector2(22, 40);
            var slider = obj.GetComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = 1;
            return slider;
        }

        static RectTransform Bar(string name, Transform parent, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<Image>().color = color;
            var rect = (RectTransform)obj.transform;
            Stretch(rect);
            return rect;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static void Bind(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Tools/Rooftop/Add Game UI", true)]
        static bool CanInstall() => !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
