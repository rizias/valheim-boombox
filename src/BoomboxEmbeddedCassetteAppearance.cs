using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxEmbeddedCassetteAppearance : MonoBehaviour
    {
        private Transform symbols;
        private string signature;
        public void Apply(RecordedCassette cassette)
        {
            var active = BoomboxItemData.Properties.Where(p => cassette != null && cassette.Rank(p) > 0).ToArray();
            string next = (cassette == null ? "none" : cassette.prefab) + ":" + string.Join("|", active);
            if (next == signature)
                return;
            signature = next;
            if (symbols != null)
            {
                symbols.gameObject.SetActive(false);
                Destroy(symbols.gameObject);
            }
            symbols = new GameObject("InstalledCassetteProperties").transform;
            symbols.SetParent(transform, false);
            var model = GetComponent<VikingBoomboxVisualState>();
            Vector3 center = transform.InverseTransformPoint((model.ReelLeft.position + model.ReelRight.position) * .5f);
            float separation = Vector3.Distance(model.ReelLeft.position, model.ReelRight.position)
                / Mathf.Max(.001f, transform.lossyScale.x);
            for (int i = 0; i < active.Length; i++)
            {
                var marker = BoomboxItemPrefabs.CreatePropertySymbol(active[i], symbols);
                if (marker == null)
                    continue;
                marker.transform.localPosition = center
                    + Vector3.right * ((i - (active.Length - 1) * .5f) * separation * .22f)
                    - Vector3.up * separation * .32f
                    + Vector3.forward * .006f;
                marker.transform.localScale = Vector3.one * .6f;
                marker.SetActive(true);
            }
        }
    }
}
