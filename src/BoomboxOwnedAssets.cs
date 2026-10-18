using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxOwnedAssets : MonoBehaviour
    {
        internal readonly List<Object> Assets = new List<Object>();

        private void OnDestroy()
        {
            foreach (var asset in Assets)
                if (asset != null)
                    Destroy(asset);
            Assets.Clear();
        }
    }
}
