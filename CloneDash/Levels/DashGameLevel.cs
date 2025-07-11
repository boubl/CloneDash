﻿using CloneDash.Animation;
using CloneDash.Characters;
using CloneDash.Compatibility.MuseDash;
using CloneDash.Data;
using CloneDash.Fevers;
using CloneDash.Game.Entities;
using CloneDash.Game.Input;
using CloneDash.Game.Logic;
using CloneDash.Game.Statistics;
using CloneDash.Levels;
using CloneDash.Menu;
using CloneDash.Scenes;
using CloneDash.Scripting;
using CloneDash.Settings;

using Nucleus;
using Nucleus.Audio;
using Nucleus.Core;
using Nucleus.Engine;
using Nucleus.Entities;
using Nucleus.Input;
using Nucleus.ManagedMemory;
using Nucleus.Types;
using Nucleus.UI;
using Nucleus.UI.Elements;

using Raylib_cs;

using System.Diagnostics.CodeAnalysis;

using Color = Raylib_cs.Color;
using MouseButton = Nucleus.Input.MouseButton;
using Sound = Nucleus.Audio.Sound;

namespace CloneDash.Game;

[Nucleus.MarkForStaticConstruction]
public partial class DashGameLevel(ChartSheet? Sheet) : Level
{
	public LuaEnv Lua;

	public static ConCommand musicseek = ConCommand.Register(nameof(musicseek), (_, args) => {
		var level = EngineCore.Level.AsNullable<DashGameLevel>();
		if (level == null) {
			Logs.Warn("Not in game context!");
			return;
		}

		double? d = args.GetDouble(0);
		if (!d.HasValue) Logs.Warn("Did not specify a time!");
		else level.SeekTo(d.Value);
	});


	private static void clonedash_openmdlevel_execute(ConCommand cmd, ConCommandArguments args) {
		var md_level = args.GetString(0);
		if (md_level == null) {
			Logs.Warn("Provide a name.");
			return;
		}

		var map = args.GetInt(1);
		if (map == null) {
			Logs.Warn("Provide a difficulty.");
			return;
		}

		MuseDashSong? song = MuseDashCompatibility.Songs.FirstOrDefault(x => x.BaseName == md_level);
		if (song == null) {
			Logs.Warn("Can't find that song.");
			Logs.Print("Here are some similar names:");
			foreach (var s in MuseDashCompatibility.Songs.Where(x => x.BaseName.ToLower().Contains(md_level.ToLower())))
				Logs.Print($"    {s.Name} ({s.BaseName})");
			return;
		}

		DashGameLevel.LoadLevel(song, map.Value, (args.GetInt(2) ?? 0) == 1);
	}
	private static void clonedash_openmdlevel_autocomplete(ConCommandBase cmd, string argsStr, ConCommandArguments args, int curArgPos, ref string[] returns, ref string[]? returnHelp) {
		var songs = MuseDashCompatibility.Songs.Where(x => x.BaseName.StartsWith(args.GetString(curArgPos) ?? "")).ToArray();
		int len = Math.Clamp(songs.Length, 0, 20);
		if (len == 0) return;

		returns = new string[len];
		returnHelp = new string[len];

		for (int i = 0; i < len; i++) {
			var song = songs[i];
			returns[i] = song.BaseName;
			returnHelp[i] = song.Name;
		}
	}

	public static ConCommand mdlevel = ConCommand.Register(nameof(mdlevel), clonedash_openmdlevel_execute, clonedash_openmdlevel_autocomplete, "Opens a Muse Dash level.");

	public static ConCommand cdrestest = ConCommand.Register(nameof(cdrestest), (_, args) => {
		Vector2F winSize;
		switch (args.GetString(0)) {
			case "16:9": winSize = new(1600, 900); break;
			case "19.5:9": winSize = new(1950, 900); break;
			case "4:3": winSize = new(1600, 1200); break;
			default:
				Logs.Warn($"Expected 16:9, 19.5:9, or 4:3.");
				return;

		}

		Vector2F monPos = OS.GetMonitorPosition(0);
		Vector2F monSize = OS.GetMonitorSize(0);
		Vector2F winPos = (monPos + (monSize / 2)) - (winSize / 2);
		EngineCore.Window.Position = new((int)winPos.X, (int)winPos.Y);
		EngineCore.Window.Size = new((int)winSize.X, (int)winSize.Y);
	});

	public static ConVar profilegameload = ConVar.Register(nameof(profilegameload), "0", ConsoleFlags.None, "Profiles the game during loading, then triggers an engine interrupt afterwards to tell you how long each individual component took.");

	public static DashGameLevel? LoadLevel(ChartSong song, int mapID, bool autoplay) {
		Interlude.Begin($"Loading '{song.Name}'...");
		if (profilegameload.GetBool()) {
			Logs.Debug("Starting the sequential profiler.");
			StaticSequentialProfiler.Start();
		}

		DashGameLevel? workingLevel = null;
		try {
			var sheet = song.GetSheet(mapID);
			workingLevel = new DashGameLevel(sheet);

		}
		catch (Exception ex) {
			Logs.Warn($"CD_GameLevel.LoadLevel (preload): {ex.Message}. LoadLevel cancelled");
		}

		if (workingLevel == null)
			return null;

		EngineCore.LoadLevel(workingLevel, autoplay);
		return workingLevel;
	}


	public void SeekTo(double time) {
		InMashState = false;
		MashingEntity = null;

		if (Music != null) {
			Music.Playhead = (float)time;
		}

		Stats.Reset();
		foreach (var entity in Entities) {
			if (entity is not DashEnemy entCD)
				continue;

			entCD.Reset();
		}

		Combo = 0;
		Health = 250;
		InFever = false;
		WhenDidFeverStart = 0;
		lastNoteHit = false;
		Score = 0;
		Fever = 0;

		AutoPlayer.Reset();

		foreach (var entity in Entities) {
			if (entity is DashModelEntity mEnt && mEnt.HitTime < time) {
				switch (mEnt.Interactivity) {
					case EntityInteractivity.Hit:
						mEnt.Hit(mEnt.Pathway, 0);
						mEnt.RewardPlayer();
						break;
					case EntityInteractivity.SamePath:
						mEnt.Hit(mEnt.Pathway, 0);
						mEnt.RewardPlayer();
						break;
					case EntityInteractivity.Avoid:
						mEnt.Pass();
						mEnt.RewardPlayer();
						break;
				}
			}
		}
	}

	public const string STRING_HP = "HP: {0}";
	public const string STRING_FEVERY = "FEVER! {0}s";
	public const string STRING_FEVERN = "FEVER: {0}/{1}";
	public const string STRING_COMBO = "COMBO";
	public const string STRING_SCORE = "SCORE";
	public const string FONT = "Noto Sans";

	public const float PLAYER_OFFSET_X = 0.25f;
	public const float PLAYER_OFFSET_Y = 0.775f;
	public const float PLAYER_OFFSET_HIT_Y = -0.267f;

	public bool InHit { get; private set; } = false;

	public bool SuppressHitMessages { get; set; }
	public void EnterHitState() {
		InHit = true;
		SuppressHitMessages = false;
	}
	public void ExitHitState() {
		InHit = false;
	}

	public float XPos { get; private set; }

	[MemberNotNullWhen(true, nameof(MashingEntity))] public bool InMashState { get; private set; }

	public DashModelEntity? MashingEntity;
	private SecondOrderSystem MashZoomSOS = new(1.1f, 0.9f, 2f, 0);
	private TextEffect mashTextEffect;

	/// <summary>
	/// Enters the mash state, which causes all attacks to be redirected into this entity.
	/// </summary>
	/// <param name="ent"></param>
	public void EnterMashState(DashModelEntity ent) {
		if (IValidatable.IsValid(mashTextEffect))
			mashTextEffect.Remove();

		mashTextEffect = SpawnTextEffect("HITS: 1", new(0), TextEffectTransitionOut.SlideUp, Game.Pathway.PATHWAY_DUAL_COLOR);
		mashTextEffect.SuppressAutoDeath = true;
		UpdateMashTextEffect();

		InMashState = true;
		MashingEntity = ent;
	}
	public void UpdateMashTextEffect() {
		if (!IValidatable.IsValid(mashTextEffect)) return;
		if (!IValidatable.IsValid(MashingEntity)) return;

		mashTextEffect.Position = GetPathway(PathwaySide.Top).Position;
		mashTextEffect.Text = $"HITS: {MashingEntity.Hits}";
	}
	/// <summary>
	/// Exits the mash state.
	/// </summary>
	public void ExitMashState() {
		if (IValidatable.IsValid(mashTextEffect))
			mashTextEffect.Remove();

		InMashState = false;
		MashingEntity = null;
	}

