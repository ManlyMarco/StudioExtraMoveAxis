using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Illusion.Extensions;
using KKAPI;
using KKAPI.Studio;
using KKAPI.Studio.UI;
using KKAPI.Utilities;
using Studio;
using UnityEngine;

namespace StudioExtraMoveAxis
{
    [BepInPlugin(GUID, Name, Version)]
    [DefaultExecutionOrder(32000)] // Stop snapbacks during right click drag counterrotation
    [BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    public partial class StudioExtraMoveAxisPlugin : BaseUnityPlugin
    {
        public const string GUID = "StudioExtraMoveAxis";
        public const string Name = "Extra move axis in bottom right corner";
        public const string Version = Constants.Version;

        // Used for scrolling mouse wheel over the gizmo when a character is selected with FK on
        private static readonly string[] BoneCycleOrder = new string[]
        {
#if KK || KKS
            // Hips is index 0 — skipped during scroll unless currently selected
            "cf_j_hips",
            // Upper body (scroll UP = ascending index)
            "cf_j_spine01",
            "cf_j_spine02",
            "cf_j_neck",
            "cf_j_head",
            "cf_j_shoulder_r",
            "cf_j_arm00_r",
            "cf_j_forearm01_r",
            "cf_j_hand_r",
            "cf_j_shoulder_l",
            "cf_j_arm00_l",
            "cf_j_forearm01_l",
            "cf_j_hand_l",
            // Left hand fingers (KK/KKS: no "hand_" prefix)
            "cf_j_thumb01_l",
            "cf_j_thumb02_l",
            "cf_j_thumb03_l",
            "cf_j_index01_l",
            "cf_j_index02_l",
            "cf_j_index03_l",
            "cf_j_middle01_l",
            "cf_j_middle02_l",
            "cf_j_middle03_l",
            "cf_j_ring01_l",
            "cf_j_ring02_l",
            "cf_j_ring03_l",
            "cf_j_little01_l",
            "cf_j_little02_l",
            "cf_j_little03_l",
            // Right hand fingers (KK/KKS: no "hand_" prefix)
            "cf_j_thumb01_r",
            "cf_j_thumb02_r",
            "cf_j_thumb03_r",
            "cf_j_index01_r",
            "cf_j_index02_r",
            "cf_j_index03_r",
            "cf_j_middle01_r",
            "cf_j_middle02_r",
            "cf_j_middle03_r",
            "cf_j_ring01_r",
            "cf_j_ring02_r",
            "cf_j_ring03_r",
            "cf_j_little01_r",
            "cf_j_little02_r",
            "cf_j_little03_r",
            // Lower body — bottom-to-top so scroll UP goes toe→foot→leg→hip
            "cf_j_toes_r",
            "cf_j_leg03_r",
            "cf_j_leg01_r",
            "cf_j_thigh00_r",
            "cf_j_toes_l",
            "cf_j_leg03_l",
            "cf_j_leg01_l",
            "cf_j_thigh00_l",
            "cf_j_waist01"
#else
            // Hips is index 0 — skipped during scroll unless currently selected
            "cf_j_hips",
            // Upper body (scroll UP = ascending index)
            "cf_j_spine01",
            "cf_j_spine02",
            "cf_j_neck",
            "cf_j_head",
            "cf_j_shoulder_r",
            "cf_j_armup00_r",
            "cf_j_armlow01_r",
            "cf_j_hand_r",
            "cf_j_shoulder_l",
            "cf_j_armup00_l",
            "cf_j_armlow01_l",
            "cf_j_hand_l",
            // Left hand fingers
            "cf_j_hand_thumb01_l",
            "cf_j_hand_thumb02_l",
            "cf_j_hand_thumb03_l",
            "cf_j_hand_index01_l",
            "cf_j_hand_index02_l",
            "cf_j_hand_index03_l",
            "cf_j_hand_middle01_l",
            "cf_j_hand_middle02_l",
            "cf_j_hand_middle03_l",
            "cf_j_hand_ring01_l",
            "cf_j_hand_ring02_l",
            "cf_j_hand_ring03_l",
            "cf_j_hand_little01_l",
            "cf_j_hand_little02_l",
            "cf_j_hand_little03_l",
            // Right hand fingers
            "cf_j_hand_thumb01_r",
            "cf_j_hand_thumb02_r",
            "cf_j_hand_thumb03_r",
            "cf_j_hand_index01_r",
            "cf_j_hand_index02_r",
            "cf_j_hand_index03_r",
            "cf_j_hand_middle01_r",
            "cf_j_hand_middle02_r",
            "cf_j_hand_middle03_r",
            "cf_j_hand_ring01_r",
            "cf_j_hand_ring02_r",
            "cf_j_hand_ring03_r",
            "cf_j_hand_little01_r",
            "cf_j_hand_little02_r",
            "cf_j_hand_little03_r",
            // Lower body — bottom-to-top so scroll UP goes toe→foot→leg→hip
            "cf_j_toes01_r",
            "cf_j_foot01_r",
            "cf_j_leglow01_r",
            "cf_j_legup00_r",
            "cf_j_toes01_l",
            "cf_j_foot01_l",
            "cf_j_leglow01_l",
            "cf_j_legup00_l",
            "cf_j_kosi01"
#endif
        };

        internal static new ManualLogSource Logger;

        private static ConfigEntry<bool> _showGizmo;
#if !PH
        private static ConfigEntry<bool> _referenceToSelectedObject;
#endif

        private static Harmony _hi;

        // Diagnostic logging master switch. Left in place for easy reactivation:
        // flip to true to restore the verbose shift-roll / drag tracing.
        private const bool DIAG = false;
#pragma warning disable 162 // unreachable while DIAG is false — by design
        private static void DiagLog(string m) { if (DIAG) Logger.LogInfo(m); }
#pragma warning restore 162

        private static GameObject _gizmoRoot;
        private static GameObject _moveObj, _rotObj, _scaleObj;
        private static GuideMove[] _guideMoves;

        private static HashSet<GuideObject> _selectedObjects;
        private static bool _lastAnySelected;

        private static Camera _camera;
        private static float _lastFov;
        private static int _lastScreenWidth;
        private static int _lastScreenHeight;

        private void Awake()
        {
            Logger = base.Logger;

            _showGizmo = Config.Bind("Extra gizmos", "Show extra move gizmo", false,
                "Show extra set of gizmos in the bottom right corner of the screen. An object must be selected for gizmo to be visible." +
                "You can use left toolbar to turn the gizmo on or off.");
#if !PH
            _referenceToSelectedObject = Config.Bind("Extra gizmos", "Use selected object as reference", true,
                "If true, using the extra XYZ move gizmo is the same as using the default gizmo on the currently selected object (so direction of the arrow may not be the same as direction of movement).\n" +
                "If false, current camera position is used as the reference frame, so using the extra XYZ gizmo will move the object to where the gizmo arrows are actually pointing.\n" +
                "Change currently selected object to apply the setting.");
#endif

            if (StudioAPI.StudioLoaded)
            {
                // for debug purposes, doesn't get called normally
                Initialize();
            }
            else
            {
                var buttonTex = ResourceUtils.GetEmbeddedResource("toolbar_icon.png", typeof(StudioExtraMoveAxisPlugin).Assembly).LoadTexture(TextureFormat.DXT5, false);
                var tgl = CustomToolbarButtons.AddLeftToolbarToggle(buttonTex, _showGizmo.Value, b => _showGizmo.Value = b);
                _showGizmo.SettingChanged += (o, eventArgs) =>
                {
                    tgl.SetValue(_showGizmo.Value, false);
                    SetVisibility();
                };

                StudioAPI.StudioLoadedChanged += (sender, args) => Initialize();
            }
        }

#if DEBUG
        private void OnDestroy()
        {
            Destroy(_gizmoRoot);
            _hi?.UnpatchAll(_hi.Id);
            _selectedObjects = null;
        }
#endif

        private void Update()
        {
            if (_selectedObjects == null)
            {
                if (_showGizmo.Value)
                    Logger.LogWarning("_selectedObjects is null!");
                return;
            }
            if (!_showGizmo.Value) return;

            var anySelected = _selectedObjects.Count > 0;
            if (_lastAnySelected != anySelected)
            {
                _lastAnySelected = anySelected;
                DiagLog($"Selection state changed. anySelected={anySelected}, Count={_selectedObjects.Count}");
                SetVisibility();
            }

            if (_lastFov != _camera.fieldOfView || _lastScreenWidth != _camera.pixelWidth || _lastScreenHeight != _camera.pixelHeight)
            {
                AdjustScaleToFov();
                _lastScreenWidth = _camera.pixelWidth;
                _lastScreenHeight = _camera.pixelHeight;
            }

            // Pre-capture parent & bone world rotations for shift-roll (left-click).
            // In Update(), bone transforms still reflect the previous frame's FKCtrl output (correct FK pose).
            // By LateUpdate, the Animator resets bones to bind pose, making transform.rotation wrong
            // until FKCtrl re-applies — which is why we must capture here, not in LateUpdate.
            if (_extraGizmoDragActive && _selectedObjects != null)
            {
                var shiftGuide = _selectedObjects.FirstOrDefault(x => x.isActive);
                if (shiftGuide?.transformTarget != null)
                {
                    _extraGizmoUpdateParentWorldRot = shiftGuide.transformTarget.parent?.rotation ?? Quaternion.identity;
                    _extraGizmoUpdateBoneWorldRot = shiftGuide.transformTarget.rotation;
                }
            }

            CheckHoverExit();
            HandleBoneScroll();
            HandleMiddleClickReset();
            HandleReverseRotation();
            AdvanceFingerSync();
            HandleCustomDrag();
            ApplyRotationRestrictions();
        }

        private void LateUpdate()
        {
            HandleFingerTransformOverride();
            HandleExtraGizmoShiftRoll();
        }

        private void HandleMiddleClickReset()
        {
            if (!Input.GetMouseButtonDown(2)) return;
            if (_gizmoRoot == null || _selectedObjects == null || _selectedObjects.Count == 0) return;

            Vector3 gizmoScreenPos = _camera.WorldToScreenPoint(_gizmoRoot.transform.position);
            Vector2 mousePos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            Vector2 gizmo2D = new Vector2(gizmoScreenPos.x, gizmoScreenPos.y);

            if (Vector2.Distance(mousePos, gizmo2D) > (Screen.height * 0.20f)) return;

            foreach (var g in _selectedObjects)
            {
                if (g == null || !g.isActive) continue;

                // Special case: zeroing cf_j_hips counter-rotates its two adjacent bones
                // (cf_j_spine01 and cf_j_kosi01) to preserve the character's world pose.
                if (g.transformTarget != null && IsHipsBone(g.transformTarget.name.ToLower()))
                {
                    Quaternion qHipsOld = Quaternion.Euler(g.changeAmount.rot);
                    g.changeAmount.rot = Vector3.zero;

                    var charBones = GetFKBonesFromGuide(g);
                    if (charBones != null)
                    {
#if KK || KKS
                        foreach (string adjacentName in new[] { "cf_j_spine01", "cf_j_waist01" })
#else
                        foreach (string adjacentName in new[] { "cf_j_spine01", "cf_j_kosi01" })
#endif
                        {
                            var boneInfo = charBones.FirstOrDefault(b =>
                                b?.guideObject?.transformTarget != null &&
                                b.guideObject.transformTarget.name.ToLower() == adjacentName);
                            if (boneInfo?.guideObject != null)
                            {
                                // Bake the hips rotation into the child so world orientation is preserved.
                                // Q_child_new = Q_hips_old * Q_child_old
                                Quaternion qChildNew = qHipsOld * Quaternion.Euler(boneInfo.guideObject.changeAmount.rot);
                                boneInfo.guideObject.changeAmount.rot = qChildNew.eulerAngles;
                            }
                        }
                    }
                }
                else
                {
                    g.changeAmount.rot = Vector3.zero;
                }
            }
        }

        internal static bool _customDragActive = false;
        internal static bool _extraGizmoDragActive = false;
        internal static Vector2 _dragStartPos;
        internal static Vector3 _dragStartRot;
        internal static Vector2 _dragDirection;
        internal static bool _dragDirectionSet;
        internal static GuideObject _draggedGuideObject;
        internal static string _draggedConstrainedAxis; // "x", "y", "z"

        // Reverse FK (right-click drag) state
        private struct ReverseRotChildInfo
        {
            public GuideObject guideObject;
            public Quaternion changeAmountQuat;  // Quaternion.Euler(changeAmount.rot) at drag start
            public float weight;                 // fraction of deltaInv to apply (1.0 = full counter-rot, 0.5 = half)
            public Quaternion targetQuat;        // computed counter-rotation; written to transform in LateUpdate, synced to changeAmount at drag end
        }

        // Named child map: bones that should counter-rotate when their parent is right-click dragged.
        // Used for bones whose FK children aren't direct transform-hierarchy children.
#if KK || KKS
        private static readonly Dictionary<string, string[]> _reverseRotNamedChildren = new Dictionary<string, string[]>
        {
            { "cf_j_spine02", new[] { "cf_j_neck", "cf_j_shoulder_r", "cf_j_shoulder_l" } },
            { "cf_j_waist01", new[] { "cf_j_thigh00_r", "cf_j_thigh00_l" } }
#else
        private static readonly Dictionary<string, string[]> _reverseRotNamedChildren = new Dictionary<string, string[]>
        {
            { "cf_j_spine02", new[] { "cf_j_neck", "cf_j_shoulder_r", "cf_j_shoulder_l" } },
            { "cf_j_kosi01",  new[] { "cf_j_legup00_r", "cf_j_legup00_l" } }
#endif
        };

        private static bool _rightDragActive;
        private static Vector2 _rightDragStartPos;
        private static Vector3 _rightDragStartRot;
        private static GuideObject _rightDragGuideObject;
        private static string _rightDragAxis;
        private static string _rightDragBoneName;
        private static List<ReverseRotChildInfo> _rightDragChildren;
        private static Quaternion _rightDragParentWorldRot;
        private static bool _rightDragShiftMode;
        private static Vector2 _rightDragDirection;
        private static bool _rightDragDirectionSet;
        private static bool _rightDragIsHandBone;  // hand bone mode: real-time finger transform override + spread sync at drag end

        // Finger sync phase: spread changeAmount.rot writes across frames to stay under
        // Galatea's bulk-change detection threshold (MIN_CHANGED_BONES = 5).
        private static List<ReverseRotChildInfo> _fingerSyncChildren;
        private static int _fingerSyncIndex;
        private const int FINGER_SYNC_PER_FRAME = 4;

        // After a shift-roll drag (left or right click), block shift-scroll until both shift and mouse are released
        private static bool _shiftRollCooldown;

        // Shift-roll state for left-click drag on unconstrained bones (Studio's native drag, overridden in LateUpdate)
        internal static bool _extraGizmoShiftRollMode;
        private static Vector2 _extraGizmoShiftStartPos;
        private static Vector3 _extraGizmoShiftStartRot;
        private static Quaternion _extraGizmoShiftParentWorldRot;
        private static Vector2 _extraGizmoShiftDirection;
        private static bool _extraGizmoShiftDirectionSet;
        private static int _extraGizmoShiftDiagCount; // diagnostic frame counter, reset on shift-toggle
        private static Quaternion _extraGizmoShiftBoneWorldRot; // bone's full world rotation at shift-enter (rest * changeAmount)
        private static GuideObject _extraGizmoShiftGuide; // the guide object the shift-roll is anchored to

        // Pre-captured parent/bone world rotations from Update() — bone transforms in Update still
        // reflect the PREVIOUS frame's FKCtrl output (correct FK pose). By LateUpdate, the Animator
        // has already reset bones to bind pose, making transform.rotation stale/wrong until FKCtrl
        // re-applies changeAmount. This was the root cause of the multi-axis rotation bug on
        // secondary FK bones (e.g. Spine02 when Spine01 is rotated).
        private static Quaternion _extraGizmoUpdateParentWorldRot;
        private static Quaternion _extraGizmoUpdateBoneWorldRot;

        private void HandleReverseRotation()
        {
            // --- Start ---
            if (Input.GetMouseButtonDown(1))
            {
                if (_rightDragActive || _customDragActive) return;
                if (_gizmoRoot == null || _selectedObjects == null || _selectedObjects.Count == 0) return;

                Vector3 gizmoScreenPos = _camera.WorldToScreenPoint(_gizmoRoot.transform.position);
                Vector2 mousePos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
                if (Vector2.Distance(mousePos, new Vector2(gizmoScreenPos.x, gizmoScreenPos.y)) > Screen.height * 0.20f) return;

                var guide = _selectedObjects.FirstOrDefault(x => x.isActive);
                if (guide == null || guide.transformTarget == null) return;

                _rightDragActive = true;
                _fingerSyncChildren = null; // cancel any in-progress sync from previous drag
                _fingerSyncIndex = 0;
                _rightDragGuideObject = guide;
                _rightDragStartPos = mousePos;
                _rightDragStartRot = guide.changeAmount.rot;
                _rightDragParentWorldRot = guide.transformTarget?.parent?.rotation ?? Quaternion.identity;
                _rightDragShiftMode = false;
                _rightDragDirectionSet = false;
                _rightDragDirection = Vector2.zero;

                // Determine constrained axis (same rules as normal drag)
                string name = guide.transformTarget.name.ToLower();
                _rightDragBoneName = name;
                _rightDragAxis = null;
                if (IsKneeBone(name)) _rightDragAxis = "x";
                else if (IsElbowBone(name)) _rightDragAxis = "y";
                else if (IsThumbTipBone(name)) _rightDragAxis = "y";
                else if (IsFingerMidSegment(name)) _rightDragAxis = "z";

                // Find FK children to counter-rotate. Two sources:
                //   1. Direct transform-hierarchy children (works for arms, legs, spine chain)
                //   2. Named child map for bones where intermediate non-FK transforms break parent check
                _rightDragChildren = new List<ReverseRotChildInfo>();
                var charBones = GetFKBonesFromGuide(guide);
                if (charBones != null)
                {
                    // Source 1: direct transform-parent children
                    foreach (var bone in charBones)
                    {
                        if (bone?.guideObject?.transformTarget != null &&
                            bone.guideObject != guide &&
                            bone.guideObject.transformTarget.parent == guide.transformTarget)
                        {
                            _rightDragChildren.Add(new ReverseRotChildInfo
                            {
                                guideObject = bone.guideObject,
                                changeAmountQuat = Quaternion.Euler(bone.guideObject.changeAmount.rot),
                                weight = 1.0f
                            });
                        }
                    }

                    // Source 2: named children for bones with non-direct hierarchy
                    if (_reverseRotNamedChildren.TryGetValue(name, out string[] extraNames))
                    {
                        foreach (var extraName in extraNames)
                        {
                            var namedBone = charBones.FirstOrDefault(b =>
                                b?.guideObject?.transformTarget != null &&
                                b.guideObject.transformTarget.name.ToLower() == extraName);
                            if (namedBone != null && !_rightDragChildren.Any(c => c.guideObject == namedBone.guideObject))
                            {
                                _rightDragChildren.Add(new ReverseRotChildInfo
                                {
                                    guideObject = namedBone.guideObject,
                                    changeAmountQuat = Quaternion.Euler(namedBone.guideObject.changeAmount.rot),
                                    weight = 1.0f
                                });
                            }
                        }
                    }

#if !(KK || KKS)
                    // Source 3: hand bone — split counter-rotation 50/50 across finger 01 and 02 bones.
                    // Uses explicit name matching instead of transform hierarchy because intermediate
                    // non-FK transforms between the hand and finger bones break the direct parent check.
                    // Filter by side suffix (_r/_l) so only the dragged hand's fingers are affected.
                    // Disabled for KK: right-hand finger 01 rest-pose ~180° Y+Z offset breaks the
                    // Z-projection constraint math. Hand counter-rotation works without fingers.
                    if (IsHandBone(name))
                    {
                        // HS2+: defer finger writes to drag end (spread sync) to avoid
                        // ABMX/FKHeight disruption. HS uses real-time writes (no FKHeightAdjust).
                        _rightDragIsHandBone = true;

                        string sideSuffix = name.EndsWith("_r") ? "_r" : "_l";
                        _rightDragChildren.Clear(); // discard any direct-hierarchy finds; rebuild by name
                        foreach (var bone in charBones)
                        {
                            if (bone?.guideObject?.transformTarget == null || bone.guideObject == guide) continue;
                            string bn = bone.guideObject.transformTarget.name.ToLower();
                            if (!bn.EndsWith(sideSuffix)) continue; // skip opposite hand's fingers
                            bool is01 = bn.Contains("index01") || bn.Contains("middle01") ||
                                        bn.Contains("ring01") || bn.Contains("little01");
                            bool is02 = bn.Contains("index02") || bn.Contains("middle02") ||
                                        bn.Contains("ring02") || bn.Contains("little02");
                            if (is01 || is02)
                            {
                                _rightDragChildren.Add(new ReverseRotChildInfo
                                {
                                    guideObject = bone.guideObject,
                                    changeAmountQuat = Quaternion.Euler(bone.guideObject.changeAmount.rot),
                                    weight = 0.5f,
                                    targetQuat = Quaternion.Euler(bone.guideObject.changeAmount.rot)
                                });
                            }
                        }
                    }
#endif
                }
                return;
            }

            // --- End ---
            if (_rightDragActive && !Input.GetMouseButton(1))
            {
                if (_rightDragIsHandBone && _rightDragChildren != null)
                {
                    // Non-HS: spread sync across frames (≤4/frame) to stay under Galatea's
                    // bulk-change detection threshold (5 bones). LateUpdate continues to
                    // override finger transforms during sync so FKCtrl doesn't snap them back.
                    _fingerSyncChildren = new List<ReverseRotChildInfo>(_rightDragChildren);
                    _fingerSyncIndex = 0;
                }
                if (_rightDragShiftMode) _shiftRollCooldown = true;
                _rightDragActive = false;
                _rightDragGuideObject = null;
                _rightDragChildren = null;
                _rightDragShiftMode = false;
                _rightDragIsHandBone = false;
                return;
            }

            // --- During drag ---
            if (!_rightDragActive || _rightDragGuideObject == null) return;

            Vector2 currentMousePos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);

            // Shift key: seamlessly toggle roll mode — unconstrained bones only.
            // Constrained bones (knees, elbows, fingers) stay locked to their single axis regardless of shift.
            bool shiftNow = _rightDragAxis == null && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            if (shiftNow != _rightDragShiftMode)
            {
                _rightDragShiftMode = shiftNow;
                _rightDragStartRot = _rightDragGuideObject.changeAmount.rot;
                _rightDragStartPos = currentMousePos;
                _rightDragParentWorldRot = _rightDragGuideObject.transformTarget?.parent?.rotation ?? Quaternion.identity;
                _rightDragDirectionSet = false;
                if (_rightDragChildren != null)
                    for (int i = 0; i < _rightDragChildren.Count; i++)
                    {
                        var c = _rightDragChildren[i];
                        c.changeAmountQuat = Quaternion.Euler(c.guideObject.changeAmount.rot);
                        _rightDragChildren[i] = c;
                    }
                return; // skip this frame; next frame delta will be zero from the new anchor
            }

            Vector2 delta = currentMousePos - _rightDragStartPos;

            if (delta.magnitude <= 2f) return;

            float sensitivity = 0.5f;
            Vector3 newRot = _rightDragStartRot;

            if (_rightDragShiftMode)
            {
                // Shift — roll around camera's forward axis. Lock to initial drag direction so any
                // mouse gesture (not just horizontal) contributes, consistent with left-click drag feel.
                if (!_rightDragDirectionSet) { _rightDragDirection = delta.normalized; _rightDragDirectionSet = true; }
                float rollAngle = Vector2.Dot(delta, _rightDragDirection) * sensitivity;
                Quaternion worldDelta = Quaternion.AngleAxis(rollAngle, _camera.transform.forward);
                Quaternion localDelta = Quaternion.Inverse(_rightDragParentWorldRot) * worldDelta * _rightDragParentWorldRot;
                newRot = (localDelta * Quaternion.Euler(_rightDragStartRot)).eulerAngles;
            }
            else if (_rightDragAxis != null)
            {
                // Constrained bone — map the most natural mouse axis to the bone's rotation axis
                // Signs match left-click gizmo convention
                float mouseDelta;
                if (_rightDragAxis == "x") mouseDelta = -delta.y;       // vertical mouse → X (pitch)
                else if (_rightDragAxis == "y") mouseDelta = -delta.x;  // horizontal mouse → Y (yaw)
                else mouseDelta = -delta.x;                              // horizontal mouse → Z (roll)

                if (_rightDragAxis == "x") newRot.x += mouseDelta * sensitivity;
                else if (_rightDragAxis == "y") newRot.y += mouseDelta * sensitivity;
                else newRot.z += mouseDelta * sensitivity;
            }
            else
            {
                // Unconstrained bone — camera-relative rotation.
                // Mouse movement is mapped to world-space rotation axes derived from the camera,
                // then converted to bone-local space via the parent's world rotation captured at drag start.
                Vector3 camRight = _camera.transform.right;
                Vector3 camUp = _camera.transform.up;
                float yawAngle = -delta.x * sensitivity;
                float pitchAngle = delta.y * sensitivity;
                Quaternion worldDelta = Quaternion.AngleAxis(pitchAngle, camRight) * Quaternion.AngleAxis(yawAngle, camUp);
                Quaternion localDelta = Quaternion.Inverse(_rightDragParentWorldRot) * worldDelta * _rightDragParentWorldRot;
                newRot = (localDelta * Quaternion.Euler(_rightDragStartRot)).eulerAngles;
            }

            // Apply rotation to the selected bone
            _rightDragGuideObject.changeAmount.rot = newRot;

            // Compensate each child to preserve its world rotation.
            // Formula: Qcn = Inv(Qsn) * Qs0 * Qc0  (= deltaInv * Qc0)
            // Derivation: world_child = ...parent_world * Qs * Qc. To keep world_child constant
            // when Qs changes Qs0→Qsn: Qs0*Qc0 = Qsn*Qcn → Qcn = Inv(Qsn)*Qs0*Qc0.
            // This works correctly when FK changeAmount ≈ localRotation (normal HS2 Studio FK mode).
            // Then clamp hinge children to their anatomically allowed axes.
            if (_rightDragChildren != null && _rightDragChildren.Count > 0)
            {
                Quaternion qs0 = Quaternion.Euler(_rightDragStartRot);
                Quaternion qsn = Quaternion.Euler(newRot);
                Quaternion deltaInv = Quaternion.Inverse(qsn) * qs0;

                for (int i = 0; i < _rightDragChildren.Count; i++)
                {
                    var child = _rightDragChildren[i];
                    // Weight < 1: partial counter-rotation (e.g. 0.5 = absorb 50% of the rotation change)
                    Quaternion effectiveDeltaInv = child.weight >= 1.0f
                        ? deltaInv
                        : Quaternion.Slerp(Quaternion.identity, deltaInv, child.weight);
                    Quaternion qcn = effectiveDeltaInv * child.changeAmountQuat;

                    // Enforce anatomical constraints via quaternion projection + angle clamping.
                    // Projection uses the same method as ApplyRotationRestrictions.
                    // Clamping is applied only here (right-click reverse FK) — not to left-click or FK panel.
                    if (child.guideObject.transformTarget != null)
                    {
                        string cn = child.guideObject.transformTarget.name.ToLower();
                        float num;
                        if (IsKneeBone(cn))
                        {
                            // Knee: X only projection
                            num = Mathf.Sqrt(qcn.w * qcn.w + qcn.x * qcn.x);
                            qcn = num > 0.0001f ? new Quaternion(qcn.x / num, 0f, 0f, qcn.w / num) : Quaternion.identity;

                            // Hemisphere correction: when the parent crosses 0° on any axis, the
                            // counter-rotation can flip the quaternion (q and -q are the same rotation
                            // but ClampHingeX treats the sign flip as "wrong bend direction" → snap to
                            // identity, causing the visible kick). Normalize to the same hemisphere as
                            // the knee's starting rotation so the clamp direction check stays consistent.
                            float sw = child.changeAmountQuat.w, sx = child.changeAmountQuat.x;
                            float sn = Mathf.Sqrt(sw * sw + sx * sx);
                            if (sn > 0.0001f)
                            {
                                float dot = qcn.x * (sx / sn) + qcn.w * (sw / sn);
                                if (dot < 0f)
                                    qcn = new Quaternion(-qcn.x, 0f, 0f, -qcn.w);
                            }

                            // Now safe to clamp — ~155° max knee bend
                            ClampHingeX(ref qcn, Quaternion.Euler(155f, 0f, 0f));
                        }
                        else if (IsElbowBone(cn))
                        {
                            // Elbow: Y only projection, then Euler clamp per arm.
                            // Left arm curls positive Y [0°→165°], right curls negative Y [360°→200°].
                            // Euler clamping after projection avoids the quaternion hemisphere
                            // issue that breaks direction detection at angles > 180°.
                            num = Mathf.Sqrt(qcn.w * qcn.w + qcn.y * qcn.y);
                            qcn = num > 0.0001f ? new Quaternion(0f, qcn.y / num, 0f, qcn.w / num) : Quaternion.identity;
                            float ey = qcn.eulerAngles.y;
                            if (cn.Contains("_l"))
                            {
                                // Left arm: valid [0°, 165°], midpoint of invalid zone = 262.5°
                                if (ey > 165f && ey <= 262.5f) ey = 165f;
                                else if (ey > 262.5f) ey = 0f;
                            }
                            else
                            {
                                // Right arm: valid {0°} ∪ [200°, 360°), midpoint of invalid zone = 100°
                                if (ey > 0f && ey < 100f) ey = 0f;
                                else if (ey >= 100f && ey < 200f) ey = 200f;
                            }
                            qcn = Quaternion.Euler(0f, ey, 0f);
                        }
                        else if (IsThumbTipBone(cn))
                        {
                            // Thumb tip: Y only, no additional clamping
                            num = Mathf.Sqrt(qcn.w * qcn.w + qcn.y * qcn.y);
                            qcn = num > 0.0001f ? new Quaternion(0f, qcn.y / num, 0f, qcn.w / num) : Quaternion.identity;
                        }
                        else if (IsFingerMidSegment(cn))
                        {
                            // Finger mid segments: Z only, clamp to [0°, 90°]
                            num = Mathf.Sqrt(qcn.w * qcn.w + qcn.z * qcn.z);
                            qcn = num > 0.0001f ? new Quaternion(0f, 0f, qcn.z / num, qcn.w / num) : Quaternion.identity;
                            float curlSign = cn.EndsWith("_l") ? 1f : -1f;
                            ClampFingerZ(ref qcn, curlSign);
                        }
                        else if (IsFingerBase(cn))
                        {
                            // Finger base bones: extract Z-only change from counter-rotation,
                            // clamp asymmetrically (30° extension, 60° curl), recombine with
                            // original X/Y. Delta-from-start avoids rest-pose assumptions.
                            Vector3 origEuler = child.changeAmountQuat.eulerAngles;
                            Quaternion fingerDelta = Quaternion.Inverse(child.changeAmountQuat) * qcn;
                            float dzn = Mathf.Sqrt(fingerDelta.w * fingerDelta.w + fingerDelta.z * fingerDelta.z);
                            Quaternion zDelta = dzn > 0.0001f
                                ? new Quaternion(0f, 0f, fingerDelta.z / dzn, fingerDelta.w / dzn)
                                : Quaternion.identity;
                            if (zDelta.w < 0f) zDelta = new Quaternion(0f, 0f, -zDelta.z, -zDelta.w);
                            float deltaZDeg = Mathf.Atan2(zDelta.z, zDelta.w) * 2f * Mathf.Rad2Deg;
                            float curlSign = cn.EndsWith("_l") ? 1f : -1f;
                            float minDeg = curlSign > 0f ? -30f : -60f; // extension limit
                            float maxDeg = curlSign > 0f ? 60f : 30f;   // curl limit
                            deltaZDeg = Mathf.Clamp(deltaZDeg, minDeg, maxDeg);
                            qcn = Quaternion.Euler(origEuler.x, origEuler.y, origEuler.z + deltaZDeg);
                        }
                    }

                    // Hand bone mode: store target rotation for LateUpdate transform override.
                    // changeAmount.rot is NOT written during drag — only the transform is
                    // overridden in LateUpdate after FKCtrl, avoiding onChangeRot events entirely.
                    if (_rightDragIsHandBone)
                    {
                        child.targetQuat = qcn;
                        _rightDragChildren[i] = child;
                    }
                    else
                    {
                        child.guideObject.changeAmount.rot = qcn.eulerAngles;
                    }
                }
            }
        }

        // Clamps a quaternion already projected to X-only between identity and a given max Euler.
        // Uses signed half-angle comparison to detect bend direction and magnitude.
        private static void ClampHingeX(ref Quaternion q, Quaternion maxEuler)
        {
            // Project maxEuler to X-only
            float numMax = Mathf.Sqrt(maxEuler.w * maxEuler.w + maxEuler.x * maxEuler.x);
            Quaternion qMax = numMax > 0.0001f
                ? new Quaternion(maxEuler.x / numMax, 0f, 0f, maxEuler.w / numMax)
                : Quaternion.identity;

            float halfAngle = Mathf.Atan2(q.x, q.w);
            float halfAngleMax = Mathf.Atan2(qMax.x, qMax.w);

            if (halfAngle * halfAngleMax < 0f) q = Quaternion.identity; // wrong direction → straight
            else if (Mathf.Abs(halfAngle) > Mathf.Abs(halfAngleMax)) q = qMax; // past limit → clamp
        }

        // Clamps a Z-only quaternion to [0°, 90°] range during hand counter-rotation.
        // curlSign: +1 for left hand (positive Z = curl), -1 for right hand (negative Z = curl).
        private static void ClampFingerZ(ref Quaternion q, float curlSign)
        {
            // Canonical form: ensure w >= 0 so halfAngle stays in [-π/2, π/2]
            if (q.w < 0f) q = new Quaternion(0f, 0f, -q.z, -q.w);

            float halfAngle = Mathf.Atan2(q.z, q.w);
            float halfMax = Mathf.PI / 4f; // 90°

            if (halfAngle * curlSign < 0f)
                q = Quaternion.identity; // backward past 0° → clamp to straight
            else if (Mathf.Abs(halfAngle) > halfMax)
                q = new Quaternion(0f, 0f, curlSign * Mathf.Sin(halfMax), Mathf.Cos(halfMax)); // past 90° → clamp
        }

        // Advances the finger sync phase: writes up to FINGER_SYNC_PER_FRAME bone
        // changeAmount.rot values per frame, with onChangeRot suppressed to avoid ABMX disruption.
        // Called in Update so FKCtrl picks up the values in the same frame's LateUpdate.
        private void AdvanceFingerSync()
        {
            if (_fingerSyncChildren == null || _fingerSyncIndex >= _fingerSyncChildren.Count) return;

            int end = Math.Min(_fingerSyncIndex + FINGER_SYNC_PER_FRAME, _fingerSyncChildren.Count);
            for (int i = _fingerSyncIndex; i < end; i++)
            {
                var child = _fingerSyncChildren[i];
                var ca = child.guideObject.changeAmount;
                var saved = ca.onChangeRot;
                ca.onChangeRot = null;
                ca.rot = child.targetQuat.eulerAngles;
                ca.onChangeRot = saved;
            }
            _fingerSyncIndex = end;
        }

        // Overrides finger bone transforms in LateUpdate (after FKCtrl via DefaultExecutionOrder)
        // during two phases:
        // 1. Right-click drag on hand bones — real-time counter-rotation visible during drag
        // 2. Post-drag sync phase — holds transforms while changeAmount.rot is spread across frames
        private void HandleFingerTransformOverride()
        {
            // During drag: real-time finger counter-rotation after FKCtrl
            if (_rightDragIsHandBone && _rightDragChildren != null)
            {
                foreach (var child in _rightDragChildren)
                {
                    if (child.guideObject?.transformTarget != null)
                        child.guideObject.transformTarget.localRotation = child.targetQuat;
                }
                return;
            }

            // Sync phase: hold transforms while spreading changeAmount.rot writes
            if (_fingerSyncChildren != null)
            {
                foreach (var child in _fingerSyncChildren)
                {
                    if (child.guideObject?.transformTarget != null)
                        child.guideObject.transformTarget.localRotation = child.targetQuat;
                }

                // All changeAmount.rot values synced — FKCtrl has correct data, safe to release
                if (_fingerSyncIndex >= _fingerSyncChildren.Count)
                {
                    _fingerSyncChildren = null;
                    _fingerSyncIndex = 0;
                }
            }
        }

        // Intercepts Studio's native left-click drag on unconstrained bones while shift is held,
        // overriding the axis-ring rotation with a camera-forward roll. Runs in LateUpdate so it
        // writes after Studio's EventSystem callbacks and always wins the last-write race.
        private void HandleExtraGizmoShiftRoll()
        {
            if (!StudioAPI.StudioLoaded || _camera == null) return;

            // Right-click drag owns its own shift-roll path (HandleReverseRotation in Update).
            // Bail here so LateUpdate doesn't stomp it.
            if (_rightDragActive) return;

            if (!_extraGizmoDragActive)
            {
                if (_extraGizmoShiftRollMode) _shiftRollCooldown = true;
                _extraGizmoShiftRollMode = false;
                _extraGizmoShiftGuide = null;
                return;
            }

            // Sanity-check: if the left mouse button is no longer held, clear the drag flag.
            // OnEndDragHook may not fire if the cursor leaves the gizmo area mid-drag.
            if (!Input.GetMouseButton(0))
            {
                _extraGizmoDragActive = false;
                if (_extraGizmoShiftRollMode) _shiftRollCooldown = true;
                _extraGizmoShiftRollMode = false;
                _extraGizmoShiftGuide = null;
                return;
            }

            var guide = _selectedObjects?.FirstOrDefault(x => x.isActive);
            if (guide == null || guide.transformTarget == null) return;

            // Skip constrained bones — they go through HandleCustomDrag, not here
            string name = guide.transformTarget.name.ToLower();
            if (IsKneeBone(name) || IsElbowBone(name) || IsThumbTipBone(name) || IsFingerMidSegment(name))
                return;

            bool shiftNow = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Vector2 currentMousePos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);

            // Bone changed mid-drag (e.g. user scrolled to a new bone while shift-rolling).
            // Re-anchor start state to the new bone so it begins with zero accumulated delta,
            // preventing the old bone's roll from being copied onto the new one.
            if (_extraGizmoShiftRollMode && guide != _extraGizmoShiftGuide)
            {
                _extraGizmoShiftGuide = guide;
                _extraGizmoShiftStartRot = guide.changeAmount.rot;
                _extraGizmoShiftStartPos = currentMousePos;
                _extraGizmoShiftParentWorldRot = _extraGizmoUpdateParentWorldRot;
                _extraGizmoShiftBoneWorldRot = _extraGizmoUpdateBoneWorldRot;
                _extraGizmoShiftDirectionSet = false;
                _extraGizmoShiftDiagCount = 0;
                DiagLog($"[ShiftRoll-LClick] RE-ANCHOR — bone={guide.transformTarget.name}");
                return; // skip this frame so next delta starts from zero
            }

            if (shiftNow != _extraGizmoShiftRollMode)
            {
                _extraGizmoShiftRollMode = shiftNow;
                _extraGizmoShiftStartRot = guide.changeAmount.rot;
                _extraGizmoShiftStartPos = currentMousePos;
                // Use pre-captured values from Update() — by LateUpdate the Animator has reset
                // bone transforms to bind pose, making transform.rotation stale until FKCtrl runs.
                // The Update()-captured values still reflect the previous frame's FKCtrl output.
                _extraGizmoShiftParentWorldRot = _extraGizmoUpdateParentWorldRot;
                _extraGizmoShiftBoneWorldRot = _extraGizmoUpdateBoneWorldRot;
                _extraGizmoShiftDirectionSet = false;
                if (shiftNow)
                {
                    _extraGizmoShiftGuide = guide;
                    _extraGizmoShiftDiagCount = 0;
                    DiagLog($"[ShiftRoll-LClick] ENTER — bone={guide.transformTarget.name}, parent={guide.transformTarget.parent?.name}");
                    DiagLog($"  cam.forward={_camera.transform.forward:F3}  cam.name={_camera.name}");
                    DiagLog($"  parentWorldRot={_extraGizmoShiftParentWorldRot.eulerAngles:F1}");
                    DiagLog($"  boneWorldRot={_extraGizmoShiftBoneWorldRot.eulerAngles:F1}");
                    DiagLog($"  startRot(changeAmount)={_extraGizmoShiftStartRot:F1}");
                    DiagLog($"  transformTarget.localEuler={guide.transformTarget.localEulerAngles:F1}");
                }
                return;
            }

            if (!_extraGizmoShiftRollMode) return;

            Vector2 delta = currentMousePos - _extraGizmoShiftStartPos;
            if (delta.magnitude <= 2f) return;

            if (!_extraGizmoShiftDirectionSet)
            {
                _extraGizmoShiftDirection = delta.normalized;
                _extraGizmoShiftDirectionSet = true;
                DiagLog($"[ShiftRoll-LClick] first delta — dir={_extraGizmoShiftDirection:F3}");
            }
            float rollAngle = Vector2.Dot(delta, _extraGizmoShiftDirection) * 0.5f;
            Quaternion worldDelta = Quaternion.AngleAxis(rollAngle, _camera.transform.forward);

            // Convert world-space roll to changeAmount space via the parent's world rotation.
            // Same formula as the right-click shift-roll path. The parent rotation was pre-captured
            // in Update() where bone transforms still have correct FK values (see comment on
            // _extraGizmoUpdateParentWorldRot). Previously this captured in LateUpdate where the
            // Animator had already reset transforms to bind pose, causing stale parent rotations
            // and the multi-axis rotation bug on secondary FK bones.
            Quaternion localDelta = Quaternion.Inverse(_extraGizmoShiftParentWorldRot) * worldDelta * _extraGizmoShiftParentWorldRot;
            Quaternion desiredLocalQ = localDelta * Quaternion.Euler(_extraGizmoShiftStartRot);
            guide.changeAmount.rot = desiredLocalQ.eulerAngles;
            if (_extraGizmoShiftDiagCount < 3)
            {
                _extraGizmoShiftDiagCount++;
                DiagLog($"[ShiftRoll-LClick] frame {_extraGizmoShiftDiagCount}: rollAngle={rollAngle:F2}  localDelta.euler={localDelta.eulerAngles:F1}  after={guide.changeAmount.rot:F1}");
            }
        }

        private void HandleCustomDrag()
        {
            if (_customDragActive && _draggedGuideObject != null)
            {
                if (!Input.GetMouseButton(0)) // Drop drag if mouse released
                {
                    _customDragActive = false;
                    _draggedGuideObject = null;
                    return;
                }

                Vector2 currentMousePos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
                Vector2 delta = currentMousePos - _dragStartPos;

                if (delta.magnitude > 2f)
                {
                    if (!_dragDirectionSet)
                    {
                        // Compute the optimal drag direction from the bone's rotation axis projected
                        // onto screen space. This makes drag sensitivity consistent regardless of
                        // camera angle — the user always drags perpendicular to the axis's screen
                        // projection for maximum effect.
                        _dragDirection = ComputeOptimalDragDirection(_draggedGuideObject, _draggedConstrainedAxis);
                        _dragDirectionSet = true;
                    }

                    float distance = Vector2.Dot(delta, _dragDirection);
                    float angleDelta = distance * 0.5f;

                    Vector3 newRot = _dragStartRot;

                    if (_draggedConstrainedAxis == "x")
                        newRot.x += angleDelta;
                    else if (_draggedConstrainedAxis == "y")
                        newRot.y += angleDelta;
                    else if (_draggedConstrainedAxis == "z")
                        newRot.z += angleDelta;

                    _draggedGuideObject.changeAmount.rot = newRot;
                }
            }
        }

        /// <summary>
        /// Projects the bone's constrained rotation axis onto screen space and returns the
        /// perpendicular direction — the mouse direction that produces maximum rotation.
        /// This ensures consistent drag sensitivity regardless of camera orientation.
        /// Falls back to the camera's right vector if the axis points directly at the camera.
        /// </summary>
        private Vector2 ComputeOptimalDragDirection(GuideObject guide, string axis)
        {
            // Get the local axis vector
            Vector3 localAxis;
            if (axis == "x") localAxis = Vector3.right;
            else if (axis == "y") localAxis = Vector3.up;
            else localAxis = Vector3.forward;

            // Transform to world space via the bone's parent rotation
            Quaternion parentWorldRot = guide.transformTarget.parent?.rotation ?? Quaternion.identity;
            Vector3 worldAxis = parentWorldRot * localAxis;

            // Project the axis onto screen space: get screen positions of the bone and bone+axis
            Vector3 boneWorldPos = guide.transformTarget.position;
            Vector3 screenA = _camera.WorldToScreenPoint(boneWorldPos);
            Vector3 screenB = _camera.WorldToScreenPoint(boneWorldPos + worldAxis);

            Vector2 screenAxis = new Vector2(screenB.x - screenA.x, screenB.y - screenA.y);

            // If the axis points directly at/away from the camera, screen projection is near-zero.
            // Fall back to a reasonable default (camera right → horizontal drag).
            if (screenAxis.magnitude < 0.5f)
            {
                return new Vector2(1f, 0f);
            }

            screenAxis.Normalize();

            // The optimal drag direction is perpendicular to the axis projection.
            // Rotating "around" the axis means moving perpendicular to it on screen.
            return new Vector2(-screenAxis.y, screenAxis.x);
        }

        private void ApplyRotationRestrictions()
        {
            if (_selectedObjects == null || _selectedObjects.Count == 0) return;

            bool extraAllowX = false;
            bool extraAllowY = false;
            bool extraAllowZ = false;

            // Apply to the World Gizmos (all selected objects)
            foreach (var g in _selectedObjects)
            {
                if (g == null || g.transformTarget == null) continue;

                string name = g.transformTarget.name.ToLower();

                bool allowX = true;
                bool allowY = true;
                bool allowZ = true;

                // Knees: X only
                if (IsKneeBone(name))
                {
                    allowY = false; allowZ = false;
                }
                // Elbows: Y only
                else if (IsElbowBone(name))
                {
                    allowX = false; allowZ = false;
                }
                // Thumbs outermost: Y only
                else if (IsThumbTipBone(name))
                {
                    allowX = false; allowZ = false;
                }
                // Others outermost 2: Z only
                else if (IsFingerMidSegment(name))
                {
                    allowX = false; allowY = false;
                }

                extraAllowX |= allowX;
                extraAllowY |= allowY;
                extraAllowZ |= allowZ;

                // Restrict the physical UI rings for THIS world bone, but ALWAYS leave the center orb (else = true)
                Transform rot = g.transform.Find("rotation");
                if (rot != null)
                {
                    foreach (Transform t in rot)
                    {
                        string tName = t.name.ToLower();
                        if (tName == "x") t.gameObject.SetActiveIfDifferent(allowX);
                        else if (tName == "y") t.gameObject.SetActiveIfDifferent(allowY);
                        else if (tName == "z") t.gameObject.SetActiveIfDifferent(allowZ);
                        else t.gameObject.SetActiveIfDifferent(true); // Center orb
                    }
                }

                // Mathematically lock the bone — only during ExtraMoveAxis gizmo drags.
                // Gating on extra-gizmo or custom-drag ensures the studio's own FK panel inputs are never snapped back.
                if ((!allowX || !allowY || !allowZ) && (_extraGizmoDragActive || (_customDragActive && _draggedGuideObject == g)))
                {
                    Quaternion q = Quaternion.Euler(g.changeAmount.rot);

                    if (allowX && !allowY && !allowZ)
                    {
                        float num = Mathf.Sqrt(q.w * q.w + q.x * q.x);
                        q = num > 0.0001f ? new Quaternion(q.x / num, 0f, 0f, q.w / num) : Quaternion.identity;
                    }
                    else if (!allowX && allowY && !allowZ)
                    {
                        float num = Mathf.Sqrt(q.w * q.w + q.y * q.y);
                        q = num > 0.0001f ? new Quaternion(0f, q.y / num, 0f, q.w / num) : Quaternion.identity;
                    }
                    else if (!allowX && !allowY && allowZ)
                    {
                        float num = Mathf.Sqrt(q.w * q.w + q.z * q.z);
                        q = num > 0.0001f ? new Quaternion(0f, 0f, q.z / num, q.w / num) : Quaternion.identity;
                    }
                    else
                    {
                        Vector3 currentRot = q.eulerAngles;
                        if (!allowX) currentRot.x = 0f;
                        if (!allowY) currentRot.y = 0f;
                        if (!allowZ) currentRot.z = 0f;
                        q = Quaternion.Euler(currentRot);
                    }

                    g.changeAmount.rot = q.eulerAngles;
                }
            }

            // Apply global combined permissions to the Extra Gizmo
            if (_rotObj != null)
            {
                foreach (Transform t in _rotObj.transform)
                {
                    string tName = t.name.ToLower();
                    if (tName == "x") t.gameObject.SetActiveIfDifferent(extraAllowX);
                    else if (tName == "y") t.gameObject.SetActiveIfDifferent(extraAllowY);
                    else if (tName == "z") t.gameObject.SetActiveIfDifferent(extraAllowZ);
                    else t.gameObject.SetActiveIfDifferent(true);
                }
            }
        }

        // SanitizeAxis removed, using superior Quaternion projection above

        private static GuideObject _originalSelectedBone;
        private static bool _isShiftScrollingActive;
        private static int _shiftScrollDistance;

        private void CheckHoverExit()
        {
            if (!_isShiftScrollingActive || _gizmoRoot == null) return;

            // Pillarboxing breaks strict pixel distances. Use a flexible percentage of the screen height.
            Vector3 gizmoScreenPos = _camera.WorldToScreenPoint(_gizmoRoot.transform.position);
            Vector2 mousePos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            Vector2 gizmo2D = new Vector2(gizmoScreenPos.x, gizmoScreenPos.y);

            // 35% of the screen height acts as a very generous and flexible "bottom right quadrant" exit boundary
            if (Vector2.Distance(mousePos, gizmo2D) > (Screen.height * 0.35f))
            {
                // Revert to original selection
                if (_originalSelectedBone != null)
                {
                    foreach (var obj in GuideObjectManager.Instance.hashSelectObject)
                    {
                        if (obj != _originalSelectedBone)
                        {
                            obj.isActive = false;
                        }
                    }
                    GuideObjectManager.Instance.selectObject = _originalSelectedBone;
                }
                _isShiftScrollingActive = false;
                _originalSelectedBone = null;
                _shiftScrollDistance = 0;
            }
        }

        private void HandleBoneScroll()
        {
            float scroll = 0f;
            try
            {
                scroll = Input.GetAxis("Mouse ScrollWheel");
            }
            catch { }

            if (Mathf.Abs(scroll) < 0.01f)
            {
                scroll = Input.mouseScrollDelta.y;
            }

            if (Mathf.Abs(scroll) < 0.01f) return;
            // Logger.LogDebug($"[Scroll] Detected scroll input: {scroll}");

            // If a constrained-bone drag is active, cancel it so the scroll can cycle bones cleanly.
            // Without this, HandleCustomDrag() overwrites the old bone's rotation every frame and
            // makes bone cycling appear to do nothing.
            if (_customDragActive)
            {
                _customDragActive = false;
                _draggedGuideObject = null;
            }

            if (!StudioAPI.StudioLoaded)
            {
                Logger.LogDebug("[Scroll] Studio is not loaded");
                return;
            }
            if (_selectedObjects == null || _selectedObjects.Count == 0)
            {
                Logger.LogDebug("[Scroll] No selected objects");
                return;
            }

            // Restrict scrolling to when the mouse is physically near the gizmo on screen
            if (_gizmoRoot != null)
            {
                Vector3 gizmoScreenPos = _camera.WorldToScreenPoint(_gizmoRoot.transform.position);
                Vector2 mousePos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
                Vector2 gizmo2D = new Vector2(gizmoScreenPos.x, gizmoScreenPos.y);

                float dist = Vector2.Distance(mousePos, gizmo2D);
                // About 20% of the screen height gives a nice, generous hover zone around the widget
                if (dist > (Screen.height * 0.20f))
                {
                    Logger.LogDebug($"[Scroll] Mouse too far from widget. Distance: {dist}, Threshold: {Screen.height * 0.20f}");
                    return;
                }
            }
            else
            {
                Logger.LogDebug("[Scroll] Gizmo root is null");
            }

            var guide = _selectedObjects.FirstOrDefault(x => x.isActive);
            if (guide == null || guide.transformTarget == null)
            {
                Logger.LogDebug("[Scroll] Selected guide or transformTarget is null");
                return;
            }

            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool mouseHeld = Input.GetMouseButton(0) || Input.GetMouseButton(1);

            // Clear shift-roll cooldown once both shift and mouse are fully released
            if (_shiftRollCooldown && !shiftHeld && !mouseHeld)
                _shiftRollCooldown = false;

            // Suppress shift-scroll during any active drag or while cooldown is active
            if (_shiftRollCooldown || _rightDragActive || _extraGizmoDragActive)
                shiftHeld = false;

            if (shiftHeld)
            {
                if (!_isShiftScrollingActive)
                {
                    _isShiftScrollingActive = true;
                    _originalSelectedBone = guide; // Record the starting bone
                    _shiftScrollDistance = 0;
                }
            }
            else
            {
                if (_isShiftScrollingActive)
                {
                    // They let go of shift, but haven't moused away. If they scroll now without shift, cancel multi-select.
                    _isShiftScrollingActive = false;
                    _originalSelectedBone = null;
                    _shiftScrollDistance = 0;
                }
            }

            GuideObject refGuide = _isShiftScrollingActive && _originalSelectedBone != null ? _originalSelectedBone : guide;

            // Try to find if this is part of a character FK
            List<OCIChar.BoneInfo> charBones = GetFKBonesFromGuide(refGuide);
            if (charBones == null || charBones.Count == 0)
            {
                Logger.LogDebug($"[Scroll] GetFKBonesFromGuide returned null/empty for guide {(refGuide.transformTarget != null ? refGuide.transformTarget.name : "null")}");
                return;
            }

            // Determine our current bone's place in the custom order list
            string currentName = refGuide.transformTarget.name;
            int currentIndex = GetBoneOrderIndex(currentName);
            Logger.LogDebug($"[Scroll] Scroll success. currentName={currentName}, currentIndex={currentIndex}, shiftHeld={shiftHeld}");


            int step = scroll > 0 ? 1 : -1; // scroll up usually > 0

            if (shiftHeld)
            {
                _shiftScrollDistance += step;
                // Allow up to 2 additional bones beyond the original selection
                if (_shiftScrollDistance > 2) _shiftScrollDistance = 2;
                if (_shiftScrollDistance < -2) _shiftScrollDistance = -2;

                // Reset to 1 object first
                GuideObjectManager.Instance.selectObject = _originalSelectedBone;

                int dir = Math.Sign(_shiftScrollDistance);
                int count = Math.Abs(_shiftScrollDistance);

                if (currentIndex == -1) currentIndex = 0;

                bool shiftCurrentIsHips = currentIndex == 0;

                for (int i = 1; i <= count; i++)
                {
                    int nextIndex = (currentIndex + dir * i) % BoneCycleOrder.Length;
                    if (nextIndex < 0) nextIndex += BoneCycleOrder.Length;

                    // Skip hips (index 0) unless we started on hips
                    if (nextIndex == 0 && !shiftCurrentIsHips) continue;

                    string targetName = BoneCycleOrder[nextIndex].ToLower();

                    var nextBone = charBones.FirstOrDefault(b => b.guideObject != null && b.guideObject.transformTarget != null && b.guideObject.transformTarget.name.ToLower() == targetName);

                    if (nextBone != null)
                    {
                        nextBone.guideObject.isActive = true;
                        var selectedObjects = GuideObjectManager.Instance.hashSelectObject;
                        if (!selectedObjects.Contains(nextBone.guideObject))
                        {
                            selectedObjects.Add(nextBone.guideObject);
                        }
                    }
                }
            }
            else
            {
                // Hips is at index 0 — scrollable FROM hips, but skipped when scrolling from other bones
                bool currentIsHips = currentIndex == 0;

                for (int i = 1; i <= BoneCycleOrder.Length; i++)
                {
                    int nextIndex = 0;
                    if (currentIndex == -1)
                    {
                        nextIndex = 0;
                        currentIndex = 0;
                    }
                    else
                    {
                        nextIndex = (currentIndex + step * i) % BoneCycleOrder.Length;
                        if (nextIndex < 0) nextIndex += BoneCycleOrder.Length;
                    }

                    // Skip hips (index 0) unless we started on hips
                    if (nextIndex == 0 && !currentIsHips) continue;

                    string targetName = BoneCycleOrder[nextIndex].ToLower();

                    var nextBone = charBones.FirstOrDefault(b => b.guideObject != null && b.guideObject.transformTarget != null && b.guideObject.transformTarget.name.ToLower() == targetName);

                    if (nextBone != null)
                    {
                        GuideObjectManager.Instance.selectObject = nextBone.guideObject;
                        break;
                    }
                }
            }
        }

        private List<OCIChar.BoneInfo> GetFKBonesFromGuide(GuideObject guide)
        {
            GuideObject currentGuide = guide;
            while (currentGuide != null)
            {
                ObjectCtrlInfo tempSel;
                if (Studio.Studio.Instance.dicObjectCtrl.TryGetValue(currentGuide.dicKey, out tempSel))
                {
                    if (tempSel is OCIChar ociChar && ociChar.fkCtrl.enabled)
                    {
                        return ociChar.listBones;
                    }
                    if (tempSel is OCIItem ociItem && ociItem.isFK && ociItem.itemFKCtrl.enabled)
                    {
                        return ociItem.listBones;
                    }
                }

                // The FK bone itself doesn't directly map to the Character.
                // We must traverse up the parent chain to find the Root guide object.
                currentGuide = currentGuide.parentGuide;
            }

            // Fallback for games like Koikatsu where `parentGuide` chain might be broken or nonexistent for FK bones.
            foreach (var kvp in Studio.Studio.Instance.dicObjectCtrl)
            {
                if (kvp.Value is OCIChar fallbackChar && fallbackChar.fkCtrl.enabled)
                {
                    if (fallbackChar.listBones != null)
                    {
                        foreach (var bone in fallbackChar.listBones)
                        {
                            if (bone != null && bone.guideObject == guide)
                            {
                                return fallbackChar.listBones;
                            }
                        }
                    }
                }
                else if (kvp.Value is OCIItem fallbackItem && fallbackItem.isFK && fallbackItem.itemFKCtrl.enabled)
                {
                    if (fallbackItem.listBones != null)
                    {
                        foreach (var bone in fallbackItem.listBones)
                        {
                            if (bone != null && bone.guideObject == guide)
                            {
                                return fallbackItem.listBones;
                            }
                        }
                    }
                }
            }

            return null;
        }

        private int GetBoneOrderIndex(string boneName)
        {
            string lowerBoneName = boneName.ToLower();
            for (int i = 0; i < BoneCycleOrder.Length; i++)
            {
                if (lowerBoneName == BoneCycleOrder[i].ToLower()) return i;
            }
            return -1;
        }

        private static void Initialize()
        {
            if (!StudioAPI.StudioLoaded) return;

            _camera = Camera.main;
            if (_camera == null) throw new ArgumentException("Camera.main not found");

            var origRoot = GuideObjectManager.Instance.objectOriginal;
            if (origRoot == null) throw new ArgumentException("origRoot not found");

            _selectedObjects = GuideObjectManager.Instance.hashSelectObject ?? throw new ArgumentException("Couldn't get hashSelectObject");

            _gizmoRoot = Instantiate(origRoot, _camera.transform);
            _gizmoRoot.gameObject.name = "CustomManipulatorGizmo";

            var go = _gizmoRoot.GetComponent<GuideObject>();
            Destroy(go);

            AdjustScaleToFov();

            _gizmoRoot.transform.localEulerAngles = new Vector3(17f, 150f, 343f);

            var visibleLayer = LayerMask.NameToLayer("Studio/Select");
            foreach (Transform rootChild in _gizmoRoot.transform)
            {
                switch (rootChild.name)
                {
                    case "move":
                        _moveObj = rootChild.gameObject;
                        _guideMoves = _moveObj.GetComponentsInChildren<GuideMove>();
                        break;

                    case "rotation":
                        _rotObj = rootChild.gameObject;
                        break;

                    case "scale":
                        _scaleObj = rootChild.gameObject;
                        break;

                    default:
                        Destroy(rootChild.gameObject);
                        break;
                }

                // todo configurable? can work between 0.75 and 1.5
                rootChild.localScale = Vector3.one;

                foreach (var subChild in rootChild.GetComponentsInChildren<Transform>(true))
                {
                    subChild.gameObject.layer = visibleLayer;
                    // Fix center point gizmos being disabled in some games
                    subChild.gameObject.SetActiveIfDifferent(true);
                }
            }

            if (_moveObj == null) throw new ArgumentException("_moveObj not found");
            if (_rotObj == null) throw new ArgumentException("_rotObj not found");
            if (_scaleObj == null) throw new ArgumentException("_scaleObj not found");

            SetVisibility(GuideObjectManager.Instance.mode);

            _gizmoRoot.SetActiveIfDifferent(true);

            _hi = Harmony.CreateAndPatchAll(typeof(Hooks), GUID);
        }

        private static void AdjustScaleToFov()
        {
            // Calculate using the camera's actual pixel viewport rect (accommodates letterboxing/aspect ratios)
            var screenPos = new Vector3(_camera.pixelRect.xMin + _camera.pixelWidth * 0.9f, _camera.pixelRect.yMin + _camera.pixelHeight * 0.14f, 6f);

            _gizmoRoot.transform.position = _camera.ScreenToWorldPoint(screenPos);
            var fov = _camera.fieldOfView;
            _gizmoRoot.transform.localScale = Vector3.one * (fov / 23f);
            _lastFov = fov;
        }

#if !PH
        private static void SetMoveRootTr(Transform rootTransform)
        {
            if (!_referenceToSelectedObject.Value)
                rootTransform = _gizmoRoot.transform;

            for (var i = 0; i < _guideMoves.Length; i++)
            {
                var guideMove = _guideMoves[i];
                guideMove.moveCalc = GuideMove.MoveCalc.TYPE3;
                guideMove.transformRoot = rootTransform;
            }
        }
#endif

        private static void SetVisibility()
        {
            SetVisibility(GuideObjectManager.Instance.mode);
        }

        private static void SetVisibility(int value)
        {
            if (_moveObj == null || _rotObj == null || _scaleObj == null) return;

            //todo add setting
            if (!_lastAnySelected || !_showGizmo.Value)
            {
                _moveObj.SetActiveIfDifferent(false);
                _rotObj.SetActiveIfDifferent(false);
                _scaleObj.SetActiveIfDifferent(false);
                return;
            }

            switch (value)
            {
                case 0:
#if !PH
                    // Some objects can't be moved
                    var moveIsVisible = Singleton<Studio.Studio>.Instance.workInfo?.visibleAxisTranslation ?? true;
                    _moveObj.SetActiveIfDifferent(moveIsVisible);
#else
                    _moveObj.SetActiveIfDifferent(true);
#endif
                    _rotObj.SetActiveIfDifferent(false);
                    _scaleObj.SetActiveIfDifferent(false);
                    break;

                case 1:
                    _moveObj.SetActiveIfDifferent(false);
                    _rotObj.SetActiveIfDifferent(true);
                    _scaleObj.SetActiveIfDifferent(false);
                    break;

                case 2:
                    // todo some objects can't be scaled
                    _moveObj.SetActiveIfDifferent(false);
                    _rotObj.SetActiveIfDifferent(false);
                    _scaleObj.SetActiveIfDifferent(true);
                    break;

                default:
                    Logger.LogWarning("Unknown GuideObject mode - " + value);
                    break;
            }
        }

        private static class Hooks
        {
            #region Cursor lock when dragging

            private static bool _locked;

            [HarmonyPostfix]
            [HarmonyPatch(typeof(GuideBase), nameof(GuideBase.OnBeginDrag))]
            private static void OnBeginDragHook(GuideBase __instance/*, PointerEventData eventData*/)
            {
                if (_gizmoRoot != null && __instance.transform.parent?.parent == _gizmoRoot.transform)
                {
                    _locked = true;
                    _extraGizmoDragActive = true;
                    var gc = GameCursor.Instance;
                    // Save current cursor position and lock it
                    gc.SetCursorLock(true);
                    // Stop the game resetting cursor position to the center of the screen on every frame, which breaks how gizmo dragging works
                    gc.enabled = false;
                    // Prevent camera script from unlocking the cursor on every frame
                    UnityEngine.Object.FindObjectOfType<Studio.CameraControl>().isCursorLock = false;
                }

                // Simplify check: As long as it's not a translation (move) arrow, and we are dragging a constrained bone, trigger constraint!
                if (__instance is GuideBase && __instance.name.ToLower() != "x" && __instance.name.ToLower() != "y" && __instance.name.ToLower() != "z")
                {
                    GuideObject go = __instance.guideObject;
                    // Extra gizmo's guideObject was destroyed — fall back to the selected bone
                    if ((go == null || go.transformTarget == null) && _selectedObjects != null)
                        go = _selectedObjects.FirstOrDefault(x => x.isActive);
                    if (go != null && go.transformTarget != null)
                    {
                        string name = go.transformTarget.name.ToLower();
                        string constrainedAxis = null;

                        // Discover if bone is constrained
                        if (IsKneeBone(name)) constrainedAxis = "x";
                        else if (IsElbowBone(name)) constrainedAxis = "y";
                        else if (IsThumbTipBone(name)) constrainedAxis = "y";
                        else if (IsFingerMidSegment(name)) constrainedAxis = "z";

                        if (constrainedAxis != null && !_rightDragActive)
                        {
                            _customDragActive = true;
                            _draggedGuideObject = go;
                            _dragStartPos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
                            _dragStartRot = go.changeAmount.rot;
                            _dragDirectionSet = false;
                            _dragDirection = Vector2.zero;
                            _draggedConstrainedAxis = constrainedAxis;
                        }
                    }
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(GuideBase), nameof(GuideBase.OnEndDrag))]
            private static void OnEndDragHook(/*GuideBase __instance*/)
            {
                _extraGizmoDragActive = false;
                if (_locked)
                {
                    GameCursor.Instance.SetCursorLock(false);
                    _locked = false;
                    GameCursor.Instance.enabled = true;
                    FindObjectOfType<Studio.CameraControl>().isCursorLock = true;
                }
            }

            // Suppress Studio's native axis-ring rotation during extra-gizmo shift-roll.
            // GuideRotation.OnDrag is the actual rotation handler on the ring components —
            // GuideBase.OnDrag alone is insufficient because GuideRotation overrides it.
            [HarmonyPrefix]
            [HarmonyPatch(typeof(GuideRotation), nameof(GuideRotation.OnDrag))]
            private static bool OnDragRotationHook()
            {
                if (_extraGizmoShiftRollMode)
                {
                    if (_extraGizmoShiftDiagCount == 0)
                        DiagLog("[ShiftRoll-LClick] GuideRotation.OnDrag SUPPRESSED");
                    return false;
                }
                return true;
            }

            [HarmonyPrefix]
            [HarmonyPatch(typeof(GuideBase), nameof(GuideBase.OnDrag))]
            private static bool OnDragBaseHook()
            {
                if (_extraGizmoShiftRollMode) return false;
                return true;
            }

            #endregion

            #region Attaching our gizmo to stock gizmo code

            [HarmonyPrefix]
            [HarmonyPatch(typeof(GuideObjectManager), nameof(GuideObjectManager.mode), MethodType.Setter)]
            private static void SetModeHook(int value, int ___m_Mode)
            {
                if (value != ___m_Mode && _moveObj != null)
                {
                    SetVisibility(value);
                }
            }

#if !PH
            [HarmonyPostfix]
            [HarmonyPatch(typeof(GuideObjectManager), nameof(GuideObjectManager.SetVisibleTranslation))]
            private static void SetVisibleTranslationHook()
            {
                SetVisibility();
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(GuideObjectManager), nameof(GuideObjectManager.AddObject))]
            [HarmonyPatch(typeof(GuideObjectManager), nameof(GuideObjectManager.SetDeselectObject))]
            private static void AddObjectHook(GuideObject _object)
            {
                if (_object == null || _selectedObjects == null) return;

                var selectedObj = _selectedObjects.FirstOrDefault(x => x.isActive);
                if (selectedObj != null)
                    SetMoveRootTr(selectedObj.transformTarget);

                SetVisibility();
            }
#endif
            #endregion
        }

