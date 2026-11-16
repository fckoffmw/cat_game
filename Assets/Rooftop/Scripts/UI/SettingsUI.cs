using UnityEngine;
using UnityEngine.UI;

namespace Rooftop.UI
{
    public sealed class SettingsUI : MonoBehaviour
    {
        const string VolumeKey = "Rooftop.MasterVolume";
        [SerializeField] Slider volume;
        [SerializeField] MainMenuUI mainMenu;

        public void Initialize()
        {
            float saved = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
            AudioListener.volume = saved;
            volume.SetValueWithoutNotify(saved);
        }

        public void Show()
        {
            Initialize();
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);

        public void SetVolume(float value)
        {
            value = Mathf.Clamp01(value);
            AudioListener.volume = value;
            PlayerPrefs.SetFloat(VolumeKey, value);
        }

        public void Back()
        {
            PlayerPrefs.Save();
            Hide();
            mainMenu.Show();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) PlayerPrefs.Save();
        }

        void OnApplicationQuit() => PlayerPrefs.Save();
    }
}
