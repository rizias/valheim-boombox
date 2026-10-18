using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxAmplifierVisual : MonoBehaviour
    {
        private AudioDeck deck;
        private bool refreshed;
        private ItemDrop currentWorldDrop;
        public void Initialize(AudioDeck ownerDeck)
        {
            deck = ownerDeck;
            Refresh();
        }
        public void Refresh()
        {
            if (refreshed && deck.WorldDrop == currentWorldDrop)
                return;
            refreshed = true;
            currentWorldDrop = deck.WorldDrop;
            var model = deck.Visual.GetComponent<VikingBoomboxVisualState>();
            if (deck.Mount != null)
                deck.Mount.RefreshModelBounds();
            var collider = deck.GetComponent<BoxCollider>();
            if (collider != null)
            {
                collider.center = model.ModelBounds.center;
                collider.size = model.ModelBounds.size;
            }
            var worldCollider = deck.WorldDrop != null ? deck.WorldDrop.GetComponent<BoxCollider>() : null;
            if (worldCollider != null)
            {
                worldCollider.center = model.ModelBounds.center;
                worldCollider.size = model.ModelBounds.size;
            }
        }
    }
}
