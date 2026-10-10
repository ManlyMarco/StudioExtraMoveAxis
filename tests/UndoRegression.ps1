$ErrorActionPreference = 'Stop'

# Compile the real reset method (and, once present, the complete undo helper) against
# small Studio/Unity boundary doubles. This is an executable behavior test: no source
# text is used as a pass/fail assertion. Unity's own quaternion math is out of scope.
$common = Join-Path $PSScriptRoot '../Common_StudioExtraMoveAxis'
$source = Get-Content (Join-Path $common 'StudioExtraMoveAxisPlugin.cs') -Raw
$signature = 'private void HandleMiddleClickReset()'
$start = $source.IndexOf($signature, [StringComparison]::Ordinal)
if ($start -lt 0) { throw 'Cannot locate production HandleMiddleClickReset method' }
$open = $source.IndexOf('{', $start)
$depth = 0
$close = -1
for ($i = $open; $i -lt $source.Length; $i++) {
    if ($source[$i] -eq '{') { $depth++ }
    elseif ($source[$i] -eq '}') {
        $depth--
        if ($depth -eq 0) { $close = $i; break }
    }
}
if ($close -lt 0) { throw 'Cannot isolate production reset method' }
$resetMethod = $source.Substring($start, $close - $start + 1)
function Get-ProductionMethod([string] $name) {
    $match = [regex]::Match($source, "private\s+static\s+(?:bool|void)\s+$name\s*\(")
    if (-not $match.Success) { throw "Cannot locate production $name hook" }
    $begin = $match.Index
    $brace = $source.IndexOf('{', $begin)
    $level = 0
    for ($j = $brace; $j -lt $source.Length; $j++) {
        if ($source[$j] -eq '{') { $level++ }
        elseif ($source[$j] -eq '}') {
            $level--
            if ($level -eq 0) { return $source.Substring($begin, $j - $begin + 1) }
        }
    }
    throw "Cannot isolate production $name hook"
}
$hookNames = @('RotationPotentialDrag','RotationEndPrefix','RotationEndPostfix','RotationPushPrefix','RotationHistoryPrefix','RotationHistoryClear')
$hookMethods = ($hookNames | ForEach-Object { Get-ProductionMethod $_ }) -join "`n"
$pointerMethod = Get-ProductionMethod 'IsLeftRotationPointer'
$helperPath = Join-Path $common 'RotationUndo.cs'
$helperPresent = Test-Path $helperPath
$helper = if ($helperPresent) { Get-Content $helperPath -Raw } else { '' }
# A complete production helper may begin with using directives. Keep those at the
# top of the generated compilation unit rather than interleaving them with stubs.
$helperBody = [regex]::Replace($helper, '(?m)^\s*using\s+[^;]+;\s*$', '')