	/// <summary>
	/// Is an entity on-screen and/or event currently warning the player? Used to draw the "!" warning on the side (and, if the entity wants to, on the entity itself)
	/// </summary>
	public bool IsWarning { get; set; } = false;


	// Player input system
	public InputState InputState { get; private set; }
	public List<ICloneDashInputSystem> InputReceivers { get; } = [];

	public AutoPlayer AutoPlayer { get; private set; }
	/// <summary>
	/// Timing system.
	/// </summary>
	public Conductor Conductor { get; private set; }
	public MusicTrack? Music { get; private set; }
	public ModelEntity Player { get; set; }
	public ModelEntity HologramPlayer { get; set; }
	public Boss Boss { get; set; }
	public Pathway TopPathway { get; set; }
	public Pathway BottomPathway { get; set; }

	/// <summary>
	/// Is the game currently paused
	/// </summary>
	public double UnpauseTime { get; private set; } = 0;
	public double DeltaUnpauseTime => Realtime - UnpauseTime;

	/// <summary>
	/// How many ticks have passed, meant for debugging
	/// </summary>
	public int Ticks { get; private set; } = 0;

	// WIP pausing
	// return false to not spawn the pause menu
	private bool startPause() {
		if (lastNoteHit)
			return false;
		if (Conductor.Time < 0)
			return false;

		if (Music != null)
			Music.Paused = true;
		Paused = true;
		UnpauseTime = 0;

		return true;
	}
	private void startUnpause() {
		Scene.PlaySound(SceneSound.Unpause, 0);
		UnpauseTime = Realtime;
		Timers.Simple(3, () => {
			fullUnpause();
		});
	}
	private void fullUnpause() {
		if (Music != null)
			Music.Paused = false;
		Paused = false;
		UnpauseTime = 0;
	}

	public void ForcePause() {
		if (Music != null)
			Music.Paused = true;
		Paused = true;
	}
	public void ForceUnpause() {
		if (Music != null)
			Music.Paused = false;
		Paused = false;
	}

	int attackP = 0;
	int failP = 0;

	public enum CharacterAnimation
	{
		Walk,
		AirFail,
		GroundFail,
		AirHit,
		GroundHit,
		Hold
	}

	private bool __deferringAsync = false;

	public StatisticsData Stats;
	public ICharacterDescriptor Character;
	public IFeverDescriptor? FeverFX;
	public ISceneDescriptor Scene;

	public string AnimationCDD(CharacterAnimationType type) {
		return Character.GetPlayAnimation(type);
	}

	public void PlayerAnim_EnqueueRun(ModelEntity model) {
		model.Model.SetToSetupPose();
		model.Animations.AddAnimation(0, AnimationCDD(CharacterAnimationType.Run), true);
	}

	public void PlayerAnim_ForceJump(ModelEntity model) {
		model.Animations.SetAnimation(0, AnimationCDD(CharacterAnimationType.Jump), false);
		PlayerAnim_EnqueueRun(model);
	}
	public void PlayerAnim_ForceMiss(ModelEntity model) {
		model.Animations.SetAnimation(0, AnimationCDD(CharacterAnimationType.RoadMiss), false);
		PlayerAnim_EnqueueRun(model);
	}
	public void PlayerAnim_ForceAttackAir(ModelEntity model, bool perfect) {
		if (perfect)
			model.Animations.SetAnimation(0, AnimationCDD(CharacterAnimationType.AirPerfect), false);
		else
			model.Animations.SetAnimation(0, AnimationCDD(CharacterAnimationType.AirGreat), false);
	}
	public void PlayerAnim_ForceAttackGround(ModelEntity model, bool perfect) {
		if (perfect)
			model.Animations.SetAnimation(0, AnimationCDD(CharacterAnimationType.RoadPerfect), false);
		else
			model.Animations.SetAnimation(0, AnimationCDD(CharacterAnimationType.RoadGreat), false);
	}

	public void PlayerAnim_ForceAttackDouble(ModelEntity model) {
		model.Animations.SetAnimation(0, AnimationCDD(CharacterAnimationType.Double), false);
	}

	public void PlayerAnim_EnterSustain() {
		Player.Animations.SetAnimation(0, AnimationCDD(CharacterAnimationType.Press), true);
	}
	public void PlayerAnim_ExitSustain() {
		Player.Animations.ClearAnimation(0);
		if (InAir) {
			Player.Animations.SetAnimation(0, AnimationCDD(CharacterAnimationType.AirPressEnd), false);
		}
		// We'll also kill the hologram player here
		HologramPlayer.Animations.StopAnimation(0);
		PlayerAnim_EnqueueRun(Player);
	}

	ShaderInstance hologramShader;

	public override void Initialize(params object[] args) {
		Stats = new(Sheet);
		using (StaticSequentialProfiler.StartStackFrame("CD_GameLevel.Initialize")) {
			Interlude.Spin(submessage: "Retrieving descriptors...");

			using (StaticSequentialProfiler.StartStackFrame("Get Descriptors")) {
				var charData = CharacterMod.GetCharacterData();
				if (charData == null) throw new ArgumentNullException(nameof(charData));
				Character = charData;

				var sceneData = SceneMod.GetSceneData();
				if (sceneData == null) throw new ArgumentNullException(nameof(sceneData));
				Scene = sceneData;

				var feverFX = FeverMod.GetFeverData();
				FeverFX = feverFX;
			}

			Interlude.Spin(submessage: "Initializing Lua...");
			using (StaticSequentialProfiler.StartStackFrame("Initialize Lua")) {
				Lua = new(this);
				Lua.State.Environment["game"] = new LuaGame(this);
			}

			Interlude.Spin(submessage: "Initializing the scene...");
			using (StaticSequentialProfiler.StartStackFrame("Initialize Scene/Fever")) {
				Scene.Initialize(this);
				FeverFX?.Initialize(this);

				pressIdle = Scene.GetPressIdleSound();
			}

			Interlude.Spin();

			MaxHealth = (float)Character.GetDefaultHP();

			Render3D = false;
			Health = MaxHealth;


			Interlude.Spin(submessage: "Initializing input...");
			using (StaticSequentialProfiler.StartStackFrame("Build Inputs")) {
				// build the input system
				var inputInterface = typeof(ICloneDashInputSystem);
				var inputs = AppDomain.CurrentDomain.GetAssemblies()
					.SelectMany(x => x.GetTypes())
					.Where(x => inputInterface.IsAssignableFrom(x) && !x.IsInterface && !x.IsAbstract)
					.Select(x => Activator.CreateInstance(x)).ToList();

				foreach (object input in inputs)
					InputReceivers.Add((ICloneDashInputSystem)input);
			}


			Interlude.Spin(submessage: "Initializing your character...");
			using (StaticSequentialProfiler.StartStackFrame("Initialize Character")) {
				hologramShader = Shaders.LoadFragmentShaderFromFile("shaders", "hologram.fs");
				Interlude.Spin();
				Player = Add(ModelEntity.Create(Character.GetPlayModel(this)));
				Interlude.Spin();

				HologramPlayer = Add(ModelEntity.Create(Character.GetPlayModel(this)));
				Player.Scale = new(1.25f);

				HologramPlayer.Scale = Player.Scale;
				HologramPlayer.Shader = hologramShader;

				Player.Model.SetToSetupPose();
				Player.Animations.AddAnimation(0, AnimationCDD(CharacterAnimationType.In), true);
			}


			Interlude.Spin(submessage: "Loading boss...");
			using (StaticSequentialProfiler.StartStackFrame("Initialize Boss")) {
				Boss = Add(new Boss());
				Boss.RendersItself = false;
			}

			Interlude.Spin(submessage: "Loading internal entities...");

			using (StaticSequentialProfiler.StartStackFrame("Setup Internal Ents")) {
				HologramPlayer.Visible = false;
				AutoPlayer = Add<AutoPlayer>();
				AutoPlayer.Enabled = args.Length == 0 ? false : (bool)args[0];
				TopPathway = Add<Pathway>(PathwaySide.Top);
				BottomPathway = Add<Pathway>(PathwaySide.Bottom);
				Interlude.Spin();

				Conductor = Add<Conductor>();
				Interlude.Spin();
			}

			Lua.State.Environment["conductor"] = new LuaConductor(Conductor);

			using (StaticSequentialProfiler.StartStackFrame("Load Enemies")) {
				if (Sheet != null) {
					if (!__deferringAsync) {
						foreach (var ent in Sheet.Entities)
							LoadEntity(ent);

						foreach (var ev in Sheet.Events)
							LoadEvent(ev);

						Entities.Sort((x, y) => (x is DashEnemy xE && y is DashEnemy yE) ? xE.GetJudgementHitTime().CompareTo(yE.GetJudgementHitTime()) : 0);
					}
				}
				Boss.Build();
			}
			Interlude.Spin(submessage: "Loading audio...");

			//foreach (var tempoChange in Sheet)
			if (Sheet != null) {
				foreach (var bpmChange in Sheet.TempoChanges) {
					Conductor.AddTempoChange(bpmChange.Time, bpmChange.Measure, bpmChange.BPM);
				}
			}
			else
				Conductor.AddTempoChange(0, 0, 120);

			using (StaticSequentialProfiler.StartStackFrame("Sheet.Song.GetAudioTrack()")) {
				if (Sheet != null) {
					Music = Sheet.Song.GetAudioTrack();
					Music.Loops = false;
					Music.Playing = true;
					if (args.Length > 1) {
						double measure = (double)args[1];
						Music.Playhead = (float)Conductor.MeasureToSeconds(measure);
					}
				}
				else
					Music = null;
			}
			Interlude.Spin(submessage: "Ready!");

			UIBar = this.UI.Add<CD_Player_UIBar>();
			UIBar.Size = new(0, 64);

			Scorebar = this.UI.Add<CD_Player_Scorebar>();
			Scorebar.Size = new(0, 128);

			if (CommandLine.GetParam("pretime", 5d) > 0)
				Scene.PlaySound(SceneSound.Begin, 0);
		}

		if (StaticSequentialProfiler.Profiling) {
			StaticSequentialProfiler.End(out var stack, out var accumulators);
			EngineCore.Interrupt(() => {
				Graphics2D.SetDrawColor(255, 255, 255);
				var lines = stack.ToStringArray();
				int y = 0;

				Graphics2D.DrawText(8, 8 + (y++ * 16), "Accumulators:", "Consolas", 15);
				for (int i = 0; i < accumulators.Count; i++, y++)
					Graphics2D.DrawText(8, 8 + (y * 16), $"  {accumulators[i].Key}: {accumulators[i].Value.Timer.Elapsed.TotalMilliseconds:F4} ms", "Consolas", 15);
				y++;
				Graphics2D.DrawText(8, 8 + (y++ * 16), "Stack:", "Consolas", 15);
				for (int i = 0; i < lines.Length; i++, y++)
					Graphics2D.DrawText(8, 8 + (y * 16), $"  {lines[i]}", "Consolas", 15);

			}, false);
		}

		MainThread.RunASAP(Interlude.End, ThreadExecutionTime.AfterFrame);
	}
	public bool Debug { get; set; } = true;
	public Panel PauseWindow { get; private set; }
	private bool lastNoteHit = false;

