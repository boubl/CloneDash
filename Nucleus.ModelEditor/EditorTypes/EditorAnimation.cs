﻿using Newtonsoft.Json;
using Nucleus.Models;
using Nucleus.Types;
using Raylib_cs;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Mail;
using System.Reflection;
using System.Security.Cryptography;

namespace Nucleus.ModelEditor;

public enum KeyframeState
{
	NotKeyframed,
	PendingKeyframe,
	Keyframed
}

public interface IBoneTimeline
{
	public EditorBone Bone { get; set; }
}

public interface ISlotTimeline
{
	public EditorSlot Slot { get; set; }
}

public interface IProperty<T>
{
	/// <summary>
	/// Gets the default value (setup value)
	/// </summary>
	/// <returns></returns>
	public T GetSetupValue();
	/// <summary>
	/// Gets the current value
	/// </summary>
	/// <returns></returns>
	public T GetValue();
	/// <summary>
	/// Compares two <typeparamref name="T"/>'s. The reason you must define this (and usually, it can just be a == b) is for things like draw order; a draw order in this case
	/// would be an integer array, which would requiring iterating over two integer arrays and comparing their values rather than comparing references of the arrays
	/// </summary>
	/// <param name="a"></param>
	/// <param name="b"></param>
	/// <returns></returns>
	public bool Compare(T? a, T? b);
}

public interface IBoneProperty<T> : IBoneTimeline, IProperty<T>
{

}
public interface ISlotProperty<T> : ISlotTimeline, IProperty<T>
{

}

public struct TimelineKeyframePairs(EditorTimeline timeline, IFCurve[] curves, IKeyframe[] keyframes) : IKeyframe
{
	public EditorTimeline Timeline => timeline;
	public IFCurve[] FCurves => curves;
	public IKeyframe[] Keyframes => keyframes;

	public double GetTime() => Keyframes[0].GetTime();

	public object? GetValue() => throw new NotImplementedException();
	public T? GetValue<T>() => throw new NotImplementedException();
	public Type GetValueType() => throw new NotImplementedException();

	/// <summary>
	/// You can use this, as it calls <see cref="IFCurve.SetKeyframeTime(IKeyframe, double)"/> properly.
	/// </summary>
	/// <param name="time"></param>
	public void SetTime(double time) {
		time = Math.Max(0, time);
		for (int i = 0; i < curves.Length; i++) {
			curves[i].SetKeyframeTime(keyframes[i], time);
		}
		ModelEditor.Active?.QueueAnimationUpdate();
	}

	public void SetValue(object? value) {
		for (int i = 0; i < keyframes.Length; i++) {
			keyframes[i].SetValue(value);
		}
	}

	public void SetValue<T>(T? value) {
		for (int i = 0; i < keyframes.Length; i++) {
			keyframes[i].SetValue(value);
		}
	}

	/// <summary>
	/// Does this even help? This is mostly for use with HashSet...
	/// </summary>
	/// <returns></returns>
	/// <exception cref="Exception"></exception>
	public override int GetHashCode() {
		switch (Keyframes.Length) {
			case 1: return HashCode.Combine(Timeline, Keyframes[0]);
			case 2: return HashCode.Combine(Timeline, Keyframes[0], Keyframes[1]);
			case 3: return HashCode.Combine(Timeline, Keyframes[0], Keyframes[1], Keyframes[2]);
			case 4: return HashCode.Combine(Timeline, Keyframes[0], Keyframes[1], Keyframes[2], Keyframes[3]);
			case 5: return HashCode.Combine(Timeline, Keyframes[0], Keyframes[1], Keyframes[2], Keyframes[3], Keyframes[4]);
			case 6: return HashCode.Combine(Timeline, Keyframes[0], Keyframes[1], Keyframes[2], Keyframes[3], Keyframes[4], Keyframes[5]);
			case 7: return HashCode.Combine(Timeline, Keyframes[0], Keyframes[1], Keyframes[2], Keyframes[3], Keyframes[4], Keyframes[5], Keyframes[6]);
			default: throw new Exception();
		}
	}
}