$stubs = @'
#pragma warning disable 0649, 0169, 0414
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using Studio;
using HarmonyLib;
namespace HarmonyLib {
 public sealed class Traverse {
  private readonly object target;
  private string member;
  private Traverse(object target){this.target=target;}
  public static Traverse Create(object target){return new Traverse(target);}
  public Traverse Property(string name){member=name;return this;}
  public T GetValue<T>(){return (T)target.GetType().GetProperty(member).GetValue(target,null);}
 }
}
namespace UnityEngine {
 public struct Vector2 {
  public float x,y;
  public Vector2(float x,float y){this.x=x;this.y=y;}
  public static float Distance(Vector2 a,Vector2 b){float x=a.x-b.x,y=a.y-b.y;return (float)Math.Sqrt(x*x+y*y);}
  public float magnitude {get{return (float)Math.Sqrt(x*x+y*y);}}
  public static Vector2 operator -(Vector2 a,Vector2 b){return new Vector2(a.x-b.x,a.y-b.y);}
 }
 public struct Vector3 {
  public float x,y,z;
  public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 zero {get{return new Vector3();}}
  public override string ToString(){return x+","+y+","+z;}
 }
 public struct Quaternion {
  // These cases use rotations around X only. The double preserves angle addition,
  // wraparound, and equivalent Euler angles; it does not emulate Unity's 3D math.
  private readonly float xAngle;
  private Quaternion(float x){xAngle=x;}
  public static Quaternion Euler(Vector3 v){return new Quaternion(v.x);}
  public static Quaternion identity {get{return new Quaternion(0);}}
  public static Quaternion AngleAxis(float angle,Vector3 axis){return new Quaternion(angle);}
  public static Quaternion Inverse(Quaternion q){return new Quaternion(-q.xAngle);}
  public Vector3 eulerAngles {get{float x=xAngle%360f;if(x<0)x+=360f;return new Vector3(x,0,0);}}
  public static Quaternion operator *(Quaternion a,Quaternion b){return new Quaternion(a.xAngle+b.xAngle);}
  public static float Angle(Quaternion a,Quaternion b){float d=Math.Abs((a.xAngle-b.xAngle)%360f);return Math.Min(d,360f-d);}
 }
 public static class Input {
  public static bool MiddleDown;
  public static Vector3 mousePosition;
  public static bool GetMouseButtonDown(int button){return button==2 && MiddleDown;}
  public static bool GetMouseButton(int button){return false;}
 }
 public static class Screen {public static int height=1000;}
 public class Object {public static T FindObjectOfType<T>() where T:new(){return new T();}}
 public class MonoBehaviour {}
 public class Transform {public string name;public Vector3 position,right,up;public Transform parent;public Quaternion rotation;}
 public class GameObject {public Transform transform=new Transform();}
 public class Camera {public static Camera main=new Camera();public Transform transform=new Transform();public Vector3 WorldToScreenPoint(Vector3 p){return p;}}
}
namespace UnityEngine.EventSystems {
 public sealed class PointerEventData {
  public enum InputButton {Left,Right,Middle}
  public InputButton button {get;set;}
  public Vector2 position;
 }
 public interface IBeginDragHandler {void OnBeginDrag(PointerEventData e);}
 public interface IDragHandler {void OnDrag(PointerEventData e);}
 public interface IEndDragHandler {void OnEndDrag(PointerEventData e);}
}
namespace Studio {
 public interface ICommand {void Do();void Undo();void Redo();}
 public sealed class CommandController {
  public readonly List<ICommand> Commands=new List<ICommand>();
  private int cursor;
  public void Add(ICommand command){Commands.Add(command);cursor=Commands.Count;}
  public void Push(ICommand command){Add(command);}
  public void Undo(){if(cursor>0)Commands[--cursor].Undo();}
  public void Redo(){if(cursor<Commands.Count)Commands[cursor++].Redo();}
  public void Clear(){Commands.Clear();cursor=0;}
 }
 public sealed class Studio {
  public static Studio Instance=new Studio();
  public CommandController commandController=new CommandController();
  public readonly Dictionary<int,ChangeAmount> Registry=new Dictionary<int,ChangeAmount>();
  public static ChangeAmount GetChangeAmount(int key){ChangeAmount value;return Instance.Registry.TryGetValue(key,out value)?value:null;}
 }
 public sealed class UndoRedoManager {
  public void Push(ICommand command){Studio.Instance.commandController.Add(command);}
 }
 public static class GuideCommand {
  public struct EqualsInfo {
   public int dicKey;
   public Vector3 oldValue,newValue;
   public EqualsInfo(int key,Vector3 oldValue,Vector3 newValue){dicKey=key;this.oldValue=oldValue;this.newValue=newValue;}
  }
  public sealed class RotationEqualsCommand : ICommand {
   private readonly EqualsInfo[] infos;
   public RotationEqualsCommand(EqualsInfo[] infos){this.infos=infos;}
   public void Do(){Redo();}
   public void Undo(){foreach(var info in infos){var c=Studio.GetChangeAmount(info.dicKey);if(c!=null)c.rot=info.oldValue;}}
   public void Redo(){foreach(var info in infos){var c=Studio.GetChangeAmount(info.dicKey);if(c!=null)c.rot=info.newValue;}}
  }
 }
 public class ChangeAmount {
  private Vector3 value;
  public Action onChangeRot;
  public Vector3 rot {get{return value;}set{this.value=value;if(onChangeRot!=null)onChangeRot();}}
 }
 public class GuideObject {public bool isActive=true;public int dicKey;public Transform transformTarget=new Transform();public ChangeAmount changeAmount=new ChangeAmount();}
 public class GuideBase {public GuideObject guideObject;public Transform transform=new Transform();}
 public class GuideRotation : GuideBase {}
 public class CameraControl {public bool isCursorLock;}
 public class OIBoneInfo {public GuideObject guideObject;}
 public sealed class GuideObjectManager {
  public static GuideObjectManager Instance=new GuideObjectManager();
  public Dictionary<int,ChangeAmount> selectObjectDictionary=new Dictionary<int,ChangeAmount>();
 }
}
public sealed class GameCursor {
 public static GameCursor Instance=new GameCursor();
 public bool enabled;
 public void SetCursorLock(bool value){}
}
public static class Singleton<T> where T:new() {
 private static readonly T Value=new T();
 public static bool IsInstance(){return true;}
 public static T Instance {get{return Value;}}
}
namespace StudioExtraMoveAxis {
 public partial class StudioExtraMoveAxisPlugin {
  internal static HashSet<GuideObject> _selectedObjects=new HashSet<GuideObject>();
  internal static GameObject _gizmoRoot=new GameObject();
  internal static Camera _camera=new Camera();
  internal static Dictionary<GuideObject,List<OIBoneInfo>> BoneMap=new Dictionary<GuideObject,List<OIBoneInfo>>();
  internal struct ReverseRotChildInfo {public GuideObject guideObject;public Quaternion changeAmountQuat;public float weight;public Quaternion targetQuat;}
  private static List<ReverseRotChildInfo> _fingerSyncChildren,_rightDragChildren;
  private static int _fingerSyncIndex;
  private static bool _rightDragIsHandBone;
  private static bool _rightDragActive;
  internal static bool _customDragActive,_extraGizmoDragActive,_extraGizmoShiftRollMode;
  private static GuideObject _rightDragGuideObject,_draggedGuideObject;
  private static bool IsKneeBone(string name){return false;}
  private static bool IsElbowBone(string name){return false;}
  private static bool IsThumbTipBone(string name){return false;}
  private static bool IsFingerMidSegment(string name){return false;}
  private static bool IsHipsBone(string name){return name=="cf_j_hips";}
  public static HashSet<GuideObject> GetSelectedObjects(){return _selectedObjects;}
  private static List<OIBoneInfo> GetFKBonesFromGuide(GuideObject g){List<OIBoneInfo> bones;return BoneMap.TryGetValue(g,out bones)?bones:null;}
  public void TestMiddleClickReset(){HandleMiddleClickReset();}
'@ + $resetMethod + "`n" + $pointerMethod + "`n private static class Hooks {`n" + $hookMethods + "`n }`n }`n}`n"

$tests = @'
namespace StudioExtraMoveAxis {
 public static class Regression {
  static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
  static GuideObject Bone(int key,string name,float angle){return new GuideObject{dicKey=key,transformTarget=new Transform{name=name},changeAmount=new ChangeAmount{rot=new Vector3(angle,0,0)}};}
  static void Reset(){Studio.Studio.Instance.commandController.Clear();StudioExtraMoveAxisPlugin._selectedObjects.Clear();StudioExtraMoveAxisPlugin.BoneMap.Clear();Input.MiddleDown=true;Input.mousePosition=Vector3.zero;}
  public static void RunReset(){
   Reset();
   var hips=Bone(10,"cf_j_hips",30);var spine=Bone(11,"cf_j_spine01",10);var kosi=Bone(12,"cf_j_kosi01",20);
   Studio.Studio.Instance.Registry[10]=hips.changeAmount;Studio.Studio.Instance.Registry[11]=spine.changeAmount;Studio.Studio.Instance.Registry[12]=kosi.changeAmount;
   StudioExtraMoveAxisPlugin.BoneMap[hips]=new List<OIBoneInfo>{new OIBoneInfo{guideObject=spine},new OIBoneInfo{guideObject=kosi}};
   StudioExtraMoveAxisPlugin._selectedObjects.Add(hips);
   new StudioExtraMoveAxisPlugin().TestMiddleClickReset();
   Require(hips.changeAmount.rot.x==0 && spine.changeAmount.rot.x==40 && kosi.changeAmount.rot.x==50,"hips reset and child compensation did not execute");
   Require(Studio.Studio.Instance.commandController.Commands.Count==1,"reset should create one grouped undo command");
   Studio.Studio.Instance.commandController.Undo();
   Require(hips.changeAmount.rot.x==30 && spine.changeAmount.rot.x==10 && kosi.changeAmount.rot.x==20,"undo did not restore hips and children");
   Studio.Studio.Instance.commandController.Redo();
   Require(hips.changeAmount.rot.x==0 && spine.changeAmount.rot.x==40 && kosi.changeAmount.rot.x==50,"redo did not restore compensated reset");
  }
 }
}
'@

$helperTests = @'
namespace StudioExtraMoveAxis {
 public partial class StudioExtraMoveAxisPlugin {
  private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
  private static GuideObject TestBone(int key,float angle){
   var g=new GuideObject{dicKey=key,transformTarget=new Transform{name="cf_j_armup00_r"},changeAmount=new ChangeAmount{rot=new Vector3(angle,0,0)}};
   Studio.Studio.Instance.Registry[key]=g.changeAmount;
   return g;
  }
  private static void Fresh(){
   EndRotationUndo();Studio.Studio.Instance.commandController.Clear();Studio.Studio.Instance.Registry.Clear();
   _selectedObjects.Clear();GuideObjectManager.Instance.selectObjectDictionary.Clear();
   _fingerSyncChildren=null;_fingerSyncIndex=0;_rightDragChildren=null;_rightDragIsHandBone=false;
   _rightDragActive=false;_customDragActive=false;_extraGizmoDragActive=false;
   _rotationUndoSource=null;_suppressNativeRotationUndo=false;
  }
  private static void FlushPendingFinger(){
   if(_fingerSyncChildren==null)return;
   foreach(var child in _fingerSyncChildren)child.guideObject.changeAmount.rot=child.targetQuat.eulerAngles;
   _fingerSyncChildren=null;_fingerSyncIndex=0;
  }
  public static void TestRepeatedGesture(){
   Fresh();var bone=TestBone(21,5);
   BeginRotationUndo(0);
   WriteRotation(bone,new Vector3(10,0,0));
   WriteRotation(bone,new Vector3(25,0,0));
   EndRotationUndo();
   Check(Studio.Studio.Instance.commandController.Commands.Count==1,"repeated writes should group into one command");
   Studio.Studio.Instance.commandController.Undo();
   Check(bone.changeAmount.rot.x==5,"gesture undo lost its initial angle");
   Studio.Studio.Instance.commandController.Redo();
   Check(bone.changeAmount.rot.x==25,"gesture redo did not keep final angle");
  }
  public static void TestRetarget(){
   Fresh();var first=TestBone(22,5);var next=TestBone(23,15);
   BeginRotationUndo(0);
   WriteRotation(first,new Vector3(30,0,0));
   WriteRotation(next,new Vector3(40,0,0));
   EndRotationUndo();
   Check(Studio.Studio.Instance.commandController.Commands.Count==1,"selection retarget should remain one gesture");
   Studio.Studio.Instance.commandController.Undo();
   Check(first.changeAmount.rot.x==5 && next.changeAmount.rot.x==15,"retarget undo did not restore both bones");
   Studio.Studio.Instance.commandController.Redo();
   Check(first.changeAmount.rot.x==30 && next.changeAmount.rot.x==40,"retarget redo did not restore both bones");
  }
  public static void TestNoOps(){
   Fresh();var bone=TestBone(24,0);
   BeginRotationUndo(0);EndRotationUndo();
   BeginRotationUndo(0);WriteRotation(bone,new Vector3(0,0,0));EndRotationUndo();
   BeginRotationUndo(0);WriteRotation(bone,new Vector3(360,0,0));EndRotationUndo();
   BeginRotationUndo(0);WriteRotation(bone,new Vector3(0,0,0));EndRotationUndo();
   Check(Studio.Studio.Instance.commandController.Commands.Count==0,"empty or equivalent rotations polluted undo history");
  }
  public static void TestDeletedAndReusedKey(){
   Fresh();var original=TestBone(25,10);
   BeginRotationUndo(0);WriteRotation(original,new Vector3(45,0,0));EndRotationUndo();
   var reused=new ChangeAmount{rot=new Vector3(99,0,0)};
   Studio.Studio.Instance.Registry.Remove(25);
   Studio.Studio.Instance.commandController.Undo();
   Check(original.changeAmount.rot.x==45,"deleted bone received undo after leaving the scene");
   Studio.Studio.Instance.Registry[25]=reused;
   Check(reused.rot.x==99,"undo wrote to a different bone reusing the deleted key");
   Studio.Studio.Instance.commandController.Redo();
   Check(reused.rot.x==99,"redo wrote to a different bone reusing the deleted key");
  }
  public static void TestDeferredFinger(){
   Fresh();var finger=TestBone(26,10);
   BeginRotationUndo(1);
   TrackRotationUndo(finger);
   QueueFingerSync(new List<ReverseRotChildInfo>{new ReverseRotChildInfo{guideObject=finger,targetQuat=Quaternion.Euler(new Vector3(40,0,0))}});
   EndRotationUndo();
   Check(Studio.Studio.Instance.commandController.Commands.Count==1,"pending finger target should be in the drag command");
   Check(ReadRotation(finger).x==40,"pending finger target should be the next drag baseline");
   Studio.Studio.Instance.commandController.Undo();
   Check(ReadRotation(finger).x==10,"finger undo did not replace the pending target with the original angle");
   FlushPendingFinger();
   Check(finger.changeAmount.rot.x==10,"deferred sync overwrote finger undo");
   Studio.Studio.Instance.commandController.Redo();
   Check(ReadRotation(finger).x==40,"finger redo did not restore pending target angle");
   FlushPendingFinger();
   Check(finger.changeAmount.rot.x==40,"deferred sync overwrote finger redo");
  }
  public static void TestPendingBaseline(){
   Fresh();var finger=TestBone(27,10);
   QueueFingerSync(new List<ReverseRotChildInfo>{new ReverseRotChildInfo{guideObject=finger,targetQuat=Quaternion.Euler(new Vector3(40,0,0))}});
   BeginRotationUndo(0);
   Check(ReadRotation(finger).x==40,"new drag did not read pending finger target");
   WriteRotation(finger,new Vector3(50,0,0));
   EndRotationUndo();
   Check(Studio.Studio.Instance.commandController.Commands.Count==1,"drag after pending sync should create one command");
   Studio.Studio.Instance.commandController.Undo();
   Check(finger.changeAmount.rot.x==40,"drag undo used stale pre-sync finger angle");
  }
  private static MethodInfo Hook(string name){
   var method=typeof(Hooks).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic);
   Check(method!=null,"missing compiled production hook "+name);return method;
  }
  private static object[] PointerArguments(MethodInfo method,GuideRotation guide,PointerEventData pointer){
   var parameters=method.GetParameters();var args=new object[parameters.Length];
   for(int i=0;i<parameters.Length;i++){
    if(parameters[i].ParameterType==typeof(GuideRotation))args[i]=guide;
    else if(parameters[i].ParameterType==typeof(PointerEventData) || parameters[i].ParameterType==typeof(object))args[i]=pointer;
    else throw new Exception("unexpected pointer hook argument: "+parameters[i].Name);
   }
   return args;
  }
  private static bool Potential(GuideRotation guide,PointerEventData pointer){
   var method=Hook("RotationPotentialDrag");var result=method.Invoke(null,PointerArguments(method,guide,pointer));
   return result is bool ? (bool)result : true;
  }
  private sealed class EndResult {
   public bool Continue,Owned;
   public Dictionary<int,Vector3> Old;
   public Dictionary<int,ChangeAmount> Amounts;
  }
  private static EndResult EndPrefix(GuideRotation guide,PointerEventData pointer,
      Dictionary<int,Vector3> old,Dictionary<int,ChangeAmount> amounts){
   var method=Hook("RotationEndPrefix");var parameters=method.GetParameters();var args=new object[parameters.Length];
   int oldSlot=-1,amountSlot=-1,stateSlot=-1;
   for(int i=0;i<parameters.Length;i++){
    var p=parameters[i].ParameterType;
    if(p==typeof(GuideRotation))args[i]=guide;
    else if(p==typeof(PointerEventData) || p==typeof(object))args[i]=pointer;
    else if(p==typeof(Dictionary<int,Vector3>).MakeByRefType()){args[i]=old;oldSlot=i;}
    else if(p==typeof(Dictionary<int,ChangeAmount>).MakeByRefType()){args[i]=amounts;amountSlot=i;}
    else if(p==typeof(bool).MakeByRefType()){args[i]=false;stateSlot=i;}
    else throw new Exception("unexpected native end argument: "+parameters[i].Name);
   }
   var result=method.Invoke(null,args);
   return new EndResult{Continue=result is bool?(bool)result:true,Owned=(bool)args[stateSlot],
     Old=(Dictionary<int,Vector3>)args[oldSlot],Amounts=(Dictionary<int,ChangeAmount>)args[amountSlot]};
  }
  private static void EndPostfix(bool owned){Hook("RotationEndPostfix").Invoke(null,new object[]{owned});}
  private static bool MayPush(ICommand command){return (bool)Hook("RotationPushPrefix").Invoke(null,new object[]{command});}
  private static void HistoryPrefix(){Hook("RotationHistoryPrefix").Invoke(null,new object[0]);}
  private static void HistoryClear(){Hook("RotationHistoryClear").Invoke(null,new object[0]);}
  private static GuideRotation OwnedHandle(GuideObject guide){
   var handle=new GuideRotation{guideObject=guide};
   handle.transform.parent=new Transform{parent=_gizmoRoot.transform};
   return handle;
  }
  private static PointerEventData Pointer(PointerEventData.InputButton button){return new PointerEventData{button=button};}
  private sealed class ProbeCommand : ICommand {public void Do(){}public void Undo(){}public void Redo(){}}
  public static void TestPointerOwnership(){
   Fresh();var bone=TestBone(31,5);_selectedObjects.Add(bone);var handle=OwnedHandle(bone);
   Check(!Potential(handle,Pointer(PointerEventData.InputButton.Right)),"right pointer was allowed into native rotation");
   Check(_rotationUndo==null,"right pointer started a left rotation transaction");
   Check(Potential(handle,Pointer(PointerEventData.InputButton.Left)),"left pointer was blocked from rotation");
   Check(_rotationUndo!=null && _rotationUndoSource==handle,"left pointer did not own the native rotation");
   var amount=new Dictionary<int,ChangeAmount>{{bone.dicKey,bone.changeAmount}};
   var right=EndPrefix(handle,Pointer(PointerEventData.InputButton.Right),new Dictionary<int,Vector3>(),amount);
   Check(!right.Continue && !right.Owned,"right release was allowed to complete a left rotation");
   Check(_rotationUndo!=null,"right release closed the left transaction");
   var left=EndPrefix(handle,null,new Dictionary<int,Vector3>(),amount);
   Check(left.Continue && left.Owned,"left release did not complete its own rotation");
   EndPostfix(left.Owned);
   Check(_rotationUndo==null,"left release left the transaction open");
  }
  public static void TestNativeReleaseAfterScroll(){
   Fresh();var first=TestBone(32,5);var next=TestBone(33,15);_selectedObjects.Add(first);var handle=OwnedHandle(first);
   Potential(handle,Pointer(PointerEventData.InputButton.Left));
   WriteRotation(first,new Vector3(30,0,0));
   _selectedObjects.Clear();_selectedObjects.Add(next);TrackRotationUndo(next);WriteRotation(next,new Vector3(40,0,0));
   var current=new Dictionary<int,ChangeAmount>{{next.dicKey,next.changeAmount}};
   GuideObjectManager.Instance.selectObjectDictionary=current;
   var stale=new Dictionary<int,ChangeAmount>{{first.dicKey,first.changeAmount}};
   var old=new Dictionary<int,Vector3>{{first.dicKey,new Vector3(5,0,0)}};
   var end=EndPrefix(handle,Pointer(PointerEventData.InputButton.Left),old,stale);
   Check(end.Owned && end.Continue,"owned release did not retain native cleanup");
   Check(object.ReferenceEquals(end.Amounts,current),"native release did not use current selection dictionary");
   Check(end.Old.Count==1 && end.Old.ContainsKey(next.dicKey) && end.Old[next.dicKey].x==40,
     "native release retained stale pre-scroll rotation dictionary");
   Check(!MayPush(new GuideCommand.RotationEqualsCommand(new GuideCommand.EqualsInfo[0])),"duplicate native rotation command was not suppressed");
   Check(MayPush(new ProbeCommand()),"unrelated command was suppressed with native rotation");
   EndPostfix(end.Owned);
   Check(Studio.Studio.Instance.commandController.Commands.Count==1,"scroll retarget added duplicate or missing history");
   Check(MayPush(new GuideCommand.RotationEqualsCommand(new GuideCommand.EqualsInfo[0])),"native rotation suppression leaked past release");
   Studio.Studio.Instance.commandController.Undo();
   Check(first.changeAmount.rot.x==5 && next.changeAmount.rot.x==15,"one undo did not restore pre-scroll and post-scroll targets");
  }
  public static void TestHistoryInterrupt(){
   Fresh();var bone=TestBone(34,5);BeginRotationUndo(0);WriteRotation(bone,new Vector3(25,0,0));
   _extraGizmoDragActive=true;_customDragActive=true;
   HistoryPrefix();
   Check(_rotationUndo==null && !_extraGizmoDragActive && !_customDragActive,"history did not stop active rotation writers");
   Check(Studio.Studio.Instance.commandController.Commands.Count==1,"history interrupt did not close the rotation command");
   WriteRotation(bone,new Vector3(30,0,0));EndRotationUndo();
   Check(Studio.Studio.Instance.commandController.Commands.Count==1,"custom write after history resumed old transaction");
   BeginRotationUndo(0);HistoryClear();
   Check(_rotationUndo==null && _rotationUndoSource==null && !_suppressNativeRotationUndo,"clear left rotation history state active");
  }
  public static void TestHistoryInterruptHand(){
   Fresh();var finger=TestBone(35,10);BeginRotationUndo(1);TrackRotationUndo(finger);
   _rightDragIsHandBone=true;_rightDragActive=true;
   _rightDragChildren=new List<ReverseRotChildInfo>{new ReverseRotChildInfo{guideObject=finger,targetQuat=Quaternion.Euler(new Vector3(40,0,0))}};
   HistoryPrefix();
   Check(Studio.Studio.Instance.commandController.Commands.Count==1,"history interrupt lost transform-only hand command");
   Check(!_rightDragActive && !_rightDragIsHandBone && _rightDragChildren==null,"history interrupt left hand drag live");
   Check(ReadRotation(finger).x==40,"history interrupt discarded pending finger target");
   FlushPendingFinger();Check(finger.changeAmount.rot.x==40,"history interrupt finger sync failed to commit target");
  }
  public static void TestClearFlushesPendingFingers(){
   Fresh();var first=TestBone(39,10);var second=TestBone(40,20);var removed=TestBone(41,30);
   int notified=0;Action firstEvent=()=>notified++;Action secondEvent=()=>notified++;
   first.changeAmount.onChangeRot=firstEvent;second.changeAmount.onChangeRot=secondEvent;
   var replacement=new ChangeAmount{rot=new Vector3(99,0,0)};
   Studio.Studio.Instance.Registry[41]=replacement;
   QueueFingerSync(new List<ReverseRotChildInfo>{
    new ReverseRotChildInfo{guideObject=first,targetQuat=Quaternion.Euler(new Vector3(40,0,0))},
    new ReverseRotChildInfo{guideObject=second,targetQuat=Quaternion.Euler(new Vector3(55,0,0))},
    new ReverseRotChildInfo{guideObject=removed,targetQuat=Quaternion.Euler(new Vector3(70,0,0))}
   });
   Check(first.changeAmount.rot.x==10 && second.changeAmount.rot.x==20,"fixture must begin before deferred values are stored");
   HistoryClear();
   Check(first.changeAmount.rot.x==40 && second.changeAmount.rot.x==55,"Clear discarded pending finger pose angles");
   Check(_fingerSyncChildren==null && _fingerSyncIndex==0,"Clear left pending finger sync queued");
   Check(removed.changeAmount.rot.x==30 && replacement.rot.x==99,"Clear wrote to deleted or reused finger key");
   Check(Studio.Studio.Instance.commandController.Commands.Count==0,"Clear added an unrelated undo entry");
   Check(notified==0,"Clear fired finger change events during silent sync");
   Check(object.ReferenceEquals(first.changeAmount.onChangeRot,firstEvent) &&
     object.ReferenceEquals(second.changeAmount.onChangeRot,secondEvent),"Clear did not restore finger change handlers");
   first.changeAmount.rot=new Vector3(41,0,0);
   Check(notified==1,"restored finger change handler did not fire on the next edit");
  }
  public static void TestInactiveSelectedSecondary(){
   Fresh();var active=TestBone(36,10);var secondary=TestBone(37,20);secondary.isActive=false;
   _selectedObjects.Add(active);_selectedObjects.Add(secondary);
   BeginRotationUndo(0);
   active.changeAmount.rot=new Vector3(30,0,0);
   secondary.changeAmount.rot=new Vector3(40,0,0); // native multi-selection write bypasses our writer
   EndRotationUndo();
   Check(Studio.Studio.Instance.commandController.Commands.Count==1,"native multi-selection made more than one command");
   Studio.Studio.Instance.commandController.Undo();
   Check(active.changeAmount.rot.x==10 && secondary.changeAmount.rot.x==20,"inactive selected secondary target was omitted from undo");
  }
 }
}
'@

if ($helperPresent) { $tests += $helperTests }
Add-Type -TypeDefinition ($stubs + $helperBody + $tests)
$cases = [ordered]@{
 'reset groups hips and children' = { [StudioExtraMoveAxis.Regression]::RunReset() }
}
if ($helperPresent) {
 $cases['repeated gesture'] = { [StudioExtraMoveAxis.StudioExtraMoveAxisPlugin]::TestRepeatedGesture() }
 $cases['selection retarget'] = { [StudioExtraMoveAxis.StudioExtraMoveAxisPlugin]::TestRetarget() }
 $cases['empty and equivalent rotations'] = { [StudioExtraMoveAxis.StudioExtraMoveAxisPlugin]::TestNoOps() }
 $cases['deleted and reused keys'] = { [StudioExtraMoveAxis.StudioExtraMoveAxisPlugin]::TestDeletedAndReusedKey() }
 $cases['deferred finger undo and redo'] = { [StudioExtraMoveAxis.StudioExtraMoveAxisPlugin]::TestDeferredFinger() }
 $cases['new drag after pending finger sync'] = { [StudioExtraMoveAxis.StudioExtraMoveAxisPlugin]::TestPendingBaseline() }
 $cases['left pointer owns native rotation'] = { [StudioExtraMoveAxis.StudioExtraMoveAxisPlugin]::TestPointerOwnership() }
 $cases['native release after scroll'] = { [StudioExtraMoveAxis.StudioExtraMoveAxisPlugin]::TestNativeReleaseAfterScroll() }
 $cases['history interruption stops writers'] = { [StudioExtraMoveAxis.StudioExtraMoveAxisPlugin]::TestHistoryInterrupt() }
 $cases['history interruption preserves hand target'] = { [StudioExtraMoveAxis.StudioExtraMoveAxisPlugin]::TestHistoryInterruptHand() }
 $cases['history clear flushes pending fingers'] = { [StudioExtraMoveAxis.StudioExtraMoveAxisPlugin]::TestClearFlushesPendingFingers() }
 $cases['inactive selected secondary'] = { [StudioExtraMoveAxis.StudioExtraMoveAxisPlugin]::TestInactiveSelectedSecondary() }
}
$failures = @()
foreach ($case in $cases.GetEnumerator()) {
 try { & $case.Value; Write-Output "PASS: $($case.Key)" }
 catch { $failures += "$($case.Key): $($_.Exception.InnerException?.Message ?? $_.Exception.Message)" }
}
if ($failures.Count) { throw ($failures -join '; ') }