	public float GetPlayerY(double jumpRatio) {
		var height = EngineCore.GetWindowHeight();

		return (float)(NMath.Remap(jumpRatio, 0, 1, Game.Pathway.GetPathwayBottom(), Game.Pathway.GetPathwayTop())) + 225;
	}

	private SecondOrderSystem? sos_yoff;

	InputState inputState = new();
	public override void PreThink(ref FrameState frameState) {
		Ticks++;
		XPos = Game.Pathway.GetPathwayLeft();

		if (Music != null && lastNoteHit && Music.Paused && Sheet != null) {
			Stats.UploadScore(Score);
			EngineCore.LoadLevel(new StatisticsLevel(), Sheet, Stats);
			return;
		}

		if (ShouldExitFever && InFever)
			ExitFever();

		inputState.Reset();
		if (AutoPlayer.Enabled) {
			AutoPlayer.Play(ref inputState);
			foreach (ICloneDashInputSystem playerInput in InputReceivers)
				playerInput.Poll(ref frameState, ref inputState, InputAction.PauseGame);
		}
		else if (!IValidatable.IsValid(EngineCore.KeyboardFocusedElement)) {
			foreach (ICloneDashInputSystem playerInput in InputReceivers)
				playerInput.Poll(ref frameState, ref inputState);
		}

		InputState = inputState;

		if (InMashState)
			UpdateMashTextEffect();

		if (inputState.PauseButton) {
			if (Music != null && Music.Paused) {
				startUnpause();
				if (IValidatable.IsValid(PauseWindow))
					PauseWindow.Remove();
			}
			else {
				if (startPause()) {
					PauseWindow = this.UI.Add<Panel>();
					PauseWindow.Size = new(300, 400);
					PauseWindow.Center();

					var flex = PauseWindow.Add<FlexPanel>();
					flex.Dock = Dock.Fill;
					flex.Direction = Directional180.Vertical;
					flex.ChildrenResizingMode = FlexChildrenResizingMode.StretchToFit;
					flex.DockPadding = RectangleF.TLRB(4);

					var play = flex.Add<Button>();
					play.BorderSize = 0;
					play.Text = "Return to Game";
					play.TextSize = 24;
					play.Image = Textures.LoadTextureFromFile("ui/pause_play.png");
					play.ImageOrientation = ImageOrientation.Fit;
					play.MouseReleaseEvent += delegate (Element self, FrameState state, MouseButton clickedButton) {
						PauseWindow.Remove();
						startUnpause();
					};
					play.PaintOverride += Button_PaintOverride;

					var restart = flex.Add<Button>();
					restart.BorderSize = 0;
					restart.Text = "Restart Level";
					restart.TextSize = 24;
					restart.Image = Textures.LoadTextureFromFile("ui/pause_restart.png");
					restart.ImageOrientation = ImageOrientation.Fit;
					restart.MouseReleaseEvent += delegate (Element self, FrameState state, MouseButton clickedButton) {
						Interlude.Begin($"Reloading '{Sheet.Song.Name}'...");

						if (profilegameload.GetBool())
							StaticSequentialProfiler.Start();

						EngineCore.LoadLevel(new DashGameLevel(Sheet), AutoPlayer.Enabled);
					};
					restart.PaintOverride += Button_PaintOverride;

					var settings = flex.Add<Button>();
					settings.BorderSize = 0;
					settings.Text = "Open Preferences...";
					settings.TextSize = 24;
					settings.Image = Textures.LoadTextureFromFile("ui/pause_settings.png");
					settings.ImageOrientation = ImageOrientation.Fit;
					settings.MouseReleaseEvent += delegate (Element self, FrameState state, MouseButton clickedButton) {
						var panel = UI.Add<Panel>();
						panel.DrawPanelBackground = false;
						panel.Anchor = Anchor.Center;
						panel.Origin = Anchor.Center;
						panel.DynamicallySized = true;
						panel.Size = new(0.9f);

						var titlebar = panel.Add<Titlebar>();
						titlebar.Dock = Dock.Top;
						titlebar.MinimizeButton.Visible = false;
						titlebar.MaximizeButton.Visible = false;
						titlebar.CloseButton.MouseReleaseEvent += (_, _, _) => {
							panel.Remove();
						};
						titlebar.Title = "Settings";

						var settings = panel.Add<SettingsEditor>();
						settings.Dock = Dock.Fill;
						settings.DockMargin = RectangleF.TLRB(0, 8, 8, 0);

						panel.MakePopup();
					};
					settings.PaintOverride += Button_PaintOverride;

					var back2menu = flex.Add<Button>();
					back2menu.BorderSize = 0;
					back2menu.Text = "Exit to Menu";
					back2menu.TextSize = 24;
					back2menu.Image = Textures.LoadTextureFromFile("ui/pause_exit.png");
					back2menu.ImageOrientation = ImageOrientation.Fit;
					back2menu.MouseReleaseEvent += delegate (Element self, FrameState state, MouseButton clickedButton) {
						EngineCore.LoadLevel(new MainMenuLevel());
					};
					back2menu.PaintOverride += Button_PaintOverride;
				}
			}
			return;
		}

		float? yoff = null;

		bool holdingTop = Sustains.IsSustaining(PathwaySide.Top), holdingBottom = Sustains.IsSustaining(PathwaySide.Bottom);
		bool holding = holdingTop || holdingBottom;
		if ((holdingTop && holdingBottom) || InMashState)
			yoff = Game.Pathway.GetPathwayY(PathwaySide.Both);
		else if (holdingTop)
			yoff = Game.Pathway.GetPathwayY(PathwaySide.Top);
		else if (holdingBottom)
			yoff = Game.Pathway.GetPathwayY(PathwaySide.Bottom);

		if (yoff.HasValue) {
			if (sos_yoff == null)
				sos_yoff = new(15, 1, 1, yoff.Value);

			yoff = yoff.Value - (frameState.WindowHeight * -0.15f);
		}
		else
			sos_yoff = null;

		var playerY = yoff ?? GetPlayerY(CharacterYRatio);

		float conductorInTime = Conductor.PreStartTime <= 0 ? 1 : (float)NMath.Remap(Conductor.Time, -Conductor.PreStartTime, -Conductor.PreStartTime / 1.5f, 0, 1, clampInput: true);
		conductorInTime = 1 - NMath.Ease.OutQuad(conductorInTime);

		Player.Position = new Vector2F(
			(Game.Pathway.GetPathwayLeft() - 185) - (conductorInTime * frameState.WindowWidth / 2f),
			sos_yoff?.Update(playerY) ?? playerY
		);
		Player.Scale = new(PlayerScale);

		HologramPlayer.Position = new Vector2F(
			Game.Pathway.GetPathwayLeft() - 185,
			GetPlayerY(HologramCharacterYRatio)
		);

		HologramPlayer.Scale = new(PlayerScale);

		if (HologramPlayer.PlayingAnimation || HologramPlayer.AnimationQueued) {
			HologramPlayer.Visible = true;
			HologramPlayer.SetShaderUniform("time", (float)(Conductor.Time - lastHologramHitTime) * 2);
		}
		else {
			HologramPlayer.Visible = false;
		}

		VisibleEntities.Clear();

		foreach (var entity in Entities) {
			if (entity is Boss) continue;
			if (entity is not DashEnemy)
				continue;

			var entCD = entity as DashEnemy;
			// Visibility testing
			// ShouldDraw overrides ForceDraw here, which is intentional, although the naming convention is confusing and should be adjusted (maybe the names swapped?)
			if ((entCD.CheckVisTest(frameState) || entCD.ForceDraw) && entCD.ShouldDraw) {
				VisibleEntities.Add(entCD);

				if (entCD.Warns && !entCD.Dead && !InMashState)
					IsWarning = true;
			}
		}

		var lastEntity = (DashModelEntity)Entities.Last(x => x is DashEnemy);

		if (lastEntity.GetJudgementHitTime() + lastEntity.Length < Conductor.Time && !lastNoteHit) {
			lastNoteHit = true;
			if (Stats.CalculateFullCombo()) {
				Logs.Info("Full combo achieved.");
				Scene.PlaySound(SceneSound.FullCombo, 0);
			}
		}

		// Sort the visible entities by their hit time
		VisibleEntities.Sort((x, y) => x.GetJudgementHitTime().CompareTo(y.GetJudgementHitTime()));

		IterateEvents();

		//LockEntityBuffer();

		// Removes entities marked for removal safely
		foreach (var entity in Entities)
			if (entity is DashModelEntity && ((DashModelEntity)entity).MarkedForRemoval)
				Remove(entity);

		//UnlockEntityBuffer(); LockEntityBuffer();

		//foreach (var e in Events)
		//e.TryCall();

		// Resets the player animation state controller.
		// Does not reset any actively playing animations, just the internal state machines
		// used to determine when animations are triggered and on what.
		resetPlayerAnimState();

		// Start input processing.
		// Bottom is executed first, so if two pathway attacks happen on the same frame, it can exit the jump state
		// before jumping again, allowing the attack to work as expected
		HitLogic(PathwaySide.Bottom);
		HitLogic(PathwaySide.Top);

		// This loop is mostly for per-tick polls that need to occur, ie. when entities have been fully missed.
		// It is ran after input processing.
		foreach (var entity in VisibleEntities) {
			var timeToHit = entity.GetJudgementTimeUntilHit();
			switch (entity.Interactivity) {
				case EntityInteractivity.Hit:
				case EntityInteractivity.Sustain:
					if (!entity.Dead) {
						PathwaySide currentPathway = Pathway;

						// Is it too late for the player to hit this entity anyway?
						if (timeToHit < -entity.PreGreatRange
							&& !(entity is SustainBeam se && se.HeldState == true)
							&& !(entity is Masher me && me.Hits > 0)
						) {
							entity.Miss();
						}
					}
					break;
				case EntityInteractivity.SamePath:
					if (NMath.InRange(timeToHit, -entity.PreGreatRange, 0)) {
						PathwaySide pathCurrentCharacter = Pathway;
						if (pathCurrentCharacter == PathwaySide.Both || pathCurrentCharacter == entity.Pathway && entity.Hits == 0) {
							entity.Hit(pathCurrentCharacter, 0);
							Scene.PlaySound(entity.Type switch {
								EntityType.Heart => SceneSound.HP,
								EntityType.Score => SceneSound.Score,
								_ => SceneSound.Score
							}, 1);
						}
					}
					break;
				case EntityInteractivity.Avoid:
					// Checks if the player has completely failed to avoid the entity, and if so, damages the player.

					if (Pathway == entity.Pathway && timeToHit < -entity.PrePerfectRange && !entity.DidRewardPlayer) {
						//entity.Hit(Game.PlayerController.Pathway);
						entity.DamagePlayer();
					}

					// If the player is now avoiding the entity, then reward the player for missing it, and make it so they cant be damaged by it)
					if (Pathway != entity.Pathway && timeToHit < 0 && !entity.DidDamagePlayer) {
						entity.Pass();
					}

					break;
			}
			//entity.WhenVisible();
		}

		FrameDebuggingStrings.Add($"HoldingTopPathwaySustain {Sustains.GetSustainsActiveCount(PathwaySide.Top)}");
		FrameDebuggingStrings.Add($"HoldingBottomPathwaySustain {Sustains.GetSustainsActiveCount(PathwaySide.Top)}");

		Scene.Think(this);
		if (InFever)
			FeverFX?.Think(this);
	}

