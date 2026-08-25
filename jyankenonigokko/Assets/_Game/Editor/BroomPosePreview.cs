using UnityEditor;
using UnityEngine;

namespace MagicHand.EditorTools
{
    /// <summary>
    /// 搭乗ポーズを1枚の画像に描き出す確認用ツール。
    ///
    /// ボーンの角度を数字で見ても姿勢が想像できないので、実際に描いて確かめるために置いてある。
    /// 実行モードに入らず、編集モードでクリップを適用した結果をそのまま撮る。
    /// 飛行時間が絡まないぶん、何度でも同じ絵が得られる。
    /// </summary>
    public static class BroomPosePreview
    {
        private const string CharacterPrefab =
            "Assets/Shokubutsu Studio/Free Low Poly Cubic Humans/URP/Prefabs/Characters/Mages/Mage_01.prefab";

        private const string RideClip = "Assets/_Game/Animations/Broom_Ride.anim";

        /// <summary>
        /// 搭乗ポーズを描き出す。
        /// </summary>
        /// <param name="outputPath">保存先。ASCII のパスにすること。</param>
        /// <param name="cameraOffset">キャラの足元から見たカメラの位置。</param>
        /// <param name="size">出力する画像の一辺。</param>
        public static string Capture(string outputPath, Vector3 cameraOffset, int size = 700)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefab);
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RideClip);

            if (prefab == null || clip == null) return "プレハブかクリップが見つかりません";

            GameObject root = new GameObject("BroomPosePreview");

            // ステージの中に置くと背景に紛れて姿勢が読めないので、何も無い高所で撮る
            root.transform.position = new Vector3(0f, 500f, 0f);

            try
            {
                GameObject character = Object.Instantiate(prefab, root.transform);
                character.transform.localPosition = Vector3.zero;
                character.transform.localRotation = Quaternion.identity;
                clip.SampleAnimation(character, 0f);

                // 進行方向（+Z）が分かるよう、足元に細い矢印を置く
                AddDirectionMarker(root.transform);

                AttachBroomCopy(root.transform);

                return Render(root.transform, outputPath, cameraOffset, size);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>進行方向を示す床の矢印。どちらを向いているかが絵だけでは分からないため。</summary>
        private static void AddDirectionMarker(Transform parent)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "ForwardMarker";
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = new Vector3(0f, -0.02f, 0.9f);
            bar.transform.localScale = new Vector3(0.08f, 0.02f, 1.8f);
            Object.DestroyImmediate(bar.GetComponent<Collider>());
        }

        /// <summary>シーンのプレイヤーが持っているほうきを複製して、搭乗位置に置く。</summary>
        private static void AttachBroomCopy(Transform parent)
        {
            PlayerBroomVisual source = Object.FindFirstObjectByType<PlayerBroomVisual>(FindObjectsInactive.Include);
            if (source == null) return;

            var so = new SerializedObject(source);
            var broom = so.FindProperty("broom").objectReferenceValue as GameObject;
            if (broom == null) return;

            GameObject copy = Object.Instantiate(broom, parent);
            copy.SetActive(true);
            copy.transform.localPosition = so.FindProperty("ridePosition").vector3Value;
            copy.transform.localRotation = Quaternion.Euler(so.FindProperty("rideRotation").vector3Value);
        }

        private static string Render(Transform target, string outputPath, Vector3 cameraOffset, int size)
        {
            GameObject camGo = new GameObject("PreviewCamera");
            RenderTexture rt = new RenderTexture(size, size, 24);
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, false);

            try
            {
                Camera cam = camGo.AddComponent<Camera>();
                cam.transform.position = target.position + cameraOffset;
                cam.transform.LookAt(target.position + Vector3.up * 1.0f);
                cam.fieldOfView = 35f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;

                System.IO.File.WriteAllBytes(outputPath, tex.EncodeToPNG());
                return "saved: " + outputPath;
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(tex);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