public abstract class EditorTimeline
{
	public static readonly Color TIMELINE_COLOR_ROTATION = new(50, 255, 50);
	public static readonly Color TIMELINE_COLOR_TRANSLATE = new(50, 50, 255);
	public static readonly Color TIMELINE_COLOR_SCALE = new(255, 50, 50);
	public static readonly Color TIMELINE_COLOR_SHEAR = new(255, 255, 70);
	public static readonly Color TIMELINE_COLOR_ATTACHMENT = new(170);
	public static readonly Color TIMELINE_COLOR_SLOT_COLOR = new(255, 95, 175);
	/// <summary>
	/// Optional <see cref="Color"/>, used in the dope sheet
	/// </summary>
	public virtual Color Color => Color.White;
	public abstract void Apply(EditorModel model, double time);
	public abstract KeyframeState KeyframedAt(double time);
	public abstract double CalculateMaxTime();

	public abstract IEnumerable<double> GetKeyframeTimes();
	public abstract IEnumerable<TimelineKeyframePairs> GetKeyframes();

	protected KeyframeState KeyframedAtCalc<TL, VL>(TL obj, double time) where TL : IKeyframeQueryable<VL>, IProperty<VL> {
		return !obj.TryGetKeyframedValueAtTime(time, out var key) ? KeyframeState.NotKeyframed : obj.Compare(key, obj.GetValue()) ? KeyframeState.Keyframed : KeyframeState.PendingKeyframe;
	}

	public void ScaleTime(double refFPS) {
		foreach(var kf in GetKeyframes()) {
			kf.SetTime(kf.GetTime() * refFPS);
		}
	}
}

public interface IKeyframeQueryable<T>
{
	public abstract bool KeyframedAtTime(double time);
	public abstract bool TryGetKeyframedValueAtTime(double time, out T? value);
	public abstract void InsertKeyframe(double time, T value);
}

public abstract class CurveTimeline : EditorTimeline;

public abstract class CurveTimeline1 : CurveTimeline, IKeyframeQueryable<float>
{
	public FCurve<float> Curve = new();

	public bool KeyframedAtTime(double time) => Curve.TryFindKeyframe(time, out var _);
	public bool TryGetKeyframedValueAtTime(double time, out float value) {
		var found = Curve.TryFindKeyframe(time, out var key);
		value = 0;
		if (!found) return false;

		value = key?.Value ?? 0;
		return true;
	}

	public void InsertKeyframe(double time, float value) {
		Curve.AddKeyframe(new(time, value));
	}

	public override double CalculateMaxTime() => Curve.Last?.Time ?? 0;

	public override IEnumerable<TimelineKeyframePairs> GetKeyframes() {
		foreach (var keyframe in Curve.GetKeyframes())
			yield return new(this, [Curve], [keyframe]);
	}

	public override IEnumerable<double> GetKeyframeTimes() {
		foreach (var keyframe in Curve.GetKeyframes())
			yield return keyframe.Time;
	}
}

public abstract class GenericStepTimeline<T> : CurveTimeline, IKeyframeQueryable<T>
{
	public FCurve<T> Curve = new();
	public bool KeyframedAtTime(double time) => Curve.TryFindKeyframe(time, out var _);
	public bool TryGetKeyframedValueAtTime(double time, out T? value) {
		var found = Curve.TryFindKeyframe(time, out var key);
		value = default;
		if (!found) return false;

		value = key == null ? value : key.Value;
		return true;
	}

	public void InsertKeyframe(double time, T value) {
		Curve.AddKeyframe(new(time, value));
	}

	public override double CalculateMaxTime() => Curve.Last?.Time ?? 0;


	public override IEnumerable<TimelineKeyframePairs> GetKeyframes() {
		foreach (var keyframe in Curve.GetKeyframes())
			yield return new(this, [Curve], [keyframe]);
	}
	public override IEnumerable<double> GetKeyframeTimes() {
		foreach (var keyframe in Curve.GetKeyframes())
			yield return keyframe.Time;
	}
}

public abstract class CurveTimeline2 : CurveTimeline, IKeyframeQueryable<Vector2F>
{
	public FCurve<float> CurveX = new();
	public FCurve<float> CurveY = new();

	public bool KeyframedAtTime(double time) => CurveX.TryFindKeyframe(time, out var _);
	public bool TryGetKeyframedValueAtTime(double time, out Vector2F value) {
		var foundX = CurveX.TryFindKeyframe(time, out var keyX);
		var foundY = CurveY.TryFindKeyframe(time, out var keyY);
		Debug.Assert(foundX == foundY);

		value = Vector2F.Zero;
		if (!foundX) return false;

		value = new(keyX?.Value ?? 0, keyY?.Value ?? 0);
		return true;
	}

	public void InsertKeyframe(double time, Vector2F value) {
		CurveX.AddKeyframe(new(time, value.X));
		CurveY.AddKeyframe(new(time, value.Y));
	}