	private void Button_PaintOverride(Element self, float width, float height) {
		Button b = self as Button;
		var backpre = self.BackgroundColor;

		var back = Element.MixColorBasedOnMouseState(self, backpre, new(0, 0.8f, 2.4f, 1f), new(0, 1.2f, 0.6f, 1f));
		var fore = Element.MixColorBasedOnMouseState(self, self.ForegroundColor, new(0, 0.8f, 1.8f, 1f), new(0, 1.2f, 0.6f, 1f));

		Graphics2D.SetDrawColor(back);
		Graphics2D.DrawRectangle(0, 0, width, height);
		var text = b.Text;
		var tSize = Graphics2D.GetTextSize(text, b.Font, b.TextSize);
		b.ImageDrawing(new((height / -4) + (tSize.X / -2), 0), new(height, height));
		Graphics2D.SetDrawColor(255, 255, 255);
		Graphics2D.DrawText(new((width / 2) + (height / 4), height / 2), text, b.Font, b.TextSize, Anchor.Center);
	}

	public override void Think(FrameState frameState) {

	}
	public override void PostThink(FrameState frameState) {
		// Perform player animation. Things in HitLogic will trigger certain flags, which will result in 
		// specific animations being played
		determinePlayerAnimationState();
	}
	/// <summary>
	/// Gets the games <see cref="Pathway"/> from a <see cref="PathwaySide"/><br></br>
	/// Note: If strict is off (default), it will return the bottom pathway if PathwaySide.Middle is passed, otherwise it will throw an exception.
	/// </summary>
	/// <param name="pathway"></param>
	/// <param name="strict"></param>
	/// <returns></returns>
	/// <exception cref="ArgumentException"></exception>
	public Pathway GetPathway(PathwaySide pathway, bool strict = false) {
		switch (pathway) {
			case PathwaySide.Top:
				return TopPathway;
			case PathwaySide.Bottom:
				return BottomPathway;
			case PathwaySide.Both:
				if (strict)
					break;
				else
					return BottomPathway;
			case PathwaySide.None:
				break;
		}

		throw new ArgumentException("pathway");
	}

	public Pathway GetPathway(DashModelEntity ent) => GetPathway(ent.Pathway);

	/// <summary>
	/// Creates an entity from a C# type and adds it to <see cref="GameplayManager.Entities"/>.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8604 // Possible null reference argument.
	public T CreateEntity<T>() where T : DashModelEntity => (T)Add((T)Activator.CreateInstance(typeof(T)));
#pragma warning restore CS8604 // Possible null reference argument.
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

	/// <summary>
	/// Creates an event from an EventType enumeration, and adds it to <see cref="GameplayManager.Events"/>.
	/// </summary>
	/// <param name="t"></param>
	/// <returns></returns>
	/*public CD_BaseEvent AddEvent(EventType t) {
            CD_BaseEvent e = CD_BaseEvent.CreateFromType(this.Game, t);
            return e;
        }*/

