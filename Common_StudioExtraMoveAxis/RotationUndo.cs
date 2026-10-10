using System;
using System.Collections.Generic;
using System.Linq;
using Studio;
using UnityEngine;

namespace StudioExtraMoveAxis
{
    public partial class StudioExtraMoveAxisPlugin
    {
        private sealed class RotationUndoTarget
        {
            internal GuideObject Guide;
            internal ChangeAmount Amount;
            internal int Key;
            internal Vector3 Before;
            internal Vector3 After;
            internal bool Deferred;
        }

        private static Dictionary<int, RotationUndoTarget> _rotationUndo;
        private static int _rotationUndoButton;
        private static GuideBase _rotationUndoSource;
        private static bool _suppressNativeRotationUndo;
        internal static bool RotationUndoActive { get { return _rotationUndo != null; } }

        internal static void BeginRotationUndo(int button, GuideBase source = null)
        {
            // Potential-drag and begin-drag both arrive for the same native handle.
            if (_rotationUndo != null && _rotationUndoButton == button && _rotationUndoSource == source)
                return;
            EndRotationUndo();
            _rotationUndo = new Dictionary<int, RotationUndoTarget>();
            _rotationUndoButton = button;
            _rotationUndoSource = source;
            TrackSelectedRotations();
        }

        private static void TrackSelectedRotations()
        {
            if (_selectedObjects == null) return;
            foreach (var guide in _selectedObjects)
                if (guide != null) TrackRotationUndo(guide);
        }

        internal static void TrackRotationUndo(GuideObject guide)
        {
            if (_rotationUndo == null || guide == null || guide.changeAmount == null) return;
            if (Studio.Studio.GetChangeAmount(guide.dicKey) != guide.changeAmount) return;
            if (_rotationUndo.ContainsKey(guide.dicKey)) return;
            _rotationUndo.Add(guide.dicKey, new RotationUndoTarget
            {
                Guide = guide, Amount = guide.changeAmount, Key = guide.dicKey,
                Before = ReadRotation(guide)
            });
        }

        // Deferred finger angles are already visible, even before FK's saved value catches up.
        internal static Vector3 ReadRotation(GuideObject guide)
        {
            if (_rightDragIsHandBone && _rightDragChildren != null)
                foreach (var child in _rightDragChildren)
                    if (child.guideObject == guide) return child.targetQuat.eulerAngles;
            if (_fingerSyncChildren != null)
                foreach (var child in _fingerSyncChildren)
                    if (child.guideObject == guide) return child.targetQuat.eulerAngles;
            return guide.changeAmount.rot;
        }

        internal static void DropFingerSync(GuideObject guide)
        {
            if (_fingerSyncChildren == null) return;
            for (int i = _fingerSyncChildren.Count - 1; i >= 0; --i)
                if (_fingerSyncChildren[i].guideObject == guide)
                {
                    _fingerSyncChildren.RemoveAt(i);
                    if (i < _fingerSyncIndex) --_fingerSyncIndex;
                }
            if (_fingerSyncChildren.Count == 0) _fingerSyncChildren = null;
        }

        internal static void WriteRotation(GuideObject guide, Vector3 value)
        {
            TrackRotationUndo(guide);
            DropFingerSync(guide);
            guide.changeAmount.rot = value;
        }

        internal static void QueueFingerSync(List<ReverseRotChildInfo> children)
        {
            var pending = new List<ReverseRotChildInfo>();
            if (_fingerSyncChildren != null)
                for (int i = _fingerSyncIndex; i < _fingerSyncChildren.Count; ++i)
                    pending.Add(_fingerSyncChildren[i]);
            foreach (var child in children)
            {
                pending.RemoveAll(c => c.guideObject == child.guideObject);
                if (child.guideObject != null) pending.Add(child);
            }
            _fingerSyncChildren = pending.Count == 0 ? null : pending;
            _fingerSyncIndex = 0;
        }

        private static void FlushFingerSync()
        {
            if (_fingerSyncChildren == null) return;
            foreach (var child in _fingerSyncChildren)
            {
                var guide = child.guideObject;
                if (guide == null || Studio.Studio.GetChangeAmount(guide.dicKey) != guide.changeAmount) continue;
                var amount = guide.changeAmount;
                var saved = amount.onChangeRot;
                try { amount.onChangeRot = null; amount.rot = child.targetQuat.eulerAngles; }
                finally { amount.onChangeRot = saved; }
            }
            _fingerSyncChildren = null;
            _fingerSyncIndex = 0;
        }