	public override double CalculateMaxTime() => CurveX.Last?.Time ?? 0;


	public override IEnumerable<TimelineKeyframePairs> GetKeyframes() {
		for (int i = 0; i < CurveX.Count; i++)
			yield return new(this, [CurveX, CurveY], [CurveX[i], CurveY[i]]);
	}
	public override IEnumerable<double> GetKeyframeTimes() {
		foreach (var keyframe in CurveX.GetKeyframes())
			yield return keyframe.Time;
	}
}

public class RotationTimeline : CurveTimeline1, IBoneProperty<float>
{
	public override Color Color => TIMELINE_COLOR_ROTATION;
	public EditorBone Bone { get; set; }
	public override void Apply(EditorModel model, double time) {
		Bone.Rotation = Curve.DetermineValueAtTime(time);
	}
	public float GetSetupValue() => Bone.SetupRotation;
	public float GetValue() => Bone.Rotation;
	public bool Compare(float a, float b) => a == b;
	public override KeyframeState KeyframedAt(double time) => KeyframedAtCalc<RotationTimeline, float>(this, time);
}


public class TranslateTimeline : CurveTimeline2, IBoneProperty<Vector2F>
{
	public override Color Color => TIMELINE_COLOR_TRANSLATE;
	public EditorBone Bone { get; set; }
	public override void Apply(EditorModel model, double time) {
		Bone.Position = new(
			CurveX.DetermineValueAtTime(time),
			CurveY.DetermineValueAtTime(time)
		);
	}
	public Vector2F GetSetupValue() => Bone.SetupPosition;
	public Vector2F GetValue() => Bone.Position;
	public bool Compare(Vector2F a, Vector2F b) => a == b;
	public override KeyframeState KeyframedAt(double time) => KeyframedAtCalc<TranslateTimeline, Vector2F>(this, time);
}

public class TranslateXTimeline : CurveTimeline1, IBoneProperty<float>
{
	public override Color Color => TIMELINE_COLOR_TRANSLATE;
	public EditorBone Bone { get; set; }
	public override void Apply(EditorModel model, double time) {
		Bone.PositionX = Curve.DetermineValueAtTime(time);
	}
	public float GetSetupValue() => Bone.SetupPositionX;
	public float GetValue() => Bone.PositionX;
	public bool Compare(float a, float b) => a == b;
	public override KeyframeState KeyframedAt(double time) => KeyframedAtCalc<TranslateXTimeline, float>(this, time);
}

public class TranslateYTimeline : CurveTimeline1, IBoneProperty<float>
{
	public override Color Color => TIMELINE_COLOR_TRANSLATE;
	public EditorBone Bone { get; set; }
	public override void Apply(EditorModel model, double time) {
		Bone.PositionY = Curve.DetermineValueAtTime(time);
	}
	public float GetSetupValue() => Bone.SetupPositionY;
	public float GetValue() => Bone.PositionY;
	public bool Compare(float a, float b) => a == b;
	public override KeyframeState KeyframedAt(double time) => KeyframedAtCalc<TranslateYTimeline, float>(this, time);
}





public class ScaleTimeline : CurveTimeline2, IBoneProperty<Vector2F>
{
	public override Color Color => TIMELINE_COLOR_SCALE;
	public EditorBone Bone { get; set; }
	public override void Apply(EditorModel model, double time) {
		Bone.Scale = new(
			CurveX.DetermineValueAtTime(time),
			CurveY.DetermineValueAtTime(time)
		);
	}
	public Vector2F GetSetupValue() => Bone.SetupScale;
	public Vector2F GetValue() => Bone.Scale;
	public bool Compare(Vector2F a, Vector2F b) => a == b;
	public override KeyframeState KeyframedAt(double time) => KeyframedAtCalc<ScaleTimeline, Vector2F>(this, time);
}

public class ScaleXTimeline : CurveTimeline1, IBoneProperty<float>
{
	public override Color Color => TIMELINE_COLOR_SCALE;
	public EditorBone Bone { get; set; }
	public override void Apply(EditorModel model, double time) {
		Bone.ScaleX = Curve.DetermineValueAtTime(time);
	}
	public float GetSetupValue() => Bone.SetupScaleX;
	public float GetValue() => Bone.ScaleX;
	public bool Compare(float a, float b) => a == b;
	public override KeyframeState KeyframedAt(double time) => KeyframedAtCalc<ScaleXTimeline, float>(this, time);
}