	public PollResult LastPollResult { get; private set; } = PollResult.Empty;
	/// <summary>
	/// Polling function which figures out the closest, potentially-hit entity and returns the result.
	/// </summary>
	/// <param name="pathway"></param>
	/// <returns>A <see cref="PollResult"/>, if it hit something, Hit is true, and vice versa.</returns>
	public PollResult Poll(PathwaySide pathway) {
		foreach (DashEnemy entity in VisibleEntities) {
			// If the entity has no interactivity, ignore it in the poll
			if (!entity.Interactive)
				continue;

			// If the entity says its dead, ignore it
			if (entity.Dead)
				continue;

			switch (entity.Interactivity) {
				case EntityInteractivity.Hit:
				case EntityInteractivity.Sustain:
					bool isValidHit = entity.Interactivity == EntityInteractivity.Hit ? !entity.Dead : !(entity as SustainBeam).HeldState;
					if (isValidHit && Game.Pathway.ComparePathwayType(entity.Pathway, pathway)) {
						double distance = entity.GetJudgementTimeUntilHit();
						double pregreat = -entity.PreGreatRange, postgreat = entity.PostGreatRange;
						double preperfect = -entity.PrePerfectRange, postperfect = entity.PostPerfectRange;
						if (NMath.InRange(distance, pregreat, postgreat)) { // hit occured
							var greatness = (NMath.InRange(distance, preperfect, postperfect) ? "PERFECT" : "GREAT") + " " + Math.Round(distance * 1000, 1) + "ms";
							LastPollResult = PollResult.Create(entity, distance, greatness);
							return LastPollResult;
						}
					}
					break;
			}
		}

		LastPollResult = PollResult.Empty;
		return LastPollResult;
	}

	/// <summary>
	/// Spawns a <see cref="TextEffect"/> into the game and adds it to the game.
	/// </summary>
	/// <param name="text">The text</param>
	/// <param name="position">Where it spawns (it will rise upwards after being spawned)</param>
	/// <param name="color">The color of the text</param>
	public TextEffect SpawnTextEffect(string text, Vector2F position, TextEffectTransitionOut transitionOut = TextEffectTransitionOut.SlideUp, Color? color = null) {
		if (color == null)
			color = new Color(255, 255, 255, 255);

		return Add(new TextEffect(text, position, transitionOut, color.Value));
	}

	public List<DashEvent> Events = [];
	public HashSet<DashEvent> ActiveEvents = [];
	public HashSet<DashEvent> HandledEvents = [];

	private bool shouldActivateEvent(DashEvent ev) => ev.TriggerType switch {
		EventTriggerType.AtTimeMinusLength => Conductor.Time >= (ev.Time - ev.Length),
		EventTriggerType.AtTime => Conductor.Time >= ev.Time,
		_ => false
	};
	private bool shouldDeactivateEvent(DashEvent ev) => ev.TriggerType switch {
		EventTriggerType.AtTimeMinusLength => Conductor.Time >= ev.Time,
		EventTriggerType.AtTime => Conductor.Time >= (ev.Time + ev.Length),
		_ => false
	};

	public void IterateEvents() {
		foreach (var ev in Events) {
			if (ActiveEvents.Contains(ev)) {
				// Determine if the event needs to be deactivated
				if (shouldDeactivateEvent(ev)) {
					HandledEvents.Add(ev);
					ActiveEvents.Remove(ev);
					ev.Deactivate();
					Logs.Debug($"Deactivating {ev.GetType().Name}");
				}
			}
			else if (!HandledEvents.Contains(ev)) {
				// Determine if the event needs to be activated
				if (shouldActivateEvent(ev)) {
					ActiveEvents.Add(ev);
					ev.Activate();
					Logs.Debug($"Activating {ev.GetType().Name}");
				}
			}
			// The event has both been activated and deactivated, so its ignored
		}
	}

	/// <summary>
	/// Loads an event from a <see cref="ChartEvent"/> representation, builds a <see cref="MapEvent"/> out of it, and adds it to  <see cref="GameplayManager.Events"/>.
	/// </summary>
	/// <param name="ChartEvent"></param>
	public void LoadEvent(ChartEvent ChartEvent) {
		Interlude.Spin(submessage: "Loading events...");
		var ev = DashEvent.CreateFromType(this, ChartEvent.Type);

		ev.Time = ChartEvent.Time;
		ev.Length = ChartEvent.Length;

		ev.Score = ChartEvent.Score;
		ev.Fever = ChartEvent.Fever;
		ev.Damage = ChartEvent.Damage;
		ev.BossAction = ChartEvent.BossAction;

		ev.Build();

		Events.Add(ev);
	}

	/// <summary>
	/// Loads an entity from a <see cref="ChartEntity"/> representation, builds a <see cref="MapEntity"/> out of it, and adds it to <see cref="GameplayManager.Entities"/>.
	/// </summary>
	/// <param name="ChartEntity"></param>
	public void LoadEntity(ChartEntity ChartEntity) {
		Interlude.Spin(submessage: "Loading entities...");

		if (!DashEnemy.TryCreateFromType(this, ChartEntity.Type, out DashEnemy? ent)) {
			Console.WriteLine("No load entity handler for type " + ChartEntity.Type);
			return;
		}

		ent.Pathway = ChartEntity.Pathway;
		ent.EnterDirection = ChartEntity.EnterDirection;
		ent.Variant = ChartEntity.Variant;

		ent.HitTime = ChartEntity.HitTime;
		ent.ShowTime = ChartEntity.ShowTime;
		ent.Length = ChartEntity.Length;
		ent.Speed = ChartEntity.Speed;
		ent.Flipped = ChartEntity.Flipped;
		ent.Blood = ChartEntity.Blood;

		ent.FeverGiven = ChartEntity.Fever;
		ent.DamageTaken = ChartEntity.Damage;
		ent.ScoreGiven = ChartEntity.Score;
		ent.HealthGiven = ChartEntity.Health;

		ent.RelatedToBoss = ChartEntity.RelatedToBoss;

		ent.RendersItself = false;
		ent.DebuggingInfo = ChartEntity.DebuggingInfo;

		Stats.RegisterEnemy(ent);

		ent.Build();
	}


	public float PlayerScale => 1;
	public float PlayScale { get; set; } = 1.2f;
	public float GlobalScale => 1f;
	public float BackgroundScale => 1f;

	public override void PreRenderBackground(FrameState frameState) {
		Boss.Scale = new(GlobalScale);
		Boss.Position = new(0, 450);
	}

	public override void PreRender(FrameState frameState) {
		base.PreRender(frameState);
		//Stopwatch test = Stopwatch.StartNew();
		Rlgl.PushMatrix();
		Rlgl.Scalef(BackgroundScale, BackgroundScale, 1);

		Scene.RenderBackground(this);
		if (InFever)
			FeverFX?.Render(this);

		Rlgl.PopMatrix();
		//Logs.Info(test.Elapsed.TotalMilliseconds);
	}