        // --- Bone role helpers: work for both HS2 and KK naming conventions ---
        // HS2: cf_J_LegLow01_R → "leglow01_r"   KK: cf_j_leg01_R → "leg01_r"
        // All inputs must be .ToLower()'d already.

        private static bool IsKneeBone(string n) =>
            n.Contains("leglow01_") || n.Contains("_leg01_");

        private static bool IsElbowBone(string n) =>
            n.Contains("armlow01_") || n.Contains("forearm01_");

        private static bool IsThumbTipBone(string n) =>
            n.Contains("thumb03_");  // HS2 "hand_thumb03_r", KK "thumb03_r" — both contain this

        private static bool IsFingerMidSegment(string n) =>
            n.Contains("index02") || n.Contains("index03") ||
            n.Contains("middle02") || n.Contains("middle03") ||
            n.Contains("ring02") || n.Contains("ring03") ||
            n.Contains("little02") || n.Contains("little03");

        private static bool IsFingerBase(string n) =>
            n.Contains("index01") || n.Contains("middle01") ||
            n.Contains("ring01") || n.Contains("little01");

        private static bool IsHandBone(string n) =>
            n == "cf_j_hand_r" || n == "cf_j_hand_l";

        private static bool IsHipsBone(string n) =>
            n == "cf_j_hips";

        // Pelvis: HS2 "cf_j_kosi01", KK "cf_j_waist01"
        private static bool IsPelvisBone(string n) =>
            n == "cf_j_kosi01" || n == "cf_j_waist01";
    }
}
