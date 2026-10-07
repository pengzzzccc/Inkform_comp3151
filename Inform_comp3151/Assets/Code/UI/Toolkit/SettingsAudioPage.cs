using Inkform.Settings;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// The AUDIO settings page: mute plus the three volume sliders. Sliders push into
    /// SettingsStore on change and are pulled back (SetValueWithoutNotify) on Refresh — never
    /// both, or a drag would feed back into itself.
    /// </summary>
    public class SettingsAudioPage : SettingsSubPage
    {
        public override string Title => "AUDIO";

        private OptionRow muteRow;
        private Slider masterSlider, musicSlider, sfxSlider;
        private Label masterLabel, musicLabel, sfxLabel;

        public SettingsAudioPage(SettingsPanel owner) : base(owner, "UI/SettingsAudio")
        {
            muteRow = AddOnOffRow("Mute", () => SettingsStore.Muted, SettingsStore.SetMuted);

            masterLabel = AddSliderRow("Main Volume", 0f, 1f, out masterSlider);
            musicLabel = AddSliderRow("Music Volume", 0f, 1f, out musicSlider);
            sfxLabel = AddSliderRow("SFX Volume", 0f, 1f, out sfxSlider);

            masterSlider.RegisterValueChangedCallback(e =>
            {
                SettingsStore.SetMasterVolume(e.newValue);
                masterLabel.text = Percent(e.newValue);
            });
            musicSlider.RegisterValueChangedCallback(e =>
            {
                SettingsStore.SetMusicVolume(e.newValue);
                musicLabel.text = Percent(e.newValue);
            });
            sfxSlider.RegisterValueChangedCallback(e =>
            {
                SettingsStore.SetSfxVolume(e.newValue);
                sfxLabel.text = Percent(e.newValue);
            });
        }

        public override void Refresh()
        {
            muteRow.Value = OnOffText(SettingsStore.Muted);
            SetSlider(masterSlider, SettingsStore.MasterVolume, masterLabel, Percent);
            SetSlider(musicSlider, SettingsStore.MusicVolume, musicLabel, Percent);
            SetSlider(sfxSlider, SettingsStore.SfxVolume, sfxLabel, Percent);
        }
    }
}