	public override void CalcView2D(FrameState frameState, ref Camera2D cam) {
		var zoomValue = MashZoomSOS.Update(InMashState ? 1 : 0) * .5f;
		cam.Zoom = ((frameState.WindowHeight / 900 / 2) * PlayScale) + (zoomValue / 5f);
		cam.Rotation = 0.0f;
		cam.Offset = new(frameState.WindowWidth / 2, frameState.WindowHeight / 2);
		cam.Target = new(frameState.WindowWidth / 1 * zoomValue, 0);
		cam.Offset += cam.Target;

		//cam.Offset = new(frameState.WindowWidth * Game.Pathway.PATHWAY_LEFT_PERCENTAGE * .5f, frameState.WindowHeight * 0.5f);
		//cam.Target = cam.Offset;
	}
	public void ConditionallyRenderVisibleEntities(FrameState frameState, Predicate<DashEnemy> enemyPredicate) {
		foreach (Entity ent in VisibleEntities) {
			if (ent is not DashEnemy entCD) continue;
			if (!enemyPredicate(entCD)) continue;

			float yPosition = Game.Pathway.GetPathwayY(entCD.Pathway);

			Graphics2D.SetDrawColor(255, 255, 255);
			var p = new Vector2F((float)entCD.XPos, yPosition);
			; // Calculate the final beat position on the track
			  //entCD.ChangePosition(ref p); // Allow the entity to modify the position before it goes to the renderer
			  //ent.Position = p;
			ent.Render(frameState);
			Rlgl.DrawRenderBatchActive();
		}
	}
	public override void Render(FrameState frameState) {
		Rlgl.DisableDepthTest();
		Rlgl.DisableBackfaceCulling();

		//Raylib.DrawLineV(new(-100000, 0), new(100000, 0), Color.Red);
		//Raylib.DrawLineV(new(0, -100000), new(0, 100000), Color.Green);
		Rlgl.DrawRenderBatchActive();

		// Pathways
		TopPathway.Render();
		BottomPathway.Render();

		// Hold notes
		ConditionallyRenderVisibleEntities(frameState, x => x.Type == EntityType.SustainBeam);

		// Boss
		Boss.Render();

		// The other entities, that aren't sustain beams, in order of top -> bottom pathway
		ConditionallyRenderVisibleEntities(frameState, x => x.Type != EntityType.SustainBeam && x.Pathway == PathwaySide.Top);
		ConditionallyRenderVisibleEntities(frameState, x => x.Type != EntityType.SustainBeam && x.Pathway == PathwaySide.Bottom);

		FrameDebuggingStrings.Add("Visible Entities: " + VisibleEntities.Count);
		FrameDebuggingStrings.Add($"Player Animation: {Player.Animations.Channels[0].CurrentEntry?.Animation?.Name ?? "<null>"}");
		FrameDebuggingStrings.Add($"Hologram-Player Animation: {HologramPlayer.Animations.Channels[0].CurrentEntry?.Animation?.Name ?? "<null>"}");
		FrameDebuggingStrings.Add($"Player Y: {CharacterYRatio}");
		FrameDebuggingStrings.Add($"Hologram-Player Y: {HologramCharacterYRatio}");
	}

	public override void Render2D(FrameState frameState) {
		base.Render2D(frameState);

		foreach (Entity ent in VisibleEntities) {
			if (ent is not DashEnemy)
				continue;

			var entCD = (DashEnemy)ent;
			//Graphics2D.DrawText(ent.Position, entCD.DebuggingInfo, "Consolas", 20);
		}
	}

	/// <summary>
	/// Currently visible entities this tick
	/// </summary>
	public List<DashEnemy> VisibleEntities { get; private set; } = [];

	private double LastAttackTime;
	private PathwaySide LastAttackPathway;

	public void BroadcastEntitySignal(Entity entityFrom, EntitySignalType signalType, object? data = null) {
		if (entityFrom is not DashModelEntity mentFrom) return;

		foreach (var entity in Entities) {
			if (entity is not DashModelEntity ment) continue;
			ment.OnSignalReceived(mentFrom, signalType, data);
		}
	}
	public void SendEntitySignal(Entity entityFrom, Entity entityTo, EntitySignalType signalType, object? data = null) {
		if (entityFrom is not DashModelEntity mentFrom) return;

		if (entityTo is not DashModelEntity ment) return;
		ment.OnSignalReceived(mentFrom, signalType, data);
	}

	private void HitLogic(PathwaySide pathway) {
		int amountOfTimesHit = pathway == PathwaySide.Top ? InputState.TopClicked : InputState.BottomClicked;
		bool keyHitOnThisSide = amountOfTimesHit > 0;

		if (!keyHitOnThisSide)
			return;

		for (int i = 0; i < amountOfTimesHit; i++) {
			EnterHitState();

			LastAttackTime = Conductor.Time;
			LastAttackPathway = pathway;

			// Hit testing
			PollResult? pollResult = null;
			if (InMashState) {
				//if (Debug)
				//Console.WriteLine($"mashing entity = {MashingEntity}");

				MashingEntity.Hit(pathway, 0);
			}
			else {
				var poll = Poll(pathway);
				pollResult = poll;

				if (poll.Hit) {
					poll.HitEntity.WasHitPerfect = poll.IsPerfect;
					poll.HitEntity.Hit(pathway, poll.DistanceToHit);

					if (SuppressHitMessages == false) {
						Color c = poll.HitEntity.HitColor;
						SpawnTextEffect(poll.Greatness, GetPathway(pathway).Position, TextEffectTransitionOut.SlideUp, c);
						Scene.PlaySound(poll.HitEntity.Type switch {
							EntityType.Single => poll.HitEntity.Variant switch {
								EntityVariant.Small => SceneSound.Quiet,
								EntityVariant.Medium1 => SceneSound.Medium1,
								EntityVariant.Medium2 => SceneSound.Medium2,
								EntityVariant.Large1 => SceneSound.Loud1,
								EntityVariant.Large2 => SceneSound.Loud2,
								EntityVariant.Boss1 => SceneSound.Quiet,
								EntityVariant.Boss2 => SceneSound.Medium1,
								EntityVariant.Boss3 => SceneSound.Medium2,
								_ => SceneSound.Medium1
							},
							EntityType.SustainBeam => SceneSound.PressTop,
							EntityType.Raider => SceneSound.Loud1,
							_ => SceneSound.Medium1
						}, 1);
					}
				}
			}

			// Trigger animation events on the player controller
			var hitSomething = pollResult.HasValue && pollResult.Value.Hit;
			if (pathway == PathwaySide.Top)
				AttackAir(pollResult ?? default);
			else
				AttackGround(pollResult ?? default);

			ExitHitState();

			//if (Debug)
			//Console.WriteLine($"poll.Hit = {hitSomething}, entity = {((pollResult.HasValue && pollResult.Value.Hit) ? pollResult.Value.HitEntity.ToString() : "NULL")}");
		}
	}

	/// <summary>
	/// Current health of the player<br></br>
	/// Default: 250
	/// </summary>
	public float Health { get; private set; }

	/// <summary>
	/// Maximum health the player can have, the player will have this much health on spawn<br></br>
	/// Default: 250
	/// </summary>
	public float MaxHealth { get; set; } = 250;

	/// <summary>
	/// How much health does the player lose every second?<br></br>
	/// Default: 0
	/// </summary>
	public float HealthDrain { get; set; } = 0;
	/// <summary>
	/// Current fever bar.<br></br>
	/// Default: 0
	/// </summary>
	public float Fever { get; private set; } = 0;

	/// <summary>
	/// How much fever needs to be obtained until entering fever state<br></br>
	/// Default: 120
	/// </summary>
	public float MaxFever { get; set; } = 120;

	/// <summary>
	/// How much fever, in seconds, does a full fever bar provide?<br></br>
	/// Default: 6
	/// </summary>
	public float FeverTime { get; set; } = 6;
	/// <summary>
	/// Is the player currently in fever?
	/// </summary>
	public bool InFever { get; private set; } = false;
	/// <summary>
	/// When did the fever start?
	/// </summary>
	public double WhenDidFeverStart { get; private set; } = -1000000d;
	/// <summary>
	/// Should the player exit fever?
	/// </summary>
	private bool ShouldExitFever => (Conductor.Time - WhenDidFeverStart) >= FeverTime;
	/// <summary>
	/// How much fever time is left?
	/// </summary>
	public double FeverTimeLeft => FeverTime - (Conductor.Time - WhenDidFeverStart);
	/// <summary>
	/// Returns the fever time left as a value of 0-1, where 0 is the end and 1 is the start. Good for animation.
	/// </summary>
	private double FeverRatio => 1f - ((Conductor.Time - WhenDidFeverStart) / FeverTime);
	/// <summary>
	/// Current score of the player.
	/// </summary>
	public int Score { get; private set; } = 0;
	/// <summary>
	/// Which entity is being held on the top pathway
	/// </summary>
	//public CD_BaseMEntity? HoldingTopPathwaySustain { get; private set; } = null;
	/// <summary>
	/// Which entity is being held on the bottom pathway
	/// </summary>
	//public CD_BaseMEntity? HoldingBottomPathwaySustain { get; private set; } = null;


	/// <summary>
	/// Is the player in the air right now?
	/// </summary>
	public bool InAir => Conductor.Time - __whenjump < __jumpmax;

	public double AirTime => (Conductor.Time - __whenjump);
	public double TimeToAnimationEnds => __jumpAnimationStops - (Conductor.Time - __whenjump);

	public double Hologram_AirTime => (Conductor.Time - __whenHjump);
	public double Hologram_TimeToAnimationEnds => __jumpAnimationHStops - (Conductor.Time - __whenHjump);

	public ISustainManager Sustains = new StackBasedSustainManager();

	/// <summary>
	/// Can the player jump right now?
	/// </summary>
	public bool CanJump => !InAir;

	private double __jumpmax = 0.5d;
	private double __jumpAnimationStops = 0.5d;
	private double __jumpAnimationHStops = 0.5d;
	private double __whenjump = -2000000000000d;
	private double __whenHjump = -2000000000000d;

