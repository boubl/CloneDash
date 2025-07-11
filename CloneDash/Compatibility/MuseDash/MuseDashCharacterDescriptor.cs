﻿using AssetStudio;

using CloneDash.Characters;
using CloneDash.Game;

using Fmod5Sharp;
using Fmod5Sharp.FmodTypes;

using Nucleus;
using Nucleus.Audio;
using Nucleus.Engine;
using Nucleus.Extensions;
using Nucleus.Models.Runtime;

using System.Collections.Specialized;

namespace CloneDash.Compatibility.MuseDash;

public class MuseDashCharacterExpression : ICharacterExpression
{
	private CharacterExpression Expression;
	private string Talk;
	private string AudioName;
	string ICharacterExpression.GetEndAnimationName() {
		return $"{Expression.AnimName}_end";
	}

	string ICharacterExpression.GetIdleAnimationName() {
		return $"{Expression.AnimName}_standby";
	}

	void ICharacterExpression.GetSpeech(Level level, out string text, out Sound voice) {
		text = Talk;
		voice = MuseDashCompatibility.LoadSoundFromName(level, AudioName);
	}

	string ICharacterExpression.GetStartAnimationName() {
		return $"{Expression.AnimName}_start";
	}

	public static MuseDashCharacterExpression From(CharacterConfigData data) {
		MuseDashCharacterExpression expression = new MuseDashCharacterExpression();
		int i = Random.Shared.Next(0, data.Expressions.Count);
		expression.Expression = data.Expressions[i];

		var audioNames = expression.Expression.AudioNames;
		var audioI = Random.Shared.Next(0, audioNames.Count);

		expression.Talk = data.Localization["english"].Expressions[i][audioI];
		expression.AudioName = audioNames[audioI];
		return expression;
	}
}
public class MuseDashCharacterRetriever : ICharacterProvider
{
	int ICharacterProvider.Priority => 0;

	public string GetName(CharacterConfigData cfd) => $"md_{cfd.BGM.Replace("_bgm", "")}";

	IEnumerable<string> ICharacterProvider.GetAvailable() {
		foreach (var character in MuseDashCompatibility.Characters) {
			yield return GetName(character);
		}
	}

	ICharacterDescriptor? ICharacterProvider.FindByName(string name) {
		foreach (var character in MuseDashCompatibility.Characters) {
			if (name != GetName(character)) continue;

			return new MuseDashCharacterDescriptor(character);
		}

		return null;
	}
}

[Nucleus.MarkForStaticConstruction]
public class MuseDashCharacterDescriptor(CharacterConfigData configData) : ICharacterDescriptor
{
	public static ConCommand nextmdchar = ConCommand.Register(nameof(nextmdchar), (_, _) => {
		var chvar = ConVar.Get(nameof(CharacterMod.character))!;
		var clonedash_character_value = chvar.GetString();
		ICharacterProvider retriever = new MuseDashCharacterRetriever();
		bool next = false;
		foreach (var character in retriever.GetAvailable()) {
			if (character == clonedash_character_value) next = true;
			else if (next) {
				chvar.SetValue(character);
				Logs.Info($"Selecting '{character}'");
				return;
			}
		}
		Logs.Warn("No more characters available.");
	});

	public string GetName() => $"{configData.Localization["english"].CosName} {configData.Localization["english"].CharacterName}";
	public string? GetDescription() => configData.Localization["english"].Description;
	public string GetAuthor() => "PeroPeroGames";
	public string GetPerk() => $"{configData.Localization["english"].Skill}";
	public double GetDefaultHP() => int.TryParse(configData.DefaultHP, out var hp) ? hp : 250;

	public ModelData GetFailModel(Level level) {
		throw new NotImplementedException();
	}

	public string? GetLogicControllerData() => null;

	public ICharacterExpression? GetMainShowExpression() {
		MuseDashCharacterExpression expression = MuseDashCharacterExpression.From(configData);
		return expression;
	}

	public string? GetMainShowInitialExpression() => null;

	// I hate this!
	public static ModelData PullModelDataFromSkeletonMecanim(Level level, MonoBehaviour skeletonMecanim) {
		var assets = MuseDashCompatibility.StreamingAssets;

		// This pulls out skeletonDataAsset m_PathID
		// todo: refactor this abomination
		var skeletonDataAsset = (long)((OrderedDictionary)(skeletonMecanim!.ToType())["skeletonDataAsset"]!)["m_PathID"]!;

		// read the skeleton now
		var mainShowAssetMB = assets.FindAssetByPathID<MonoBehaviour>(skeletonDataAsset)!;
		OrderedDictionary mainShowAsset = mainShowAssetMB.ToType();
		// pull out the JSON
		var jsonPathID = (long)((OrderedDictionary)mainShowAsset["skeletonJSON"]!)["m_PathID"]!;
		var atlasAssets = (List<object>)mainShowAsset["atlasAssets"]!;


		var atlasBase = (OrderedDictionary)atlasAssets[0];
		var atlasBaseID = (long)atlasBase["m_PathID"]!;

		// read atlas data
		var atlasMB = assets.FindAssetByPathID<MonoBehaviour>(atlasBaseID)!;
		OrderedDictionary atlasInfo = atlasMB.ToType();

		var atlasPathID = (long)((OrderedDictionary)atlasInfo["atlasFile"]!)["m_PathID"]!;
		var materials = (List<object>)atlasInfo["materials"]!;

		long[] textureIDs = new long[materials.Count];
		Material[] materialsIn = new Material[materials.Count];
		int i = 0;
		foreach (var materialBaseObj in materials) {
			var materialBase = (OrderedDictionary)materialBaseObj;
			var materialPathID = (long)materialBase["m_PathID"]!;

			// read material data
			var materialMB = assets.FindAssetByPathID<Material>(materialPathID)!;
			var texPtr = materialMB.m_SavedProperties.m_TexEnvs.First()!.Value.m_Texture;
			if (!texPtr.TryGet(out var tex)) throw new Exception();
			textureIDs[i] = tex.m_PathID;
			materialsIn[i] = materialMB;
			i++;
		}

		return MuseDashModelConverter.MD_GetModelData(level, jsonPathID, atlasPathID, textureIDs, materialsIn);
	}

