using System.Collections;
using BepInEx;
using HarmonyLib;
using Studio;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StudioObjectManipulatorGizmo
{
    [BepInPlugin(GUID, Name, Version)]
    [BepInProcess("CharaStudio")]
    public class ObjectManipulatorGizmoPlugin : BaseUnityPlugin
    {
        public const string GUID = "shortcutsKoi.guideObjectPort";
        public const string Name = "GuideShortcut";
        public const string Version = "1.0.0";

        private static GameObject guideEX = new GameObject();

        private float fov = 1f;

        private readonly string[] guideExName = new string[3]
        {
            "guideCloneMove",
            "guideCloneRot",
            "guideCloneScale"
        };

        private readonly string[] guideName = new string[3]
        {
            "move",
            "rotation",
            "scale"
        };

        private readonly float scale = 23f;

        private void Start()
        {
            Harmony.CreateAndPatchAll(typeof(ObjectManipulatorGizmoPlugin));
            StartCoroutine(Loading());

            IEnumerator Loading()
            {
                while (SceneManager.GetActiveScene().buildIndex != 1) yield return null;
                fov = Camera.main.fieldOfView;
            }
        }

        private void Update()
        {
            if (SceneManager.GetActiveScene().buildIndex != 1) return;
            for (var i = 0; i < guideName.Length; i++)
            {
                if ((bool) GameObject.Find("M Root(Clone)/" + guideName[i]) && !GameObject.Find(guideExName[i]))
                {
                    guideEX = Instantiate(GameObject.Find("M Root(Clone)/" + guideName[i]).gameObject);
                    guideEX.gameObject.name = guideExName[i];
                    guideEX.layer = 5;
                    if ((bool) GameObject.Find(guideName[i]).transform.parent.GetComponent<GuideObject>())
                    {
                        if (GameObject.Find(guideName[i]).transform.parent.GetComponent<GuideObject>().transformTarget
                            .parent.name == "CommonSpace")
                        {
                            if (i != 1)
                                guideEX.transform.localScale *= Camera.main.fieldOfView / scale;
                            else
                                guideEX.transform.localScale *= Camera.main.fieldOfView / scale;
                        }
                        else
                        {
                            if (i == 0) guideEX.transform.localScale *= 1.5f * Camera.main.fieldOfView / scale;
                            if (i == 1) guideEX.transform.localScale *= 2.7f * Camera.main.fieldOfView / scale;
                        }
                    }

                    var position = new Vector3(Screen.width * 0.9f, Screen.height * 0.15f, 6f);
                    guideEX.transform.position = Camera.main.ScreenToWorldPoint(position);
                    guideEX.transform.parent = Camera.main.transform;
                }

                if (!GameObject.Find(guideName[i]) && (bool) GameObject.Find(guideExName[i]))
                    Destroy(GameObject.Find(guideExName[i]));
                if ((bool) GameObject.Find(guideExName[i]))
                {
                    guideEX.transform.eulerAngles = new Vector3(0f, 0f, 0f);
                    if (fov != Camera.main.fieldOfView)
                    {
                        var position2 = new Vector3(Screen.width * 0.9f, Screen.height * 0.15f, 6f);
                        guideEX.transform.position = Camera.main.ScreenToWorldPoint(position2);
                        guideEX.transform.localScale *= Camera.main.fieldOfView / fov;
                        fov = Camera.main.fieldOfView;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(GuideObject), "SetScale")]
        [HarmonyPostfix]
        private static void SetScalePostfix(float ___m_ScaleRate)
        {
            if ((bool) guideEX)
                guideEX.transform.localScale = Vector3.one * ___m_ScaleRate * Studio.Studio.optionSystem.manipulateSize;
        }
    }
}