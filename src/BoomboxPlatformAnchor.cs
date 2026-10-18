using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    [DefaultExecutionOrder(10001)]
    public sealed class BoomboxPlatformAnchor : MonoBehaviour
    {
        public const string PlatformTokenKey = "local.valheim.boombox.platform";
        public Transform Platform
        {
            get;
            private set;
        }
        private AudioDeck deck;
        private bool searching;
        private float retryAt;
        private readonly List<ZDO> candidates = new List<ZDO>();

        public void Initialize(AudioDeck owner)
        {
            deck = owner;
        }

        public static ZNetView Support(Collider collider)
        {
            var body = collider != null ? collider.attachedRigidbody : null;
            if (body == null || body.GetComponent<Ship>() == null && body.GetComponent<Vagon>() == null)
                return null;
            return body.GetComponent<ZNetView>();
        }

        public bool Bind(Collider collider)
        {
            ClearSupport();
            var view = Support(collider);
            if (view == null || view.GetZDO() == null)
                return false;
            if (!view.IsOwner())
                view.ClaimOwnership();
            if (!view.IsOwner())
                return false;
            var zdo = view.GetZDO();
            string token = zdo.GetString(PlatformTokenKey, "");
            if (string.IsNullOrEmpty(token))
            {
                token = Guid.NewGuid().ToString("N");
                zdo.Set(PlatformTokenKey, token);
            }
            var prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            deck.State.platformGuid = token;
            deck.State.platformPrefab = prefab != null
                ? prefab.name
                : view.gameObject.name.Replace("(Clone)", "").Trim();
            SetId(zdo.m_uid);
            Platform = view.transform;
            Vector3 local = Platform.InverseTransformPoint(transform.position);
            Quaternion rotation = Quaternion.Inverse(Platform.rotation) * transform.rotation;
            deck.State.platformX = local.x;
            deck.State.platformY = local.y;
            deck.State.platformZ = local.z;
            deck.State.platformQx = rotation.x;
            deck.State.platformQy = rotation.y;
            deck.State.platformQz = rotation.z;
            deck.State.platformQw = rotation.w;
            StoreScale(Platform.lossyScale);
            return true;
        }

        public void ClearSupport()
        {
            if (deck == null)
                return;
            deck.State.platformGuid = null;
            deck.State.platformPrefab = null;
            deck.State.platformUserId = 0;
            deck.State.platformObjectId = 0;
            Platform = null;
        }

        public void Follow()
        {
            if (deck == null
                || deck.State.onBack
                || string.IsNullOrEmpty(deck.State.platformGuid)
                || ZDOMan.instance == null
                || ZNetScene.instance == null)
                return;
            var id = new ZDOID(deck.State.platformUserId, deck.State.platformObjectId);
            var zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null || zdo.GetString(PlatformTokenKey, "") != deck.State.platformGuid)
            {
                Platform = null;
                if (!searching && Time.unscaledTime >= retryAt)
                    StartCoroutine(FindPlatform());
                return;
            }
            var instance = ZNetScene.instance.FindInstance(zdo.m_uid);
            Platform = instance != null ? instance.transform : null;
            var local = new Vector3(deck.State.platformX, deck.State.platformY, deck.State.platformZ);
            var localRotation = new Quaternion(
                deck.State.platformQx,
                deck.State.platformQy,
                deck.State.platformQz,
                deck.State.platformQw
            );
            if (Platform != null)
            {
                transform.position = Platform.TransformPoint(local);
                transform.rotation = Platform.rotation * localRotation;
                StoreScale(Platform.lossyScale);
            }
            else
            {
                var scale = new Vector3(
                    deck.State.platformScaleX,
                    deck.State.platformScaleY,
                    deck.State.platformScaleZ
                );
                transform.position = Matrix4x4.TRS(zdo.GetPosition(), zdo.GetRotation(), scale).MultiplyPoint3x4(local);
                transform.rotation = zdo.GetRotation() * localRotation;
            }
            deck.State.Position = transform.position;
            deck.State.yaw = transform.eulerAngles.y;
        }

        private IEnumerator FindPlatform()
        {
            searching = true;
            string token = deck.State.platformGuid;
            string prefab = deck.State.platformPrefab;
            candidates.Clear();
            int index = 0;
            if (!string.IsNullOrEmpty(prefab))
            {
                while (ZDOMan.instance != null
                    && !ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, candidates, ref index))
                {
                    if (deck.State.platformGuid != token)
                    {
                        searching = false;
                        yield break;
                    }
                    yield return null;
                }
                if (deck.State.platformGuid == token)
                {
                    foreach (var zdo in candidates)
                    {
                        if (zdo == null || zdo.GetString(PlatformTokenKey, "") != token)
                            continue;
                        SetId(zdo.m_uid);
                        break;
                    }
                }
            }
            searching = false;
            retryAt = Time.unscaledTime + 3f;
        }

        private void SetId(ZDOID id)
        {
            deck.State.platformUserId = id.UserID;
            deck.State.platformObjectId = id.ID;
        }
        private void StoreScale(Vector3 scale)
        {
            deck.State.platformScaleX = scale.x;
            deck.State.platformScaleY = scale.y;
            deck.State.platformScaleZ = scale.z;
        }
        private void LateUpdate()
        {
            if (BoomboxPlugin.Instance != null && BoomboxPlugin.Instance.AudioWorldActive)
                Follow();
        }
    }
}
