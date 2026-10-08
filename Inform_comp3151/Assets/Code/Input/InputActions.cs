using Inkform.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Inkform.Input
{
    /// <summary>
    /// The single shared InputSystem_Actions instance for the whole project (InputHandler,
    /// MapViewPart, the settings Controls page, BindingTools). One instance means a runtime rebind
    /// written here is seen by every consumer.
    ///
    /// Binding overrides are the official runtime layer on top of the .inputactions asset: the asset
    /// stays the single source of the default bindings (rebind there, Unity regenerates the wrapper),
    /// and user remaps live as overrides that load here before the first scene.
    /// </summary>
    public static class InputActions
    {
        public static readonly InputSystem_Actions Wrapper = new InputSystem_Actions();

        // BeforeSceneLoad, like SettingsStore.LoadAndApply — same "must be in effect before anything
        // renders/accepts input" reason. Order vs. SettingsStore.Load is undefined, hence the applier
        // reads PlayerPrefs directly through BindingOverridesJson rather than a cached field.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadBindingOverrides()
        {
            string json = SettingsStore.BindingOverridesJson;
            if (!string.IsNullOrEmpty(json))
                Wrapper.asset.LoadBindingOverridesFromJson(json);
        }
    }
}
