﻿
using CloneDash.Animation;
using CloneDash.Scenes;
using CloneDash.Settings;

using Nucleus;
using Nucleus.Engine;
using Nucleus.ManagedMemory;
using Nucleus.Types;

using Raylib_cs;

namespace CloneDash.Game.Entities
{
	public class SustainBeam : DashEnemy
	{
		public SustainBeam() : base(EntityType.SustainBeam) {
			Interactivity = EntityInteractivity.Sustain;
			DoesDamagePlayer = true;
		}
		public override void Initialize() {
			base.Initialize();
		}

		public bool WasHit { get; private set; } = false;
		public bool HeldState { get; private set; } = false;
		public bool StopAcceptingInput { get; private set; } = false;

		public Pathway PathwayCheck;

		private double lastCheckTime;

		public override void OnReset() {
			base.OnReset();
			WasHit = false;
			HeldState = false;
			StopAcceptingInput = false;
		}

		protected override void OnHit(PathwaySide attackedPath, double distanceToHit) {
			if (HeldState == true)
				return;
			if (StopAcceptingInput == true)
				return;

			var lvl = Level.As<DashGameLevel>();
			PathwayCheck = lvl.GetPathway(attackedPath);
			HeldState = true;
			WasHit = true;
			ForceDraw = true;
			lastCheckTime = lvl.Conductor.Time;
			lvl.Sustains.StartSustainBeam(this);
			lvl.AddCombo();
			lvl.AddFever(FeverGiven);
		}

		protected override void OnMiss() {
			if (HeldState == false) {
				Level.As<DashGameLevel>().Sustains.FailSustainBeam(this);
				PunishPlayer();
			}
		}

		public override bool VisTest(float gamewidth, float gameheight, float xPosition) {
			return NMath.InRange(GetVisualTimeUntilHit(), -2 - Length, 2);
		}

		protected override void OnReward() {
			GetStats().Hit(this, 0);
		}

		public override void Think(FrameState frameState) {
			if (HeldState) {
				// check if sustain complete
				var lvl = GetGameLevel();
				lvl.Sustains.ThinkSustainBeam(this);
			}
		}

		public float StartPosition { get; private set; }
		public float RotationDegsPerSecond = 200;
		private void drawStartQuad(DashGameLevel game, ref FrameState fs, float x) {
			x -= (float)InputSettings.VisualOffset;
			var tex = start;
			var xpos = (HeldState ? game.GetPathway(Pathway).Position.X : (float)XPosFromTimeOffset(x));
			var ypos = game.GetPathway(Pathway).Position.Y;
			var rot = (float)((game.Conductor.Time * RotationDegsPerSecond) % 360) * -1;
			Raylib.DrawTexturePro(tex, new(0, 0, tex.Width, tex.Height), new(xpos, ypos, tex.Width * 2, tex.Height * 2), new(tex.Width, tex.Height), rot, Color.White with { A = beamAlpha });
		}
		private void drawEndQuad(DashGameLevel game, ref FrameState fs, float x) {
			x -= (float)InputSettings.VisualOffset;
			var tex = end;
			var xpos = (float)XPosFromTimeOffset(x);
			var ypos = game.GetPathway(Pathway).Position.Y;
			var rot = (float)((game.Conductor.Time * RotationDegsPerSecond) % 360) * -1;
			Raylib.DrawTexturePro(tex, new(0, 0, tex.Width, tex.Height), new(xpos, ypos, tex.Width * 2, tex.Height * 2), new(tex.Width, tex.Height), rot, Color.White with { A = beamAlpha });
		}

		private SecondOrderSystem sosFail = new(2, 1, 1, 0);
		private byte beamAlpha;
		public void drawScrollQuad(DashGameLevel game, Texture tex, ref FrameState fs, float xOffset, float yOffset) {
			float voffset = -(float)InputSettings.VisualOffset;
			var xStart = (float)XPosFromTimeOffset(voffset);
			var xMid = HeldState ? game.GetPathway(Pathway).Position.X : xStart;
			var xEnd = (float)XPosFromTimeOffset((float)Length + voffset);
			var ypos = game.GetPathway(Pathway).Position.Y + yOffset;
			var height = tex.Height;

			Rlgl.Begin(DrawMode.TRIANGLES);
			Rlgl.DisableBackfaceCulling();

			Rlgl.Color4ub(255, 255, 255, beamAlpha);

			var maxLength = (xEnd - xStart) / (tex.Width * 2);
			var length = maxLength - ((xEnd - xMid) / (tex.Width * 2));

			xMid = xMid + xOffset;
			Rlgl.SetTexture(tex.HardwareID);
			{
				Rlgl.TexCoord2f(length, 0); Rlgl.Vertex2f(xMid, ypos + -height);
				Rlgl.TexCoord2f(length, 1); Rlgl.Vertex2f(xMid, ypos + height);
				Rlgl.TexCoord2f(maxLength, 1); Rlgl.Vertex2f(xEnd, ypos + height);

				Rlgl.TexCoord2f(maxLength, 1); Rlgl.Vertex2f(xEnd, ypos + height);
				Rlgl.TexCoord2f(maxLength, 0); Rlgl.Vertex2f(xEnd, ypos + -height);
				Rlgl.TexCoord2f(length, 0); Rlgl.Vertex2f(xMid, ypos + -height);
			}
			Rlgl.End();
			Rlgl.DrawRenderBatchActive();
		}

		public override void Render(FrameState frameState) {
			if (!ShouldDraw) return;

			var game = Level.As<DashGameLevel>();
			beamAlpha = Convert.ToByte(NMath.Remap(sosFail.Update(DidPunishPlayer ? 1 : 0), 0, 1, 255, 127, true));

			drawScrollQuad(game, body, ref frameState, 0, 0);

			var time = game.Conductor.Time * 5;
			var sv = (float)(Math.Sin(time) * 10);
			var cv = (float)(Math.Cos(time) * 10);

			drawScrollQuad(game, up, ref frameState, cv / 2, sv);
			drawScrollQuad(game, down, ref frameState, sv / 2, cv);

			drawStartQuad(game, ref frameState, 0);
			drawEndQuad(game, ref frameState, (float)Length);
		}

		private Texture start;
		private Texture end;
		private Texture body;
		private Texture up;
		private Texture down;

		public override void Build() {
			base.Build();

			var level = Level.As<DashGameLevel>();
			var scene = level.Scene;

			scene.GetSustainResources(Pathway, out start, out end, out body, out up, out down, out RotationDegsPerSecond);
		}

		internal void Complete() {
			var lvl = GetGameLevel();

			HeldState = false;
			StopAcceptingInput = true;
			ShouldDraw = false;
			RewardPlayer();

			lvl.AddCombo();
			lvl.AddFever(FeverGiven);
			lvl.Sustains.CompleteSustainBeam(this);
			lvl.Scene.PlaySound(SceneSound.PressTop, 1);
		}
		internal void Fail() {
			var lvl = GetGameLevel();

			HeldState = false;
			StopAcceptingInput = true;
			ShouldDraw = false;
			PunishPlayer();

			lvl.Sustains.FailSustainBeam(this);

			GetStats().Miss(this);
		}
		internal void Hold() {
			var lvl = GetGameLevel();

			var now = GetConductor().Time;
			var delta = now - lastCheckTime;
			if (delta >= 0.1) { // Give 10 score for every 100ms held (should this be done differently?)
				lastCheckTime = now;
				lvl.AddScore(10);
			}
		}
	}
}