        internal static void EndRotationUndo()
        {
            if (_rotationUndo == null) return;
            var targets = _rotationUndo.Values.ToArray();
            _rotationUndo = null;
            var changed = new List<RotationUndoTarget>();
            foreach (var target in targets)
            {
                if (target.Guide == null || Studio.Studio.GetChangeAmount(target.Key) != target.Amount) continue;
                target.After = ReadRotation(target.Guide);
                target.Deferred = (_rightDragIsHandBone && _rightDragChildren != null &&
                    _rightDragChildren.Any(c => c.guideObject == target.Guide)) ||
                    (_fingerSyncChildren != null && _fingerSyncChildren.Any(c => c.guideObject == target.Guide));
                if (Quaternion.Angle(Quaternion.Euler(target.Before), Quaternion.Euler(target.After)) > 0.0001f)
                    changed.Add(target);
            }
            if (changed.Count > 0 && Singleton<UndoRedoManager>.IsInstance())
                Singleton<UndoRedoManager>.Instance.Push(new RotationUndoCommand(changed.ToArray()));
        }

        private sealed class RotationUndoCommand : global::Studio.ICommand
        {
            private readonly RotationUndoTarget[] _targets;
            internal RotationUndoCommand(RotationUndoTarget[] targets) { _targets = targets; }
            public void Do() { Apply(false); }
            public void Redo() { Apply(false); }
            public void Undo() { Apply(true); }

            private void Apply(bool undo)
            {
                var immediate = new List<GuideCommand.EqualsInfo>();
                var fingers = new List<ReverseRotChildInfo>();
                foreach (var target in _targets)
                {
                    // A reused scene key must never move a replacement object.
                    if (target.Guide == null || Studio.Studio.GetChangeAmount(target.Key) != target.Amount) continue;
                    DropFingerSync(target.Guide);
                    if (target.Deferred)
                        fingers.Add(new ReverseRotChildInfo
                        {
                            guideObject = target.Guide,
                            targetQuat = Quaternion.Euler(undo ? target.Before : target.After)
                        });
                    else
                        immediate.Add(new GuideCommand.EqualsInfo
                        {
                            dicKey = target.Key, oldValue = target.Before, newValue = target.After
                        });
                }
                var command = new GuideCommand.RotationEqualsCommand(immediate.ToArray());
                if (undo) command.Undo(); else command.Redo();
                QueueFingerSync(fingers);
            }
        }

        private static bool OwnsRotationHandle(GuideBase handle)
        {
            if (!(handle is GuideRotation)) return false;
            if (_gizmoRoot != null && handle.transform.parent != null &&
                handle.transform.parent.parent == _gizmoRoot.transform) return true;
            var guide = handle.guideObject;
            if (guide == null || guide.transformTarget == null) return false;
            string name = guide.transformTarget.name.ToLower();
            return IsKneeBone(name) || IsElbowBone(name) || IsThumbTipBone(name) || IsFingerMidSegment(name);
        }

        private static void PrepareNativeRotation()
        {
            TrackSelectedRotations();
            // Native rotation reads ChangeAmount directly. Materialize only its selected targets.
            if (_selectedObjects == null) return;
            foreach (var guide in _selectedObjects)
                if (guide != null)
                {
                    Vector3 value = ReadRotation(guide);
                    DropFingerSync(guide);
                    var saved = guide.changeAmount.onChangeRot;
                    try { guide.changeAmount.onChangeRot = null; guide.changeAmount.rot = value; }
                    finally { guide.changeAmount.onChangeRot = saved; }
                }
        }

        private static void TickRotationUndo()
        {
            if (_rotationUndo == null || Input.GetMouseButton(_rotationUndoButton)) return;
            FinishRotationBeforeHistory();
        }

        private static void FinishRotationBeforeHistory()
        {
            // Stop every writer before replay: a still-held pointer must not overwrite Undo.
            EndRotationUndo();
            if (_rightDragIsHandBone && _rightDragChildren != null) QueueFingerSync(_rightDragChildren);
            _rightDragActive = false;
            _rightDragGuideObject = null;
            _rightDragChildren = null;
            _rightDragIsHandBone = false;
            _customDragActive = false;
            _draggedGuideObject = null;
            _extraGizmoDragActive = false;
            _extraGizmoShiftRollMode = false;
        }
    }
}
