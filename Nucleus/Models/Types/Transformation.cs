﻿using Nucleus.Types;
using System.Diagnostics;
using System.Numerics;

namespace Nucleus.Models
{
	public struct Transformation
	{
		// matrix components
		public float A;
		public float B;
		public float C;
		public float D;
		public float X;
		public float Y;

		public void Decompose(out float a, out float b, out float c, out float d, out float x, out float y) {
			a = A; b = B;
			c = C; d = D;
			x = X; y = Y;
		}

		// cached values from CalculateWorldTransformation
		// needed for World/Local rotation operations
		private float Rotation;
		private float ShearX;

		public Vector2F Translation => new(X, Y);

		public Transformation(float a, float b, float c, float d, float x, float y, float rot, float shearX) {
			A = a;
			B = b;
			C = c;
			D = d;
			X = x;
			Y = y;
			Rotation = rot;
			ShearX = shearX;
		}

		public static Transformation CalculateWorldTransformation(Vector2F pos, float rot, Vector2F scale, Vector2F shear, TransformMode transformType = TransformMode.Normal, Transformation? parent = null, bool triggerDebugger = false) {
			if (triggerDebugger) Debug.Assert(false, "Debugger triggered!");

			float posX = pos.X, posY = pos.Y;
			float scaleX = scale.X, scaleY = scale.Y;
			float shearX = shear.X, shearY = shear.Y;

			float a = 0, b = 0, c = 0, d = 0, x = 0, y = 0;

			float r_p_90_p_sy_RADS = NMath.ToRadians(rot + 90 + shearY);
			float r_p_sx_RADS = NMath.ToRadians(rot + shearX);

			if (parent == null) {
				a = MathF.Cos(r_p_sx_RADS) * scaleX;
				b = MathF.Cos(r_p_90_p_sy_RADS) * scaleY;
				c = MathF.Sin(r_p_sx_RADS) * scaleX;
				d = MathF.Sin(r_p_90_p_sy_RADS) * scaleY;

				x = posX;
				y = posY;

				return new(a, b, c, d, x, y, rot, shearX);
			}
			else {
				Transformation parentMatrix = parent.Value;
				float pA = parentMatrix.A, pB = parentMatrix.B,
					  pC = parentMatrix.C, pD = parentMatrix.D,
					  pX = parentMatrix.X, pY = parentMatrix.Y;

				x = pA * posX + pB * posY + pX;
				y = pC * posX + pD * posY + pY;

				float lA, lB, lC, lD;

				switch (transformType) {
					case TransformMode.Normal:
						lA = MathF.Cos(r_p_sx_RADS) * scaleX;
						lB = MathF.Cos(r_p_90_p_sy_RADS) * scaleY;
						lC = MathF.Sin(r_p_sx_RADS) * scaleX;
						lD = MathF.Sin(r_p_90_p_sy_RADS) * scaleY;

						a = pA * lA + pB * lC;
						b = pA * lB + pB * lD;
						c = pC * lA + pD * lC;
						d = pC * lB + pD * lD;

						break;
					case TransformMode.OnlyTranslation:
						a = MathF.Cos(r_p_sx_RADS) * scaleX;
						b = MathF.Cos(r_p_90_p_sy_RADS) * scaleY;
						c = MathF.Sin(r_p_sx_RADS) * scaleX;
						d = MathF.Sin(r_p_90_p_sy_RADS) * scaleY;
						break;
					case TransformMode.NoRotationOrReflection:
						float sc = pA * pA + pC * pC;
						float prX;

						if (sc > 0.0001f) {
							sc = MathF.Abs(pA * pD - pB * pC) / sc;
							pB = pC * sc;
							pD = pA * sc;
							prX = MathF.Atan2(pC, pA).ToDegrees();
						}
						else {
							pA = pC = 0;
							prX = 90 - MathF.Atan2(pD, pB).ToDegrees();
						}

						float rX = (rot + shear.X - prX).ToRadians();
						float rY = (rot + shear.Y - prX + 90).ToRadians();

						lA = MathF.Cos(rX) * scaleX;
						lB = MathF.Cos(rY) * scaleY;
						lC = MathF.Sin(rX) * scaleX;
						lD = MathF.Sin(rY) * scaleY;

						a = pA * lA - pB * lC;
						b = pA * lB - pB * lD;
						c = pC * lA + pD * lC;
						d = pC * lB + pD * lD;

						break;
					case TransformMode.NoScale:
					case TransformMode.NoScaleOrReflection:
						var rr = rot.ToRadians();
						var cs = MathF.Cos(rr);
						var ss = MathF.Sin(rr);

						float zA = pA * cs + pB * ss, zC = pC * cs + pD * ss, acsqr = MathF.Sqrt((zA * zA) + (zC * zC)), iacsqr = 1f / acsqr;
						zA *= acsqr > 0.00001 ? iacsqr : acsqr; zC *= acsqr > 0.00001 ? iacsqr : acsqr;
						float ca2 = MathF.PI / 2f + MathF.Atan2(zC, zA);
						float zB = MathF.Cos(ca2) * acsqr, zD = MathF.Sin(ca2) * acsqr;

						float ns_lA = MathF.Cos(shearX.ToRadians());
						float ns_lB = MathF.Cos((90 + shearY).ToRadians());
						float ns_lC = MathF.Sin(shearX.ToRadians());
						float ns_lD = MathF.Sin((90 + shearY).ToRadians());

						a = zA * ns_lA + zB * ns_lC;
						b = zA * ns_lB + zB * ns_lD;
						c = zC * ns_lA + zD * ns_lC;
						d = zC * ns_lB + zD * ns_lD;

						break;
				}
			}

			return new(a, b, c, d, x, y, rot, shearX);
		}

		public Vector2F WorldToLocal(float worldX, float worldY) {
			float invDet = 1f / (A * D - B * C);
			float x = worldX - X, y = worldY - Y;

			return new(
				x * D * invDet - y * B * invDet,
				y * A * invDet - x * C * invDet
			);
		}
		public Vector2F WorldToLocal(Vector2F worldPos) => WorldToLocal(worldPos.X, worldPos.Y);

		public Vector2F LocalToWorld(float localX, float localY) => new(
			localX * A + localY * B + X,
			localX * C + localY * D + Y
		);
		public Vector2F LocalToWorld(Vector2F localPos) => LocalToWorld(localPos.X, localPos.Y);

		public float WorldToLocalRotation(float worldRotation) {
			float sin = MathF.Sin(worldRotation.ToRadians());
			float cos = MathF.Cos(worldRotation.ToRadians());
			return MathF.Atan2(A * sin - C * cos, D * cos - B * sin).ToDegrees() + Rotation - ShearX;
		}

		public float LocalToWorldRotation(float localRotation) {
			localRotation -= Rotation - ShearX;
			float sin = MathF.Sin(localRotation.ToRadians());
			float cos = MathF.Cos(localRotation.ToRadians());
			return MathF.Atan2(cos * C + sin * D, cos * A + sin * B).ToDegrees();
		}
	}
}
