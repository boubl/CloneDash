﻿using FftSharp;

using Raylib_cs;

using System;
using System.Diagnostics;
using System.Numerics;

namespace Nucleus.Audio
{
	public class Sound : ISound
	{
		public const uint SAMPLE_RATE = 44100; // hz

		SoundManagement? Parent;
		Raylib_cs.Sound Underlying;
		bool SelfDisposing;
		public Sound(SoundManagement? parent, Raylib_cs.Sound underlying, bool selfDisposing = true) {
			Parent = parent;
			Underlying = underlying;
			SelfDisposing = selfDisposing;
		}
		private bool disposedValue;
		public bool IsValid() => !disposedValue;

		public double Duration => (Underlying.FrameCount) / (double)SAMPLE_RATE;

		private double? playTime;
		public bool Playing => playTime != null && (EngineCore.Level.Curtime - playTime.Value) < Duration;

		public ulong UsedBits => Underlying.FrameCount == 0 ? 0 :
			// size * rate * channels = bits per second
			Underlying.Stream.SampleSize * Underlying.Stream.SampleRate * Underlying.Stream.Channels
			/ Underlying.FrameCount; // this is wrong...

		Engine.Timer? loopTimer;
		private float __loopVolume, __loopPitch, __loopPan;
		public void Play(float volume = 1.0f, float pitch = 1.0f, float pan = 0.5f, bool looping = false) {
			Debug.Assert(Parent != null);
			Parent?.PlaySound(this, volume * __volumeMultiplier, pitch, pan);
			playTime = EngineCore.Level.Curtime;
			if (looping) {
				__loopVolume = volume;
				__loopPitch = pitch;
				__loopPan = pan;
				loopTimer = EngineCore.Level.Timers.Simple((float)Duration, LoopSelf);
			}
		}

		private void LoopSelf() {
			playTime = EngineCore.Level.Curtime;
			Parent?.PlaySound(this, __loopVolume * __volumeMultiplier, __loopPitch, __loopPan);
			loopTimer = EngineCore.Level.Timers.Simple((float)Duration, LoopSelf);
		}

		public void Stop() {
			playTime = null;
			Debug.Assert(Parent != null);
			Parent?.StopSound(this);
			if (loopTimer != null)
				EngineCore.Level.Timers.Stop(loopTimer);
		}

		protected virtual void Dispose(bool disposing) {
			if (!disposedValue && SelfDisposing) {
				MainThread.RunASAP(() => {
					foreach (var cv in boundConVars)
						cv.OnChange -= Cv_OnChange;

					Raylib.StopSound(Underlying);
					Raylib.UnloadSound(Underlying);
					Parent?.EnsureISoundRemoved(this);
				}, ThreadExecutionTime.BeforeFrame);
				disposedValue = true;
			}
		}
		~Sound() { if (SelfDisposing) Dispose(false); }
		public void Dispose() {
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		public static implicit operator Raylib_cs.Sound(Sound self) => self.Underlying;



		private float __volumeMultiplier = 1f;
		List<ConVar> boundConVars = [];
		private void recalculateVolumeMultiplier() {
			__volumeMultiplier = 1;
			if (boundConVars.Count == 0)
				return;

			foreach (var cv in boundConVars)
				__volumeMultiplier *= (float)cv.GetDouble();
		}
		public void BindVolumeToConVar(ConVar cv) {
			boundConVars.Add(cv);
			cv.OnChange += Cv_OnChange;
			recalculateVolumeMultiplier();
		}
		private void Cv_OnChange(ConVar self, CVValue old, CVValue now) => recalculateVolumeMultiplier();

	}
}