	public static ModelData PullModelDataFromGameObject(Level level, string name) {
		var assets = MuseDashCompatibility.StreamingAssets;

		var mainshowObject = assets.FindAssetByName<GameObject>(name);
		var skeletonMecanim = mainshowObject!.GetMonoBehaviorByScriptName("SkeletonMecanim");
		if (skeletonMecanim == null)
			skeletonMecanim = mainshowObject!.GetMonoBehaviorByScriptName("SkeletonAnimation");
		if(skeletonMecanim == null) {
			// OK time to go through the depths of hell for a victory model
			var rectTransform = mainshowObject.GetFirstComponent<RectTransform>()!;
			rectTransform.m_Children[0].TryGet(out rectTransform!);
			rectTransform.m_GameObject.TryGet(out mainshowObject);
			skeletonMecanim = mainshowObject.GetMonoBehaviorByScriptName("SkeletonGraphic");
		}


		return PullModelDataFromSkeletonMecanim(level, skeletonMecanim!);
	}

	public ModelData GetMainShowModel(Level level) => PullModelDataFromGameObject(level, configData.MainShow);

	public MusicTrack? GetMainShowMusic(Level level) {
		var audioclip = MuseDashCompatibility.StreamingAssets.FindAssetByName<AudioClip>(configData.BGM);
		if (audioclip == null) return null;

		var audiodata = audioclip.m_AudioData.GetData();

		if (audioclip.m_Type == FMODSoundType.UNKNOWN) {
			FmodSoundBank bank = FsbLoader.LoadFsbFromByteArray(audiodata);
			bank.Samples[0].RebuildAsStandardFileFormat(out var at, out var fileExtension);
			return EngineCore.Level.Sounds.LoadMusicFromMemory(at!);
		}

		throw new NotImplementedException();
	}

	public string GetMainShowStandby() => "BgmStandby";

	private Dictionary<CharacterAnimationType, List<string>> anims;
	private void convertAnimations() {
		if (anims != null) return;

		var assets = MuseDashCompatibility.StreamingAssets;

		var mainshowObject = assets.FindAssetByName<GameObject>(configData.BattleShow);
		var actionController = mainshowObject!.GetMonoBehaviorByScriptName("SpineActionController")!;
		var actions = (List<object>)actionController.ToType()["actionData"]!;
		anims = [];
		foreach (var actionObj in actions) {
			var action = (OrderedDictionary)actionObj;

			bool isRandomSequence = (byte)action["isRandomSequence"]! > 0;

			CharacterAnimationType type = (string)action["name"]! switch {
				"char_run" => CharacterAnimationType.Run,
				"in" => CharacterAnimationType.In,
				"char_hurt" => CharacterAnimationType.RoadHurt,
				"char_jump_hurt" => CharacterAnimationType.JumpHurt,
				"char_die" => CharacterAnimationType.Die,
				"char_press" => CharacterAnimationType.Press,
				"char_atk_miss" => CharacterAnimationType.RoadMiss,
				"char_atk_g" => CharacterAnimationType.RoadGreat,
				"char_atk_p" => CharacterAnimationType.RoadPerfect,
				"char_jump" => CharacterAnimationType.Jump,
				"char_jumphit" => CharacterAnimationType.AirPerfect,

				// todo: research this better
				"char_downhit" => CharacterAnimationType.NotApplicable,
				"char_downpress" => CharacterAnimationType.DownPressHit,
				"char_uphit" => CharacterAnimationType.NotApplicable,
				"char_uppress" => CharacterAnimationType.UpPressHit,
				"char_uppress_end" => CharacterAnimationType.AirPressEnd,
				"char_big_press" => CharacterAnimationType.Press,

				// ???
				"char_up_press_s2b" => CharacterAnimationType.NotApplicable,
				"char_up_press_b2s" => CharacterAnimationType.NotApplicable,
				"char_down_press_s2b" => CharacterAnimationType.NotApplicable,
				"char_down_press_b2s" => CharacterAnimationType.NotApplicable,

				"char_bighit" => CharacterAnimationType.Double,
				"char_up_press_s" => CharacterAnimationType.NotApplicable,
				"char_down_press_s" => CharacterAnimationType.NotApplicable,
				"char_uppress_hurt" => CharacterAnimationType.AirPressHurt,
				"char_jumphit_great" => CharacterAnimationType.AirGreat,

				_ => CharacterAnimationType.NotApplicable
			};
			if (type == CharacterAnimationType.NotApplicable) continue;

			if (!anims.TryGetValue(type, out var individualAnims)) {
				individualAnims = [];
				anims[type] = individualAnims;
			}

			var actionIdx = (List<object>)action["actionIdx"]!;
			foreach (var actionId in actionIdx) {
				individualAnims.Add((string)actionId!);
			}
			//individualAnims.Add()
		}

	}

	public string GetPlayAnimation(CharacterAnimationType animationType) {
		convertAnimations();
		return anims[animationType].Random();
	}

	public ModelData GetPlayModel(Level level) => PullModelDataFromGameObject(level, configData.BattleShow);
	public ModelData GetVictoryModel(Level level) => PullModelDataFromGameObject(level, configData.VictoryShow);
	public string GetVictoryStandby() => "standby";
}