	public void Heal(float health) {
		Health = Math.Clamp(Health + health, 0, MaxHealth);
	}

	/// <summary>
	/// Damage the player.
	/// </summary>
	/// <param name="entity"></param>
	/// <param name="damage"></param>
	public void Damage(DashModelEntity? entity, float damage) {
		Health -= damage;
		ResetCombo();
	}
	public double LastFeverIncreaseTime { get; private set; } = -2000;
	/// <summary>
	/// Adds to the players fever value, and automatically enters fever when the player has maxed out the fever bar.
	/// </summary>
	/// <param name="fever"></param>
	public void AddFever(float fever) {
		if (InFever)
			return;

		Fever = Math.Clamp(Fever + fever, 0, MaxFever);
		LastFeverIncreaseTime = Conductor.Time;
		if (Fever >= MaxFever)
			EnterFever();
	}
	/// <summary>
	/// Enters fever.
	/// </summary>
	private void EnterFever() {
		InFever = true;
		WhenDidFeverStart = Conductor.Time;
		FeverFX?.Start(this);
		Scene.PlaySound(SceneSound.Fever, 0);
	}
	/// <summary>
	/// Exits fever.
	/// </summary>
	private void ExitFever() {
		InFever = false;
		Fever = 0;
		WhenDidFeverStart = -1000000d;
	}
	/// <summary>
	/// Adds 1 to the players combo.
	/// </summary>
	public void AddCombo() {
		Combo++;
		__lastCombo = Conductor.Time;
	}

	/// <summary>
	/// Resets the players combo.
	/// </summary>
	public void ResetCombo() => Combo = 0;
	/// <summary>
	/// Adds to the players score.
	/// </summary>
	/// <param name="score"></param>
	public void AddScore(int score) {
		float s = (float)score;
		Score += (int)s;
	}
	/// <summary>
	/// Removes from the players score.
	/// </summary>
	/// <param name="score"></param>
	public void RemoveScore(int score) => Score -= score;

	/// <summary>
	/// This is a callback for <see cref="ISustainManager"/> implementations. It expects wasSustaining, inSustaining, and sustainCount to be relative to the current pathway.
	/// </summary>
	/// <param name="sustain"></param>
	/// <param name="pathway"></param>
	/// <param name="wasSustainingBefore"></param>
	/// <param name="isSustainingNow"></param>
	/// <param name="sustainCount"></param>
	public void OnSustainCallback(SustainBeam sustain, PathwaySide pathway, bool wasSustainingBefore, bool isSustainingNow, int sustainCount) {
		bool nowInsustain = Sustains.ActiveSustains() > 0;

		playeranim_startsustain = !playeranim_insustain && nowInsustain;
		playeranim_endsustain = playeranim_insustain && !nowInsustain;
		if (pathway == PathwaySide.Top)
			playeranim_startsustain_top = !wasSustainingBefore && isSustainingNow;
		else
			playeranim_startsustain_bottom = !wasSustainingBefore && isSustainingNow;

		playeranim_startsustain = playeranim_startsustain || (playeranim_startsustain_top && playeranim_startsustain_bottom);

		playeranim_insustain = nowInsustain;

		if (pressIdle != null) {
			if (pressIdle.Playing && !nowInsustain)
				pressIdle.Playing = false;
			else if (!pressIdle.Playing && nowInsustain)
				pressIdle.Playing = true;
		}
	}

	MusicTrack? pressIdle;

	public delegate void AttackEvent(DashGameLevel game, PathwaySide side);
	public event AttackEvent? OnAirAttack;
	public event AttackEvent? OnGroundAttack;

	public float CharacterYRatio => (float)Math.Clamp(NMath.Ease.OutExpo(TimeToAnimationEnds * 10), 0, 1);
	public float HologramCharacterYRatio => (float)Math.Clamp(NMath.Ease.OutExpo(Hologram_TimeToAnimationEnds * 10), 0, 1);


	private bool playeranim_miss = false;
	private bool playeranim_jump = false;
	private bool playeranim_attackair = false;
	private bool playeranim_attackground = false;
	private bool playeranim_attackdouble = false;

	private bool playeranim_perfect = false;

	private bool playeranim_startsustain = false;
	private bool playeranim_startsustain_top = false;
	private bool playeranim_startsustain_bottom = false;
	private bool playeranim_insustain = false;
	private bool playeranim_endsustain = false;

	private void resetPlayerAnimState() {
		playeranim_miss = false;
		playeranim_jump = false;

		playeranim_attackair = false;
		playeranim_attackground = false;
		playeranim_attackdouble = false;

		playeranim_perfect = false;

		playeranim_startsustain = false;
		playeranim_startsustain_top = false;
		playeranim_startsustain_bottom = false;
		playeranim_endsustain = false;
		playeranim_insustain = Sustains.IsSustaining();
	}
	private double lastHologramHitTime = -20000;
	private void logTests(string testStr) {
		//Logs.Debug($"PlayerAnimationState: {testStr}");
	}
	private void DrawPlayerState() {
		string[] lines = [
			$"__whenjump:                     {__whenjump}",
				$"__whenHjump:                    {__whenHjump}",
				$"playeranim_miss:                {playeranim_miss}",
				$"playeranim_jump:                {playeranim_jump}",
				$"playeranim_attackair:           {playeranim_attackair}",
				$"playeranim_attackground:        {playeranim_attackground}",
				$"playeranim_attackdouble:        {playeranim_attackdouble}",
				$"playeranim_perfect:             {playeranim_perfect}",
				$"playeranim_startsustain:        {playeranim_startsustain}",
				$"playeranim_startsustain_top:    {playeranim_startsustain_top}",
				$"playeranim_startsustain_bottom: {playeranim_startsustain_bottom}",
				$"playeranim_insustain:           {playeranim_insustain}",
				$"playeranim_endsustain:          {playeranim_endsustain}"
		];

		int y = 0;
		Graphics2D.SetDrawColor(255, 255, 255);
		foreach (var line in lines) {
			Graphics2D.DrawText(8, 8 + y, line, "Consolas", 14);
			y += 14;
		}
	}
	private void determinePlayerAnimationState() {
		ModelEntity playerTarget;
		bool suppress_hologram = false;
		// Call end sustain animation. But only if we're ending a sustain and not starting a new one immediately
		if (playeranim_endsustain && !playeranim_startsustain) {
			PlayerAnim_ExitSustain();
			logTests("Exiting sustain.");
		}
		else if (playeranim_endsustain && playeranim_startsustain) {
			// Suppress any hologram animations.
			logTests("Suppressing further hologram animations.");
			suppress_hologram = true;
		}

		if (playeranim_attackdouble) {
			PlayerAnim_ForceAttackDouble(Player);
			PlayerAnim_EnqueueRun(Player);

			resetPlayerAnimState();
			logTests("Double attack");
			__whenjump = -2000000000;
			__whenHjump = -2000000000;
			return;
		}

		if (playeranim_startsustain) {
			PlayerAnim_EnterSustain();

			logTests("Sustain started");

			if (playeranim_startsustain_bottom && playeranim_startsustain_top) {
				resetPlayerAnimState();
				logTests("Not allowing further animation; both sustains pressed at once!");
				return;
			}

			if (!suppress_hologram && playeranim_attackair && playeranim_startsustain_bottom && !playeranim_startsustain_top) {
				// Hologram player attacks the air, while the player starts the bottom sustain.
				PlayerAnim_ForceAttackAir(HologramPlayer, playeranim_perfect);
				//EngineCore.Interrupt(() => DrawPlayerState(), false);
			}

			if (!suppress_hologram && playeranim_attackground && playeranim_startsustain_top && !playeranim_startsustain_bottom) {
				// Hologram player attacks the ground, while the player starts the top sustain.
				PlayerAnim_ForceAttackGround(HologramPlayer, playeranim_perfect);
			}

			resetPlayerAnimState();
			return;
		}

		if (!suppress_hologram && playeranim_insustain) { // We use the same things for playeranim_attack etc, but redirect it to the hologram player
			playerTarget = HologramPlayer;

			if (playeranim_startsustain_top || playeranim_startsustain_bottom) {
				resetPlayerAnimState();
				return;
			}

			if (playeranim_attackair || playeranim_attackground) {
				lastHologramHitTime = Conductor.Time;
				Logs.Info("Setting last hologram hit time");
			}

			if (playeranim_attackair) {
				PlayerAnim_ForceAttackAir(HologramPlayer, playeranim_perfect);
				__whenHjump = Conductor.Time;
				//EngineCore.Interrupt(() => DrawPlayerState(), false);
			}
			else if (playeranim_attackground) {
				PlayerAnim_ForceAttackGround(HologramPlayer, playeranim_perfect);
				//EngineCore.Interrupt(() => DrawPlayerState(), false);
				__whenHjump = -2000000000000d;
			}
		}
		else {
			playerTarget = Player;
			if (playeranim_attackair) {
				PlayerAnim_ForceAttackAir(Player, playeranim_perfect);
				__whenjump = Conductor.Time;
				PlayerAnim_EnqueueRun(Player);
			}
			else if (playeranim_attackground) {
				PlayerAnim_ForceAttackGround(Player, playeranim_perfect);
				__whenjump = -2000000000000d;
				PlayerAnim_EnqueueRun(Player);
			}
			else if (playeranim_jump) {
				PlayerAnim_ForceJump(Player);
				__whenjump = Conductor.Time;
			}
			else if (playeranim_miss) {
				__whenjump = -2000000000000d;
				PlayerAnim_ForceMiss(Player);
			}
		}

		/*FrameDebuggingStrings.Add($"PlayerAnimState:");

		FrameDebuggingStrings.Add($"    playeranim_miss                 : {playeranim_miss}");
		FrameDebuggingStrings.Add($"    playeranim_jump                 : {playeranim_jump}");
		FrameDebuggingStrings.Add($"    playeranim_attackair            : {playeranim_attackair}");
		FrameDebuggingStrings.Add($"    playeranim_attackground         : {playeranim_attackground}");
		FrameDebuggingStrings.Add($"    playeranim_attackdouble         : {playeranim_attackdouble}");
		FrameDebuggingStrings.Add($"    playeranim_perfect              : {playeranim_perfect}");
		FrameDebuggingStrings.Add($"    playeranim_startsustain         : {playeranim_startsustain}");
		FrameDebuggingStrings.Add($"    playeranim_startsustain_top     : {playeranim_startsustain_top}");
		FrameDebuggingStrings.Add($"    playeranim_startsustain_bottom  : {playeranim_startsustain_bottom}");
		FrameDebuggingStrings.Add($"    playeranim_insustain            : {playeranim_insustain}");
		FrameDebuggingStrings.Add($"    playeranim_endsustain           : {playeranim_endsustain}");*/

		resetPlayerAnimState();
	}