public class ScaleYTimeline : CurveTimeline1, IBoneProperty<float>
{
	public override Color Color => TIMELINE_COLOR_SCALE;
	public EditorBone Bone { get; set; }
	public override void Apply(EditorModel model, double time) {
		Bone.ScaleY = Curve.DetermineValueAtTime(time);
	}
	public float GetSetupValue() => Bone.SetupScaleY;
	public float GetValue() => Bone.ScaleY;
	public bool Compare(float a, float b) => a == b;
	public override KeyframeState KeyframedAt(double time) => KeyframedAtCalc<ScaleYTimeline, float>(this, time);
}

public class ShearTimeline : CurveTimeline2, IBoneProperty<Vector2F>
{
	public override Color Color => TIMELINE_COLOR_SHEAR;
	public EditorBone Bone { get; set; }
	public override void Apply(EditorModel model, double time) {
		Bone.Shear = new(
			CurveX.DetermineValueAtTime(time),
			CurveY.DetermineValueAtTime(time)
		);
	}
	public Vector2F GetSetupValue() => Bone.Shear;
	public Vector2F GetValue() => Bone.Shear;
	public bool Compare(Vector2F a, Vector2F b) => a == b;
	public override KeyframeState KeyframedAt(double time) => KeyframedAtCalc<ShearTimeline, Vector2F>(this, time);
}

public class ShearXTimeline : CurveTimeline1, IBoneProperty<float>
{
	public override Color Color => TIMELINE_COLOR_SHEAR;
	public EditorBone Bone { get; set; }
	public override void Apply(EditorModel model, double time) {
		Bone.ShearX = Curve.DetermineValueAtTime(time);
	}
	public float GetSetupValue() => Bone.ShearX;
	public float GetValue() => Bone.ShearX;
	public bool Compare(float a, float b) => a == b;
	public override KeyframeState KeyframedAt(double time) => KeyframedAtCalc<ShearXTimeline, float>(this, time);
}

public class ShearYTimeline : CurveTimeline1, IBoneProperty<float>
{
	public override Color Color => TIMELINE_COLOR_SHEAR;
	public EditorBone Bone { get; set; }
	public override void Apply(EditorModel model, double time) {
		Bone.ShearY = Curve.DetermineValueAtTime(time);
	}
	public float GetSetupValue() => Bone.ShearY;
	public float GetValue() => Bone.ShearY;
	public bool Compare(float a, float b) => a == b;
	public override KeyframeState KeyframedAt(double time) => KeyframedAtCalc<ShearYTimeline, float>(this, time);
}


public class ActiveAttachmentTimeline : GenericStepTimeline<EditorAttachment?>, ISlotProperty<EditorAttachment?>
{
	public override Color Color => TIMELINE_COLOR_ATTACHMENT;
	public EditorSlot Slot { get; set; }
	public override void Apply(EditorModel model, double time) {
		Slot.ActiveAttachment = Curve.DetermineValueAtTime(time);
	}
	public EditorAttachment? GetSetupValue() => Slot.SetupActiveAttachment;
	public EditorAttachment? GetValue() => Slot.ActiveAttachment;
	public bool Compare(EditorAttachment? a, EditorAttachment? b) => a == b;
	public override KeyframeState KeyframedAt(double time) => KeyframedAtCalc<ActiveAttachmentTimeline, EditorAttachment?>(this, time);
}


public class SlotColorTimeline : CurveTimeline, ISlotProperty<Color>, IKeyframeQueryable<Color>
{
	public FCurve<float> CurveR { get; set; } = new();
	public FCurve<float> CurveG { get; set; } = new();
	public FCurve<float> CurveB { get; set; } = new();
	public FCurve<float> CurveA { get; set; } = new();
	public override Color Color => TIMELINE_COLOR_SLOT_COLOR;
	public EditorSlot Slot { get; set; }

