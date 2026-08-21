using Inkform.Bus;
using Inkform.Item;
using Inkform.Progression;
using UnityEngine;

namespace Inkform.Level.CrystalMine
{
    /// <summary>Event-driven, persisted tutorial progression for the Mine Cave 1 onboarding chain.</summary>
    public sealed class CrystalMineTutorialController : MonoBehaviour
    {
        private const int FindRopeGun = 0;
        private const int StoreBomb = 1;
        private const int LaunchBomb = 2;
        private const int DestroyFormation = 3;
        private const int CollectCrystal = 4;
        private const int Complete = 5;

        [SerializeField] private string findRopeGunText = "Explore Mine Cave 1 and find the Rope Gun.";
        [SerializeField] private string storeBombText = "Use the Rope Gun to collect a bomb.";
        [SerializeField] private string launchBombText = "Launch the stored bomb.";
        [SerializeField] private string destroyCrystalText = "Use a bomb to destroy a crystal formation.";
        [SerializeField] private string collectCrystalText = "Collect the crystal shards.";

        private void OnEnable()
        {
            ProgressionBus.RewardGranted += OnRewardGranted;
            ProgressionBus.CrystalFormationDestroyed += OnFormationDestroyed;
            ProgressionBus.CrystalCollected += OnCrystalCollected;
            ItemBus.ItemStored += OnItemStored;
            ItemBus.ItemReleased += OnItemReleased;

            if (RunProgressStore.RopeGunUnlocked && RunProgressStore.TutorialStep == FindRopeGun)
                RunProgressStore.SetTutorialStep(StoreBomb);
            RefreshGuidance();
        }

        private void OnDisable()
        {
            ProgressionBus.RewardGranted -= OnRewardGranted;
            ProgressionBus.CrystalFormationDestroyed -= OnFormationDestroyed;
            ProgressionBus.CrystalCollected -= OnCrystalCollected;
            ItemBus.ItemStored -= OnItemStored;
            ItemBus.ItemReleased -= OnItemReleased;
            GuidanceBus.Clear(this);
        }

        private void OnRewardGranted(ProgressionRewardType reward)
        {
            if (reward == ProgressionRewardType.RopeGun) AdvanceFrom(FindRopeGun, StoreBomb);
        }

        private void OnItemStored(InventoryItemDefinition item) => AdvanceFrom(StoreBomb, LaunchBomb);
        private void OnItemReleased(InventoryItemDefinition item, Vector2 pos, Vector2 velocity) =>
            AdvanceFrom(LaunchBomb, DestroyFormation);
        private void OnFormationDestroyed(Vector2 position) => AdvanceFrom(DestroyFormation, CollectCrystal);
        private void OnCrystalCollected(int amount, Vector2 position) => AdvanceFrom(CollectCrystal, Complete);

        private void AdvanceFrom(int expected, int next)
        {
            if (RunProgressStore.TutorialStep != expected) return;
            RunProgressStore.SetTutorialStep(next);
            RefreshGuidance();
        }

        private void RefreshGuidance()
        {
            string message = RunProgressStore.TutorialStep switch
            {
                FindRopeGun => findRopeGunText,
                StoreBomb => storeBombText,
                LaunchBomb => launchBombText,
                DestroyFormation => destroyCrystalText,
                CollectCrystal => collectCrystalText,
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(message)) GuidanceBus.Clear(this);
            else GuidanceBus.Show(this, message);
        }
    }
}