	public bool AttackAir(PollResult result) {
		if (InMashState) {
			playeranim_attackair = true;
			playeranim_perfect = true;

			OnAirAttack?.Invoke(this, PathwaySide.Top);
			return true;
		}

		if (CanJump || result.Hit) {
			var isDHE = result.Hit && result.HitEntity is DoubleHitEnemy;
			playeranim_attackdouble = isDHE;
			playeranim_attackair = result.Hit && !isDHE;
			playeranim_jump = CanJump;
			playeranim_perfect |= result.IsPerfect;

			OnAirAttack?.Invoke(this, PathwaySide.Top);

			return true;
		}

		return false;
	}

	public void AttackGround(PollResult result) {
		if (InMashState) {
			playeranim_attackdouble = true;
			playeranim_perfect = true;
			OnGroundAttack?.Invoke(this, PathwaySide.Bottom);
			return;
		}

		if (result.Hit) {
			if (result.HitEntity is DoubleHitEnemy) {
				playeranim_attackdouble = true;
				playeranim_miss = false;
			}
			else {
				playeranim_attackground = true;
				playeranim_miss = false;
			}
		}
		else {
			playeranim_attackground = false;
			playeranim_miss = true;
		}

		playeranim_perfect |= result.IsPerfect;
		OnGroundAttack?.Invoke(this, PathwaySide.Bottom);
	}
	/// <summary>
	/// Gets the current pathway the player is on. Returns Top if jumping, else bottom.
	/// </summary>
	public PathwaySide Pathway {
		get {
			var state = Sustains.GetSustainState();
			if (state == PathwaySide.None) return InAir ? PathwaySide.Top : PathwaySide.Bottom;
			return state;
		}
	}

	public bool CanHit(PathwaySide pathway) {
		if (Sustains.IsSustaining(pathway))
			return false;

		if (pathway == PathwaySide.Top && InAir)
			return false;

		return true;
	}

	/// <summary>
	/// Current combo of the player (how many successful hits/avoids in a row)
	/// </summary>
	public int Combo { get; private set; } = 0;
	private double __lastCombo = -2000; // Last time a combo occured in game-time

	public double LastCombo => __lastCombo;

	internal CD_Player_UIBar UIBar;
	internal class CD_Player_UIBar : Element
	{
		public CD_Player_UIBar() {

		}
		protected override void Initialize() {
			base.Initialize();
			Dock = Dock.Bottom;
		}
		public override void Paint(float width, float height) {
			var lvl = Level.As<DashGameLevel>();

			var startAtX = width / 4f;
			var totalW = width / 2f;
			var endAtX = startAtX + totalW;

			Graphics2D.ScissorRect(RectangleF.XYWH(startAtX, 0, endAtX, height));

			Graphics2D.SetDrawColor(255, 60, 42);
			Graphics2D.DrawRectangle(width / 4f, 0, (width / 2f) * (lvl.Health / lvl.MaxHealth), 24);
			Graphics2D.SetDrawColor(255 / 2, 60 / 2, 42 / 2);
			Graphics2D.DrawRectangleOutline(width / 4f, 0, (width / 2f), 24, 2);
			Graphics2D.SetDrawColor(255, 220, 200);
			Graphics2D.DrawText(width / 2f, 12, $"HP: {lvl.Health}/{lvl.MaxHealth}", "Noto Sans", 22, Anchor.Center);
			float feverRatio;
			if (lvl.InFever)
				feverRatio = (float)lvl.FeverTimeLeft / lvl.FeverTime;
			else
				feverRatio = (float)lvl.Fever / lvl.MaxFever;

			var lastTimeHit = lvl.LastFeverIncreaseTime;

			Graphics2D.SetDrawColor(72, 160, 255);
			Graphics2D.DrawRectangle(width / 4f, 32, (width / 2f) * feverRatio, 24);

			// when hit gradient
			var gradSize = 48;
			var gradColor = new Color(162, 220, 255, (int)(float)NMath.Remap(lvl.Conductor.Time, lastTimeHit, lastTimeHit + .2f, 255, 0, clampOutput: true));
			Graphics2D.DrawGradient(new(startAtX + ((width / 2f) * feverRatio) - gradSize, 33), new(gradSize, 24 - 2), gradColor, new(gradColor.R, gradColor.G, gradColor.B, (byte)0), Dock.Left);



			Graphics2D.SetDrawColor(72 / 2, 160 / 2, 255 / 2);
			Graphics2D.DrawRectangleOutline(startAtX, 32, (width / 2f), 24, 2);
			Graphics2D.SetDrawColor(200, 220, 255);
			Graphics2D.DrawText(width / 2f, 32 + 12, lvl.InFever ? $"FEVER! {Math.Round(lvl.FeverTimeLeft, 2):0.00}s remaining" : $"FEVER: {Math.Round((lvl.Fever / lvl.MaxFever) * 100)}%", "Noto Sans", 22, Anchor.Center);

			Graphics2D.ScissorRect();
		}
	}

	internal CD_Player_Scorebar Scorebar;
	internal class CD_Player_Scorebar : Element
	{
		public CD_Player_Scorebar() {

		}
		protected override void Initialize() {
			base.Initialize();
			Dock = Dock.Top;
		}
		public override void Paint(float width, float height) {
			Graphics2D.SetDrawColor(255, 255, 255, 255);
			//if (Level.AutoPlayer.Enabled)
			//Graphics2D.DrawText(width / 2f, 32 + 48, $"AUTO", "Noto Sans", 32, Anchor.Center);
			var lvl = Level.As<DashGameLevel>();
			Graphics2D.DrawText(width * 0.4f, 32 + 24, $"{lvl.Combo}", "Noto Sans", (int)NMath.Remap(lvl.Conductor.Time - lvl.LastCombo, 0.2f, 0, 32, 40, clampOutput: true), Anchor.Center);
			Graphics2D.DrawText(width * 0.4f, 32 + 56, "COMBO", "Noto Sans", 24, Anchor.Center);

			Graphics2D.DrawText(width * 0.6f, 32 + 24, $"{lvl.Score}", "Noto Sans", 32, Anchor.Center);
			Graphics2D.DrawText(width * 0.6f, 32 + 56, "SCORE", "Noto Sans", 24, Anchor.Center);
		}
	}
}