	public override void Apply(EditorModel model, double time) {
		Slot.Color = new(
			(int)Math.Clamp(CurveR.DetermineValueAtTime(time) * 2.55f, 0, 255),
			(int)Math.Clamp(CurveG.DetermineValueAtTime(time) * 2.55f, 0, 255),
			(int)Math.Clamp(CurveB.DetermineValueAtTime(time) * 2.55f, 0, 255),
			(int)Math.Clamp(CurveA.DetermineValueAtTime(time) * 2.55f, 0, 255)
		);
	}
	public Color GetSetupValue() => Slot.SetupColor;
	public Color GetValue() => Slot.Color;
	public bool Compare(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;
	public bool KeyframedAtTime(double time) => CurveR.TryFindKeyframe(time, out var _);

	public bool TryGetKeyframedValueAtTime(double time, out Color value) {
		var fR = CurveR.TryFindKeyframe(time, out var r);
		var fG = CurveG.TryFindKeyframe(time, out var g);
		var fB = CurveB.TryFindKeyframe(time, out var b);
		var fA = CurveA.TryFindKeyframe(time, out var a);

		var sanityCheck = fR == fB == fG == fA;
		Debug.Assert(sanityCheck, "Curve evaluation failed; all four curves didn't have keyframes at this time...");
		var pass = fR == true && fB == true && fG == true && fA == true;
		if (!pass) {
			value = Color.Blank;
			return false;
		}

		value = new(
			(int)Math.Clamp((r?.Value ?? 0) * 2.55f, 0, 255),
			(int)Math.Clamp((g?.Value ?? 0) * 2.55f, 0, 255),
			(int)Math.Clamp((b?.Value ?? 0) * 2.55f, 0, 255),
			(int)Math.Clamp((a?.Value ?? 0) * 2.55f, 0, 255)
		);

		return true;
	}

	public void InsertKeyframe(double time, Color value) {
		CurveR.AddKeyframe(new(time, value.R / 2.55f));
		CurveG.AddKeyframe(new(time, value.G / 2.55f));
		CurveB.AddKeyframe(new(time, value.B / 2.55f));
		CurveA.AddKeyframe(new(time, value.A / 2.55f));
	}

	public override KeyframeState KeyframedAt(double time) => KeyframedAtCalc<SlotColorTimeline, Color>(this, time);

	public override double CalculateMaxTime() => CurveR.Last?.Time ?? 0;


	public override IEnumerable<TimelineKeyframePairs> GetKeyframes() {
		for (int i = 0; i < CurveR.Count; i++)
			yield return new(this, [CurveR, CurveG, CurveB, CurveA], [CurveR[i], CurveG[i], CurveB[i], CurveA[i]]);
	}
	public override IEnumerable<double> GetKeyframeTimes() {
		foreach (var keyframe in CurveR.GetKeyframes())
			yield return keyframe.Time;
	}
}

public class EditorAnimation : IEditorType
{
	public EditorModel GetModel() => Model;
	public EditorModel Model { get; set; }
	[JsonIgnore] public string SingleName => "animation";
	[JsonIgnore] public string PluralName => "animations";

	[JsonIgnore] public bool Hovered { get; set; }
	[JsonIgnore] public bool Selected { get; set; }

	[JsonIgnore]
	public bool Hidden {
		get => Model.ActiveAnimation != this;
		set { }
	}

	public double CalculateMaxTime() {
		double time = 0;

		foreach (var tl in Timelines) {
			var tlTime = tl.CalculateMaxTime();
			if (tlTime > time)
				time = tlTime;
		}

		return time;
	}

	public string Name { get; set; }

	public Dictionary<KeyframeProperty, HashSet<EditorBone>> SeparatedProperties = [];
	public bool DoesBoneHaveSeparatedProperty(EditorBone bone, KeyframeProperty property) {
		if (!SeparatedProperties.TryGetValue(property, out var hs)) {
			hs = [];
			SeparatedProperties[property] = hs;
		}

		return hs.Contains(bone);
	}
	public void SetDoesBoneHaveSeparatedProperty(EditorBone bone, KeyframeProperty property, bool state) {
		if (!SeparatedProperties.TryGetValue(property, out var hs)) {
			hs = [];
			SeparatedProperties[property] = hs;
		}

		if (state)
			hs.Add(bone);
		else
			hs.Remove(bone);
	}

	public List<EditorTimeline> Timelines = [];

	// This is a generic's and interfaces nightmare
	public (T Timeline, bool Created) GetTimeline<T>(IEditorType item, bool createIfMissing = true) where T : EditorTimeline, new() {
		T? timeline;
		switch (item) {
			case EditorBone bone:
				timeline = Timelines.FirstOrDefault(x => x is T tTimeline && x is IBoneTimeline btl && btl.Bone == bone) as T;
				break;
			case EditorSlot slot:
				timeline = Timelines.FirstOrDefault(x => x is T tTimeline && x is ISlotTimeline stl && stl.Slot == slot) as T;
				break;
			default:
				throw new Exception($"Unregistered IEditorType for GetTimeline<T> (got typeof '{item.GetType().Name}')");
		}

		(T Timeline, bool Created) result = (null, false);
		if (timeline == null) {
			if (!createIfMissing) return result;

			result.Created = true;
			timeline = new();

			switch (timeline) {
				case IBoneTimeline btl: btl.Bone = item as EditorBone ?? throw new Exception("wtf"); break;
				case ISlotTimeline stl: stl.Slot = item as EditorSlot ?? throw new Exception("wtf"); break;
				default: throw new Exception($"No creation-assignment function defined for GetTimeline<T> (got typeof '{item.GetType().Name}')");
			}

			Timelines.Add(timeline);
		}

		result.Timeline = timeline;
		return result;
	}

