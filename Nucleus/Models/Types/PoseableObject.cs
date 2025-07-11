﻿using Newtonsoft.Json;
using Nucleus.Types;

namespace Nucleus.Models
{
	/// <summary>
	/// A poseable object (for bones, mostly).
	/// <br></br>
	/// Supports a "setup pose" which then can have animations/etc applied to it afterwards separately.
	/// <br></br>
	/// <br></br>
	/// Notes:
	/// <br></br>
	/// - All changes to the setup pose cause a transform invalidation.<br/>
	/// </summary>
	public abstract class PoseableObject
	{
		[JsonIgnore] protected Vector2F setupPos = Vector2F.Zero, setupScale = Vector2F.One, setupShear = Vector2F.Zero;
		[JsonIgnore] protected float setupRot = 0;
		[JsonIgnore] protected TransformMode setupTransformMode = TransformMode.Normal;

		[JsonIgnore] protected Vector2F pos = Vector2F.Zero, scale = Vector2F.One, shear = Vector2F.Zero;
		[JsonIgnore] protected float rot = 0;
		[JsonIgnore] protected TransformMode? transformMode;

		public Vector2F SetupPosition { get => setupPos; set { setupPos = value; ResetToSetupPose(); } }
		public float SetupRotation { get => setupRot; set { setupRot = value; ResetToSetupPose(); } }
		public Vector2F SetupScale { get => setupScale; set { setupScale = value; ResetToSetupPose(); } }
		public Vector2F SetupShear { get => setupShear; set { setupShear = value; ResetToSetupPose(); } }
		public TransformMode SetupTransformMode { get => setupTransformMode; set { setupTransformMode = value; ResetToSetupPose(); } }

		[JsonIgnore] public float SetupPositionX { get => setupPos.X; set { setupPos.X = value; ResetToSetupPose(); } }
		[JsonIgnore] public float SetupPositionY { get => setupPos.Y; set { setupPos.Y = value; ResetToSetupPose(); } }
		[JsonIgnore] public float SetupScaleX { get => setupScale.X; set { setupScale.X = value; ResetToSetupPose(); } }
		[JsonIgnore] public float SetupScaleY { get => setupScale.Y; set { setupScale.Y = value; ResetToSetupPose(); } }
		[JsonIgnore] public float SetupShearX { get => setupShear.X; set { setupShear.X = value; ResetToSetupPose(); } }
		[JsonIgnore] public float SetupShearY { get => setupShear.Y; set { setupShear.Y = value; ResetToSetupPose(); } }

		/// <summary>
		/// If false, code using PoseableObjects should not respect pose values and only respect setup values.
		/// </summary>
		public bool CanPose { get; set; } = true;

		[JsonIgnore] public Vector2F Position { get => pos; set { pos = value; InvalidateTransform(); } }
		[JsonIgnore] public float Rotation { get => rot; set { rot = value; InvalidateTransform(); } }
		[JsonIgnore] public Vector2F Scale { get => scale; set { scale = value; InvalidateTransform(); } }
		[JsonIgnore] public Vector2F Shear { get => shear; set { shear = value; InvalidateTransform(); } }
		[JsonIgnore] public TransformMode TransformMode { get => transformMode ?? setupTransformMode; set { transformMode = value; InvalidateTransform(); } }

		[JsonIgnore] public float PositionX { get => pos.X; set { pos.X = value; InvalidateTransform(); } }
		[JsonIgnore] public float PositionY { get => pos.Y; set { pos.Y = value; InvalidateTransform(); } }
		[JsonIgnore] public float ScaleX { get => scale.X; set { scale.X = value; InvalidateTransform(); } }
		[JsonIgnore] public float ScaleY { get => scale.Y; set { scale.Y = value; InvalidateTransform(); } }
		[JsonIgnore] public float ShearX { get => shear.X; set { shear.X = value; InvalidateTransform(); } }
		[JsonIgnore] public float ShearY { get => shear.Y; set { shear.Y = value; InvalidateTransform(); } }

		protected Transformation worldTransform;

		[JsonIgnore] public Transformation WorldTransform {
			get {
				if (!WorldTransformValid) {
					var parent = GetParent();
					worldTransform = Transformation.CalculateWorldTransformation(
						SetupPosition + Position,
						SetupRotation + Rotation,
						SetupScale * Scale,
						SetupShear + Shear,
						transformMode ?? setupTransformMode, 
						parent == null ? null : parent.WorldTransform
					);
					WorldTransformValid = true;
				}

				return worldTransform;
			}
		}
		[JsonIgnore] public bool WorldTransformValid { get; protected set; }

		/// <summary>
		/// Needed for internal calculations. Just point this towards the classes parent.
		/// <br></br>
		/// </summary>
		/// <returns>The objects parent.</returns>
		public abstract PoseableObject? GetParent();
		/// <summary>
		/// Needed for internal calculations. Just point this towards the classes children.
		/// <br></br>
		/// <b>WARNING</b>: If you return null here, transform invalidations won't propagate throughout the object hierarchy!
		/// </summary>
		/// <returns>The objects children.</returns>
		public abstract IEnumerable<PoseableObject>? GetChildren();

		public void ResetToSetupPose() {
			pos = Vector2F.Zero;
			rot = 0;
			scale = Vector2F.One;
			shear = Vector2F.Zero;
			transformMode = null;
			InvalidateTransform();
		}

		public void InvalidateTransform() {
			WorldTransformValid = false;
			var children = GetChildren();
			if (children == null) return;

			foreach (var child in children)
				child.InvalidateTransform();
		}
	}
}
