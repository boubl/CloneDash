﻿using CloneDash.Game;
using CloneDash.Game.Input;

namespace CloneDash;

public static class CDUtils
{
	public static int DetermineScoreMultiplied(float baseScore, bool inFever, int combo, double accuracy) {
		if (combo <= 9) baseScore *= 1.0f;
		else if (combo <= 19) baseScore *= 1.1f;
		else if (combo <= 29) baseScore *= 1.2f;
		else if (combo <= 39) baseScore *= 1.3f;
		else if (combo <= 49) baseScore *= 1.4f;
		else baseScore *= 1.5f;

		accuracy = Math.Abs(accuracy);

		if (inFever)
			baseScore *= 1.5f;

		if (accuracy >= 25)
			baseScore *= (inFever ? 0.66666666666f : .5f);

		return (int)MathF.Round(baseScore);
	}

	public static int DetermineScoreMultiplied(this DashGameLevel game, float baseScore, PollResult pollResult) => DetermineScoreMultiplied(baseScore, game.InFever, game.Combo, pollResult.DistanceToHit);
}