	public EditorTimeline? SearchTimelineByProperty(IEditorType? type, KeyframeProperty property, int arrayIndex, bool createIfMissing) {
		var tl = SearchTimelineByProperty(type, property, out var _, arrayIndex, createIfMissing);
		return tl;
	}

	public EditorTimeline? SearchTimelineByProperty(IEditorType? type, KeyframeProperty property, out bool created, int arrayIndex, bool createIfMissing) {
		(EditorTimeline Timeline, bool Created) info = type switch {
			EditorBone bone => property switch {
				KeyframeProperty.None => new(null, false),
				KeyframeProperty.Bone_Rotation => GetTimeline<RotationTimeline>(bone, createIfMissing),
				KeyframeProperty.Bone_Translation => arrayIndex switch {
					-1 => GetTimeline<TranslateTimeline>(bone, createIfMissing),
					0 => GetTimeline<TranslateXTimeline>(bone, createIfMissing),
					1 => GetTimeline<TranslateYTimeline>(bone, createIfMissing),
					_ => new(null, false)
				},
				KeyframeProperty.Bone_Scale => arrayIndex switch {
					-1 => GetTimeline<ScaleTimeline>(bone, createIfMissing),
					0 => GetTimeline<ScaleXTimeline>(bone, createIfMissing),
					1 => GetTimeline<ScaleYTimeline>(bone, createIfMissing),
					_ => new(null, false)
				},
				KeyframeProperty.Bone_Shear => arrayIndex switch {
					-1 => GetTimeline<ShearTimeline>(bone, createIfMissing),
					0 => GetTimeline<ShearXTimeline>(bone, createIfMissing),
					1 => GetTimeline<ShearYTimeline>(bone, createIfMissing),
					_ => new(null, false)
				},
				_ => throw new Exception("Missing property to search.")
			},
			EditorSlot slot => property switch {
				KeyframeProperty.Slot_Attachment => GetTimeline<ActiveAttachmentTimeline>(slot, createIfMissing),
				KeyframeProperty.Slot_Color => GetTimeline<SlotColorTimeline>(slot, createIfMissing),
				_ => throw new Exception("Missing property to search.")
			},
			_ => new(null, false)
		};

		created = info.Created;
		return info.Timeline;
	}

	public bool Export { get; set; } = true;

	public string GetName() => Name;

	public void Apply(double time) {
		foreach (var timeline in Timelines) {
			timeline.Apply(Model, time);
		}
	}

	public virtual void OnHidden() {
		ModelEditor.Active.File.UnsetActiveAnimation(Model);
		// Force selection (pull out of any operators etc)
		ModelEditor.Active.File.DeactivateOperator(true);
	}
	public virtual void OnShown() {
		ModelEditor.Active.File.SetActiveAnimation(Model, this);
		ModelEditor.Active.File.DeactivateOperator(true);
	}

	public virtual bool CanTranslate() => false;
	public virtual bool CanRotate() => false;
	public virtual bool CanScale() => false;
	public virtual bool CanShear() => false;
	public virtual bool CanHide() => true;

	public virtual bool GetVisible() => !Hidden;

	public virtual bool CanRename() => true;
	public virtual bool CanDelete() => true;
	public EditorResult Rename(string newName) => ModelEditor.Active.File.RenameAnimation(Model, this, newName);
	public virtual void Render() { }
	public virtual void RenderOverlay() { }

	internal List<EditorBone> GetAffectedBones() {
		HashSet<EditorBone> bones = [];

		foreach (var timeline in Timelines) {
			switch (timeline) {
				case IBoneTimeline boneTimeline: bones.Add(boneTimeline.Bone); break;
				case ISlotTimeline slotTimeline: bones.Add(slotTimeline.Slot.Bone); break;
			}
		}

		return bones.ToList();
	}
}
