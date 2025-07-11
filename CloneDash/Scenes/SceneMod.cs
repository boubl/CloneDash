﻿using CloneDash.Data;
using CloneDash.Modding.Descriptors;

using Nucleus;
using Nucleus.Util;

namespace CloneDash.Scenes;

[MarkForStaticConstruction]
public static class SceneMod
{
	public static ConVar scene = ConVar.Register(nameof(scene), "clonedash", ConsoleFlags.Saved, "Your scene.");
	public static ConVar allowsceneoverride = ConVar.Register(nameof(allowsceneoverride), "1", ConsoleFlags.Saved, $"If true (and the scene specified exists on-disk), allows charts to specify the scene used during gameplay. If false, will always use ConVar \"{nameof(scene)}\"'s value.", 0, 1);
	public static ConCommand scenes = ConCommand.Register(nameof(scenes), (_, _) => {
		var scenes = GetAvailableScenes();
		foreach (var scene in scenes)
			Logs.Print($"    {scene}");
	}, "Prints all available scenes");

	public static IEnumerable<string> GetAvailableScenes() {
		ISceneProvider[] retrievers = ReflectionTools.InstantiateAllInheritorsOfInterface<ISceneProvider>();
		foreach (var retriever in retrievers)
			foreach (var characterName in retriever.GetAvailable())
				yield return characterName;
	}

	public static ISceneDescriptor? GetSceneData(ChartSong? song = null) {
		string? name = scene?.GetString();

		if (string.IsNullOrWhiteSpace(name))
			return null;

		ISceneProvider[] retrievers = ReflectionTools.InstantiateAllInheritorsOfInterface<ISceneProvider>();
		foreach (var retriever in retrievers) {
			ISceneDescriptor? descriptor = retriever.FindByName(name);
			if (descriptor == null) continue;

			return descriptor;
		}

		Logs.Warn($"WARNING: The scene '{name}' could not be found!");
		return null;
	}
}

